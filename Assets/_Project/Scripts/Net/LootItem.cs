using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // A pickup lying in the world. Spawned and removed by the server only.
    public class LootItem : NetworkBehaviour
    {
        public readonly SyncVar<int> Kind = new SyncVar<int>();
        public readonly SyncVar<int> WeaponId = new SyncVar<int>();
        public readonly SyncVar<int> Ammo = new SyncVar<int>();
        public readonly SyncVar<int> Amount = new SyncVar<int>();
        // Armour only: tier 1..3 and what is left of the piece.
        public readonly SyncVar<int> Tier = new SyncVar<int>();
        public readonly SyncVar<float> Durability = new SyncVar<float>();
        // Optics only: the id in OpticRules.
        public readonly SyncVar<int> Optic = new SyncVar<int>();

        public static readonly List<LootItem> All = new List<LootItem>();

        public override void OnStartNetwork() => All.Add(this);
        public override void OnStopNetwork() => All.Remove(this);

        // Scene unloads destroy objects without the network callbacks.
        void OnDestroy() => All.Remove(this);

        // Call before spawning.
        public void Init(LootEntry entry)
        {
            Kind.Value = (int)entry.Kind;
            WeaponId.Value = entry.WeaponId;
            Ammo.Value = (int)entry.Ammo;
            Amount.Value = entry.Amount;
            Tier.Value = entry.Tier;
            Optic.Value = entry.Optic;
            // Armour from a loot table carries no durability: it is fresh. What lies on the ground always says
            // how much is left, so every side can show it.
            Durability.Value = Armor.IsArmor(entry.Kind) ? Armor.FromLoot(entry, ArmorRules.Current).Durability : 0f;
        }

        public LootEntry ToEntry() => new LootEntry
        {
            Kind = (LootKind)Kind.Value,
            WeaponId = WeaponId.Value,
            Ammo = (AmmoType)Ammo.Value,
            Amount = Amount.Value,
            Tier = Tier.Value,
            Durability = Durability.Value,
            Optic = Optic.Value,
        };
    }

    // The one place that names loot for the player (HUD pickup prompt, inventory rows). Korean, generic names.
    public static class LootNames
    {
        public const string None = "없음";

        static readonly string[] TierNames = { "", "1단계", "2단계", "3단계" };
        static readonly string[] HelmetNames = { "헬멧", "헬멧 1단계", "헬멧 2단계", "헬멧 3단계" };
        static readonly string[] VestNames = { "조끼", "조끼 1단계", "조끼 2단계", "조끼 3단계" };

        public static string Ammo(AmmoType type) => AmmoNames.Of(type) + " 탄약";

        // "2단계"; empty for no tier.
        public static string Tier(int tier) =>
            tier >= 0 && tier < TierNames.Length ? TierNames[tier] : tier + "단계";

        public static string Slot(ArmorSlot slot) => slot == ArmorSlot.Helmet ? HelmetNames[0] : VestNames[0];

        // "헬멧 2단계". The three data tiers come from a table, so naming them never allocates.
        public static string OfArmor(ArmorSlot slot, int tier)
        {
            var names = slot == ArmorSlot.Helmet ? HelmetNames : VestNames;
            return tier >= 1 && tier < names.Length ? names[tier] : tier <= 0 ? names[0] : names[0] + " " + Tier(tier);
        }

        // What the thing is, without how much of it.
        public static string Of(LootKind kind, int weaponId, AmmoType ammo, int tier) => Of(kind, weaponId, ammo, tier, 0);

        // "4배율" for an optic; "조준경" when the id is unknown.
        public static string OfOptic(int opticId) => OpticRules.Current.Find(opticId)?.Name ?? "조준경";

        public static string Of(LootKind kind, int weaponId, AmmoType ammo, int tier, int optic)
        {
            switch (kind)
            {
                case LootKind.Weapon: return GameData.Weapons.Find(weaponId)?.Name ?? "무기";
                case LootKind.Ammo: return Ammo(ammo);
                case LootKind.Medkit: return "구급상자";
                case LootKind.Grenade: return "수류탄";
                case LootKind.Helmet: return OfArmor(ArmorSlot.Helmet, tier);
                case LootKind.Vest: return OfArmor(ArmorSlot.Vest, tier);
                case LootKind.Optic: return OfOptic(optic);
                default: return "물건";
            }
        }

        public static string Of(LootItem item) =>
            item == null ? "" : Of((LootKind)item.Kind.Value, item.WeaponId.Value, (AmmoType)item.Ammo.Value, item.Tier.Value, item.Optic.Value);

        public static string Of(LootEntry entry) =>
            entry == null ? "" : Of(entry.Kind, entry.WeaponId, entry.Ammo, entry.Tier, entry.Optic);

        // How much of it: rounds in the magazine, a count, or the durability left. Builds a string: call it when
        // the item changes, not every frame.
        public static string Amount(LootItem item)
        {
            if (item == null) return "";
            var kind = (LootKind)item.Kind.Value;
            if (kind == LootKind.Weapon) return "탄창 " + item.Amount.Value + "발";
            if (Armor.IsArmor(kind)) return "내구도 " + Points(item.Durability.Value);
            if (kind == LootKind.Optic) return Optics.ZoomText(Optics.ZoomOf(item.Optic.Value));
            return "x" + item.Amount.Value;
        }

        // One line for the pickup prompt: "Warden", "5.56mm 탄약 x30", "구급상자", "헬멧 2단계 (내구도 110)".
        public static string Label(LootItem item)
        {
            if (item == null) return "";
            var kind = (LootKind)item.Kind.Value;
            if (kind == LootKind.Ammo) return Of(item) + " x" + item.Amount.Value;
            if (Armor.IsArmor(kind)) return Of(item) + " (내구도 " + Points(item.Durability.Value) + ")";
            if ((kind == LootKind.Medkit || kind == LootKind.Grenade) && item.Amount.Value > 1) return Of(item) + " x" + item.Amount.Value;
            return Of(item);
        }

        // What a combatant wears in a slot, for an inventory row: "2단계  88 / 110", or "없음".
        public static string Worn(CombatantGear gear, ArmorSlot slot)
        {
            if (gear == null || !gear.Has(slot)) return None;
            return Tier(gear.TierOf(slot)) + "  " + Points(gear.DurabilityOf(slot)) + " / " + Points(gear.MaxDurabilityOf(slot));
        }

        // Durability as whole points; anything left shows as at least 1.
        public static int Points(float durability) => durability <= 0f ? 0 : Mathf.Max(1, Mathf.RoundToInt(durability));
    }
}
