using System;

namespace Game.Core
{
    public enum LootKind { Weapon = 0, Ammo = 1, Medkit = 2 }

    [Serializable]
    public class LootEntry
    {
        public LootKind Kind;
        public int WeaponId;   // Kind == Weapon
        public AmmoType Ammo;  // Kind == Ammo
        public int Amount = 1; // rounds (Ammo), kits (Medkit), rounds in magazine (Weapon)
        public float Weight = 1f;
    }

    [Serializable]
    public class LootTable
    {
        public LootEntry[] Entries = new LootEntry[0];

        public LootEntry Roll(Random rng)
        {
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
            if (spawnPoints > 0 && plan[0].Kind != LootKind.Weapon)
            {
                foreach (var e in Entries)
                    if (e.Kind == LootKind.Weapon) { plan[0] = e; break; }
            }
            return plan;
        }
    }
}
