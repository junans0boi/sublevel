using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The armour a combatant wears: a helmet (head) and a vest (body), three tiers each (data: ArmorRules).
    // The server owns it: it sits in the combatant's damage path, takes its tier's share of shots and blasts on
    // the zone it covers and wears down by what it took. A helmet is gone at zero; a vest stays on broken, still
    // taking 20% and keeping its carry bonus (ADR-091). Clients read the SyncVars.
    // Armour is picked up like any other loot (Combatant.ServerGive hands unknown kinds to an ILootReceiver).
    [RequireComponent(typeof(Combatant))]
    public class CombatantGear : NetworkBehaviour, ILootReceiver
    {
        public readonly SyncVar<int> HelmetTier = new SyncVar<int>();           // 0 = none
        public readonly SyncVar<float> HelmetDurability = new SyncVar<float>();
        public readonly SyncVar<int> VestTier = new SyncVar<int>();             // 0 = none
        public readonly SyncVar<float> VestDurability = new SyncVar<float>();

        Combatant _combatant;
        Func<DamageInfo, float> _filter;
        // Server truth; the SyncVars mirror it.
        ArmorPiece _helmet, _vest;

        // ---- Read-only, every side (HUD, inventory) ----

        public int TierOf(ArmorSlot slot) => slot == ArmorSlot.Helmet ? HelmetTier.Value : VestTier.Value;
        public float DurabilityOf(ArmorSlot slot) => slot == ArmorSlot.Helmet ? HelmetDurability.Value : VestDurability.Value;
        public float MaxDurabilityOf(ArmorSlot slot) => ArmorRules.Current.MaxDurability(slot, TierOf(slot));
        public bool Has(ArmorSlot slot) => TierOf(slot) > 0;
        // 0..1, for a bar; 0 when nothing is worn.
        public float FractionOf(ArmorSlot slot)
        {
            float max = MaxDurabilityOf(slot);
            return max > 0f ? Mathf.Clamp01(DurabilityOf(slot) / max) : 0f;
        }

        // Server: how much the armour took off the last hit it was asked about (0 when it did not apply), and
        // how many pieces have broken so far. For tests and feedback.
        public float LastAbsorbed { get; private set; }
        public int PiecesBroken { get; private set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _filter = ServerFilterDamage;
        }

        public override void OnStartServer()
        {
            _helmet = _vest = ArmorPiece.None;
            _combatant.ServerDamageFilter = _filter;
            Push();
        }

        public override void OnStopServer()
        {
            if (_combatant != null && _combatant.ServerDamageFilter == _filter) _combatant.ServerDamageFilter = null;
        }

        void Push()
        {
            HelmetTier.Value = _helmet.Tier;
            HelmetDurability.Value = _helmet.Durability;
            VestTier.Value = _vest.Tier;
            VestDurability.Value = _vest.Durability;
        }

        // Combatant.ServerDamageFilter: the damage as dealt in, the damage to apply out.
        float ServerFilterDamage(DamageInfo info)
        {
            LastAbsorbed = 0f;
            if (!Armor.TrySlotFor(info.Zone, info.Cause, out var slot)) return info.Amount;
            ref ArmorPiece piece = ref (slot == ArmorSlot.Helmet ? ref _helmet : ref _vest);
            bool wasWorn = piece.Worn, wasIntact = wasWorn && !piece.Broken;
            float through = Armor.Absorb(ref piece, slot, info.Amount, ArmorRules.Current);
            // A helmet is gone at zero; a vest stays on, broken (ADR-091).
            if (wasIntact && (!piece.Worn || piece.Broken)) PiecesBroken++;
            if (!wasWorn) return info.Amount;
            LastAbsorbed = info.Amount - through;
            Push();
            return through;
        }

        // ---- Server API ----

        // Puts a piece on directly (bots, tests, scripted loadouts); tier 0 takes the slot's piece off.
        // `durability` 0 or less means fresh. Returns the piece that was worn before (None if none); it is not
        // dropped. Checked by hand instead of [Server]: it returns a struct.
        public ArmorPiece ServerEquip(ArmorSlot slot, int tier, float durability = 0f)
        {
            if (!IsServerInitialized) return ArmorPiece.None;
            var before = slot == ArmorSlot.Helmet ? _helmet : _vest;
            var piece = ArmorPiece.Fresh(slot, tier, ArmorRules.Current);
            if (piece.Worn && durability > 0f) piece.Durability = Mathf.Min(durability, piece.Durability);
            if (slot == ArmorSlot.Helmet) _helmet = piece; else _vest = piece;
            Push();
            return before;
        }

        // Takes the slot's piece off and returns it (None if nothing was worn). Used by the death crate.
        public ArmorPiece ServerRemove(ArmorSlot slot) => ServerEquip(slot, 0);

        // ILootReceiver: armour loot. A better piece (higher tier, or the same tier in better shape) replaces the
        // worn one, which goes on the ground; anything else is refused and stays where it lies.
        public bool ServerTryTake(LootEntry loot)
        {
            if (!IsServerInitialized || loot == null || !Armor.IsArmor(loot.Kind) || _combatant.Dead.Value) return false;
            var slot = Armor.SlotOf(loot.Kind);
            var candidate = Armor.FromLoot(loot, ArmorRules.Current);
            var current = slot == ArmorSlot.Helmet ? _helmet : _vest;
            if (!Armor.IsBetter(candidate, current)) return false;
            if (slot == ArmorSlot.Helmet) _helmet = candidate; else _vest = candidate;
            Push();
            if (current.Worn && MatchDirector.Instance != null)
                MatchDirector.Instance.ServerSpawnLoot(Armor.ToLoot(slot, current), transform.position + Vector3.up * 0.3f);
            return true;
        }

        // ---- Owner requests ----

        // Dropping from the inventory screen: the piece comes off and lands on the ground in front.
        [ServerRpc]
        public void CmdDropArmor(int slot)
        {
            if (!_combatant.ItemRequestOk("gear DropArmor")) return;
            if (_combatant.Dead.Value || MatchDirector.Instance == null) return;
            if (slot != (int)ArmorSlot.Helmet && slot != (int)ArmorSlot.Vest) return;
            var piece = ServerRemove((ArmorSlot)slot);
            if (!piece.Worn) return;
            MatchDirector.Instance.ServerSpawnLoot(Armor.ToLoot((ArmorSlot)slot, piece),
                transform.position + transform.forward * 1.0f + Vector3.up * 0.3f);
        }
    }
}
