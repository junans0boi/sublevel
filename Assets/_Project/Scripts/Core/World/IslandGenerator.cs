using System;
using System.Collections.Generic;

namespace Game.Core
{
    // One river as the builder needs it: centre line, water surface and half width at each point (world metres).
    [Serializable]
    public class WorldRiver
    {
        public string Name;
        public float[] X, Y, Z, HalfWidth;
    }

    // One stretch of road over water: a deck from point FromPoint to ToPoint of road Road, at the road's own height.
    [Serializable]
    public struct WorldBridge
    {
        public string Name;
        public int Road, FromPoint, ToPoint;
        public float Length, Width;
    }

    // The C2 island 'Cheongnam-do' (ADR-168, ADR-204): spec -> plan, deterministic, no state between calls. Towns are TownLayout boxes levelled into the
    // ground; the road graph of roads.json runs between their avenue ends; rivers are cut into the ground and roads cross them on decks
    // (rule: a ford for dirt tracks); the same WorldPlan comes out as from the original generator, so the editor builder and the rules stay the same.
    public static class IslandGenerator
    {
        const float RoadStep = 6f;
        const int FordCenterPointSpacing = 3; // Samples are 6 m apart, so every point stays within 9 m of a centre and the 15 m flat core
        const float Causeway = 0.8f;
        const float DeckClearance = 1.3f;      // deck above the water
        const float AvenueExtension = 12f;     // the avenue runs this far past the box so the station beside its end is within reach of a road point
        const float Margin = 1.6f;
        public const int BaysPerBank = 4;      // elevator cars side by side at one station (ADR-257: 20 banks x 4 = 80 slots)
        public const float BayPitch = 5.2f;    // StationWidth plus a gap, so neighbouring bays never touch
        const float StationBay = BaysPerBank * BayPitch + 3f;   // box length kept free at an avenue end for a bank of stations

        sealed class TownBuild
        {
            public IslandTown Town;
            public int Index;
            public float Y;                       // plateau height in the world
            public TownLayoutResult Layout;
            public float AvenueWidth;
            public int AvenueRoad = -1;
        }

        public static float AvenueWidthOf(int tier) => tier == 1 ? 12f : tier == 2 ? 8f : tier == 3 ? 6f : 4.5f;

