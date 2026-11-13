using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // How much a combatant carries against what the bag holds (PUBG_GAP 4-3 batch H), and the automatic pickup of
    // ammo. Nothing of its own is replicated: the amounts are read from the replicated counts on the other
    // components, so every side computes the same numbers. The server enforces the limit where loot is handed
    // over (`ServerFit`, asked first by Combatant.ServerGive; the item receivers ask `FitCount` themselves).
    //
    // Inventory bar "가방 용량 23/60": `Local.UsedPoints` / `Local.CapacityPoints` (or `Local.Label`, which builds
    // a string: refresh it when the numbers change, not every frame).
    //
    // It also holds the worn backpack (ADR-091): tiers 1..3, picked up like loot (ILootReceiver), a higher tier
    // replaces the worn one, which goes on the ground; never a lower one.
    [RequireComponent(typeof(Combatant))]
    public class CombatantCarry : NetworkBehaviour, ILootReceiver
    {
        public readonly SyncVar<int> BackpackTier = new SyncVar<int>(); // 0 = none

        Combatant _combatant;
        CombatantGear _gear;
        CombatantHealing _healing;
        ThrowableUser _throwables;
        CombatantJerrycan _jerrycans;
        CombatantOptics _optics;
        float _nextScan;
        int _slotAmmoA, _slotAmmoB, _slotAmmoC;
        // Ammo that just left the bag (dropped, or loaded into a weapon) is not picked up again while standing
        // where it left: automatic pickup stays off until the combatant has walked away from that spot.
        int _lastReserveTotal;
        bool _suppressed;
        Vector3 _suppressFrom;

        // The local player's own, for the inventory. Null when there is none.
        public static CombatantCarry Local { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Local = null;

        // Server: how many pickups the automatic ammo pickup has made, for tests.
        public int ServerAutoPickups { get; private set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _gear = GetComponent<CombatantGear>();
            _healing = GetComponent<CombatantHealing>();
            _throwables = GetComponent<ThrowableUser>();
            _jerrycans = GetComponent<CombatantJerrycan>();
            _optics = GetComponent<CombatantOptics>();
        }

        public override void OnStartClient()
        {
            if (IsOwner && _combatant.IsPlayer) Local = this;
        }

        // An owner that returns after a reconnect (#166) gets the object after it already started as an observer.
        public override void OnOwnershipClient(FishNet.Connection.NetworkConnection prev) => OnStartClient();

        public override void OnStopClient()
        {
            if (Local == this) Local = null;
        }

        // Scene unloads destroy objects without the network callbacks.
        void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        // ---- Read-only, every side ----

        public int UsedTenths => Carry.UsedTenths(CarryRules.Current,
            _combatant.ReservePistol9.Value, _combatant.ReserveRifle556.Value, _combatant.ReserveShotgun12.Value, _combatant.ReserveRifle762.Value,
            _healing != null ? _healing.Bandages.Value : 0, _healing != null ? _healing.FirstAids.Value : 0, _combatant.Medkits.Value,
            _throwables != null ? _throwables.Grenades.Value : 0, _throwables != null ? _throwables.Smokes.Value : 0)
            + (_healing != null ? _healing.BoostTenths : 0) // ADR-099
            + (_throwables != null ? _throwables.ExtraTenths : 0) // ADR-105/110
            + (_jerrycans != null ? _jerrycans.Count.Value * Carry.Tenths(CarryRules.Current.Jerrycan) : 0) // ADR-075
            + (_optics != null ? OpticBag.Total(_optics.Bag.Value) * Carry.Tenths(CarryRules.Current.Optic) : 0); // ADR-082

        // The bag's size: the base plus what the worn vest (broken or not) and backpack add.
        public int CapacityTenths => Carry.CapacityTenths(CarryRules.Current, _gear != null ? _gear.VestTier.Value : 0, BackpackTier.Value);

        public int FreeTenths => Mathf.Max(0, CapacityTenths - UsedTenths);
        public float Used => UsedTenths / (float)Carry.Scale;
        public float Capacity => CapacityTenths / (float)Carry.Scale;
        public int UsedPoints => Carry.Points(UsedTenths);
        public int CapacityPoints => Carry.Points(CapacityTenths);
        // 0..1 (more when over capacity), for a bar.
        public float Fraction => CapacityTenths > 0 ? UsedTenths / (float)CapacityTenths : 0f;
        public string Label => "가방 용량 " + UsedPoints + "/" + CapacityPoints
            + (BackpackTier.Value > 0 ? "  (가방 " + LootNames.Tier(BackpackTier.Value) + ")" : "");

        // How many of `wanted` units of a loot kind (rounds, items) fit into the free room.
        public int FitCount(LootKind kind, AmmoType ammo, int wanted) =>
            Carry.Fit(FreeTenths, Carry.UnitTenths(kind, ammo, CarryRules.Current), wanted);

        // ---- Server ----

        // Puts a backpack on directly (bots, tests); tier 0 takes it off. Returns the tier worn before (not dropped).
        public int ServerEquipBackpack(int tier)
        {
            if (!IsServerInitialized) return 0;
            int before = BackpackTier.Value;
            BackpackTier.Value = tier >= 1 && tier <= Carry.BackpackTiers ? tier : 0;
            return before;
        }

        // ILootReceiver: a backpack of a higher tier than the worn one. The old one lands at the feet.
        public bool ServerTryTake(LootEntry loot)
        {
            if (!IsServerInitialized || loot == null || loot.Kind != LootKind.Backpack || _combatant.Dead.Value) return false;
            if (!Carry.IsBetterBackpack(loot.Tier, BackpackTier.Value)) return false;
            int old = ServerEquipBackpack(loot.Tier);
            if (old > 0 && MatchDirector.Instance != null)
                MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Backpack, Tier = old, Amount = 1 }, transform.position + Vector3.up * 0.3f);
            return true;
        }

        // The check Combatant.ServerGive makes before it takes anything. Cuts `loot.Amount` down to what fits and
        // reports the rest in `leftover`; false when none of it fits (the pickup is refused and stays where it lies).
        // Kinds that take no room always pass untouched.
        public bool ServerFit(LootEntry loot, out int leftover)
        {
            leftover = 0;
            if (loot == null) return true;
            if (Carry.UnitTenths(loot.Kind, loot.Ammo, CarryRules.Current) <= 0) return true;
            int wanted = Mathf.Max(1, loot.Amount);
            int fit = FitCount(loot.Kind, loot.Ammo, wanted);
            if (fit <= 0) return false;
            leftover = wanted - fit;
            loot.Amount = fit;
            return true;
        }

        // Server: automatic pickup stays off until the combatant walks away from here (e.g. after dropping a
        // weapon, whose rounds land beside it as ammo, ADR-081).
        public void ServerSuppressAutoPickup()
        {
            _suppressed = true;
            _suppressFrom = transform.position;
        }

        void Update()
        {
            if (!IsServerInitialized) return;
            float now = Time.time;
            if (now < _nextScan) return;
            _nextScan = now + CarryRules.Current.AutoPickupInterval;
            ServerScanAmmo();
        }

        // Picks up ammo of a calibre one of the weapons uses, from pickups within reach, as far as the bag allows.
        // Called every AutoPickupInterval; public so a test can run it once.
        public void ServerScanAmmo()
        {
            if (!IsServerInitialized || _combatant.Dead.Value) return;
            var here = transform.position;
            int total = _combatant.ReservePistol9.Value + _combatant.ReserveRifle556.Value + _combatant.ReserveShotgun12.Value + _combatant.ReserveRifle762.Value;
            if (total < _lastReserveTotal)
            {
                _suppressed = true;
                _suppressFrom = here;
            }
            _lastReserveTotal = total;
            float reach = CarryRules.Current.AutoPickupMeters;
            if (_suppressed)
            {
                float away = reach + 1f;
                if ((here - _suppressFrom).sqrMagnitude <= away * away) return;
                _suppressed = false;
            }
            _slotAmmoA = AmmoOf(_combatant.Slot0.Value);
            _slotAmmoB = AmmoOf(_combatant.Slot1.Value);
            _slotAmmoC = AmmoOf(_combatant.Slot2.Value); // the sidearm
            if (_slotAmmoA < 0 && _slotAmmoB < 0 && _slotAmmoC < 0) return;
            float reachSqr = reach * reach;
            var items = LootItem.All;
            // Backwards: taking an item removes it from the list.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (i >= items.Count) continue;
                var item = items[i];
                if (item == null || !item.IsSpawned || item.Kind.Value != (int)LootKind.Ammo) continue;
                int type = item.Ammo.Value;
                if (type != _slotAmmoA && type != _slotAmmoB && type != _slotAmmoC) continue;
                if ((item.transform.position - here).sqrMagnitude > reachSqr) continue;
                int amount = item.Amount.Value;
                int fit = FitCount(LootKind.Ammo, (AmmoType)type, amount);
                if (fit <= 0) continue;
                if (!_combatant.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = (AmmoType)type, Amount = fit })) continue;
                if (fit >= amount) item.Despawn(); else item.Amount.Value = amount - fit;
                ServerAutoPickups++;
            }
        }

        // The calibre of the weapon with this id, or -1 for an empty slot.
        static int AmmoOf(int weaponId)
        {
            if (weaponId == 0) return -1;
            var weapon = GameData.Weapons.Find(weaponId);
            return weapon != null ? (int)weapon.Ammo : -1;
        }
    }
}
