using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The optic fitted to each weapon slot (ADR-056 / ADR-058). The optic belongs to the weapon in that slot: when
    // the weapon leaves the slot the optic goes with it (Combatant calls ServerOnWeaponRemoved / ServerOnSlotsSwapped;
    // an optic that no longer fits its slot's weapon is also dropped on the ground by a server-side check).
    // The server owns it; clients read the SyncVars. Picked up like any other loot (ILootReceiver).
    [RequireComponent(typeof(Combatant))]
    public class CombatantOptics : NetworkBehaviour, ILootReceiver
    {
        // Storage for this many weapon slots; slots beyond Loadout.SlotCount stay empty.
        public const int MaxSlots = 5;

        // One id per weapon slot (0 = none). Plain SyncVars, like the other per-slot values.
        public readonly SyncVar<int> Optic0 = new SyncVar<int>();
        public readonly SyncVar<int> Optic1 = new SyncVar<int>();
        public readonly SyncVar<int> Optic2 = new SyncVar<int>();
        public readonly SyncVar<int> Optic3 = new SyncVar<int>();
        public readonly SyncVar<int> Optic4 = new SyncVar<int>();
        // ADR-082: optics loose in the bag, packed by OpticBag (count per optic id).
        public readonly SyncVar<ulong> Bag = new SyncVar<ulong>();

        Combatant _combatant;
        // Server truth; the SyncVars mirror it.
        readonly int[] _server = new int[MaxSlots];
        ulong _bag;
        CombatantCarry _carry;

        static int SlotCount => Mathf.Min(Loadout.SlotCount, MaxSlots);

        // ---- Read-only, every side (HUD, inventory, visuals) ----

        public int OpticInSlot(int slot)
        {
            switch (slot)
            {
                case 0: return Optic0.Value;
                case 1: return Optic1.Value;
                case 2: return Optic2.Value;
                case 3: return Optic3.Value;
                case 4: return Optic4.Value;
                default: return 0;
            }
        }

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
                if (_server[i] == 0) continue;
                if (!Optics.CanAttach(WeaponInSlot(i), _server[i])) ServerStash(ServerRemove(i));
            }
        }

        void Push()
        {
            Optic0.Value = _server[0];
            Optic1.Value = _server[1];
            Optic2.Value = _server[2];
            Optic3.Value = _server[3];
            Optic4.Value = _server[4];
            Bag.Value = _bag;
        }

        // ---- Server API ----

        // Fits an optic to the weapon in `slot`. An optic already there is replaced and goes to the bag (the ground
        // when the bag is full). False when the slot has no weapon or the weapon does not take that optic.
        public bool ServerAttach(int slot, int opticId)
        {
            if (!IsServerInitialized || slot < 0 || slot >= SlotCount) return false;
            if (!Optics.CanAttach(WeaponInSlot(slot), opticId)) return false;
            int before = _server[slot];
            _server[slot] = opticId;
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

        // Takes the optic off the weapon in `slot` and returns its id (0 if none); it is not dropped.
        public int ServerRemove(int slot)
        {
            if (!IsServerInitialized || slot < 0 || slot >= SlotCount) return 0;
            int id = _server[slot];
            if (id == 0) return 0;
            _server[slot] = 0;
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

        // Combatant calls this wherever a weapon leaves a slot (dropped, replaced): the weapon goes alone, its optic
        // to the bag (the ground when full) (ADR-082).
        public void ServerOnWeaponRemoved(int slot) => ServerDetachToBag(slot);

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
                if (_server[i] == 0) continue;
                into?.Add(Optics.ToLoot(_server[i]));
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
            int count = SlotCount;
            var weapons = new int[count];
            var optics = new int[count];
            for (int i = 0; i < count; i++) { weapons[i] = WeaponInSlot(i); optics[i] = _server[i]; }
            int slot = Optics.PickupSlot(weapons, optics, _combatant.ActiveSlot.Value, loot.Optic);
            if (slot >= 0) return ServerAttach(slot, loot.Optic);
            if (OpticRules.Current.Find(loot.Optic) == null || !BagHasRoom() || !OpticBag.TryAdd(ref _bag, loot.Optic)) return false;
            Push();
            return true;
        }

        // ---- Owner requests ----

        // Dropping from the inventory screen: the optic comes off and lands on the ground in front.
        [ServerRpc]
        public void CmdDropOptic(int slot)
        {
            if (_combatant.Dead.Value) return;
            ServerDetach(slot, true);
        }

        [ServerRpc]
        public void CmdDetachToBag(int slot)
        {
            if (!_combatant.Dead.Value) ServerDetachToBag(slot);
        }

        [ServerRpc]
        public void CmdAttachFromBag(int opticId, int slot)
        {
            if (!_combatant.Dead.Value) ServerAttachFromBag(opticId, slot);
        }

        [ServerRpc]
        public void CmdDropFromBag(int opticId)
        {
            if (!_combatant.Dead.Value) ServerDropFromBag(opticId);
        }

        // An optic from the ground onto a chosen weapon slot; the one that was there goes to the bag (or the ground).
        [ServerRpc]
        public void CmdAttachFromGround(NetworkObject lootObject, int slot)
        {
            if (_combatant.Dead.Value || lootObject == null) return;
            var item = lootObject.GetComponent<LootItem>();
            if (item == null || !item.IsSpawned || (LootKind)item.Kind.Value != LootKind.Optic) return;
            if ((item.transform.position - transform.position).sqrMagnitude > Combatant.PickupRange * Combatant.PickupRange) return;
            if (!ServerAttach(slot, item.Optic.Value)) return;
            item.Despawn();
        }
    }
}