        public static List<(BuildingKind kind, int count)> Composition(string text)
        {
            var list = new List<(BuildingKind, int)>();
            foreach (var part in (text ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(':');
                list.Add(((BuildingKind)Enum.Parse(typeof(BuildingKind), kv[0].Trim()), int.Parse(kv[1])));
            }
            return list;
        }

        public static WorldPlan Generate(WorldSpec spec)
        {
            var isl = spec.Island;
            var plan = new WorldPlan
            {
                Size = spec.Size, SeaLevel = spec.SeaLevel, MaxHeight = spec.MaxHeight, MaxRoadSlopeDeg = spec.MaxRoadSlopeDeg, Seed = spec.Seed,
                Rules = spec.Rules ?? new WorldRuleSpec(), Build = spec.Build ?? new WorldBuildSpec(),
            };
            var terrain = new IslandTerrain(spec);
            plan.Terrain = terrain;
            uint seed = unchecked((uint)spec.Seed);

            var towns = new List<TownBuild>();
            for (int i = 0; i < isl.Towns.Length; i++)
            {
                var t = isl.Towns[i];
                var tb = new TownBuild { Town = t, Index = i, Y = spec.SeaLevel + terrain.TownHeight[i], AvenueWidth = AvenueWidthOf(t.Tier) };
                int stations = 0;
                foreach (var st in isl.Stations) if (st.Town == t.Id) stations++;
                float reserve = StationBay;
                tb.Layout = TownLayout.Place(t.Width, t.Depth, tb.AvenueWidth, Composition(t.Composition), t.Seed, stations > 0 ? reserve : 0f, stations > 1 ? reserve : 0f);
                towns.Add(tb);
            }
            var townById = new Dictionary<string, TownBuild>();
            foreach (var tb in towns) townById[tb.Town.Id] = tb;

            // regions: the hub is the avenue centre of the region's biggest town
            for (int r = 0; r < isl.Regions.Length; r++)
            {
                TownBuild hub = null;
                foreach (var tb in towns) if (tb.Town.Region == r && (hub == null || tb.Town.Tier < hub.Town.Tier)) hub = tb;
                var rs = spec.Regions != null && r < spec.Regions.Length ? spec.Regions[r] : new RegionSpec();
                plan.Regions.Add(new WorldRegion
                {
                    Index = r, Id = isl.Regions[r].Id, Name = isl.Regions[r].Name, Kind = (RegionKind)Enum.Parse(typeof(RegionKind), isl.Regions[r].Kind),
                    CentreX = isl.Regions[r].Seeds[0], CentreZ = isl.Regions[r].Seeds[1],
                    HubX = hub.Town.X, HubY = hub.Y, HubZ = hub.Town.Z, Lots = rs.Lots, ConvergencePoints = rs.ConvergencePoints,
                });
            }

            foreach (var tb in towns)
                plan.SubZones.Add(new WorldSubZone { Name = tb.Town.Name, Region = tb.Town.Region, X = tb.Town.X, Y = tb.Y, Z = tb.Town.Z, Radius = Math.Max(tb.Town.Width, tb.Town.Depth) * 0.5f });

            AddAvenues(plan, towns);
            BuildRoads(spec, plan, terrain, towns, townById);
            AddPasses(spec, plan, terrain);
            AddRivers(plan, terrain);
            terrain.SetRoads(plan.Roads);

            // buildings and stations: pads keep the ground flat under and around them
            AddLots(plan, terrain, towns);
            AddStations(spec, plan, terrain, towns, townById);
            foreach (var e in plan.Elevators) terrain.AddRectPad(e.X, e.Z, WorldElevator.PadWidth * 0.5f, WorldElevator.PadDepth * 0.5f, e.FacingDeg, e.Y);
            foreach (var lot in plan.Lots) terrain.AddRectPad(lot.X, lot.Z, lot.SizeX * 0.5f + BuildingLot.PadMargin, lot.SizeZ * 0.5f + BuildingLot.PadMargin, lot.YawDeg, lot.Y);
            AddConvergence(spec, plan, terrain, towns);
            WorldGenerator.Scatter(spec.Regions, plan, terrain, seed, true);
            WorldGenerator.Scatter(spec.Regions, plan, terrain, seed, false);
            return plan;
        }

        // ---- towns ----

        // Road along the town's avenue, a vertex every 6 m (the road graph rule wants points to link), flat at the plateau height.
        static void AddAvenues(WorldPlan plan, List<TownBuild> towns)
        {
            foreach (var tb in towns)
            {
                var t = tb.Town;
                float half = t.Width * 0.5f + AvenueExtension;
                int steps = (int)Math.Ceiling(half * 2f / RoadStep); steps += steps % 2;   // even: a vertex at the centre, which is the region hub
                var road = new WorldRoad { Name = "Avenue-" + t.Id, Kind = RoadKind.Trunk, Width = tb.AvenueWidth, Region = t.Region };
                road.X = new float[steps + 1]; road.Y = new float[steps + 1]; road.Z = new float[steps + 1];
                for (int i = 0; i <= steps; i++)
                {
                    float lx = -half + 2f * half * i / steps;
                    IslandMath.TownToWorld(t, lx, 0f, out road.X[i], out road.Z[i]);
                    road.Y[i] = tb.Y;
                }
                plan.Roads.Add(road);
                tb.AvenueRoad = plan.Roads.Count - 1;
                // the blocks' streets: parallel to the avenue and across it, flat at the plateau height
                int k = 0;
                foreach (var st in tb.Layout.Streets)
                {
                    if (st.Avenue) continue;
                    bool alongX = st.X1 - st.X0 > st.Z1 - st.Z0;
                    float len = alongX ? st.X1 - st.X0 : st.Z1 - st.Z0, mid = alongX ? (st.Z0 + st.Z1) * 0.5f : (st.X0 + st.X1) * 0.5f, from = alongX ? st.X0 : st.Z0;
                    int n = Math.Max(1, (int)Math.Ceiling(len / RoadStep));
                    var street = new WorldRoad { Name = $"Street-{t.Id}-{++k}", Kind = RoadKind.Trunk, Width = alongX ? st.Z1 - st.Z0 : st.X1 - st.X0, Region = t.Region, X = new float[n + 1], Y = new float[n + 1], Z = new float[n + 1] };
                    for (int i = 0; i <= n; i++)
                    {
                        float u = from + len * i / n;
                        IslandMath.TownToWorld(t, alongX ? u : mid, alongX ? mid : u, out street.X[i], out street.Z[i]);
                        street.Y[i] = tb.Y;
                    }
                    plan.Roads.Add(street);
                }
            }
        }

        // World end of a town's avenue nearest to a place.
        public static void AvenueEnd(IslandTown t, float towardX, float towardZ, out float x, out float z, int forceEnd = 0)
        {
            float half = t.Width * 0.5f + AvenueExtension;
            IslandMath.TownToWorld(t, -half, 0f, out float ax, out float az);
            IslandMath.TownToWorld(t, half, 0f, out float bx, out float bz);
            if (forceEnd < 0 || (forceEnd == 0 && IslandMath.Dist(ax, az, towardX, towardZ) <= IslandMath.Dist(bx, bz, towardX, towardZ))) { x = ax; z = az; } else { x = bx; z = bz; }
        }

        // Steepest natural ground between the footprint's corners and its centre (before any pad), in degrees.
        static float NaturalSlopeDeg(IslandTerrain terrain, float x, float z, float w, float d, float yawDeg)
        {
            double yaw = yawDeg * Math.PI / 180.0, c = Math.Cos(yaw), s = Math.Sin(yaw);
            float h0 = terrain.Natural(x, z), worst = 0f;
            for (int k = 0; k < 4; k++)
            {
                float rx = (k % 2 == 0 ? -1f : 1f) * w * 0.5f, rz = (k < 2 ? -1f : 1f) * d * 0.5f;
                float px = x + (float)(rx * c + rz * s), pz = z + (float)(-rx * s + rz * c);
                float run = (float)Math.Sqrt(rx * rx + rz * rz);
                worst = Math.Max(worst, Math.Abs(terrain.Natural(px, pz) - h0) / run);
            }
            return (float)(Math.Atan(worst) * 180.0 / Math.PI);
        }

        static void AddLots(WorldPlan plan, IslandTerrain terrain, List<TownBuild> towns)
        {
            foreach (var tb in towns)
            {
                var t = tb.Town;
                int n = 0;
                double deg2rad = Math.PI / 180.0;
                foreach (var lot in tb.Layout.Lots)
                {
                    IslandMath.TownToWorld(t, lot.X, lot.Z, out float x, out float z);
                    float yaw = lot.YawDeg - t.RotDeg;
                    yaw = (float)(((yaw % 360.0) + 360.0) % 360.0);
                    plan.Lots.Add(new BuildingLot
                    {
                        Name = $"{t.Id}-{++n}", Kind = lot.Kind, Region = t.Region, SubZone = tb.Index,
                        X = x, Y = tb.Y, Z = z, SizeX = lot.Width, SizeZ = lot.Depth, YawDeg = yaw, GroundSlopeDeg = NaturalSlopeDeg(terrain, x, z, lot.Width, lot.Depth, yaw),
                    });
                }
                if (deg2rad < 0) break;
            }
        }

        // ---- roads ----

        static float[] Spline(float[] control, bool switchback, float amplitude, float wavelength)
        {
            var s = IslandMath.CatmullRom(control, RoadStep);
            // resample to an even step along the curve
            var xs = new List<float>(); var zs = new List<float>();
            float carry = 0f;
            xs.Add(s[0]); zs.Add(s[1]);
            for (int i = 2; i + 1 < s.Length; i += 2)
            {
                float ax = s[i - 2], az = s[i - 1], bx = s[i], bz = s[i + 1];
                float len = IslandMath.Dist(ax, az, bx, bz);
                float pos = RoadStep - carry;
                while (pos <= len) { xs.Add(ax + (bx - ax) * pos / len); zs.Add(az + (bz - az) * pos / len); pos += RoadStep; }
                carry = len - (pos - RoadStep);
            }
            xs.Add(s[s.Length - 2]); zs.Add(s[s.Length - 1]);
            int n = xs.Count;
            var along = new float[n];
            for (int i = 1; i < n; i++) along[i] = along[i - 1] + IslandMath.Dist(xs[i], zs[i], xs[i - 1], zs[i - 1]);
            float total = along[n - 1];
            var output = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                float x = xs[i], z = zs[i];
                if (switchback)
                {
                    var q = xs[Math.Min(n - 1, i + 1)]; var qz = zs[Math.Min(n - 1, i + 1)];
                    var o = xs[Math.Max(0, i - 1)]; var oz = zs[Math.Max(0, i - 1)];
                    float nx = -(qz - oz), nz = q - o, nl = (float)Math.Sqrt(nx * nx + nz * nz);
                    if (nl > 1e-4f) { nx /= nl; nz /= nl; }
                    float env = Math.Min(1f, Math.Min(along[i] / 70f, (total - along[i]) / 70f));
                    float off = amplitude * env * (float)Math.Sin(2.0 * Math.PI * along[i] / wavelength);
                    x += nx * off; z += nz * off;
                }
                output[i * 2] = x; output[i * 2 + 1] = z;
            }
            return output;
        }

