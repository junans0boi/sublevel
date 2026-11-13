using System;

namespace Game.Core
{
    // The healing items (PUBG_GAP 4-3 batch H). Using one takes time; only a finished use heals and consumes it.
    public enum HealKind { None = 0, Bandage = 1, FirstAid = 2, Medkit = 3 }

    [Serializable]
    public class HealItemRules
    {
        public float Seconds = 3f;  // how long the use takes
        public float Amount = 12f;  // health given when it finishes
        public float MaxHp = 75f;   // it heals only up to here (a medkit: to full)

        public HealItemRules() { }
        public HealItemRules(float seconds, float amount, float maxHp)
        {
            Seconds = seconds;
            Amount = amount;
            MaxHp = maxHp;
        }
    }

    [Serializable]
    public class HealRules
    {
        // The rules in use. Defaults until the data loader replaces them, so everything runs without a data file.
        public static HealRules Current = new HealRules();

        // Loot kinds of the new pickups. Core/Loot.cs belongs to another batch, so the values are fixed here, well
        // away from the kinds being added there; the enum gets the matching names on integration
        // (`Bandage = 13, FirstAidKit = 14`). The medkit keeps LootKind.Medkit.
        public const LootKind BandageLootKind = (LootKind)13;
        public const LootKind FirstAidLootKind = (LootKind)14;
        public const string BandageName = "붕대";
        public const string FirstAidName = "응급키트";
        public const string MedkitName = "구급상자";

        public HealItemRules Bandage = new HealItemRules(3f, 12f, 75f);
        public HealItemRules FirstAid = new HealItemRules(6f, 40f, 75f);
        public HealItemRules Medkit = new HealItemRules(8f, 100f, 100f);
        public float MoveFactor = 0.6f; // walking speed while using an item
    }

    public static class Healing
    {
        public static HealItemRules For(HealKind kind, HealRules rules)
        {
            switch (kind)
            {
                case HealKind.Bandage: return rules.Bandage;
                case HealKind.FirstAid: return rules.FirstAid;
                case HealKind.Medkit: return rules.Medkit;
                default: return null;
            }
        }

        public static string NameOf(HealKind kind)
        {
            switch (kind)
            {
                case HealKind.Bandage: return HealRules.BandageName;
                case HealKind.FirstAid: return HealRules.FirstAidName;
                case HealKind.Medkit: return HealRules.MedkitName;
                default: return "";
            }
        }

        public static float SecondsOf(HealKind kind, HealRules rules) => For(kind, rules)?.Seconds ?? 0f;

        // The kind a loot kind stands for (None when it is not a healing pickup).
        public static HealKind KindOfLoot(LootKind loot)
        {
            if (loot == HealRules.BandageLootKind) return HealKind.Bandage;
            if (loot == HealRules.FirstAidLootKind) return HealKind.FirstAid;
            if (loot == LootKind.Medkit) return HealKind.Medkit;
            return HealKind.None;
        }

        // Health this item may bring a combatant at `hp` up to.
        public static bool CanUse(HealKind kind, float hp, HealRules rules)
        {
            var item = For(kind, rules);
            return item != null && hp > 0f && hp < item.MaxHp;
        }

        // What finishing the item would give at `hp`: its amount, but never past its own ceiling.
        public static float Amount(HealKind kind, float hp, HealRules rules)
        {
            var item = For(kind, rules);
            if (item == null || hp <= 0f) return 0f;
            return Math.Max(0f, Math.Min(item.Amount, item.MaxHp - hp));
        }

        // The item the H key uses: a bandage while health is under its ceiling, then the first-aid kit, then the
        // medkit; above the small items' ceiling only the medkit helps. None when nothing fits.
        public static HealKind BestFit(float hp, int bandages, int firstAids, int medkits, HealRules rules)
        {
            if (bandages > 0 && CanUse(HealKind.Bandage, hp, rules)) return HealKind.Bandage;
            if (firstAids > 0 && CanUse(HealKind.FirstAid, hp, rules)) return HealKind.FirstAid;
            if (medkits > 0 && CanUse(HealKind.Medkit, hp, rules)) return HealKind.Medkit;
            return HealKind.None;
        }

        // 0..1 of a use, for the ring.
        public static float Progress01(double now, double start, float duration)
        {
            if (duration <= 0f) return 1f;
            double p = (now - start) / duration;
            return p <= 0.0 ? 0f : p >= 1.0 ? 1f : (float)p;
        }
    }
}
