using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum LootKind { Weapon = 0, Ammo = 1, Medkit = 2, Helmet = 3, Vest = 4, Grenade = 10, Optic = 11, Key = 12, Bandage = 13, FirstAidKit = 14, Smoke = 15, Jerrycan = 16, Backpack = 17, Drink = 18, Pill = 19, Syringe = 20, Flash = 21, Molotov = 22, GasFilter = 23, Antidote = 24 }

    [Serializable]
    public class LootEntry
    {
        public LootKind Kind;
        public int WeaponId;   // Kind == Weapon
        public AmmoType Ammo;  // Kind == Ammo
        public int Amount = 1; // rounds (Ammo), kits (Medkit), grenades (Grenade), rounds in magazine (Weapon)
        public int Tier;       // Kind == Helmet / Vest / Backpack: 1..3
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

        // What a spawn point of a role (ADR-256) may hold. W weapon, A ammunition, H healing and boosts, G protection, X anything.
        public static bool Fits(LootKind kind, char role)
        {
            switch (role)
            {
                case 'W': return kind == LootKind.Weapon;
                case 'A': return kind == LootKind.Ammo;
                case 'H': return kind == LootKind.Medkit || kind == LootKind.Bandage || kind == LootKind.FirstAidKit || kind == LootKind.Drink || kind == LootKind.Pill || kind == LootKind.Syringe;
                case 'G': return kind == LootKind.Helmet || kind == LootKind.Vest || kind == LootKind.Backpack;
                default: return true;
            }
        }

        public LootEntry RollRole(Random rng, char role)
        {
            float total = 0f;
            foreach (var e in Entries) if (Fits(e.Kind, role)) total += e.Weight;
            if (total <= 0f) return Roll(rng);
            double pick = rng.NextDouble() * total;
            foreach (var e in Entries)
            {
                if (!Fits(e.Kind, role)) continue;
                pick -= e.Weight;
                if (pick < 0) return e;
            }
            return Roll(rng);
        }

        // One role letter per spawn point (the builder bakes them into the spawn names, see LootSpawnName).
        public LootEntry[] PlanByRole(IReadOnlyList<char> roles, int seed)
        {
            var rng = new Random(seed);
            var plan = new LootEntry[roles.Count];
            for (int i = 0; i < plan.Length; i++) plan[i] = RollRole(rng, roles[i]);
            return plan;
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

    // Spawn point names carry the role and the region: "LootSpawn_W3_17" = weapon point in region 3.
    public static class LootSpawnName
    {
        public static string Make(string prefix, char role, int region, int index) => $"{prefix}_{role}{region}_{index}";

        public static bool Parse(string name, out char role, out int region)
        {
            role = 'X'; region = -1;
            int u = name.IndexOf('_');
            if (u < 0 || u + 2 >= name.Length) return false;
            role = name[u + 1];
            int end = name.IndexOf('_', u + 1);
            return end > u + 2 && int.TryParse(name.Substring(u + 2, end - u - 2), out region);
        }
    }

    // Regional guarantee on top of the random roll (ADR-255): a kind (the jerrycan, ADR-200/ADR-221) that every region must offer at least `minPerRegion` times.
    public static class LootGuarantee
    {
        // Changes `plan` in place. regionOf[i] is the region of spawn point i (-1: none). Points holding a weapon (the farming guarantee, index 0) and points that
        // already hold the kind are left alone; a region short of the kind gets random other points turned into it, chosen by the seed. Returns how many were turned.
        public static int Apply(LootEntry[] plan, IReadOnlyList<int> regionOf, int regions, LootTable table, LootKind kind, int minPerRegion, int seed)
        {
            LootEntry entry = null;
            foreach (var e in table.Entries) if (e.Kind == kind) { entry = e; break; }
            if (entry == null || plan == null || regionOf == null || minPerRegion <= 0) return 0;
            var rng = new Random(unchecked(seed + 7919));
            int turned = 0;
            for (int r = 0; r < regions; r++)
            {
                int have = 0;
                var candidates = new List<int>();
                for (int i = 0; i < plan.Length && i < regionOf.Count; i++)
                {
                    if (regionOf[i] != r) continue;
                    if (plan[i] != null && plan[i].Kind == kind) have++;
                    else if (plan[i] == null || plan[i].Kind != LootKind.Weapon) candidates.Add(i);
                }
                while (have < minPerRegion && candidates.Count > 0)
                {
                    int pick = rng.Next(candidates.Count);
                    plan[candidates[pick]] = entry;
                    candidates.RemoveAt(pick);
                    have++; turned++;
                }
            }
            return turned;
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