        static TownBuild aTownOf(string id, Dictionary<string, TownBuild> byId) => byId.TryGetValue(id, out var tb) ? tb : null;

        // Waypoints that stand inside either end town's box (plus a verge) are dropped: the road runs from the avenue end.
        static float[] Outside(float[] via, TownBuild a, TownBuild b)
        {
            var keep = new List<float>();
            for (int i = 0; i + 1 < via.Length; i += 2)
            {
                if ((a != null && IslandTerrain.OutsideTown(a.Town, via[i], via[i + 1]) < 10f) || (b != null && IslandTerrain.OutsideTown(b.Town, via[i], via[i + 1]) < 10f)) continue;
                keep.Add(via[i]); keep.Add(via[i + 1]);
            }
            return keep.ToArray();
        }

        // Points that lead a road away from an avenue end: straight out along the avenue, and, when the destination lies beside the box
        // rather than beyond its end, an elbow past the box corner so the road never cuts through the rows of lots.
        static void Leave(IslandTown t, float endX, float endZ, float towardX, float towardZ, List<float> output)
        {
            IslandMath.WorldToTown(t, endX, endZ, out float lx, out _);
            IslandMath.WorldToTown(t, towardX, towardZ, out float tx, out float tz);
            float sx = lx < 0f ? -1f : 1f, hw = t.Width * 0.5f, hd = t.Depth * 0.5f;
            float outX = sx * (hw + AvenueExtension + 36f);
            IslandMath.TownToWorld(t, outX, 0f, out float x, out float z);
            output.Add(x); output.Add(z);
            bool behind = (tx - sx * hw) * sx < 0f;                                  // destination is above the end, not beyond it
            if (Math.Abs(tz) > hd + 10f && behind)
            {
                IslandMath.TownToWorld(t, sx * (hw + AvenueExtension + 30f), (tz > 0f ? 1f : -1f) * (hd + 34f), out x, out z);
                output.Add(x); output.Add(z);
            }
        }

