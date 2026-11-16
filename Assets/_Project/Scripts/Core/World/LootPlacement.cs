using System;
using System.Collections.Generic;

namespace Game.Core
{
    public struct LootBuilding
    {
        public BuildingKind Kind;
        public int Available;      // loot points the building plan has
    }

    // How many of a building's loot points get an item (#245, ADR-193): points available x the weight of the kind, one
    // each at least, and when the map's cap is smaller the poor kinds lose theirs first. Pure numbers.
    public static class LootPlacement
    {
        // Military and industrial buildings are the hot spots, homes and sheds the poor ones. The station holds no loot.
        public static float KindWeight(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Barracks: return 2.2f;
                case BuildingKind.FactoryHall: return 1.7f;
                case BuildingKind.School: case BuildingKind.Market: return 1.4f;
                case BuildingKind.Warehouse: case BuildingKind.Apartment: return 1.2f;
                case BuildingKind.Villa: return 1.0f;
                case BuildingKind.Shop: return 0.95f;
                case BuildingKind.House: return 0.9f;
                case BuildingKind.WatchTower: return 0.8f;
                case BuildingKind.Shed: return 0.6f;
                case BuildingKind.Stall: return 0.35f;
                case BuildingKind.ElevatorStation: return 0.01f; // never read: Available is 0 for stations
                default: return 1f;
            }
        }

        // Points to use in each building, in the order given. Never more than a building has, never more than spec.MaxLootSpawns in all.
        public static int[] Allocate(IReadOnlyList<LootBuilding> buildings, WorldBuildSpec spec)
        {
            int n = buildings.Count;
            var counts = new int[n];
            var wanted = new int[n];
            long total = 0;
            for (int i = 0; i < n; i++)
            {
                var b = buildings[i];
                if (b.Available <= 0 || b.Kind == BuildingKind.ElevatorStation) continue;
                wanted[i] = Math.Min(b.Available, Math.Max(1, (int)Math.Round(spec.LootPerBuilding * KindWeight(b.Kind), MidpointRounding.AwayFromZero)));
                total += wanted[i];
            }
            int cap = spec.MaxLootSpawns > 0 ? spec.MaxLootSpawns : int.MaxValue;
            if (total <= cap) { Array.Copy(wanted, counts, n); return counts; }

            // One each for the richest buildings first, so a cap below the building count empties the poor ones.
            var order = new List<int>();
            for (int i = 0; i < n; i++) if (wanted[i] > 0) order.Add(i);
            order.Sort((a, b) =>
            {
                int c = KindWeight(buildings[b].Kind).CompareTo(KindWeight(buildings[a].Kind));
                return c != 0 ? c : a.CompareTo(b);
            });
            int left = cap;
            foreach (int i in order) { if (left == 0) break; counts[i] = 1; left--; }
            if (left == 0) return counts;

            // The rest in proportion to what each still wants, the largest remainders taking the odd points.
            long extra = 0;
            for (int i = 0; i < n; i++) if (wanted[i] > 1) extra += wanted[i] - 1;
            var fraction = new double[n];
            int given = 0;
            for (int i = 0; i < n; i++)
            {
                if (wanted[i] <= 1) continue;
                double share = (double)(wanted[i] - 1) * left / extra;
                int whole = (int)share;
                counts[i] += whole; given += whole; fraction[i] = share - whole;
            }
            var byFraction = new List<int>(order);
            byFraction.Sort((a, b) =>
            {
                int c = fraction[b].CompareTo(fraction[a]);
                if (c != 0) return c;
                c = KindWeight(buildings[b].Kind).CompareTo(KindWeight(buildings[a].Kind));
                return c != 0 ? c : a.CompareTo(b);
            });
            foreach (int i in byFraction)
            {
                if (given >= left) break;
                if (counts[i] < wanted[i]) { counts[i]++; given++; }
            }
            return counts;
        }
    }
}
