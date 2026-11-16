using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum WorldRule
    {
        ElevatorCount, LotCount, ConvergenceCount, PassCount,
        ElevatorSpacing, ElevatorNearLots, ElevatorDoorstepLot, ElevatorOffRoad,
        LotFootprint,
        AboveSea, FlatPad, LotGroundSlope, LotOverlap, LotOnRoad, ConvergenceClear,
        RoadSlope, RoadUnderWater, RoadGraph,
        RegionsLeak, ElevatorUnreachable, LandArea, InsideMap, Malformed,
    }

    public struct WorldViolation
    {
        public WorldRule Rule;
        public string Detail;

        public override string ToString() => Rule + ": " + Detail;
    }

    // Open-world design rules (ADR-050, ADR-052) as checks over a generated plan. Reports, never throws.
    public static class WorldRules
    {
        public const float MaxElevatorRoadDistance = 12f;
        public const float MinClearRadius = 8f;
        public const float MinAboveSea = 0.3f;
        public const float FlatTolerance = 0.35f;
        public const float WadeDepth = 0.6f;
        public const float WalkCell = 2f;
        const float PassBlockMargin = 4f;
        const float RoadJoin = 1.5f;

        // The limits come from the plan (world.json); a plan built by hand without them is checked with the defaults.
        static WorldRuleSpec R(WorldPlan plan) => plan.Rules ?? new WorldRuleSpec();

        public static List<WorldViolation> Validate(WorldPlan plan)
        {
            var found = new List<WorldViolation>();
            if (plan == null)
            {
                found.Add(new WorldViolation { Rule = WorldRule.Malformed, Detail = "no plan" });
                return found;
            }
            try
            {
                Counts(plan, found);
                Placement(plan, found);
                Roads(plan, found);
                Ground(plan, found);
            }
            catch (Exception e)
            {
                found.Add(new WorldViolation { Rule = WorldRule.Malformed, Detail = e.GetType().Name + ": " + e.Message });
            }
            return found;
        }

        static void Add(List<WorldViolation> found, WorldRule rule, string detail) => found.Add(new WorldViolation { Rule = rule, Detail = detail });

        static float Distance(float ax, float az, float bx, float bz) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        // ---- how many of everything ----

        static void Counts(WorldPlan plan, List<WorldViolation> found)
        {
            var rules = R(plan);
            foreach (var region in plan.Regions)
            {
                int elevators = 0, lots = 0, points = 0;
                foreach (var e in plan.Elevators) if (e.Region == region.Index) elevators++;
                foreach (var l in plan.Lots) if (l.Region == region.Index) lots++;
                foreach (var p in plan.ConvergencePoints) if (p.Region == region.Index) points++;
                if (elevators < rules.MinElevators || elevators > rules.MaxElevators)
                    Add(found, WorldRule.ElevatorCount, $"region {region.Id} has {elevators} elevators, expected {rules.MinElevators}-{rules.MaxElevators}");
                if (lots < region.Lots || lots < rules.MinLotsNearElevator * rules.MinElevators)
                    Add(found, WorldRule.LotCount, $"region {region.Id} has {lots} building lots, expected {Math.Max(region.Lots, rules.MinLotsNearElevator * rules.MinElevators)}");
                if (points != region.ConvergencePoints)
                    Add(found, WorldRule.ConvergenceCount, $"region {region.Id} has {points} convergence points, expected {region.ConvergencePoints}");
                bool hasPass = plan.Regions.Count < 2;
                foreach (var pass in plan.Passes) hasPass |= pass.RegionA == region.Index || pass.RegionB == region.Index;
                if (!hasPass) Add(found, WorldRule.PassCount, $"region {region.Id} has no pass to any neighbour");
            }
        }

        // ---- where things stand ----

        static void Placement(WorldPlan plan, List<WorldViolation> found)
        {
            var rules = R(plan);
            float sea = plan.SeaLevel;
            bool Inside(float x, float z) => x >= 0f && z >= 0f && x <= plan.Size && z <= plan.Size;
            // Are all these spots dry and level with the pad height?
            void Pad(string name, float y, float[] xs, float[] zs)
            {
                bool wet = false, uneven = false;
                for (int i = 0; i < xs.Length; i++)
                {
                    float h = plan.Height(xs[i], zs[i]);
                    wet |= h < sea + MinAboveSea;
                    uneven |= Math.Abs(h - y) > FlatTolerance;
                }
                if (wet) Add(found, WorldRule.AboveSea, $"{name} is not above the water");
                if (uneven) Add(found, WorldRule.FlatPad, $"{name} does not stand on a flat pad");
            }

            var elevators = plan.Elevators;
            for (int i = 0; i < elevators.Count; i++)
            {
                var e = elevators[i];
                if (!Inside(e.X, e.Z)) Add(found, WorldRule.InsideMap, e.Name);
                for (int j = i + 1; j < elevators.Count; j++)
                    if (Distance(e.X, e.Z, elevators[j].X, elevators[j].Z) < rules.MinElevatorSpacing)
                        Add(found, WorldRule.ElevatorSpacing, $"{e.Name} and {elevators[j].Name}");
                int near = 0;
                foreach (var lot in plan.Lots)
                    if (lot.Distance(e.X, e.Z) <= rules.LotRange) near++;
                if (near < rules.MinLotsNearElevator)
                    Add(found, WorldRule.ElevatorNearLots, $"{e.Name} has {near} building lots within {rules.LotRange} m, expected {rules.MinLotsNearElevator}+");
                bool doorstep = false;
                foreach (var lot in plan.Lots) doorstep |= lot.Distance(e.X, e.Z) <= rules.DoorstepRange;
                if (!doorstep) Add(found, WorldRule.ElevatorDoorstepLot, $"{e.Name} has no building lot within {rules.DoorstepRange} m");
                Pad(e.Name, e.Y, new[] { e.X, e.X + 3f, e.X - 3f, e.X, e.X }, new[] { e.Z, e.Z, e.Z, e.Z + 3f, e.Z - 3f });
            }

            var lots = plan.Lots;
            for (int i = 0; i < lots.Count; i++)
            {
                var lot = lots[i];
                var xs = new float[9];
                var zs = new float[9];
                bool inside = true;
                for (int c = 0; c < 9; c++)
                {
                    // centre, edge midpoints and corners of the pad: the footprint plus its margin
                    lot.ToWorld((c % 3 - 1) * (lot.SizeX * 0.5f + BuildingLot.PadMargin - 0.25f), (c / 3 - 1) * (lot.SizeZ * 0.5f + BuildingLot.PadMargin - 0.25f), out xs[c], out zs[c]);
                    inside &= Inside(xs[c], zs[c]);
                }
                if (!inside) Add(found, WorldRule.InsideMap, lot.Name);
                Buildings.FootprintLimits(lot.Kind, out float minX, out float minZ, out float maxX, out float maxZ);
                if (lot.Kind == BuildingKind.ElevatorStation || lot.SizeX < minX - 0.01f || lot.SizeZ < minZ - 0.01f || lot.SizeX > maxX + 0.01f || lot.SizeZ > maxZ + 0.01f)
                    Add(found, WorldRule.LotFootprint, $"{lot.Name}: a {lot.Kind} cannot be built on {lot.SizeX:F1} x {lot.SizeZ:F1} m");
                Pad(lot.Name, lot.Y, xs, zs);
                if (lot.GroundSlopeDeg > rules.MaxLotGroundSlopeDeg)
                    Add(found, WorldRule.LotGroundSlope, $"{lot.Name} was cut into a {lot.GroundSlopeDeg:F0} degree slope");
                for (int j = i + 1; j < lots.Count; j++)
                    if (BuildingLot.Overlap(lot, lots[j], 0f)) Add(found, WorldRule.LotOverlap, $"{lot.Name} and {lots[j].Name}");
                string road = RoadAcross(plan, lot);
                if (road != null) Add(found, WorldRule.LotOnRoad, $"{road} runs through {lot.Name}");
            }

            foreach (var p in plan.ConvergencePoints)
            {
                if (!Inside(p.X, p.Z)) Add(found, WorldRule.InsideMap, p.Name);
                if (p.ClearRadius < MinClearRadius) Add(found, WorldRule.ConvergenceClear, $"{p.Name} clear radius {p.ClearRadius} < {MinClearRadius}");
                foreach (var lot in lots)
                    if (lot.Distance(p.X, p.Z) < Math.Max(p.ClearRadius, MinClearRadius))
                        Add(found, WorldRule.ConvergenceClear, $"{lot.Name} stands inside {p.Name}");
                foreach (var e in elevators)
                    if (e.Pad().Distance(p.X, p.Z) < Math.Max(p.ClearRadius, MinClearRadius))
                        Add(found, WorldRule.ConvergenceClear, $"{e.Name} stands inside {p.Name}");
                var xs = new float[9];
                var zs = new float[9];
                for (int c = 0; c < 9; c++)
                {
                    double a = c * Math.PI / 4.0;
                    float r = c == 8 ? 0f : MinClearRadius - 0.5f;
                    xs[c] = p.X + (float)Math.Sin(a) * r;
                    zs[c] = p.Z + (float)Math.Cos(a) * r;
                }
                Pad(p.Name, p.Y, xs, zs);
            }

            foreach (var p in plan.LootPoints)
                if (!Inside(p.X, p.Z)) Add(found, WorldRule.InsideMap, p.Name);
            foreach (var pass in plan.Passes)
                if (!Inside(pass.X, pass.Z)) Add(found, WorldRule.InsideMap, pass.Name);
            foreach (var zone in plan.SubZones)
                if (!Inside(zone.X, zone.Z)) Add(found, WorldRule.InsideMap, zone.Name);
            int strays = 0;
            foreach (var s in plan.Rocks) if (!Inside(s.X, s.Z)) strays++;
            foreach (var s in plan.Trees) if (!Inside(s.X, s.Z)) strays++;
            if (strays > 0) Add(found, WorldRule.InsideMap, $"{strays} rocks or trees");
        }

        // Name of a road whose surface reaches into the lot, or null.
        static string RoadAcross(WorldPlan plan, BuildingLot lot)
        {
            float bound = 0.5f * (float)Math.Sqrt(lot.SizeX * lot.SizeX + lot.SizeZ * lot.SizeZ);
            foreach (var road in plan.Roads)
            {
                float half = road.Width * 0.5f;
                for (int i = 0; i + 1 < road.X.Length; i++)
                {
                    float ax = road.X[i], az = road.Z[i], bx = road.X[i + 1], bz = road.Z[i + 1];
                    float length = Distance(ax, az, bx, bz);
                    if (Distance(ax, az, lot.X, lot.Z) > bound + half + length) continue;
                    int steps = Math.Max(1, (int)Math.Ceiling(length / 1.5f));
                    for (int s = 0; s <= steps; s++)
                    {
                        float t = s / (float)steps;
                        if (lot.Distance(ax + (bx - ax) * t, az + (bz - az) * t) < half) return road.Name;
                    }
                }
            }
            return null;
        }

        // ---- roads ----

        static void Roads(WorldPlan plan, List<WorldViolation> found)
        {
            float maxSlope = (float)Math.Tan(plan.MaxRoadSlopeDeg * Math.PI / 180.0) + 0.01f;
            float dry = plan.SeaLevel + 0.2f;
            var offset = new int[plan.Roads.Count];
            int total = 0;
            for (int r = 0; r < plan.Roads.Count; r++)
            {
                var road = plan.Roads[r];
                offset[r] = total;
                int n = Math.Min(road.X.Length, Math.Min(road.Y.Length, road.Z.Length));
                if (n != road.X.Length || n != road.Y.Length || n != road.Z.Length || n < 2)
                    Add(found, WorldRule.Malformed, $"{road.Name} has {road.X.Length}/{road.Y.Length}/{road.Z.Length} coordinates");
                total += road.X.Length;
                bool steep = false, wet = false, outside = false;
                for (int i = 0; i < n; i++)
                {
                    float x = road.X[i], z = road.Z[i];
                    outside |= x < 0f || z < 0f || x > plan.Size || z > plan.Size;
                    bool deck = road.Bridge != null && ((i < road.Bridge.Length && road.Bridge[i]) || (i > 0 && i - 1 < road.Bridge.Length && road.Bridge[i - 1]));
                    wet |= road.Y[i] < dry || (!deck && plan.Height(x, z) < dry);   // under a deck the riverbed may be low
                    if (i == 0) continue;
                    float run = Distance(x, z, road.X[i - 1], road.Z[i - 1]);
                    steep |= run > 0.01f && Math.Abs(road.Y[i] - road.Y[i - 1]) / run > maxSlope;
                }
                if (steep) Add(found, WorldRule.RoadSlope, $"{road.Name} is steeper than {plan.MaxRoadSlopeDeg} degrees");
                if (wet) Add(found, WorldRule.RoadUnderWater, $"{road.Name} goes under water");
                if (outside) Add(found, WorldRule.InsideMap, road.Name);
            }

            // Road graph: points along a road are linked; a road end is linked to any road point it touches.
            var parent = new int[total];
            for (int i = 0; i < total; i++) parent[i] = i;
            int Find(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }
            void Union(int a, int b) => parent[Find(a)] = Find(b);
            for (int r = 0; r < plan.Roads.Count; r++)
            {
                var road = plan.Roads[r];
                int n = road.X.Length;
                if (n == 0 || road.Z.Length != n) continue;
                for (int i = 1; i < n; i++) Union(offset[r] + i - 1, offset[r] + i);
                for (int end = 0; end < 2; end++)
                {
                    int e = end == 0 ? 0 : n - 1;
                    for (int q = 0; q < plan.Roads.Count; q++)
                    {
                        if (q == r) continue;
                        var other = plan.Roads[q];
                        for (int i = 0; i < other.X.Length && i < other.Z.Length; i++)
                            if (Distance(road.X[e], road.Z[e], other.X[i], other.Z[i]) <= RoadJoin) Union(offset[r] + e, offset[q] + i);
                    }
                }
            }
            // Graph node of the road point nearest to a place, or -1 when no road comes within reach.
            int NodeNear(float x, float z, float reach)
            {
                int best = -1;
                float bestD = reach;
                for (int r = 0; r < plan.Roads.Count; r++)
                {
                    var road = plan.Roads[r];
                    for (int i = 0; i < road.X.Length && i < road.Z.Length; i++)
                    {
                        float d = Distance(x, z, road.X[i], road.Z[i]);
                        if (d <= bestD) { bestD = d; best = offset[r] + i; }
                    }
                }
                return best < 0 ? -1 : Find(best);
            }

            var hubNode = new int[plan.Regions.Count];
            for (int k = 0; k < plan.Regions.Count; k++)
            {
                var region = plan.Regions[k];
                hubNode[k] = NodeNear(region.HubX, region.HubZ, 3f);
                if (hubNode[k] < 0) Add(found, WorldRule.RoadGraph, $"no road reaches the centre of region {region.Id}");
                else if (k > 0 && hubNode[0] >= 0 && hubNode[k] != hubNode[0])
                    Add(found, WorldRule.RoadGraph, $"regions {plan.Regions[0].Id} and {region.Id} are not linked by road");
            }
            foreach (var e in plan.Elevators)
            {
                int node = NodeNear(e.X, e.Z, MaxElevatorRoadDistance);
                bool known = e.Region >= 0 && e.Region < hubNode.Length;
                if (node < 0) Add(found, WorldRule.ElevatorOffRoad, $"no road within {MaxElevatorRoadDistance} m of {e.Name}");
                else if (known && hubNode[e.Region] >= 0 && node != hubNode[e.Region])
                    Add(found, WorldRule.ElevatorOffRoad, $"the road at {e.Name} does not join the road network");
            }
        }

        // ---- the ground itself: land area, and regions joined only through their passes ----

        static void Ground(WorldPlan plan, List<WorldViolation> found)
        {
            var rules = R(plan);
            int n = (int)(plan.Size / WalkCell) + 1;
            if (n < 3) return;
            var height = new float[n * n];
            for (int gz = 0; gz < n; gz++)
                for (int gx = 0; gx < n; gx++)
                    height[gz * n + gx] = plan.Height(gx * WalkCell, gz * WalkCell);

            int land = 0;
            float maxTan = (float)Math.Tan(rules.WalkSlopeDeg * Math.PI / 180.0);
            var walkable = new bool[n * n];
            for (int gz = 0; gz < n; gz++)
                for (int gx = 0; gx < n; gx++)
                {
                    float h = height[gz * n + gx];
                    if (h > plan.SeaLevel) land++;
                    if (h < plan.SeaLevel - WadeDepth) continue;
                    int x0 = Math.Max(0, gx - 1), x1 = Math.Min(n - 1, gx + 1), z0 = Math.Max(0, gz - 1), z1 = Math.Min(n - 1, gz + 1);
                    float sx = (height[gz * n + x1] - height[gz * n + x0]) / ((x1 - x0) * WalkCell);
                    float sz = (height[z1 * n + gx] - height[z0 * n + gx]) / ((z1 - z0) * WalkCell);
                    walkable[gz * n + gx] = sx * sx + sz * sz <= maxTan * maxTan;
                }
            float km2 = land * WalkCell * WalkCell / 1000000f;
            if (km2 < rules.MinLandKm2 || km2 > rules.MaxLandKm2)
                Add(found, WorldRule.LandArea, $"{km2:F2} km2 of land, expected {rules.MinLandKm2}-{rules.MaxLandKm2}");

            var label = new int[n * n];
            var queue = new int[n * n];
            // Walkable cell at or right next to a place, or -1.
            int CellAt(float x, float z)
            {
                int cx = (int)Math.Round(x / WalkCell), cz = (int)Math.Round(z / WalkCell);
                for (int ring = 0; ring <= 2; ring++)
                    for (int dz = -ring; dz <= ring; dz++)
                        for (int dx = -ring; dx <= ring; dx++)
                        {
                            int gx = cx + dx, gz = cz + dz;
                            if (gx < 0 || gz < 0 || gx >= n || gz >= n) continue;
                            if (walkable[gz * n + gx]) return gz * n + gx;
                        }
                return -1;
            }

            // With every pass shut, no region centre may reach another: the ridges and the sea are the walls.
            var shut = new bool[n * n];
            foreach (var pass in plan.Passes)
            {
                float reach = pass.Width * 0.5f + WorldPass.Shoulder + PassBlockMargin;
                int x0 = Math.Max(0, (int)((pass.X - reach) / WalkCell)), x1 = Math.Min(n - 1, (int)((pass.X + reach) / WalkCell) + 1);
                int z0 = Math.Max(0, (int)((pass.Z - reach) / WalkCell)), z1 = Math.Min(n - 1, (int)((pass.Z + reach) / WalkCell) + 1);
                for (int gz = z0; gz <= z1; gz++)
                    for (int gx = x0; gx <= x1; gx++)
                        if (Distance(gx * WalkCell, gz * WalkCell, pass.X, pass.Z) <= reach) shut[gz * n + gx] = true;
            }
            Components(n, walkable, shut, label, queue);
            for (int i = 0; rules.RegionsAreWalled && i < plan.Regions.Count; i++)
                for (int j = i + 1; j < plan.Regions.Count; j++)
                {
                    int a = CellAt(plan.Regions[i].HubX, plan.Regions[i].HubZ), b = CellAt(plan.Regions[j].HubX, plan.Regions[j].HubZ);
                    if (a >= 0 && b >= 0 && label[a] > 0 && label[a] == label[b])
                        Add(found, WorldRule.RegionsLeak, $"regions {plan.Regions[i].Id} and {plan.Regions[j].Id} are joined without going through a pass");
                }

            // With the passes open, every elevator must be able to walk to every other one.
            Components(n, walkable, null, label, queue);
            int home = 0;
            string homeName = null;
            foreach (var region in plan.Regions)
            {
                int cell = CellAt(region.HubX, region.HubZ);
                if (cell < 0) continue;
                if (home == 0) { home = label[cell]; homeName = region.Id; }
                else if (label[cell] != home)
                    Add(found, WorldRule.ElevatorUnreachable, $"the centre of region {region.Id} cannot be walked to from region {homeName}");
            }
            foreach (var e in plan.Elevators)
            {
                int cell = CellAt(e.X, e.Z);
                if (cell < 0) Add(found, WorldRule.ElevatorUnreachable, $"{e.Name} stands on ground nobody can walk on");
                else if (home == 0) { home = label[cell]; homeName = e.Name; }
                else if (label[cell] != home) Add(found, WorldRule.ElevatorUnreachable, $"{e.Name} cannot be walked to from {homeName}");
            }
        }

        // Labels every 4-connected patch of walkable, not-shut cells with its own number (1, 2, ...); 0 = not walkable.
        static void Components(int n, bool[] walkable, bool[] shut, int[] label, int[] queue)
        {
            Array.Clear(label, 0, label.Length);
            int next = 0;
            for (int start = 0; start < walkable.Length; start++)
            {
                if (!walkable[start] || label[start] != 0 || (shut != null && shut[start])) continue;
                next++;
                int head = 0, tail = 0;
                queue[tail++] = start;
                label[start] = next;
                while (head < tail)
                {
                    int cell = queue[head++];
                    int gx = cell % n, gz = cell / n;
                    for (int side = 0; side < 4; side++)
                    {
                        int nx = gx + (side == 0 ? 1 : side == 1 ? -1 : 0), nz = gz + (side == 2 ? 1 : side == 3 ? -1 : 0);
                        if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                        int to = nz * n + nx;
                        if (!walkable[to] || label[to] != 0 || (shut != null && shut[to])) continue;
                        label[to] = next;
                        queue[tail++] = to;
                    }
                }
            }
        }
    }
}