        static void BuildRoads(WorldSpec spec, WorldPlan plan, IslandTerrain terrain, List<TownBuild> towns, Dictionary<string, TownBuild> townById)
        {
            var isl = spec.Island;
            var nodeById = new Dictionary<string, IslandNode>();
            foreach (var nd in isl.Nodes) nodeById[nd.Id] = nd;
            float tanLimit = (float)Math.Tan(isl.RoadDesignSlopeDeg * Math.PI / 180.0);

            // pass 1: ground plan of every edge
            var shapes = new List<float[]>();
            var ends = new List<(float ax, float az, float bx, float bz)>();
            foreach (var e in isl.Roads)
            {
                float ax, az, bx, bz;
                float[] via = Outside(e.Via ?? new float[0], aTownOf(e.A, townById), aTownOf(e.B, townById));
                bool aTown = townById.TryGetValue(e.A, out var ta), bTown = townById.TryGetValue(e.B, out var tb);
                float aNodeX = aTown ? ta.Town.X : nodeById[e.A].X, aNodeZ = aTown ? ta.Town.Z : nodeById[e.A].Z;
                float bNodeX = bTown ? tb.Town.X : nodeById[e.B].X, bNodeZ = bTown ? tb.Town.Z : nodeById[e.B].Z;
                float nextAX = via.Length > 0 ? via[0] : bNodeX, nextAZ = via.Length > 0 ? via[1] : bNodeZ;
                float prevBX = via.Length > 0 ? via[via.Length - 2] : aNodeX, prevBZ = via.Length > 0 ? via[via.Length - 1] : aNodeZ;
                if (aTown) AvenueEnd(ta.Town, nextAX, nextAZ, out ax, out az, e.EndA); else { ax = aNodeX; az = aNodeZ; }
                if (bTown) AvenueEnd(tb.Town, prevBX, prevBZ, out bx, out bz, e.EndB); else { bx = bNodeX; bz = bNodeZ; }
                // a stub straight out of the avenue end, so the road clears the box before it turns
                var head = new List<float> { ax, az }; var tail = new List<float>();
                if (aTown) Leave(ta.Town, ax, az, nextAX, nextAZ, head);
                if (bTown) { var back = new List<float>(); Leave(tb.Town, bx, bz, prevBX, prevBZ, back); for (int q = back.Count - 2; q >= 2; q -= 2) { tail.Add(back[q]); tail.Add(back[q + 1]); } }
                var all = new List<float>(head); all.AddRange(via); all.AddRange(tail); all.Add(bx); all.Add(bz);
                var control = all.ToArray();
                shapes.Add(Spline(control, e.Switchback, isl.SwitchbackAmplitude, isl.SwitchbackWavelength));
                ends.Add((ax, az, bx, bz));
            }

            // fords: dirt tracks that meet a river
            for (int k = 0; k < isl.Roads.Length; k++)
            {
                if (isl.Roads[k].Class != "D") continue;
                var s = shapes[k];
                for (int r = 0; r < isl.Rivers.Length; r++)
                {
                    int runStart = -1;
                    for (int i = 0; i <= s.Length / 2; i++)
                    {
                        bool wet = i < s.Length / 2 && terrain.River(r, s[i * 2], s[i * 2 + 1], out float d, out _, out float hw, out _, out _) && d < hw + Margin;
                        if (wet)
                        {
                            if (runStart < 0) runStart = i;
                        }
                        else if (runStart >= 0)
                        {
                            // Keep disconnected crossings separate: one path can intersect a winding river more than once.
                            int runEnd = i - 1;
                            for (int center = runStart; center < runEnd; center += FordCenterPointSpacing)
                                terrain.AddFord(s[center * 2], s[center * 2 + 1]);
                            terrain.AddFord(s[runEnd * 2], s[runEnd * 2 + 1]);
                            runStart = -1;
                        }
                    }
                }
            }

            // pass 2: heights and decks
            for (int k = 0; k < isl.Roads.Length; k++)
            {
                var e = isl.Roads[k];
                var s = shapes[k];
                int n = s.Length / 2;
                bool aTown = townById.TryGetValue(e.A, out var ta), bTown = townById.TryGetValue(e.B, out var tb);
                var road = new WorldRoad
                {
                    Name = (e.A + "-" + e.B), Kind = e.Class == "D" ? RoadKind.Path : RoadKind.Trunk, Width = e.Class == "H" ? 12f : e.Class == "P" ? 8f : 4f,
                    X = new float[n], Y = new float[n], Z = new float[n], Bridge = new bool[n - 1], Ford = new bool[n - 1],
                };
                var along = new float[n];
                for (int i = 0; i < n; i++)
                {
                    road.X[i] = s[i * 2]; road.Z[i] = s[i * 2 + 1];
                    if (i > 0) along[i] = along[i - 1] + IslandMath.Dist(road.X[i], road.Z[i], road.X[i - 1], road.Z[i - 1]);
                }
                var target = new float[n]; var floor = new float[n]; var pinned = new bool[n]; var wet = new bool[n]; var surface = new float[n];
                bool ford = e.Class == "D";
                for (int i = 0; i < n; i++)
                {
                    floor[i] = float.MinValue;
                    target[i] = Math.Max(terrain.Natural(road.X[i], road.Z[i]), spec.SeaLevel + Causeway);
                    for (int r = 0; r < isl.Rivers.Length; r++)
                        if (terrain.River(r, road.X[i], road.Z[i], out float d, out float t, out float hw, out _, out _) && d < hw + Margin)
                        { wet[i] = true; surface[i] = Math.Max(surface[i], terrain.RiverSurface(r, t)); }
                }
                // where this road runs over one that was solved before, it takes that road's height (a heightfield cannot hold two roads at two heights)
                MergeWithEarlier(plan, road, target, pinned);
                if (aTown) { pinned[0] = true; target[0] = ta.Y; }
                if (bTown) { pinned[n - 1] = true; target[n - 1] = tb.Y; }
                // a road starts and ends where its neighbours do: junction nodes take the ground height there
                if (!aTown) { pinned[0] = true; target[0] = Math.Max(target[0], spec.SeaLevel + Causeway); }
                if (!bTown) { pinned[n - 1] = true; target[n - 1] = Math.Max(target[n - 1], spec.SeaLevel + Causeway); }
                if (!ford)
                {
                    int i = 0;
                    while (i < n)
                    {
                        if (!wet[i]) { i++; continue; }
                        int a = Math.Max(0, i - 1), j = i;
                        while (j < n && wet[j]) j++;
                        int b = Math.Min(n - 1, j);
                        // the deck only has to clear the water: floors on the wet vertices, the cut-and-fill solver lifts the rest within the grade
                        for (int q = i; q < j; q++) floor[q] = surface[q] + DeckClearance;
                        for (int q = a; q < b; q++) road.Bridge[q] = true;
                        i = j;
                    }
                }
                else
                {
                    int i = 0;
                    while (i < n) { if (wet[i]) road.Ford[Math.Min(i, n - 2)] = true; i++; }
                }
                SolveProfile(road.Y, target, pinned, floor, along, tanLimit);
                road.Region = RegionOfRoad(plan, terrain, road);
                plan.Roads.Add(road);
            }
            // bridges
            for (int r = 0; r < plan.Roads.Count; r++)
            {
                var road = plan.Roads[r];
                if (road.Bridge == null) continue;
                int i = 0;
                while (i < road.Bridge.Length)
                {
                    if (!road.Bridge[i]) { i++; continue; }
                    int j = i;
                    while (j < road.Bridge.Length && road.Bridge[j]) j++;
                    float len = 0f;
                    for (int q = i; q < j; q++) len += IslandMath.Dist(road.X[q], road.Z[q], road.X[q + 1], road.Z[q + 1]);
                    plan.Bridges.Add(new WorldBridge { Name = "Bridge-" + (plan.Bridges.Count + 1), Road = r, FromPoint = i, ToPoint = j, Length = len, Width = road.Width });
                    i = j;
                }
            }
        }

