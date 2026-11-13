using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Open-world island (ADR-050, ADR-052): spec -> plan, deterministic, no state between calls.
    //
    //                 north (+Z)
    //        +---------------------------+
    //        |        A  mountain        |
    //        |   ridge /--=--=--\ ridge  |      = : pass (two per border), the only way across a ridge
    //        |  B     |    C     |    D  |
    //        | wetland=   town   =factory|
    //        |        |          |       |
    //        +---------------------------+   sea all round
    //
    // Regions are the Voronoi cells of the spec centres. Each region has one compound per elevator (a flat yard
    // with lots along the roads through it); compound 0 is the hub. Roads form a tree in each region (every new
    // road joins the nearest one already there) and a ring between regions (one road through every pass).
    // Hamlets are small outlying clusters at the end of their own lane, without an elevator.
    public static class WorldGenerator
    {
        const float ApproachDistance = 55f;   // trunk roads run straight through a pass from this far out
        const float PassClearance = 50f;      // passes keep this far from any third region's ridge
        const float PassCoast = 70f;
        const float RoadStep = 6f;
        const float DesignSlopeMarginDeg = 3f; // roads are built this much gentler than the limit
        const float Causeway = 0.8f;          // roads stay this far above the water
        const float FirmGround = 1.2f;        // compound yards are at least this far above the water
        const float RidgeMargin = 40f;        // yards end this far from a ridge line (ridge 22 m + yard blend 16 m)
        const float ConvergenceClear = 10f;   // nothing is built this close to a compound's junction
        const float ConvergencePad = 9f;
        const float ElevatorSpacing = 82f;
        const float LotSetback = 5.5f;        // road edge to building front: pad margin 2 m + pad blend 3 m, so pads never tilt a road
        const float LotGap = 7.5f;            // between footprints: two pad margins + the blend between pads of different height
        const float HamletReach = 42f;
        const float DoorstepReach = 30f;      // every elevator gets a building this close (rule: 35 m)
        const float MaxLotSlopeDeg = 20f;
        const float ScatterCell = 8f;

        static readonly float[] CompoundSpacing = { 170f, 150f, 130f, 130f, 115f, 100f };
        static readonly float[] CompoundCoastSlack = { 30f, 30f, 30f, 20f, 15f, 10f };
        static readonly bool[] CompoundNearCentre = { true, true, true, false, false, false };
        static readonly float[] ElevatorRings = { 22f, 26f, 18f, 30f };
        static readonly float[] LotShrink = { 1f, 0.85f, 0.7f };
        static readonly float[] HamletSpacing = { 140f, 110f, 90f };

        // How a road's ends hang on roads solved before it (-1: free, or held by a compound yard).
        sealed class RoadLink
        {
            public int FromRoad = -1, FromPoint, ToRoad = -1, ToPoint;
        }

        public static WorldPlan Generate(WorldSpec spec)
        {
            var plan = new WorldPlan
            {
                Size = spec.Size, SeaLevel = spec.SeaLevel, MaxHeight = spec.MaxHeight,
                MaxRoadSlopeDeg = spec.MaxRoadSlopeDeg, Seed = spec.Seed,
            };
            var regions = spec.Regions ?? new RegionSpec[0];
            var terrain = new WorldTerrain(spec);
            plan.Terrain = terrain;
            uint seed = unchecked((uint)spec.Seed);
            for (int i = 0; i < regions.Length; i++)
                plan.Regions.Add(new WorldRegion
                {
                    Index = i, Id = regions[i].Id, Name = regions[i].Name, Kind = regions[i].KindValue,
                    CentreX = regions[i].CentreX, CentreZ = regions[i].CentreZ,
                    HubX = regions[i].CentreX, HubZ = regions[i].CentreZ,
                    Lots = regions[i].Lots, ConvergencePoints = regions[i].ConvergencePoints,
                });

            // Where things go (ground plan only; no heights yet).
            PlacePasses(spec, plan, terrain, seed);
            terrain.SetPasses(plan.Passes);
            PlaceCompounds(regions, plan, terrain, seed);
            terrain.SetCompounds(plan.SubZones);
            var links = new List<RoadLink>();
            var valleys = new List<float>();
            var valleyRegions = new List<int>();
            BuildTrunks(spec, plan, terrain, links, valleys, valleyRegions);
            BuildSpurs(spec, plan, terrain, links, valleys, valleyRegions);
            var hamlets = PlaceHamlets(spec, regions, plan, terrain, seed, links, valleys, valleyRegions);
            terrain.SetRoads(plan.Roads);
            PlaceElevators(spec, plan, terrain, seed, links);
            terrain.SetRoads(plan.Roads);

            // Heights: natural ground -> pass floors -> yards -> road profiles.
            terrain.SetValleys(valleys, valleyRegions);
            foreach (var pass in plan.Passes)
            {
                float a = terrain.RegionHeight(pass.RegionA, pass.X - pass.DirX * 40f, pass.Z - pass.DirZ * 40f);
                float b = terrain.RegionHeight(pass.RegionB, pass.X + pass.DirX * 40f, pass.Z + pass.DirZ * 40f);
                pass.Y = spec.SeaLevel + Math.Max(1f, (a + b) * 0.5f);
            }
            terrain.SetPasses(plan.Passes);
            foreach (var zone in plan.SubZones)
                zone.Y = Math.Max(terrain.Natural(zone.X, zone.Z), spec.SeaLevel + FirmGround);
            terrain.SetCompounds(plan.SubZones);
            float designSlope = (float)Math.Tan(Math.Max(1f, spec.MaxRoadSlopeDeg - DesignSlopeMarginDeg) * Math.PI / 180.0);
            for (int r = 0; r < plan.Roads.Count; r++)
                SolveRoadProfile(plan, terrain, r, links[r], designSlope);
            terrain.SetRoads(plan.Roads);
            foreach (var pass in plan.Passes) pass.Y = terrain.Height(pass.X, pass.Z);
            foreach (var region in plan.Regions) region.HubY = terrain.Height(region.HubX, region.HubZ);
            foreach (var elevator in plan.Elevators) elevator.Y = plan.SubZones[elevator.SubZone].Y;

            // What stands on the ground. Indoor loot is not placed here: each BuildingPlan brings its own.
            PlaceLots(regions, plan, terrain, seed, hamlets);
            foreach (var elevator in plan.Elevators)
                terrain.AddRectPad(elevator.X, elevator.Z, WorldElevator.PadWidth * 0.5f, WorldElevator.PadDepth * 0.5f, elevator.FacingDeg, elevator.Y);
            foreach (var lot in plan.Lots)
                terrain.AddRectPad(lot.X, lot.Z, lot.SizeX * 0.5f + BuildingLot.PadMargin, lot.SizeZ * 0.5f + BuildingLot.PadMargin, lot.YawDeg, lot.Y);
            PlaceConvergencePoints(regions, plan, terrain);
            Scatter(regions, plan, terrain, seed, true);
            Scatter(regions, plan, terrain, seed, false);
            return plan;
        }

        // ---- passes ----

        // Two regions are neighbours when their border has a long enough stretch well inland; the passes go on that stretch.
        static void PlacePasses(WorldSpec spec, WorldPlan plan, WorldTerrain terrain, uint seed)
        {
            int n = plan.Regions.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var a = plan.Regions[i];
                    var b = plan.Regions[j];
                    float dx = b.CentreX - a.CentreX, dz = b.CentreZ - a.CentreZ;
                    float len = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (len < 1f) continue;
                    float nx = dx / len, nz = dz / len;   // across the border, from a into b
                    float tx = -nz, tz = nx;              // along it
                    float mx = (a.CentreX + b.CentreX) * 0.5f, mz = (a.CentreZ + b.CentreZ) * 0.5f;

                    float bestFrom = 0f, bestTo = -1f, runFrom = 0f;
                    bool inRun = false;
                    for (float u = -plan.Size; u <= plan.Size + 4f; u += 4f)
                    {
                        float x = mx + tx * u, z = mz + tz * u;
                        bool ok = u <= plan.Size && x > 0f && z > 0f && x < plan.Size && z < plan.Size
                            && terrain.Coast(x, z) >= PassCoast && terrain.BorderClearance(x, z, i, j) >= PassClearance;
                        if (ok && !inRun) { inRun = true; runFrom = u; }
                        if (!ok && inRun)
                        {
                            inRun = false;
                            if (u - 4f - runFrom > bestTo - bestFrom) { bestFrom = runFrom; bestTo = u - 4f; }
                        }
                    }
                    float run = bestTo - bestFrom;
                    int count = Math.Max(0, spec.PassesPerBorder);
                    while (count > 1 && run < 80f * (count - 1) + 20f) count--;
                    if (run < 20f) count = 0;
                    for (int q = 0; q < count; q++)
                    {
                        float f = count == 1 ? 0.5f : 0.15f + 0.7f * q / (count - 1);
                        f += (WorldNoise.Hash01(i * 16 + j, q, unchecked(seed + 37u)) - 0.5f) * 0.08f;
                        float u = bestFrom + run * f;
                        plan.Passes.Add(new WorldPass
                        {
                            Name = $"Pass-{a.Id}-{b.Id}-{q + 1}", RegionA = i, RegionB = j,
                            X = mx + tx * u, Z = mz + tz * u, DirX = nx, DirZ = nz,
                            YawDeg = YawOf(nx, nz), Width = spec.PassWidth,
                        });
                    }
                }
        }

        // ---- compounds ----

        static void PlaceCompounds(RegionSpec[] regions, WorldPlan plan, WorldTerrain terrain, uint seed)
        {
            const float step = 16f;
            float offsetX = WorldNoise.Hash01(1, 2, unchecked(seed + 41u)) * step;
            float offsetZ = WorldNoise.Hash01(3, 4, unchecked(seed + 41u)) * step;
            for (int k = 0; k < regions.Length; k++)
            {
                var spec = regions[k];
                float radius = Math.Max(20f, spec.CompoundRadius);
                int need = Math.Max(1, Math.Min(8, spec.Elevators));
                // Hard limits: the yard and its blend stay off the ridges and out of the sea.
                var cx = new List<float>();
                var cz = new List<float>();
                var cc = new List<float>();
                void Consider(float x, float z)
                {
                    if (x < 0f || z < 0f || x > plan.Size || z > plan.Size) return;
                    terrain.Locate(x, z, out int region, out float border);
                    if (region != k || border < radius + RidgeMargin) return;
                    float coast = terrain.Coast(x, z);
                    if (coast < radius + 10f) return;
                    cx.Add(x); cz.Add(z); cc.Add(coast);
                }
                Consider(spec.CentreX, spec.CentreZ);
                bool centreFits = cx.Count == 1;
                for (float z = offsetZ; z < plan.Size; z += step)
                    for (float x = offsetX; x < plan.Size; x += step)
                        Consider(x, z);
                if (cx.Count == 0) continue; // nowhere to build: the rule checker reports the missing elevators

                // The hub is the spec centre, or the nearest buildable spot to it.
                int hub = 0;
                if (!centreFits)
                {
                    float bestD = float.MaxValue;
                    for (int c = 0; c < cx.Count; c++)
                    {
                        float d = Distance(cx[c], cz[c], spec.CentreX, spec.CentreZ);
                        if (d < bestD) { bestD = d; hub = c; }
                    }
                }
                int placed = 0;
                void Take(int c)
                {
                    placed++;
                    plan.SubZones.Add(new WorldSubZone { Name = $"{spec.Id}-{placed}", Region = k, X = cx[c], Z = cz[c], Radius = radius });
                }
                Take(hub);
                plan.Regions[k].HubX = cx[hub];
                plan.Regions[k].HubZ = cz[hub];

                // The rest: as far from every other compound as the region allows, giving ground level by level.
                for (int level = 0; level < CompoundSpacing.Length && placed < need; level++)
                    while (placed < need)
                    {
                        int best = -1;
                        float bestScore = float.MinValue;
                        for (int c = 0; c < cx.Count; c++)
                        {
                            if (cc[c] < radius + CompoundCoastSlack[level]) continue;
                            if (CompoundNearCentre[level] && spec.Radius > 0f && Distance(cx[c], cz[c], spec.CentreX, spec.CentreZ) > spec.Radius) continue;
                            float nearest = float.MaxValue;
                            foreach (var zone in plan.SubZones)
                                nearest = Math.Min(nearest, Distance(cx[c], cz[c], zone.X, zone.Z));
                            if (nearest < CompoundSpacing[level]) continue;
                            float score = Math.Min(nearest, 260f) + 40f * WorldNoise.Hash01((int)cx[c], (int)cz[c], unchecked(seed + 43u));
                            if (score > bestScore) { bestScore = score; best = c; }
                        }
                        if (best < 0) break;
                        Take(best);
                    }
            }
        }

        // ---- roads ----

        // Adds a road through the given corner points, resampled so no piece is longer than RoadStep.
        static int AddRoad(WorldPlan plan, string name, RoadKind kind, float width, int region, int pass, List<float> px, List<float> pz)
        {
            var xs = new List<float> { px[0] };
            var zs = new List<float> { pz[0] };
            for (int s = 1; s < px.Count; s++)
            {
                float ax = xs[xs.Count - 1], az = zs[zs.Count - 1];
                float dx = px[s] - ax, dz = pz[s] - az;
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len < 0.05f) continue;
                int steps = Math.Max(1, (int)Math.Ceiling(len / RoadStep));
                for (int q = 1; q < steps; q++)
                {
                    xs.Add(ax + dx * q / steps);
                    zs.Add(az + dz * q / steps);
                }
                xs.Add(px[s]);
                zs.Add(pz[s]);
            }
            if (xs.Count < 2) return -1;
            plan.Roads.Add(new WorldRoad
            {
                Name = name, Kind = kind, Width = width, Region = region, Pass = pass,
                X = xs.ToArray(), Y = new float[xs.Count], Z = zs.ToArray(),
            });
            return plan.Roads.Count - 1;
        }

        static void AddValleys(WorldTerrain terrain, List<float> px, List<float> pz, List<float> valleys, List<int> valleyRegions)
        {
            for (int s = 1; s < px.Count; s++)
            {
                terrain.Locate((px[s - 1] + px[s]) * 0.5f, (pz[s - 1] + pz[s]) * 0.5f, out int region, out _);
                valleys.Add(px[s - 1]); valleys.Add(pz[s - 1]); valleys.Add(px[s]); valleys.Add(pz[s]);
                valleyRegions.Add(region);
            }
        }

        // Nearest point of the region's road network (footpaths excluded), far enough from the ridges that a straight
        // road to it from anywhere else inside the region's (convex) cell never touches one.
        static bool NearestRoadPoint(WorldPlan plan, WorldTerrain terrain, int region, float x, float z, HashSet<int> among,
            out int road, out int point, out float distance)
        {
            road = -1;
            point = -1;
            distance = float.MaxValue;
            for (int r = 0; r < plan.Roads.Count; r++)
            {
                var candidate = plan.Roads[r];
                if (candidate.Kind == RoadKind.Path || (among != null && !among.Contains(r))) continue;
                for (int i = 0; i < candidate.X.Length; i++)
                {
                    float d = Distance(candidate.X[i], candidate.Z[i], x, z);
                    if (d >= distance) continue;
                    terrain.Locate(candidate.X[i], candidate.Z[i], out int at, out float border);
                    if (at != region || border < PassClearance) continue;
                    distance = d; road = r; point = i;
                }
            }
            return road >= 0;
        }

        // One road straight through every pass; on each side it joins the nearest road the region already has
        // (the hub at first), so a region's roads branch like a tree instead of all fanning out of the hub.
        static void BuildTrunks(WorldSpec spec, WorldPlan plan, WorldTerrain terrain, List<RoadLink> links, List<float> valleys, List<int> valleyRegions)
        {
            var passRoad = new int[plan.Passes.Count];
            for (int p = 0; p < plan.Passes.Count; p++)
            {
                var pass = plan.Passes[p];
                var px = new List<float> { pass.X - pass.DirX * ApproachDistance, pass.X, pass.X + pass.DirX * ApproachDistance };
                var pz = new List<float> { pass.Z - pass.DirZ * ApproachDistance, pass.Z, pass.Z + pass.DirZ * ApproachDistance };
                passRoad[p] = AddRoad(plan, "Trunk-" + pass.Name.Substring(5), RoadKind.Trunk, spec.RoadWidth, -1, p, px, pz);
                if (passRoad[p] < 0) continue;
                links.Add(new RoadLink());
                AddValleys(terrain, px, pz, valleys, valleyRegions);
            }
            for (int k = 0; k < plan.Regions.Count; k++)
            {
                var region = plan.Regions[k];
                var network = new HashSet<int>();
                var waiting = new List<int>();
                for (int p = 0; p < plan.Passes.Count; p++)
                    if (passRoad[p] >= 0 && (plan.Passes[p].RegionA == k || plan.Passes[p].RegionB == k)) waiting.Add(p);
                // Nearest pass first: the far ones then find a road to join on their way in.
                while (waiting.Count > 0)
                {
                    int next = 0, nextEnd = 0;
                    float nextD = float.MaxValue;
                    for (int w = 0; w < waiting.Count; w++)
                    {
                        var candidate = plan.Roads[passRoad[waiting[w]]];
                        int e = plan.Passes[waiting[w]].RegionA == k ? 0 : candidate.X.Length - 1;
                        float d = Distance(candidate.X[e], candidate.Z[e], region.HubX, region.HubZ);
                        if (d < nextD) { nextD = d; next = w; nextEnd = e; }
                    }
                    int from = passRoad[waiting[next]];
                    string name = "Link-" + region.Id + "-" + plan.Passes[waiting[next]].Name.Substring(5);
                    waiting.RemoveAt(next);
                    float ex = plan.Roads[from].X[nextEnd], ez = plan.Roads[from].Z[nextEnd];
                    var link = new RoadLink { FromRoad = from, FromPoint = nextEnd };
                    float tx = region.HubX, tz = region.HubZ;
                    if (NearestRoadPoint(plan, terrain, k, ex, ez, network, out int road, out int point, out float d2) && d2 < nextD)
                    {
                        tx = plan.Roads[road].X[point];
                        tz = plan.Roads[road].Z[point];
                        link.ToRoad = road;
                        link.ToPoint = point;
                    }
                    network.Add(from);
                    var px = new List<float> { ex, tx };
                    var pz = new List<float> { ez, tz };
                    int added = AddRoad(plan, name, RoadKind.Trunk, spec.RoadWidth, k, -1, px, pz);
                    if (added < 0) continue;
                    links.Add(link);
                    network.Add(added);
                    AddValleys(terrain, px, pz, valleys, valleyRegions);
                }
            }
        }

        // A side road from the nearest road of the region to a place; nothing when a road already passes within reach.
        static void AddSpur(WorldSpec spec, WorldPlan plan, WorldTerrain terrain, string name, int region, float x, float z, float reach,
            List<RoadLink> links, List<float> valleys, List<int> valleyRegions)
        {
            var link = new RoadLink();
            float fromX = plan.Regions[region].HubX, fromZ = plan.Regions[region].HubZ;
            if (NearestRoadPoint(plan, terrain, region, x, z, null, out int road, out int point, out float d))
            {
                if (d < reach) return;
                fromX = plan.Roads[road].X[point];
                fromZ = plan.Roads[road].Z[point];
                link.FromRoad = road;
                link.FromPoint = point;
            }
            var px = new List<float> { fromX, x };
            var pz = new List<float> { fromZ, z };
            if (AddRoad(plan, name, RoadKind.Spur, spec.SpurWidth, region, -1, px, pz) < 0) return;
            links.Add(link);
            AddValleys(terrain, px, pz, valleys, valleyRegions);
        }

        // Every compound that is not on a road yet gets a spur from the nearest one.
        static void BuildSpurs(WorldSpec spec, WorldPlan plan, WorldTerrain terrain, List<RoadLink> links, List<float> valleys, List<int> valleyRegions)
        {
            foreach (var zone in plan.SubZones)
            {
                bool reached = false;
                foreach (var road in plan.Roads)
                    for (int i = 0; i < road.X.Length && !reached; i++)
                        reached = Distance(road.X[i], road.Z[i], zone.X, zone.Z) < 0.5f;
                if (!reached) AddSpur(spec, plan, terrain, "Spur-" + zone.Name, zone.Region, zone.X, zone.Z, 0.5f, links, valleys, valleyRegions);
            }
        }

        // Hamlets fill the parts of a region the compounds leave empty: x, z, region triples.
        static List<float> PlaceHamlets(WorldSpec spec, RegionSpec[] regions, WorldPlan plan, WorldTerrain terrain, uint seed,
            List<RoadLink> links, List<float> valleys, List<int> valleyRegions)
        {
            const float step = 16f;
            var hamlets = new List<float>();
            float offsetX = WorldNoise.Hash01(5, 6, unchecked(seed + 45u)) * step;
            float offsetZ = WorldNoise.Hash01(7, 8, unchecked(seed + 45u)) * step;
            for (int k = 0; k < regions.Length; k++)
            {
                int made = 0;
                for (int level = 0; level < HamletSpacing.Length && made < regions[k].Hamlets; level++)
                    while (made < regions[k].Hamlets)
                    {
                        float bestX = 0f, bestZ = 0f, bestScore = float.MinValue;
                        for (float z = offsetZ; z < plan.Size; z += step)
                            for (float x = offsetX; x < plan.Size; x += step)
                            {
                                terrain.Locate(x, z, out int region, out float border);
                                if (region != k || border < 65f || terrain.Coast(x, z) < 45f) continue;
                                float nearest = float.MaxValue;
                                bool crowded = false;
                                foreach (var zone in plan.SubZones)
                                {
                                    float d = Distance(x, z, zone.X, zone.Z);
                                    crowded |= d < zone.Radius + 60f;
                                    nearest = Math.Min(nearest, d);
                                }
                                for (int h = 0; h < hamlets.Count; h += 3)
                                {
                                    float d = Distance(x, z, hamlets[h], hamlets[h + 1]);
                                    crowded |= d < HamletSpacing[level];
                                    nearest = Math.Min(nearest, d);
                                }
                                if (crowded) continue;
                                float score = Math.Min(nearest, 260f) + 40f * WorldNoise.Hash01((int)x, (int)z, unchecked(seed + 47u));
                                if (score > bestScore) { bestScore = score; bestX = x; bestZ = z; }
                            }
                        if (bestScore == float.MinValue) break;
                        made++;
                        hamlets.Add(bestX); hamlets.Add(bestZ); hamlets.Add(k);
                        AddSpur(spec, plan, terrain, $"Lane-{regions[k].Id}-{made}", k, bestX, bestZ, 12f, links, valleys, valleyRegions);
                    }
            }
            return hamlets;
        }

        // Road surface heights: follow the ground, exactly level inside compound yards, never steeper than maxSlope.
        static void SolveRoadProfile(WorldPlan plan, WorldTerrain terrain, int index, RoadLink link, float maxSlope)
        {
            var road = plan.Roads[index];
            int n = road.X.Length;
            var along = new float[n];
            var target = new float[n];
            var pinned = new bool[n];
            var high = new float[n];
            var low = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i > 0) along[i] = along[i - 1] + Distance(road.X[i], road.Z[i], road.X[i - 1], road.Z[i - 1]);
                pinned[i] = terrain.CompoundAt(road.X[i], road.Z[i], out float yard);
                target[i] = pinned[i] ? yard : Math.Max(terrain.Ground(road.X[i], road.Z[i]), plan.SeaLevel + Causeway);
            }
            // A road starts and ends at the height of the roads it joins.
            if (link.FromRoad >= 0 && link.FromRoad < index && !pinned[0])
            {
                pinned[0] = true;
                target[0] = plan.Roads[link.FromRoad].Y[link.FromPoint];
            }
            if (link.ToRoad >= 0 && link.ToRoad < index && !pinned[n - 1])
            {
                pinned[n - 1] = true;
                target[n - 1] = plan.Roads[link.ToRoad].Y[link.ToPoint];
            }
            // The slope limit turns every pinned height into a cone the rest of the road must stay inside.
            for (int i = 0; i < n; i++)
            {
                high[i] = pinned[i] ? target[i] : float.MaxValue;
                low[i] = pinned[i] ? target[i] : float.MinValue;
            }
            Envelope(high, along, maxSlope, true);
            Envelope(low, along, maxSlope, false);
            for (int i = 0; i < n; i++)
            {
                if (pinned[i]) continue;
                if (low[i] > high[i]) target[i] = (low[i] + high[i]) * 0.5f;
                else target[i] = Math.Min(high[i], Math.Max(low[i], target[i]));
            }
            // Cut (lower envelope) and fill (upper envelope) in equal parts; both obey the limit, so their mean does.
            for (int i = 0; i < n; i++) { high[i] = target[i]; low[i] = target[i]; }
            Envelope(high, along, maxSlope, true);
            Envelope(low, along, maxSlope, false);
            for (int i = 0; i < n; i++) road.Y[i] = (high[i] + low[i]) * 0.5f;
        }

        // Largest slope-limited profile below the values (lower = true) or smallest one above them.
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

        // ---- elevators ----

        // One elevator per compound, on the yard beside the junction, with a footpath to it.
        static void PlaceElevators(WorldSpec spec, WorldPlan plan, WorldTerrain terrain, uint seed, List<RoadLink> links)
        {
            float halfWidth = WorldElevator.PadWidth * 0.5f, halfDepth = WorldElevator.PadDepth * 0.5f;
            for (int c = 0; c < plan.SubZones.Count; c++)
            {
                var zone = plan.SubZones[c];
                float baseDeg = WorldNoise.Hash01(c, 7, unchecked(seed + 51u)) * 360f;
                bool placed = false;
                float x = 0f, z = 0f, facing = 0f;
                // Give up road clearance first, spacing last.
                for (int relax = 0; relax < 3 && !placed; relax++)
                    for (int ring = 0; ring < ElevatorRings.Length && !placed; ring++)
                    {
                        if (ElevatorRings[ring] > zone.Radius - 5f) continue;
                        for (int a = 0; a < 24 && !placed; a++)
                        {
                            float deg = baseDeg + a * 15f;
                            double rad = deg * Math.PI / 180.0;
                            x = zone.X + (float)Math.Sin(rad) * ElevatorRings[ring];
                            z = zone.Z + (float)Math.Cos(rad) * ElevatorRings[ring];
                            facing = (deg + 180f) % 360f;
                            if (relax < 2)
                            {
                                bool crowded = false;
                                foreach (var other in plan.Elevators)
                                    if (Distance(x, z, other.X, other.Z) < ElevatorSpacing) { crowded = true; break; }
                                if (crowded) continue;
                            }
                            if (relax < 1 && !ClearOfRoads(terrain, x, z, halfWidth, halfDepth, facing, 1.5f)) continue;
                            placed = true;
                        }
                    }
                if (!placed) continue;
                plan.Elevators.Add(new WorldElevator { Name = "Elevator-" + zone.Name, Region = zone.Region, SubZone = c, X = x, Z = z, FacingDeg = facing });
                double f = facing * Math.PI / 180.0;
                var px = new List<float> { x + (float)Math.Sin(f) * halfDepth, zone.X };
                var pz = new List<float> { z + (float)Math.Cos(f) * halfDepth, zone.Z };
                if (AddRoad(plan, "Path-" + zone.Name, RoadKind.Path, spec.PathWidth, zone.Region, -1, px, pz) < 0) continue;
                links.Add(new RoadLink());
            }
        }

        // Is a rectangle at least clearance away from every road edge? (perimeter sampled every 3 m or less)
        static bool ClearOfRoads(WorldTerrain terrain, float x, float z, float halfX, float halfZ, float yawDeg, float clearance)
        {
            double yaw = yawDeg * Math.PI / 180.0;
            float cos = (float)Math.Cos(yaw), sin = (float)Math.Sin(yaw);
            bool Clear(float right, float forward) =>
                terrain.RoadEdge(x + right * cos + forward * sin, z - right * sin + forward * cos) >= clearance;
            if (!Clear(0f, 0f)) return false;
            int nx = Math.Max(1, (int)Math.Ceiling(halfX * 2f / 3f)), nz = Math.Max(1, (int)Math.Ceiling(halfZ * 2f / 3f));
            for (int i = 0; i <= nx; i++)
            {
                float right = -halfX + halfX * 2f * i / nx;
                if (!Clear(right, halfZ) || !Clear(right, -halfZ)) return false;
            }
            for (int i = 0; i <= nz; i++)
            {
                float forward = -halfZ + halfZ * 2f * i / nz;
                if (!Clear(halfX, forward) || !Clear(-halfX, forward)) return false;
            }
            return true;
        }

        // ---- lots ----

        static void LotShape(RegionKind region, bool hamlet, int index, float h1, float h2, out BuildingKind kind, out float sizeX, out float sizeZ)
        {
            switch (region)
            {
                case RegionKind.Factory:
                    kind = hamlet ? (index == 0 ? BuildingKind.Warehouse : BuildingKind.Shed)
                        : index % 5 == 0 ? BuildingKind.FactoryHall : index % 2 == 1 ? BuildingKind.Warehouse : BuildingKind.Shed;
                    break;
                case RegionKind.Mountain:
                    kind = index == 0 ? BuildingKind.House : BuildingKind.Shed; // a cabin and its outbuildings
                    break;
                case RegionKind.Wetland:
                    kind = index % 2 == (hamlet ? 0 : 1) ? BuildingKind.House : BuildingKind.Shed;
                    break;
                default:
                    kind = hamlet ? (index == 2 ? BuildingKind.Shed : BuildingKind.House)
                        : index == 3 ? BuildingKind.Shed : index == 6 ? BuildingKind.Warehouse : BuildingKind.House;
                    break;
            }
            switch (kind)
            {
                case BuildingKind.FactoryHall: sizeX = 26f + 6f * h1; sizeZ = 15f + 3f * h2; break;
                case BuildingKind.Warehouse: sizeX = 16f + 5f * h1; sizeZ = 10.5f + 2.5f * h2; break;
                case BuildingKind.Shed: sizeX = 4f + 2f * h1; sizeZ = 4f + 1.5f * h2; break;
                default:
                    bool small = region == RegionKind.Mountain || region == RegionKind.Wetland;
                    sizeX = small ? 8f + 1.5f * h1 : 9f + 4f * h1;
                    sizeZ = small ? 7f + h2 : 7.5f + 2.5f * h2;
                    break;
            }
        }

        // Lots line the roads, fronts to the road: in every compound yard, then in the hamlets.
        static void PlaceLots(RegionSpec[] regions, WorldPlan plan, WorldTerrain terrain, uint seed, List<float> hamlets)
        {
            for (int c = 0; c < plan.SubZones.Count; c++)
            {
                var zone = plan.SubZones[c];
                WorldElevator elevator = null;
                foreach (var e in plan.Elevators)
                    if (e.SubZone == c) elevator = e;
                for (int j = 0; j < regions[zone.Region].CompoundLots; j++)
                {
                    LotShape(plan.Regions[zone.Region].Kind, false, j, WorldNoise.Hash01(c, j, unchecked(seed + 55u)), WorldNoise.Hash01(c, j, unchecked(seed + 57u)),
                        out var kind, out float sizeX, out float sizeZ);
                    var lot = new BuildingLot { Name = $"Lot-{zone.Name}-{j + 1}", Kind = kind, Region = zone.Region, SubZone = c };
                    // The first two go as near the elevator as they can: somewhere to run to on stepping out.
                    bool nearElevator = j < 2 && elevator != null;
                    float sortX = nearElevator ? elevator.X : zone.X, sortZ = nearElevator ? elevator.Z : zone.Z;
                    bool placed = TryPlaceLot(plan, terrain, lot, zone, zone.X, zone.Z, zone.Radius, sortX, sortZ, sizeX, sizeZ);
                    if (j == 0 && elevator != null && (!placed || lot.Distance(elevator.X, elevator.Z) > DoorstepReach))
                    {
                        // Nothing along the roads is close enough to the elevator: put this one right beside it instead.
                        var beside = new BuildingLot { Name = lot.Name, Kind = lot.Kind, Region = lot.Region, SubZone = c, SizeX = lot.SizeX, SizeZ = lot.SizeZ };
                        if (TryRing(plan, terrain, beside, zone, elevator.X, elevator.Z, 14f, DoorstepReach))
                        {
                            lot = beside;
                            placed = true;
                        }
                    }
                    if (placed) plan.Lots.Add(lot);
                }
            }
            for (int h = 0; h < hamlets.Count; h += 3)
            {
                int k = (int)hamlets[h + 2];
                for (int j = 0; j < regions[k].HamletLots; j++)
                {
                    LotShape(plan.Regions[k].Kind, true, j, WorldNoise.Hash01(h, j, unchecked(seed + 65u)), WorldNoise.Hash01(h, j, unchecked(seed + 67u)),
                        out var kind, out float sizeX, out float sizeZ);
                    int subZone = -1;
                    float nearest = float.MaxValue;
                    for (int c = 0; c < plan.SubZones.Count; c++)
                    {
                        float d = Distance(hamlets[h], hamlets[h + 1], plan.SubZones[c].X, plan.SubZones[c].Z);
                        if (plan.SubZones[c].Region == k && d < nearest) { nearest = d; subZone = c; }
                    }
                    var lot = new BuildingLot { Name = $"Lot-{regions[k].Id}-H{h / 3 + 1}-{j + 1}", Kind = kind, Region = k, SubZone = subZone };
                    if (TryPlaceLot(plan, terrain, lot, null, hamlets[h], hamlets[h + 1], HamletReach, hamlets[h], hamlets[h + 1], sizeX, sizeZ)) plan.Lots.Add(lot);
                }
            }
        }

        // Finds the lot a place beside a road within reach of the centre, as near (sortX, sortZ) as possible.
        // zone: the compound yard the lot must stay inside; null for a hamlet lot on open ground.
        static bool TryPlaceLot(WorldPlan plan, WorldTerrain terrain, BuildingLot lot, WorldSubZone zone, float centreX, float centreZ, float reach,
            float sortX, float sortZ, float sizeX, float sizeZ)
        {
            Buildings.FootprintLimits(lot.Kind, out float minX, out float minZ, out float maxX, out float maxZ);
            foreach (float shrink in LotShrink)
            {
                lot.SizeX = Math.Min(maxX, Math.Max(minX, sizeX * shrink));
                lot.SizeZ = Math.Min(maxZ, Math.Max(minZ, sizeZ * shrink));
                bool found = false;
                float bestKey = float.MaxValue, bestX = 0f, bestZ = 0f, bestY = 0f, bestYaw = 0f, bestSlope = 0f;
                foreach (var road in plan.Roads)
                {
                    if (road.Kind == RoadKind.Path) continue;
                    int n = road.X.Length;
                    for (int i = 0; i < n; i++)
                    {
                        if (Distance(road.X[i], road.Z[i], centreX, centreZ) > reach) continue;
                        int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                        float tx = road.X[b] - road.X[a], tz = road.Z[b] - road.Z[a];
                        float length = (float)Math.Sqrt(tx * tx + tz * tz);
                        if (length < 0.01f) continue;
                        float offset = road.Width * 0.5f + LotSetback + lot.SizeZ * 0.5f;
                        for (int side = -1; side <= 1; side += 2)
                        {
                            float nx = -tz / length * side, nz = tx / length * side;
                            lot.X = road.X[i] + nx * offset;
                            lot.Z = road.Z[i] + nz * offset;
                            float key = Distance(lot.X, lot.Z, sortX, sortZ);
                            if (key >= bestKey) continue;
                            lot.YawDeg = YawOf(-nx, -nz);
                            lot.Y = road.Y[i];
                            if (!LotFits(plan, terrain, lot, zone)) continue;
                            found = true;
                            bestKey = key; bestX = lot.X; bestZ = lot.Z; bestY = lot.Y; bestYaw = lot.YawDeg; bestSlope = lot.GroundSlopeDeg;
                        }
                    }
                }
                if (found)
                {
                    lot.X = bestX; lot.Z = bestZ; lot.Y = bestY; lot.YawDeg = bestYaw; lot.GroundSlopeDeg = bestSlope;
                    return true;
                }
                // No room left along the roads of the yard: stand it in a ring round the junction, front to the nearest road.
                if (zone != null && TryRing(plan, terrain, lot, zone, zone.X, zone.Z, ConvergenceClear + BuildingLot.PadMargin + lot.SizeZ * 0.5f, zone.Radius)) return true;
            }
            return false;
        }

        // Tries rings of positions round a point inside a compound yard; the lot fronts the nearest road.
        static bool TryRing(WorldPlan plan, WorldTerrain terrain, BuildingLot lot, WorldSubZone zone, float aroundX, float aroundZ, float nearest, float farthest)
        {
            float startDeg = WorldNoise.Hash01((int)lot.SizeX * 31 + plan.Lots.Count, 3, unchecked((uint)plan.Seed + 59u)) * 360f;
            for (float ring = nearest; ring <= farthest; ring += 3f)
                for (int step = 0; step < 24; step++)
                {
                    double rad = (startDeg + step * 15f) * Math.PI / 180.0;
                    lot.X = aroundX + (float)Math.Sin(rad) * ring;
                    lot.Z = aroundZ + (float)Math.Cos(rad) * ring;
                    lot.Y = zone.Y;
                    float toX = zone.X, toZ = zone.Z, toD = float.MaxValue;
                    foreach (var road in plan.Roads)
                        for (int i = 0; i < road.X.Length; i++)
                        {
                            float d = Distance(road.X[i], road.Z[i], lot.X, lot.Z);
                            if (d < toD) { toD = d; toX = road.X[i]; toZ = road.Z[i]; }
                        }
                    lot.YawDeg = YawOf(toX - lot.X, toZ - lot.Z);
                    if (LotFits(plan, terrain, lot, zone)) return true;
                }
            return false;
        }

        // May a lot stand here? Its pad must clear roads, other pads, the junction plaza, ridges and water.
        static bool LotFits(WorldPlan plan, WorldTerrain terrain, BuildingLot lot, WorldSubZone zone)
        {
            float bound = 0.5f * (float)Math.Sqrt(lot.SizeX * lot.SizeX + lot.SizeZ * lot.SizeZ);
            if (zone != null)
            {
                if (Distance(lot.X, lot.Z, zone.X, zone.Z) + bound + BuildingLot.PadMargin > zone.Radius) return false;
                if (lot.Distance(zone.X, zone.Z) < ConvergenceClear + BuildingLot.PadMargin) return false;
            }
            else
            {
                terrain.Locate(lot.X, lot.Z, out int region, out float border);
                if (region != lot.Region || border < 45f + bound || terrain.Coast(lot.X, lot.Z) < 22f + bound) return false;
                foreach (var yard in plan.SubZones)
                    if (Distance(lot.X, lot.Z, yard.X, yard.Z) < yard.Radius + WorldTerrain.DiscBlend + bound + 3f) return false;
                if (Math.Abs(terrain.Ground(lot.X, lot.Z) - lot.Y) > 2f) return false; // no terraces or pits
            }
            if (!ClearOfRoads(terrain, lot.X, lot.Z, lot.SizeX * 0.5f + BuildingLot.PadMargin, lot.SizeZ * 0.5f + BuildingLot.PadMargin, lot.YawDeg, WorldTerrain.PadBlend))
                return false;
            foreach (var other in plan.Lots)
                if (BuildingLot.Overlap(lot, other, LotGap)) return false;
            foreach (var elevator in plan.Elevators)
                if (BuildingLot.Overlap(lot, elevator.Pad(), LotGap - BuildingLot.PadMargin)) return false;
            float gx = (terrain.Natural(lot.X + 4f, lot.Z) - terrain.Natural(lot.X - 4f, lot.Z)) / 8f;
            float gz = (terrain.Natural(lot.X, lot.Z + 4f) - terrain.Natural(lot.X, lot.Z - 4f)) / 8f;
            lot.GroundSlopeDeg = (float)(Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180.0 / Math.PI);
            return lot.GroundSlopeDeg <= MaxLotSlopeDeg;
        }

        // ---- gameplay points ----

        // The junction of a compound is its convergence point: roads from every side, buildings all round.
        static void PlaceConvergencePoints(RegionSpec[] regions, WorldPlan plan, WorldTerrain terrain)
        {
            for (int k = 0; k < regions.Length; k++)
            {
                int made = 0;
                for (int c = 0; c < plan.SubZones.Count && made < regions[k].ConvergencePoints; c++)
                {
                    var zone = plan.SubZones[c];
                    if (zone.Region != k) continue;
                    float clear = 12f;
                    foreach (var lot in plan.Lots) clear = Math.Min(clear, lot.Distance(zone.X, zone.Z));
                    foreach (var elevator in plan.Elevators) clear = Math.Min(clear, elevator.Pad().Distance(zone.X, zone.Z));
                    made++;
                    plan.ConvergencePoints.Add(new WorldPoint
                    {
                        Name = $"Convergence-{regions[k].Id}-{made}", Region = k, X = zone.X, Y = zone.Y, Z = zone.Z, ClearRadius = clear,
                    });
                    terrain.AddCirclePad(zone.X, zone.Z, ConvergencePad, zone.Y);
                }
            }
        }

        // Rocks (cover) and trees on a jittered grid: never on roads, pads or water, thinned by region density.
        static void Scatter(RegionSpec[] regions, WorldPlan plan, WorldTerrain terrain, uint seed, bool rocks)
        {
            uint salt = unchecked(seed + (rocks ? 81u : 91u));
            float maxSlope = rocks ? 0.7f : 0.58f;           // 35 deg, 30 deg
            float cellsPerHectare = 10000f / (ScatterCell * ScatterCell);
            int cells = (int)(plan.Size / ScatterCell);
            var list = rocks ? plan.Rocks : plan.Trees;
            for (int gz = 0; gz < cells; gz++)
                for (int gx = 0; gx < cells; gx++)
                {
                    float x = (gx + WorldNoise.Hash01(gx, gz, salt)) * ScatterCell;
                    float z = (gz + WorldNoise.Hash01(gx, gz, unchecked(salt + 1u))) * ScatterCell;
                    if (terrain.Coast(x, z) < 4f) continue;
                    terrain.Locate(x, z, out int region, out _);
                    if (region < 0 || region >= regions.Length) continue;
                    float chance = (rocks ? regions[region].RocksPerHectare : regions[region].TreesPerHectare) / cellsPerHectare;
                    // Trees gather into woods and leave clearings.
                    if (!rocks) chance *= 2f * WorldTerrain.Smooth01(0.5f + 0.9f * WorldNoise.Value(x / 60f, z / 60f, unchecked(salt + 2u)));
                    if (WorldNoise.Hash01(gx, gz, unchecked(salt + 3u)) >= chance) continue;
                    if (terrain.RoadEdge(x, z) < 3f || terrain.PadDistance(x, z) < 3f) continue;
                    float h = terrain.Height(x, z);
                    if (h < plan.SeaLevel + 0.4f) continue;
                    if (!rocks && h > plan.SeaLevel + 75f) continue; // tree line
                    float sx = (terrain.Height(x + 1.5f, z) - h) / 1.5f, sz = (terrain.Height(x, z + 1.5f) - h) / 1.5f;
                    if (sx * sx + sz * sz > maxSlope * maxSlope) continue;
                    float size = WorldNoise.Hash01(gx, gz, unchecked(salt + 4u));
                    list.Add(new ScatterPoint
                    {
                        X = x, Y = h, Z = z, Region = region,
                        Scale = rocks ? 0.6f + 1.6f * size * size : 0.8f + 0.5f * size,
                        YawDeg = WorldNoise.Hash01(gx, gz, unchecked(salt + 5u)) * 360f,
                    });
                }
        }

        // ---- small helpers ----

        static float Distance(float ax, float az, float bx, float bz) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        static float YawOf(float dirX, float dirZ)
        {
            float deg = (float)(Math.Atan2(dirX, dirZ) * 180.0 / Math.PI);
            return deg < 0f ? deg + 360f : deg;
        }
    }
}
