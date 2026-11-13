using System;

namespace Game.Core
{
    public enum LootKind { Weapon = 0, Ammo = 1, Medkit = 2, Helmet = 3, Vest = 4, Grenade = 10, Optic = 11, Key = 12, Bandage = 13, FirstAidKit = 14, Smoke = 15 }

    [Serializable]
    public class LootEntry
    {
        public LootKind Kind;
        public int WeaponId;   // Kind == Weapon
        public AmmoType Ammo;  // Kind == Ammo
        public int Amount = 1; // rounds (Ammo), kits (Medkit), grenades (Grenade), rounds in magazine (Weapon)
        public int Tier;       // Kind == Helmet / Vest: 1..3
        public float Durability; // Kind == Helmet / Vest: what is left of the piece; 0 = fresh (full for its tier)
        public int Optic;      // Kind == Optic: id in OpticRules (1..8)
        public float Weight = 1f;
    }

    [Serializable]
    public class LootTable
    {
        public LootEntry[] Entries = new LootEntry[0];

        // Null when the table is empty.
        public LootEntry Roll(Random rng)
        {
            if (Entries.Length == 0) return null;
            float total = 0f;
            foreach (var e in Entries) total += e.Weight;
            double pick = rng.NextDouble() * total;
            foreach (var e in Entries)
            {
                pick -= e.Weight;
                if (pick < 0) return e;
            }
            return Entries[Entries.Length - 1];
        }

        // Deterministic loot for a set of spawn points. The first point always holds a weapon so farming is possible.
        public LootEntry[] Plan(int spawnPoints, int seed)
        {
            var rng = new Random(seed);
            var plan = new LootEntry[spawnPoints];
            for (int i = 0; i < spawnPoints; i++) plan[i] = Roll(rng);
            if (spawnPoints > 0 && plan[0] != null && plan[0].Kind != LootKind.Weapon)
            {
                foreach (var e in Entries)
                    if (e.Kind == LootKind.Weapon) { plan[0] = e; break; }
            }
            return plan;
        }
    }

    // What a dead combatant leaves behind (death crate): everything it carried, laid out in a ring around the
    // body as ordinary pickups, under a marker box that goes away after a while.
    [Serializable]
    public class CrateRules
    {
        // The rules in use. Defaults until the data loader replaces them, so everything runs without a data file.
        public static CrateRules Current = new CrateRules();

        public float RingRadius = 0.9f;      // metres from the body to each item
        public float LiftMeters = 0.3f;      // items lie this far above the ground, like any other pickup
        public float WallClearance = 0.25f;  // an item is pulled in this far from a wall the ring would cross
        public float MarkerSeconds = 90f;    // how long the marker box stays over the body
    }

    public static class LootScatter
    {
        // Where item `index` of `count` lies around the centre: evenly spaced on a circle, the first one straight
        // ahead (+z), going clockwise seen from above. A single item still lies on the ring, not on the body.
        public static void RingOffset(int index, int count, float radius, out float x, out float z)
        {
            double angle = count > 0 ? index * (Math.PI * 2.0 / count) : 0.0;
            x = (float)(Math.Sin(angle) * radius);
            z = (float)(Math.Cos(angle) * radius);
        }
    }
}