        static void MergeWithEarlier(WorldPlan plan, WorldRoad road, float[] target, bool[] pinned)
        {
            for (int i = 0; i < road.X.Length; i++)
                for (int r = 0; r < plan.Roads.Count; r++)
                {
                    var o = plan.Roads[r];
                    float reach = (road.Width + o.Width) * 0.5f + 3f, best = reach, y = 0f;
                    for (int j = 0; j + 1 < o.X.Length; j++)
                    {
                        if (o.Bridge != null && o.Bridge[j]) continue;
                        float ex = o.X[j + 1] - o.X[j], ez = o.Z[j + 1] - o.Z[j], l2 = ex * ex + ez * ez;
                        float t = l2 > 0f ? ((road.X[i] - o.X[j]) * ex + (road.Z[i] - o.Z[j]) * ez) / l2 : 0f;
                        t = Math.Max(0f, Math.Min(1f, t));
                        float px = o.X[j] + ex * t - road.X[i], pz = o.Z[j] + ez * t - road.Z[i], d = (float)Math.Sqrt(px * px + pz * pz);
                        if (d < best) { best = d; y = o.Y[j] + (o.Y[j + 1] - o.Y[j]) * t; }
                    }
                    if (best < reach) { pinned[i] = true; target[i] = y; break; }
                }
        }

