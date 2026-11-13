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

        Combatant _combatant;
        // Server truth; the SyncVars mirror it.
        readonly int[] _server = new int[MaxSlots];

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

        // Weapon in a slot, as replicated. Wiring: when Combatant gets an accessor for any slot, call it here.
        int WeaponInSlot(int slot)
        {
            if (slot == 0) return _combatant.Slot0.Value;
            if (slot == 1) return _combatant.Slot1.Value;
            return 0;
        }

        void Awake() => _combatant = GetComponent<Combatant>();

        public override void OnStartServer()
        {
            for (int i = 0; i < MaxSlots; i++) _server[i] = 0;
            Push();
        }

        void Update()
        {
            if (!IsServerInitialized) return;
            // A weapon that left its slot without a word (or was swapped for one that does not take the optic)
            // leaves its optic on the ground instead of keeping it invisibly.
            int count = SlotCount;
            for (int i = 0; i < count; i++)
            {
                if (_server[i] == 0) continue;
                if (!Optics.CanAttach(WeaponInSlot(i), _server[i])) ServerDetach(i);
            }
        }

        void Push()
        {
            Optic0.Value = _server[0];
            Optic1.Value = _server[1];
            Optic2.Value = _server[2];
            Optic3.Value = _server[3];
            Optic4.Value = _server[4];
        }

        // ---- Server API ----

        // Fits an optic to the weapon in `slot`. An optic already there is replaced and lands on the ground.
        // False when the slot has no weapon or the weapon does not take that optic.
        public bool ServerAttach(int slot, int opticId)
        {
            if (!IsServerInitialized || slot < 0 || slot >= SlotCount) return false;
            if (!Optics.CanAttach(WeaponInSlot(slot), opticId)) return false;
            int before = _server[slot];
            _server[slot] = opticId;
            Push();
            if (before != 0) Drop(before, false);
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

        // Combatant calls this wherever a weapon leaves a slot (dropped, replaced, stripped): its optic goes too.
        public void ServerOnWeaponRemoved(int slot) => ServerDetach(slot, false);

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
            Push();
        }

        void Drop(int opticId, bool inFront)
        {
            if (MatchDirector.Instance == null) return;
            var at = inFront ? transform.position + transform.forward * 1.0f + Vector3.up * 0.3f : transform.position + Vector3.up * 0.3f;
            MatchDirector.Instance.ServerSpawnLoot(Optics.ToLoot(opticId), at);
        }

        // ILootReceiver: an optic from the ground. It goes on the weapon in hand when that weapon takes it and has
        // no optic yet, else on the first other weapon that does; otherwise it is refused and stays where it lies
        // (the inventory can swap one in on purpose).
        public bool ServerTryTake(LootEntry loot)
        {
            if (!IsServerInitialized || loot == null || loot.Kind != LootKind.Optic || _combatant.Dead.Value) return false;
            int count = SlotCount;
            int active = _combatant.ActiveSlot.Value;
            if (active >= 0 && active < count && CanTake(active, loot.Optic)) return ServerAttach(active, loot.Optic);
            for (int i = 0; i < count; i++)
                if (i != active && CanTake(i, loot.Optic)) return ServerAttach(i, loot.Optic);
            return false;
        }

        bool CanTake(int slot, int opticId) => _server[slot] == 0 && Optics.CanAttach(WeaponInSlot(slot), opticId);

        // ---- Owner requests ----

        // Dropping from the inventory screen: the optic comes off and lands on the ground in front.
        [ServerRpc]
        public void CmdDropOptic(int slot)
        {
            if (_combatant.Dead.Value) return;
            ServerDetach(slot, true);
        }

        // An optic from the ground onto a chosen weapon slot; the one that was there lands on the ground.
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
