using System;

namespace Game.Core
{
    // How much a combatant can carry (PUBG_GAP 4-3 batch H): every item has a size in points, the bag holds a
    // capacity of them (more with a vest). All arithmetic is done in tenths of a point so sums are exact.
    [Serializable]
    public class CarryRules
    {
        // The rules in use. Defaults until the data loader replaces them, so everything runs without a data file.
        public static CarryRules Current = new CarryRules();

        public float Capacity = 60f;
        // Extra capacity by vest tier.
        public float VestBonus1 = 10f;
        public float VestBonus2 = 20f;
        public float VestBonus3 = 30f;

        // Size per round, by calibre.
        public float Ammo9mm = 0.2f;
        public float Ammo556 = 0.3f;
        public float Ammo12ga = 0.8f;
        public float Ammo762 = 0.5f;
        // Size per item.
        public float Bandage = 1f;
        public float FirstAid = 3f;
        public float Medkit = 6f;
        public float Grenade = 3f;
        public float Smoke = 3f;

        // Ammo on the ground this close is picked up by walking over it (when it fits and a weapon uses it).
        public float AutoPickupMeters = 1.5f;
        public float AutoPickupInterval = 0.25f;
    }

    public static class Carry
    {
        public const int Scale = 10; // tenths of a point

        public static int Tenths(float points) => (int)Math.Round(points * Scale);

        // Whole points for display; a started point counts ("23/60").
        public static int Points(int tenths) => tenths <= 0 ? 0 : (tenths + Scale - 1) / Scale;

        public static int CapacityTenths(CarryRules rules, int vestTier)
        {
            float bonus = vestTier == 1 ? rules.VestBonus1 : vestTier == 2 ? rules.VestBonus2 : vestTier == 3 ? rules.VestBonus3 : 0f;
            return Tenths(rules.Capacity + bonus);
        }

        public static int AmmoUnitTenths(AmmoType type, CarryRules rules)
        {
            switch (type)
            {
                case AmmoType.Pistol9: return Tenths(rules.Ammo9mm);
                case AmmoType.Rifle556: return Tenths(rules.Ammo556);
                case AmmoType.Shotgun12: return Tenths(rules.Ammo12ga);
                case AmmoType.Rifle762: return Tenths(rules.Ammo762);
                default: return 0;
            }
        }

        // The size of one unit of a loot kind (a round, an item); 0 for what takes no bag room (weapons, armour).
        public static int UnitTenths(LootKind kind, AmmoType ammo, CarryRules rules)
        {
            if (kind == LootKind.Ammo) return AmmoUnitTenths(ammo, rules);
            if (kind == LootKind.Medkit) return Tenths(rules.Medkit);
            if (kind == ThrowableRules.GrenadeLootKind) return Tenths(rules.Grenade);
            if (kind == ThrowableRules.SmokeLootKind) return Tenths(rules.Smoke);
            if (kind == HealRules.BandageLootKind) return Tenths(rules.Bandage);
            if (kind == HealRules.FirstAidLootKind) return Tenths(rules.FirstAid);
            return 0;
        }

        public static int UsedTenths(CarryRules rules, int pistol9, int rifle556, int shotgun12, int rifle762,
            int bandages, int firstAids, int medkits, int grenades, int smokes)
        {
            long total = (long)pistol9 * AmmoUnitTenths(AmmoType.Pistol9, rules)
                + (long)rifle556 * AmmoUnitTenths(AmmoType.Rifle556, rules)
                + (long)shotgun12 * AmmoUnitTenths(AmmoType.Shotgun12, rules)
                + (long)rifle762 * AmmoUnitTenths(AmmoType.Rifle762, rules)
                + (long)bandages * Tenths(rules.Bandage)
                + (long)firstAids * Tenths(rules.FirstAid)
                + (long)medkits * Tenths(rules.Medkit)
                + (long)grenades * Tenths(rules.Grenade)
                + (long)smokes * Tenths(rules.Smoke);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        // How many of `wanted` units of size `unitTenths` fit into `freeTenths` of room. Units that take no room
        // always fit.
        public static int Fit(int freeTenths, int unitTenths, int wanted)
        {
            if (wanted <= 0) return 0;
            if (unitTenths <= 0) return wanted;
            if (freeTenths <= 0) return 0;
            return Math.Min(wanted, freeTenths / unitTenths);
        }
    }
}