        static int RegionOfRoad(WorldPlan plan, IslandTerrain terrain, WorldRoad road)
        {
            int region = -2;
            for (int i = 0; i < road.X.Length; i += 3)
            {
                terrain.Locate(road.X[i], road.Z[i], out int r, out _);
                if (region == -2) region = r; else if (region != r) return -1;
            }
            return region < 0 ? -1 : region;
        }

        // Cut-and-fill profile: heights near the target, pinned ends and deck, no section steeper than the design grade.
        static void SolveProfile(float[] y, float[] target, bool[] pinned, float[] floor, float[] along, float maxSlope)
        {
            int n = y.Length;
            var high = new float[n]; var low = new float[n]; var goal = (float[])target.Clone();
            for (int i = 0; i < n; i++) { high[i] = pinned[i] ? target[i] : float.MaxValue; low[i] = Math.Max(pinned[i] ? target[i] : float.MinValue, floor[i]); }
            Envelope(high, along, maxSlope, true);
            Envelope(low, along, maxSlope, false);
            for (int i = 0; i < n; i++)
            {
                if (pinned[i] && floor[i] == float.MinValue) continue;
                if (low[i] > high[i]) goal[i] = (low[i] + high[i]) * 0.5f;
                else goal[i] = Math.Min(high[i], Math.Max(low[i], goal[i]));
            }
            for (int i = 0; i < n; i++) { high[i] = goal[i]; low[i] = goal[i]; }
            Envelope(high, along, maxSlope, true);
            Envelope(low, along, maxSlope, false);
            for (int i = 0; i < n; i++) y[i] = (high[i] + low[i]) * 0.5f;
        }

