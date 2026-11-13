using System;

namespace Game.Core
{
    // The two pieces of armour a combatant can wear. The helmet covers the head, the vest the body; limbs are bare.
    public enum ArmorSlot { Helmet = 0, Vest = 1 }

    // One tier of one piece. Loaded from data.
    [Serializable]
    public class ArmorTier
    {
        public float Reduction;   // share of a hit on the protected zone that the piece takes (0..1)
        public float Durability;  // damage the piece can take in total before it breaks
    }

    [Serializable]
    public class ArmorRules
    {
        // The rules in use. Defaults until the data loader replaces them, so everything runs without a data file.
        public static ArmorRules Current = new ArmorRules();

        // Index 0 is tier 1. PUBG's numbers (ADR-091): health is 100 in both games.
        public ArmorTier[] Helmets =
        {
            new ArmorTier { Reduction = 0.30f, Durability = 80f },
            new ArmorTier { Reduction = 0.40f, Durability = 150f },
            new ArmorTier { Reduction = 0.55f, Durability = 230f },
        };
        public ArmorTier[] Vests =
        {
            new ArmorTier { Reduction = 0.30f, Durability = 200f },
            new ArmorTier { Reduction = 0.40f, Durability = 220f },
            new ArmorTier { Reduction = 0.55f, Durability = 250f },
        };
        // A vest at zero durability stays on and still takes this share of every hit, wearing no further (ADR-091).
        // A helmet at zero is gone.
        public float BrokenVestReduction = 0.20f;

        public int TierCount(ArmorSlot slot) => (slot == ArmorSlot.Helmet ? Helmets : Vests).Length;

        // Null when there is no such tier (0 = nothing worn).
        public ArmorTier Find(ArmorSlot slot, int tier)
        {
            var tiers = slot == ArmorSlot.Helmet ? Helmets : Vests;
            return tier >= 1 && tier <= tiers.Length ? tiers[tier - 1] : null;
        }

        public float MaxDurability(ArmorSlot slot, int tier)
        {
            var def = Find(slot, tier);
            return def != null ? def.Durability : 0f;
        }
    }

    // A piece as worn or lying on the ground: its tier and what is left of it. Tier 0 = none. A vest can be worn
    // broken (durability 0, ADR-091); a helmet never is.
    public struct ArmorPiece
    {
        public int Tier;
        public float Durability;

        public readonly bool Worn => Tier > 0;
        public readonly bool Broken => Tier > 0 && Durability < Armor.BrokenBelow;

        public static ArmorPiece None => default;

        // A fresh piece of that tier (none when the tier does not exist).
        public static ArmorPiece Fresh(ArmorSlot slot, int tier, ArmorRules rules)
        {
            var def = rules.Find(slot, tier);
            return def == null ? None : new ArmorPiece { Tier = tier, Durability = def.Durability };
        }
    }

    public static class Armor
    {
        // Less than this left counts as broken (float dust after many hits).
        public const float BrokenBelow = 1e-3f;

        // Which piece stands between a hit and the body. Only shots and blasts are stopped by armour: falls and
        // the zone hurt regardless. Limbs are unprotected.
        public static bool TrySlotFor(HitZone zone, DamageCause cause, out ArmorSlot slot)
        {
            slot = ArmorSlot.Vest;
            if (cause != DamageCause.Shot && cause != DamageCause.Blast) return false;
            if (zone == HitZone.Head) { slot = ArmorSlot.Helmet; return true; }
            return zone == HitZone.Body;
        }

        // Takes one hit of `amount` on the zone the piece protects. Returns what gets through to the wearer.
        // The piece takes its tier's share of the hit, but never more than it has left, and loses that much
        // durability. At zero a helmet is gone (tier 0); a vest stays on, broken, and from then on takes
        // BrokenVestReduction of every hit without wearing further (ADR-091).
        public static float Absorb(ref ArmorPiece piece, ArmorSlot slot, float amount, ArmorRules rules)
        {
            // Written so that NaN passes through untouched.
            if (!(amount > 0f) || !piece.Worn) return amount;
            var def = rules.Find(slot, piece.Tier);
            if (def == null) return amount;
            if (piece.Broken)
            {
                if (slot == ArmorSlot.Vest) return amount * (1f - Clamp01(rules.BrokenVestReduction));
                piece = ArmorPiece.None;
                return amount;
            }
            float absorbed = Math.Min(amount * Clamp01(def.Reduction), piece.Durability);
            piece.Durability -= absorbed;
            if (piece.Durability < BrokenBelow)
            {
                if (slot == ArmorSlot.Vest) piece.Durability = 0f;
                else piece = ArmorPiece.None;
            }
            return amount - absorbed;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        // Worth swapping for: a higher tier, or the same tier in better shape. Anything beats wearing nothing, any
        // whole piece beats a broken vest, and one broken vest is no better than another (both take the same 20%).
        public static bool IsBetter(ArmorPiece candidate, ArmorPiece current)
        {
            if (!candidate.Worn) return false;
            if (!current.Worn) return true;
            if (candidate.Broken != current.Broken) return current.Broken;
            if (candidate.Broken) return false;
            if (candidate.Tier != current.Tier) return candidate.Tier > current.Tier;
            return candidate.Durability > current.Durability;
        }

        public static bool IsArmor(LootKind kind) => kind == LootKind.Helmet || kind == LootKind.Vest;

        public static ArmorSlot SlotOf(LootKind kind) => kind == LootKind.Helmet ? ArmorSlot.Helmet : ArmorSlot.Vest;

        public static LootKind KindOf(ArmorSlot slot) => slot == ArmorSlot.Helmet ? LootKind.Helmet : LootKind.Vest;

        // The piece a loot entry stands for. Durability 0 or less in the entry means "fresh" (loot tables);
        // more than the tier holds is cut down to it. None when the entry is not armour or names no real tier.
        public static ArmorPiece FromLoot(LootEntry loot, ArmorRules rules)
        {
            if (loot == null || !IsArmor(loot.Kind)) return ArmorPiece.None;
            var slot = SlotOf(loot.Kind);
            var def = rules.Find(slot, loot.Tier);
            if (def == null) return ArmorPiece.None;
            float durability = loot.Durability > 0f ? Math.Min(loot.Durability, def.Durability) : def.Durability;
            return new ArmorPiece { Tier = loot.Tier, Durability = durability };
        }

        // A broken vest is written as a sliver of durability: 0 in an entry means "fresh".
        public static LootEntry ToLoot(ArmorSlot slot, ArmorPiece piece) =>
            new LootEntry { Kind = KindOf(slot), Tier = piece.Tier, Durability = piece.Broken ? BrokenBelow * 0.5f : piece.Durability, Amount = 1 };
    }
}
