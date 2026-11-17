using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The pure rules of farming (ADR-294, #373): which building a bot goes to, in what order it walks its loot points,
    // and when it counts as ready. The controller feeds it the world and walks where it says.
    public static class BotFarm
    {
        // A gun with FarmMags magazines of rounds, FarmArmorPieces worn, FarmHealItems carried.
        public static bool Ready(bool hasGun, int rounds, int magSize, int armorPieces, int healItems, BotRules rules)
        {
            if (!hasGun || magSize <= 0) return false;
            return rounds >= magSize * Math.Max(1, rules.FarmMags) && armorPieces >= rules.FarmArmorPieces && healItems >= rules.FarmHealItems;
        }

        // The building to farm from (x, z): near, rich for its kind (LootPlacement.KindWeight), not visited, and not already chosen by
        // FarmBuildingCap other bots (teammates do not count against it). `others(b)` = bots of other teams that chose building b.
        // The random jitter spreads bots that start side by side. -1 when there is no building left with loot.
        public static int PickBuilding(BuildingLootIndex index, float x, float z, Func<int, int> others, ISet<int> visited, BotRules rules, Random rng)
        {
            int best = -1, bestFull = -1;
            float bestScore = float.MaxValue, bestFullScore = float.MaxValue;
            for (int b = 0; b < index.BuildingCount; b++)
            {
                if (index.SpawnsOf(b).Count == 0 || (visited != null && visited.Contains(b))) continue;
                index.CentreOf(b, out float cx, out float cz);
                float dx = cx - x, dz = cz - z;
                float distance = (float)Math.Sqrt(dx * dx + dz * dz) + (float)rng.NextDouble() * 12f;
                float weight = Math.Max(0.2f, LootPlacement.KindWeight(index.KindOf(b)) * (1f + 0.15f * (index.SpawnsOf(b).Count - 3)));
                float score = distance / weight;
                if (others(b) < rules.FarmBuildingCap)
                {
                    if (score < bestScore) { bestScore = score; best = b; }
                }
                else if (score < bestFullScore) { bestFullScore = score; bestFull = b; }
            }
            return best >= 0 ? best : bestFull;
        }

        // The unvisited loot point of the building nearest to (x, z): the walk through its rooms is a nearest-neighbour tour. -1 when all are visited.
        public static int NextSpawn(BuildingLootIndex index, int building, float x, float z, ISet<int> visitedSpawns)
        {
            int best = -1;
            float bestD = float.MaxValue;
            foreach (int s in index.SpawnsOf(building))
            {
                if (visitedSpawns.Contains(s)) continue;
                index.SpawnAt(s, out float sx, out _, out float sz);
                float d = (sx - x) * (sx - x) + (sz - z) * (sz - z);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        // At the loot point: close in the ground plane and not on another floor.
        public static bool Reached(float x, float y, float z, float sx, float sy, float sz, float arrive) =>
            (sx - x) * (sx - x) + (sz - z) * (sz - z) <= arrive * arrive && Math.Abs(sy - y) <= 2.5f;
    }
}