        static void Envelope(float[] values, float[] along, float maxSlope, bool lower)
        {
            int n = values.Length;
            for (int pass = 0; pass < 2; pass++)
                for (int q = 1; q < n; q++)
                {
                    int i = pass == 0 ? q : n - 1 - q;
                    int from = pass == 0 ? i - 1 : i + 1;
                    float reach = maxSlope * Math.Abs(along[i] - along[from]);
                    if (lower)
                    {
                        if (values[from] < float.MaxValue && values[from] + reach < values[i]) values[i] = values[from] + reach;
                    }
                    else if (values[from] > float.MinValue && values[from] - reach > values[i]) values[i] = values[from] - reach;
                }
        }

        // ---- passes, rivers ----

        static void AddPasses(WorldSpec spec, WorldPlan plan, IslandTerrain terrain)
        {
            foreach (var p in spec.Island.Passes)
            {
                // the two regions on either side: the most common regions around the pass
                var count = new int[plan.Regions.Count];
                for (int k = 0; k < 16; k++)
                {
                    double a = k * Math.PI / 8.0;
                    terrain.Locate(p.X + (float)Math.Cos(a) * 90f, p.Z + (float)Math.Sin(a) * 90f, out int r, out _);
                    if (r >= 0) count[r]++;
                }
                int ra = 0, rb = -1;
                for (int r = 1; r < count.Length; r++) if (count[r] > count[ra]) ra = r;
                for (int r = 0; r < count.Length; r++) if (r != ra && count[r] > 0 && (rb < 0 || count[r] > count[rb])) rb = r;
                if (rb < 0) rb = ra;
                var A = plan.Regions[ra]; var B = plan.Regions[rb];
                float dx = B.CentreX - A.CentreX, dz = B.CentreZ - A.CentreZ, len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len < 1f) { dx = 0f; dz = 1f; len = 1f; }
                plan.Passes.Add(new WorldPass
                {
                    Name = p.Name, RegionA = ra, RegionB = rb, X = p.X, Z = p.Z, Y = terrain.Height(p.X, p.Z),
                    DirX = dx / len, DirZ = dz / len, YawDeg = YawOf(dx, dz), Width = p.Road ? 18f : 8f,
                });
            }
        }

        static void AddRivers(WorldPlan plan, IslandTerrain terrain)
        {
            for (int r = 0; r < terrain.RiverX.Length; r++)
            {
                int n = terrain.RiverX[r].Length;
                var river = new WorldRiver { Name = "River-" + r, X = new float[n], Y = new float[n], Z = new float[n], HalfWidth = new float[n] };
                for (int i = 0; i < n; i++)
                {
                    river.X[i] = terrain.RiverX[r][i]; river.Z[i] = terrain.RiverZ[r][i];
                    terrain.River(r, river.X[i], river.Z[i], out _, out float t, out float hw, out _, out _);
                    river.Y[i] = terrain.RiverSurface(r, t); river.HalfWidth[i] = hw;
                }
                plan.Rivers.Add(river);
            }
        }

        // ---- elevator stations ----

