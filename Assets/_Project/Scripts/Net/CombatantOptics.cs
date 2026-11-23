using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The optic fitted to each weapon slot (ADR-056 / ADR-058), and since ADR-100 the other attachments too (muzzle,
    // grip, stock, magazine): each weapon slot holds one packed int (Attachments.Get/With). The optic belongs to the weapon in that slot: when
    // the weapon leaves the slot the optic goes with it (Combatant calls ServerOnWeaponRemoved / ServerOnSlotsSwapped;
    // an optic that no longer fits its slot's weapon is also dropped on the ground by a server-side check).
    // The server owns it; clients read the SyncVars. Picked up like any other loot (ILootReceiver).
    [RequireComponent(typeof(Combatant))]
    public class CombatantOptics : NetworkBehaviour, ILootReceiver
    {
        // Storage for this many weapon slots; slots beyond Loadout.SlotCount stay empty.
        public const int MaxSlots = 5;

        // Everything fitted to each weapon slot, packed by AttachSlot (0 = none). Plain SyncVars, like the other per-slot values.
        public readonly SyncVar<int> Gear0 = new SyncVar<int>();
        public readonly SyncVar<int> Gear1 = new SyncVar<int>();
        public readonly SyncVar<int> Gear2 = new SyncVar<int>();
        public readonly SyncVar<int> Gear3 = new SyncVar<int>();
        public readonly SyncVar<int> Gear4 = new SyncVar<int>();
        // ADR-082: optics loose in the bag, packed by OpticBag (count per optic id).
        public readonly SyncVar<ulong> Bag = new SyncVar<ulong>();

        Combatant _combatant;
        // Server truth; the SyncVars mirror it.
        readonly int[] _server = new int[MaxSlots];
        readonly int[] _applied = new int[MaxSlots]; // what the loadout was last told about (0 = nothing fitted)
        ulong _bag;
        CombatantCarry _carry;

        static int SlotCount => Mathf.Min(Loadout.SlotCount, MaxSlots);

        // ---- Read-only, every side (HUD, inventory, visuals) ----

        int PackedInSlot(int slot)
        {
            switch (slot)
            {
                case 0: return Gear0.Value;
                case 1: return Gear1.Value;
                case 2: return Gear2.Value;
                case 3: return Gear3.Value;
                case 4: return Gear4.Value;
                default: return 0;
            }
        }

        public int OpticInSlot(int slot) => Attachments.Get(PackedInSlot(slot), AttachSlot.Optic);
        // The id fitted in one place of the weapon in `slot` (0 = none).
        public int AttachmentInSlot(int slot, AttachSlot place) => Attachments.Get(PackedInSlot(slot), place);
        // What the grips and stock on the weapon in `slot` do to its recoil.
        public AttachMods ModsInSlot(int slot) => Attachments.ModsOf(PackedInSlot(slot), OpticRules.Current);
        // Cached per packed value: read every frame by the view recoil.
        public AttachMods ActiveMods
        {
            get
            {
                int packed = PackedInSlot(_combatant.ActiveSlot.Value);
                if (_modsPacked != packed || _mods == null) { _mods = Attachments.ModsOf(packed, OpticRules.Current); _modsPacked = packed; }
                return _mods;
            }
        }
        AttachMods _mods;
        int _modsPacked;

        public int ActiveOptic => OpticInSlot(_combatant.ActiveSlot.Value);
        // 1 for no optic and for the dot and holographic sights.
        public float ActiveZoom => Optics.ZoomOf(ActiveOptic);
        public OpticDef ActiveOpticDef => OpticRules.Current.Find(ActiveOptic);
        public int InBag(int opticId) => OpticBag.Count(Bag.Value, opticId);

        // Weapon in a slot, as replicated. Wiring: when Combatant gets an accessor for any slot, call it here.
        int WeaponInSlot(int slot) => _combatant.WeaponInSlot(slot);

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _carry = GetComponent<CombatantCarry>();
        }

        public override void OnStartServer()
        {
            for (int i = 0; i < MaxSlots; i++) _server[i] = 0;
            _bag = 0;
            Push();
        }

        void Update()
        {
            if (!IsServerInitialized) return;
            // A weapon that left its slot without a word (or was swapped for one that does not take the optic)
            // puts its optic in the bag (or on the ground when full) instead of keeping it invisibly.
            int count = SlotCount;
            for (int i = 0; i < count; i++)
            {
                // ADR-102: the magazine attachment's effect on the loadout follows what is fitted (only when it changes).
                if (_server[i] != _applied[i])
                {
                    _applied[i] = _server[i];
                    var mods = Attachments.ModsOf(_server[i], OpticRules.Current);
                    _combatant.ServerSetSlotMods(i, mods.MagFactor, mods.ReloadFactor);
                }
                if (_server[i] == 0) continue;
                for (int s = 0; s < Attachments.Slots; s++)
                {
                    int id = Attachments.Get(_server[i], (AttachSlot)s);
                    if (id != 0 && !Optics.CanAttach(WeaponInSlot(i), id)) ServerStash(ServerRemove(Attachments.GearIndex(i, (AttachSlot)s)));
                }
            }
        }

        void Push()
        {
            Gear0.Value = _server[0];
            Gear1.Value = _server[1];
            Gear2.Value = _server[2];
            Gear3.Value = _server[3];
            Gear4.Value = _server[4];
            Bag.Value = _bag;
        }

        // ADR-103: the owner's zero distance for the optic in hand; the shot crosses the line of sight there. Snapped to
        // the optic's list. Sent again whenever the optic in hand changes.
        [ServerRpc]
        public void CmdSetZero(int meters)
        {
            var def = ActiveOpticDef;
            var rules = GameData.Rules.Ballistics;
            _combatant.ZeroMeters = Zeroing.Distance(def, rules, Zeroing.IndexOf(def, rules, meters));
        }

        // ---- Server API ----

        // Fits an optic to the weapon in `slot`. An optic already there is replaced and goes to the bag (the ground
        // when the bag is full). False when the slot has no weapon or the weapon does not take that optic.
        public bool ServerAttach(int slot, int opticId)
        {
            if (!IsServerInitialized || slot < 0 || slot >= SlotCount) return false;
            if (!Optics.CanAttach(WeaponInSlot(slot), opticId)) return false;
            var place = Attachments.SlotOf(opticId);
            if (place == null) return false;
            int before = Attachments.Get(_server[slot], place.Value);
            _server[slot] = Attachments.With(_server[slot], place.Value, opticId);
            Push();
            if (before != 0) ServerStash(before);
            return true;
        }

        // ADR-082: an optic into the bag when it has room, else on the ground at the feet.
        public void ServerStash(int opticId)
        {
            if (!IsServerInitialized || opticId == 0) return;
            if (BagHasRoom() && OpticBag.TryAdd(ref _bag, opticId)) Push();
            else Drop(opticId, false);
        }

        bool BagHasRoom() => _carry == null || _carry.FreeTenths >= Carry.Tenths(CarryRules.Current.Optic);

        // From the bag onto the weapon in `slot`; the optic that was there goes to the bag (its room was just freed).
        public bool ServerAttachFromBag(int opticId, int slot)
        {
            if (!IsServerInitialized || OpticBag.Count(_bag, opticId) <= 0 || slot < 0 || slot >= SlotCount) return false;
            if (!Optics.CanAttach(WeaponInSlot(slot), opticId)) return false;
            OpticBag.TryRemove(ref _bag, opticId);
            return ServerAttach(slot, opticId);
        }

        // Off the weapon in `slot` into the bag (the ground when full).
        public void ServerDetachToBag(int slot) => ServerStash(ServerRemove(slot));

        // One optic of that id out of the bag onto the ground in front.
        public bool ServerDropFromBag(int opticId)
        {
            if (!IsServerInitialized || !OpticBag.TryRemove(ref _bag, opticId)) return false;
            Push();
            Drop(opticId, true);
            return true;
        }

        // Takes the optic (or, for a gear index from Attachments.GearIndex, the attachment) off the weapon in `slot`
        // and returns its id (0 if none); it is not dropped.
        public int ServerRemove(int gearIndex)
        {
            int slot = Attachments.WeaponSlotOf(gearIndex);
            var place = Attachments.SlotOfIndex(gearIndex);
            if (!IsServerInitialized || gearIndex < 0 || slot >= SlotCount) return 0;
            int id = Attachments.Get(_server[slot], place);
            if (id == 0) return 0;
            _server[slot] = Attachments.With(_server[slot], place, 0);
            Push();
            return id;
        }

        // Takes the optic off and puts it on the ground by the combatant. Returns its id (0 if none).
        public int ServerDetach(int slot) => ServerDetach(slot, false);

        int ServerDetach(int slot, bool inFront)
        {
            int id = ServerRemove(slot);
            if (id != 0) Drop(id, inFront);
            return id;
        }

        // Combatant calls this wherever a weapon leaves a slot (dropped, replaced), before it pushes the slot's new
        // weapon. The weapon goes alone (ADR-081); its optic stays in the slot, so a replacing weapon that takes it
        // keeps it (G5, ADR-093). The check in Update moves it to the bag (the ground when full) on the next frame
        // when the slot is now empty or its new weapon refuses it (ADR-082 (4)).
        public void ServerOnWeaponRemoved(int slot) { }

        // Combatant calls this when two weapon slots trade places: their optics trade with them.
        public void ServerOnSlotsSwapped(int a, int b)
        {
            if (!IsServerInitialized || a < 0 || b < 0 || a >= SlotCount || b >= SlotCount || a == b) return;
            int t = _server[a]; _server[a] = _server[b]; _server[b] = t;
            Push();
        }

        // Empties every slot into `into` (the death crate lays them out around the body). Nothing is dropped here.
        public void ServerStripOptics(List<LootEntry> into)
        {
            if (!IsServerInitialized) return;
            for (int i = 0; i < MaxSlots; i++)
            {
                for (int s = 0; s < Attachments.Slots; s++)
                {
                    int id = Attachments.Get(_server[i], (AttachSlot)s);
                    if (id != 0) into?.Add(Optics.ToLoot(id));
                }
                _server[i] = 0;
            }
            for (int id = 1; id <= OpticBag.MaxId; id++)
                for (int n = OpticBag.Count(_bag, id); n > 0; n--) into?.Add(Optics.ToLoot(id));
            _bag = 0;
            Push();
        }

        void Drop(int opticId, bool inFront)
        {
            if (MatchDirector.Instance == null) return;
            var at = inFront ? transform.position + transform.forward * 1.0f + Vector3.up * 0.3f : transform.position + Vector3.up * 0.3f;
            MatchDirector.Instance.ServerSpawnLoot(Optics.ToLoot(opticId), at);
        }

        // ILootReceiver: an optic from the ground (ADR-082). It goes on a weapon that takes it and has none yet
        // (Optics.PickupSlot), else into the bag when there is room — no matching weapon needed. Refused (and left
        // where it lies) only when the bag is full or the id is unknown.
        public bool ServerTryTake(LootEntry loot)
        {
            if (!IsServerInitialized || loot == null || loot.Kind != LootKind.Optic || _combatant.Dead.Value) return false;
            var place = Attachments.SlotOf(loot.Optic);
            if (place == null) return false;
            int count = SlotCount;
            var weapons = new int[count];
            var fitted = new int[count];
            for (int i = 0; i < count; i++) { weapons[i] = WeaponInSlot(i); fitted[i] = Attachments.Get(_server[i], place.Value); }
            int slot = Optics.PickupSlot(weapons, fitted, _combatant.ActiveSlot.Value, loot.Optic);
            if (slot >= 0) return ServerAttach(slot, loot.Optic);
            if (loot.Optic > OpticBag.MaxId || !BagHasRoom() || !OpticBag.TryAdd(ref _bag, loot.Optic)) return false;
            Push();
            return true;
        }

        // ---- Owner requests ----

        // Dropping from the inventory screen: the optic comes off and lands on the ground in front. `slot` is a gear
        // index: a plain weapon slot is its optic, Attachments.GearIndex names the other attachments.
        bool SlotOk(int slot, string what) => (slot >= 0 && slot < Attachments.IndexStride * Attachments.Slots && Game.Core.InputGuard.ValidSlot(Attachments.WeaponSlotOf(slot), SlotCount)) || ServerGuard.Refuse(GuardKind.Slot, _combatant, $"{what}: slot {slot}");

        [ServerRpc]
        public void CmdDropOptic(int slot)
        {
            if (!_combatant.ItemRequestOk("optics DropOptic")) return;
            if (_combatant.Dead.Value || !SlotOk(slot, "drop optic")) return;
            ServerDetach(slot, true);
        }

        [ServerRpc]
        public void CmdDetachToBag(int slot)
        {
            if (!_combatant.ItemRequestOk("optics DetachToBag")) return;
            if (!_combatant.Dead.Value && SlotOk(slot, "detach optic")) ServerDetachToBag(slot);
        }

        [ServerRpc]
        public void CmdAttachFromBag(int opticId, int slot)
        {
            if (!_combatant.ItemRequestOk("optics AttachFromBag")) return;
            if (!_combatant.Dead.Value && SlotOk(slot, "attach optic")) ServerAttachFromBag(opticId, slot);
        }

        [ServerRpc]
        public void CmdDropFromBag(int opticId)
        {
            if (!_combatant.ItemRequestOk("optics DropFromBag")) return;
            if (!_combatant.Dead.Value) ServerDropFromBag(opticId);
        }

        // An optic from the ground onto a chosen weapon slot; the one that was there goes to the bag (or the ground).
        [ServerRpc]
        public void CmdAttachFromGround(NetworkObject lootObject, int slot)
        {
            if (!_combatant.ItemRequestOk("optics AttachFromGround")) return;
            if (_combatant.Dead.Value || lootObject == null || !SlotOk(slot, "optic from ground")) return;
            var item = lootObject.GetComponent<LootItem>();
            if (item == null || !item.IsSpawned || (LootKind)item.Kind.Value != LootKind.Optic) return;
            if (!PickupSight.Reachable(transform.position, _combatant.EyePosition, item.transform, transform, GameData.Rules.Guard.LagSlackMeters))
            {
                ServerGuard.Refuse(GuardKind.Distance, _combatant, "optic pickup");
                return;
            }
            if (!ServerAttach(slot, item.Optic.Value)) return;
            item.Despawn();
        }
    }
}
