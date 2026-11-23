using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Jerrycans (ADR-067, ADR-075): a bag item (weight CarryRules.Jerrycan) that refuels the vehicle the player sits in
    // or stands next to. Pouring takes FuelRules.JerrycanSeconds; the can is used up and the fuel added only when the
    // time is out. The server cancels (the can stays) on damage, a shot, a throw, a reload, a weapon switch, a heal,
    // death or knock-down, walking off, or the vehicle moving / burning / going away. Same pattern as CombatantHealing.
    //
    // HUD: `Local.Using`, `Local.Progress01`. Key J (VehicleHud) or a double click on the bag row.
    [RequireComponent(typeof(Combatant))]
    public class CombatantJerrycan : NetworkBehaviour, ILootReceiver
    {
        public readonly SyncVar<int> Count = new SyncVar<int>();
        // ObjectId of the vehicle being refuelled; -1 = not pouring.
        public readonly SyncVar<int> TargetId = new SyncVar<int>(-1);
        public readonly SyncVar<float> Duration = new SyncVar<float>();

        public static CombatantJerrycan Local { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Local = null;

        Combatant _combatant;
        ThrowableUser _throwables;
        CombatantHealing _healing;
        CombatantCarry _carry;
        double _localStart, _serverStart;
        float _baseHp;
        int _baseSlot, _baseMag0, _baseMag1, _baseMag2, _baseThrows;

        public int ServerCompleted { get; private set; }
        public int ServerCancelled { get; private set; }

        // ---- Read-only, every side ----

        public bool Using => TargetId.Value >= 0;
        public float Progress01 => Using ? Healing.Progress01(Time.timeAsDouble, _localStart, Duration.Value) : 0f;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _throwables = GetComponent<ThrowableUser>();
            _healing = GetComponent<CombatantHealing>();
            _carry = GetComponent<CombatantCarry>();
            TargetId.OnChange += (prev, next, asServer) => { if (next >= 0) _localStart = Time.timeAsDouble; };
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

        void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        // The vehicle a can would go into now: the one the combatant sits in, else the nearest tank within reach on
        // foot. Null when none could take it (full, moving, burning). Every side (the HUD asks for its prompt).
        public static VehicleFuel TargetFor(Combatant c)
        {
            if (c == null) return null;
            var rules = FuelRules.Current;
            var seated = VehicleSeat.VehicleOf(c);
            if (seated != null)
            {
                var tank = seated.GetComponent<VehicleFuel>();
                return tank != null && Usable(tank, true, 0f, rules) ? tank : null;
            }
            VehicleFuel best = null;
            float bestDistance = float.PositiveInfinity;
            var all = Vehicle.All;
            var here = c.transform.position + Vector3.up * 0.9f;
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (v == null || (v.transform.position - here).sqrMagnitude > 64f) continue;
                var tank = v.GetComponent<VehicleFuel>();
                if (tank == null) continue;
                float d = tank.DistanceToBody(here);
                if (d < bestDistance && Usable(tank, false, d, rules))
                {
                    best = tank;
                    bestDistance = d;
                }
            }
            return best;
        }

        static bool Usable(VehicleFuel tank, bool seated, float distance, FuelRules rules)
        {
            var v = tank.Vehicle;
            if (v == null || (v.State != null && v.State.Disabled)) return false;
            return Fuel.CanRefuel(tank.Fuel01, seated, distance, v.SpeedKmh, rules);
        }

        // ---- Server ----

        void Update()
        {
            if (IsServerInitialized && Using) ServerTick(Time.timeAsDouble);
        }

        public void ServerTick(double now)
        {
            if (!IsServerInitialized || !Using) return;
            var tank = FindTank(TargetId.Value);
            if (tank == null || ShouldCancel(tank))
            {
                ServerCancel();
                return;
            }
            if (now < _serverStart + Duration.Value) return;
            TargetId.Value = -1;
            if (Count.Value <= 0 || !tank.ServerRefill()) { ServerCancelled++; return; }
            Count.Value--;
            ServerCompleted++;
        }

        static VehicleFuel FindTank(int objectId)
        {
            var all = Vehicle.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ObjectId == objectId) return all[i].GetComponent<VehicleFuel>();
            return null;
        }

        bool ShouldCancel(VehicleFuel tank)
        {
            var c = _combatant;
            if (c.Dead.Value || c.TeamBlocksAction || c.Reloading.Value || Count.Value <= 0) return true;
            if (_healing != null && _healing.Using) return true;
            float hp = c.Hp.Value;
            if (hp < _baseHp - 1e-4f) return true;
            _baseHp = hp;
            if (c.ActiveSlot.Value != _baseSlot) return true;
            if (c.Mag0.Value < _baseMag0 || c.Mag1.Value < _baseMag1 || c.Mag2.Value < _baseMag2) return true;
            _baseMag0 = c.Mag0.Value;
            _baseMag1 = c.Mag1.Value;
            _baseMag2 = c.Mag2.Value;
            if (_throwables != null && _throwables.ServerThrows != _baseThrows) return true;
            var v = tank.Vehicle;
            if (v.State != null && v.State.Disabled) return true;
            bool seated = VehicleSeat.VehicleOf(c) == v;
            float distance = seated ? 0f : tank.DistanceToBody(c.transform.position + Vector3.up * 0.9f);
            return Fuel.RefuelBroken(seated, distance, v.SpeedKmh, FuelRules.Current);
        }

        public void ServerCancel()
        {
            if (!IsServerInitialized || !Using) return;
            TargetId.Value = -1;
            ServerCancelled++;
        }

        // Starts pouring into the vehicle TargetFor picks. Refused while busy, out of cans or with no vehicle.
        public bool ServerBegin()
        {
            if (!IsServerInitialized || Using || Count.Value <= 0) return false;
            var c = _combatant;
            if (c.Dead.Value || c.TeamBlocksAction || c.Reloading.Value) return false;
            if (_healing != null && _healing.Using) return false;
            var tank = TargetFor(c);
            if (tank == null) return false;
            _baseHp = c.Hp.Value;
            _baseSlot = c.ActiveSlot.Value;
            _baseMag0 = c.Mag0.Value;
            _baseMag1 = c.Mag1.Value;
            _baseMag2 = c.Mag2.Value;
            _baseThrows = _throwables != null ? _throwables.ServerThrows : 0;
            _serverStart = Time.timeAsDouble;
            _localStart = _serverStart;
            Duration.Value = FuelRules.Current.JerrycanSeconds;
            TargetId.Value = tank.Vehicle.ObjectId;
            return true;
        }

        // Returns how many were taken (the bag may refuse).
        public int ServerAdd(int amount)
        {
            if (!IsServerInitialized || amount <= 0) return 0;
            int taken = _carry != null ? _carry.FitCount(FuelRules.JerrycanLootKind, AmmoType.Pistol9, amount) : amount;
            if (taken <= 0) return 0;
            Count.Value += taken;
            return taken;
        }

        public bool ServerTryTake(LootEntry loot)
        {
            if (!IsServerInitialized || loot == null || loot.Kind != FuelRules.JerrycanLootKind || _combatant.Dead.Value) return false;
            int wanted = Mathf.Max(1, loot.Amount);
            int taken = ServerAdd(wanted);
            if (taken <= 0) return false;
            if (taken < wanted && MatchDirector.Instance != null)
                MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = loot.Kind, Amount = wanted - taken }, transform.position + Vector3.up * 0.3f);
            return true;
        }

        // ---- Owner requests ----

        // J (or a double click): starts pouring, or stops a pour under way.
        public void RequestToggle()
        {
            if (IsServerInitialized) { if (Using) ServerCancel(); else ServerBegin(); }
            else CmdToggle();
        }

        [ServerRpc]
        void CmdToggle()
        {
            if (!_combatant.ItemRequestOk("jerrycan Toggle")) return;
            if (Using) ServerCancel(); else ServerBegin();
        }

        [ServerRpc]
        public void CmdDrop()
        {
            if (!_combatant.ItemRequestOk("jerrycan Drop")) return;
            if (_combatant.Dead.Value || Count.Value <= 0 || MatchDirector.Instance == null) return;
            if (Using) ServerCancel();
            Count.Value--;
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = FuelRules.JerrycanLootKind, Amount = 1 },
                transform.position + transform.forward * 1.0f + Vector3.up * 0.3f);
        }
    }
}