        // The design sheet's station list (town, side, fraction, region) puts each station at the end of its town's avenue, beside the road.
        static void AddStations(WorldSpec spec, WorldPlan plan, IslandTerrain terrain, List<TownBuild> towns, Dictionary<string, TownBuild> townById)
        {
            var taken = new Dictionary<string, int>();
            foreach (var st in spec.Island.Stations)
            {
                var tb = townById[st.Town];
                var t = tb.Town;
                taken.TryGetValue(t.Id, out int already);
                taken[t.Id] = already + 1;
                // first station of a town at its west end, the next at the east end; the side of the avenue alternates with the design sheet's side
                int end = already % 2 == 0 ? -1 : 1;     // west bay first, east bay second (TownLayout reserves them in that order)
                // a town whose west end has no neighbour road still works: stations only need ground
                float lat = tb.AvenueWidth * 0.5f + TownLayout.Sidewalk + WorldElevator.StationDepth * 0.5f;
                float side = (st.Side == "s" || st.Side == "e") ? 1f : -1f;
                float lx = end < 0 ? -(t.Width * 0.5f - StationBay * 0.5f - 1f) : Math.Min(t.Width * 0.5f - StationBay * 0.5f - 1f, tb.Layout.EndX + StationBay * 0.5f + 1f), lz = side * lat;
                // the front faces the avenue: local yaw 0 = +Z, so a station on the +Z side looks to -Z (180)
                float localYaw = side > 0f ? 180f : 0f;
                float yaw = (float)((((localYaw - t.RotDeg) % 360.0) + 360.0) % 360.0);
                int bank = plan.Elevators.Count / BaysPerBank;
                for (int bay = 0; bay < BaysPerBank; bay++)
                {
                    IslandMath.TownToWorld(t, lx + (bay - (BaysPerBank - 1) * 0.5f) * BayPitch, lz, out float x, out float z);
                    plan.Elevators.Add(new WorldElevator { Name = $"Elevator-{plan.Regions[st.Region].Id}-{CountIn(plan, st.Region) + 1}", Region = st.Region, SubZone = tb.Index, Bank = bank, X = x, Y = tb.Y, Z = z, FacingDeg = yaw });
                }
            }
        }

        static int CountIn(WorldPlan plan, int region)
        {
            int n = 0;
            foreach (var e in plan.Elevators) if (e.Region == region) n++;
            return n;
        }

        // ---- convergence points ----

        static void AddConvergence(WorldSpec spec, WorldPlan plan, IslandTerrain terrain, List<TownBuild> towns)
        {
            for (int r = 0; r < plan.Regions.Count; r++)
            {
                int want = plan.Regions[r].ConvergencePoints, made = 0;
                var inRegion = new List<TownBuild>();
                foreach (var tb in towns) if (tb.Town.Region == r) inRegion.Add(tb);
                inRegion.Sort((a, b) => a.Town.Tier.CompareTo(b.Town.Tier));
                // open ground around the towns of the region: rings of growing distance from the box, 16 directions, first spot that fits
                for (int ring = 0; ring < 8 && made < want; ring++)
                    foreach (var tb in inRegion)
                    {
                        var t = tb.Town;
                        float reach = Math.Max(t.Width, t.Depth) * 0.5f + 26f + ring * 22f;
                        for (int k = 0; k < 16 && made < want; k++)
                        {
                            double a = (k + (made & 1) * 0.5) * Math.PI / 8.0;
                            float x = t.X + (float)Math.Cos(a) * reach, z = t.Z + (float)Math.Sin(a) * reach;
                            if (!ConvergenceFits(plan, terrain, x, z, tb.Y)) continue;
                            made++;
                            float clear = 12f;
                            foreach (var lot in plan.Lots) clear = Math.Min(clear, lot.Distance(x, z));
                            float y = terrain.Height(x, z);
                            plan.ConvergencePoints.Add(new WorldPoint { Name = $"Convergence-{plan.Regions[r].Id}-{made}", Region = r, X = x, Y = y, Z = z, ClearRadius = clear });
                            terrain.AddCirclePad(x, z, 9f, y);
                            break;
                        }
                    }
            }
        }

        static bool ConvergenceFits(WorldPlan plan, IslandTerrain terrain, float x, float z, float townY)
        {
            if (x < 20f || z < 20f || x > plan.Size - 20f || z > 2000f) return false;
            float h = terrain.Height(x, z);
            if (h < plan.SeaLevel + 1.5f || terrain.RoadEdge(x, z) < 14f) return false;
            foreach (var lot in plan.Lots) if (lot.Distance(x, z) < 14f) return false;
            foreach (var e in plan.Elevators) if (e.Pad().Distance(x, z) < 14f) return false;
            foreach (var c in plan.ConvergencePoints) if (IslandMath.Dist(x, z, c.X, c.Z) < 60f) return false;
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4.0;
                float hh = terrain.Height(x + (float)Math.Cos(a) * 8f, z + (float)Math.Sin(a) * 8f);
                if (Math.Abs(hh - h) > 1.6f) return false;
            }
            return true;
        }

        static float YawOf(float dirX, float dirZ)
        {
            float deg = (float)(Math.Atan2(dirX, dirZ) * 180.0 / Math.PI);
            return deg < 0f ? deg + 360f : deg;
        }
    }
}
