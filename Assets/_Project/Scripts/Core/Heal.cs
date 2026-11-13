using System;

namespace Game.Core
{
    // The healing items (PUBG_GAP 4-3 batch H). Using one takes time; only a finished use heals and consumes it.
    public enum HealKind { None = 0, Bandage = 1, FirstAid = 2, Medkit = 3, Drink = 4, Pill = 5, Syringe = 6 }

    [Serializable]
    public class HealItemRules
    {
        public float Seconds = 3f;  // how long the use takes
        public float Amount = 12f;  // health given when it finishes
        public float MaxHp = 75f;   // it heals only up to here (a medkit: to full)
        public float Boost = 0f;    // > 0: a boost item; finishing it adds this much to the boost gauge instead of healing (ADR-099)

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

        // Loot kinds of the new pickups. The medkit keeps LootKind.Medkit.
        public const LootKind BandageLootKind = LootKind.Bandage;
        public const LootKind FirstAidLootKind = LootKind.FirstAidKit;
        public const string BandageName = "붕대";
        public const string FirstAidName = "응급키트";
        public const string MedkitName = "구급상자";
        // Boost items (ADR-099): original names.
        public const string DrinkName = "활력 음료";
        public const string PillName = "집중 정제";
        public const string SyringeName = "각성 주사";

        public HealItemRules Bandage = new HealItemRules(4f, 10f, 75f);   // repeats by itself up to 75 (ADR-099)
        public HealItemRules FirstAid = new HealItemRules(6f, 75f, 75f); // "up to 75"
        public HealItemRules Medkit = new HealItemRules(8f, 100f, 100f);
        public HealItemRules Drink = new HealItemRules(4f, 0f, 0f) { Boost = 40f };
        public HealItemRules Pill = new HealItemRules(6f, 0f, 0f) { Boost = 60f };
        public HealItemRules Syringe = new HealItemRules(6f, 0f, 0f) { Boost = 100f };
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
                case HealKind.Drink: return rules.Drink;
                case HealKind.Pill: return rules.Pill;
                case HealKind.Syringe: return rules.Syringe;
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
                case HealKind.Drink: return HealRules.DrinkName;
                case HealKind.Pill: return HealRules.PillName;
                case HealKind.Syringe: return HealRules.SyringeName;
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
            if (loot == LootKind.Drink) return HealKind.Drink;
            if (loot == LootKind.Pill) return HealKind.Pill;
            if (loot == LootKind.Syringe) return HealKind.Syringe;
            return HealKind.None;
        }

        public static bool IsBoost(HealKind kind) => kind == HealKind.Drink || kind == HealKind.Pill || kind == HealKind.Syringe;

        // The loot kind of a healing kind.
        public static LootKind LootOf(HealKind kind)
        {
            switch (kind)
            {
                case HealKind.Bandage: return HealRules.BandageLootKind;
                case HealKind.FirstAid: return HealRules.FirstAidLootKind;
                case HealKind.Medkit: return LootKind.Medkit;
                case HealKind.Drink: return LootKind.Drink;
                case HealKind.Pill: return LootKind.Pill;
                default: return LootKind.Syringe;
            }
        }

        // A boost item can be drunk at any health while the gauge has room; the others need health room.
        public static bool CanUseBoost(HealKind kind, float hp, float gauge, HealRules rules, BoostRules boost)
        {
            var item = For(kind, rules);
            return item != null && item.Boost > 0f && hp > 0f && gauge < boost.MaxGauge;
        }

        // The boost item the J key drinks: the smallest one that is carried.
        public static HealKind BestBoost(int drinks, int pills, int syringes) =>
            drinks > 0 ? HealKind.Drink : pills > 0 ? HealKind.Pill : syringes > 0 ? HealKind.Syringe : HealKind.None;

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
