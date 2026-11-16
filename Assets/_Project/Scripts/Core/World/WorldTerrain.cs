using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The height function of a generated world, frozen at generation time. Layers, bottom to top:
    //   natural ground (island falloff, region relief, border ridges with pass gaps)
    //   -> compound yards (flat discs) -> roads (flat core, blended shoulder) -> pads (lots, elevators, plazas).
    internal class WorldTerrain
    {
        public const float ShoreRamp = 70f;       // land rises from the waterline to its region height over this distance
        public const float SeaRamp = 40f;         // seabed reaches full depth this far offshore
        public const float RidgeSeaReach = 12f;   // ridges stay full height this far into the sea, so nobody wades round them
        public const float DiscBlend = 16f;
        public const float RoadCore = 1f;         // flat this far beyond the road edge
        public const float RoadShoulder = 10f;
        public const float PadBlend = 3f;
        public const float MaxPoolDepth = 0.55f;  // wetland pools stay wadeable (rule: 0.6 m)
        public const float SeaDepth = 8f;
        // Border ridges (see Natural): the centre line wanders, the crest and the width change along it.
        const float CrestNoise = 8f;              // crest variation of the straight ridge beside a pass
        const float RidgeMeander = 11f;           // the centre line strays this far from the straight border
        const float RidgeCrestRange = 20f;        // crest height varies by this much along the ridge
        const float RidgeMinRise = 15f;           // the lowest crest above the highest region floor beside it
        const float RidgeMaxHalfWidth = 40f;
        const float RidgeRough = 3.5f;            // broken profile: ribs and notches on the flanks
        const float PassCalm = 45f;               // within this distance of a pass the ridge is the plain straight one
        const float MountainRise = 100f, Foothills = 5f;
        const float MassifRise = 160f, MassifOffset = 0.45f;             // Style "Ridge": the massif reaches full height this far from a road or yard
        const float TownHills = 17f, FactoryHills = 13f, WetlandHills = 5f, HillsAtRidge = 0.35f;
        const float GridCell = 16f;

        readonly float size, half, sea, seabedFloor, maxHeight, islandRadius, coastNoise, coastBays, ridgeHeight, ridgeHalf;
        readonly uint seed;
        // Bays and headlands (ADR-070): waves round the coast with seeded phases; the slow ones bend the whole outline,
        // the quicker ones break up its long straight sides.
        static readonly int[] BayWaves = { 2, 3, 4, 5, 7, 9 };
        static readonly float[] BayShare = { 0.3f, 0.24f, 0.16f, 0.11f, 0.11f, 0.08f };
        readonly float[] bayPhase = new float[BayShare.Length];
        readonly int regionCount;
        readonly float[] cx, cz, baseH, relief;
        readonly RegionKind[] kind;
        readonly MountainTerrain[] massif;     // per region; null unless the region is a mountain with Style "Ridge"
        readonly float[,] centreDist;

        float[] passX = new float[0], passZ = new float[0], passHalf = new float[0], passSaddle = new float[0];
        float[] valAx = new float[0], valAz = new float[0], valBx = new float[0], valBz = new float[0];
        int[] valRegion = new int[0];
        float[] discX = new float[0], discZ = new float[0], discR = new float[0], discH = new float[0];
        int[] discRegion = new int[0];
        float[] segAx = new float[0], segAz = new float[0], segAy = new float[0];
        float[] segBx = new float[0], segBz = new float[0], segBy = new float[0], segHalf = new float[0];
        int[][] roadGrid;
        int roadCells;
        readonly List<float> padX = new List<float>(), padZ = new List<float>(), padHalfX = new List<float>(), padHalfZ = new List<float>();
        readonly List<float> padCos = new List<float>(), padSin = new List<float>(), padBound = new List<float>(), padH = new List<float>();
        readonly List<bool> padRound = new List<bool>();

        public float Sea => sea;

        public WorldTerrain(WorldSpec spec)
        {
            size = spec.Size;
            half = spec.Size * 0.5f;
            sea = spec.SeaLevel;
            seabedFloor = Math.Max(0f, sea - SeaDepth);
            maxHeight = spec.MaxHeight;
            islandRadius = spec.IslandRadius;
            coastNoise = spec.CoastNoise;
            coastBays = spec.CoastBays;
            ridgeHeight = spec.RidgeHeight;
            ridgeHalf = Math.Max(1f, spec.RidgeHalfWidth);
            seed = unchecked((uint)spec.Seed);
            for (int k = 0; k < bayPhase.Length; k++)
                bayPhase[k] = WorldNoise.Hash01(k, 0, unchecked(seed + 15u)) * 2f * (float)Math.PI;
            var regions = spec.Regions ?? new RegionSpec[0];
            regionCount = regions.Length;
            cx = new float[regionCount];
            cz = new float[regionCount];
            baseH = new float[regionCount];
            relief = new float[regionCount];
            kind = new RegionKind[regionCount];
            massif = new MountainTerrain[regionCount];
            centreDist = new float[regionCount, regionCount];
            for (int i = 0; i < regionCount; i++)
            {
                cx[i] = regions[i].CentreX;
                cz[i] = regions[i].CentreZ;
                kind[i] = regions[i].KindValue;
                baseH[i] = regions[i].BaseHeight;
                relief[i] = regions[i].Relief;
                // Peaks may reach MaxHeight but never pass it (the terrain asset clips there).
                if (kind[i] == RegionKind.Mountain)
                    relief[i] = Math.Max(0f, Math.Min(relief[i], maxHeight - sea - baseH[i] - 6f));
                if (kind[i] == RegionKind.Mountain && string.Equals(regions[i].Style, "Ridge", StringComparison.OrdinalIgnoreCase))
                    {
                    // The roads and yards start at the region centre, so the massif stands off to one side of it.
                    float a = WorldNoise.Hash01(i, 3, unchecked(seed + 4099u)) * 6.2831853f;
                    float off = MassifOffset * regions[i].Radius;
                    massif[i] = new MountainTerrain(unchecked(seed + (uint)i * 7919u), cx[i] + off * (float)Math.Cos(a), cz[i] + off * (float)Math.Sin(a), regions[i].Radius, relief[i]);
                }
            }
            for (int i = 0; i < regionCount; i++)
                for (int j = 0; j < regionCount; j++)
                {
                    float dx = cx[i] - cx[j], dz = cz[i] - cz[j];
                    centreDist[i, j] = Math.Max(0.001f, (float)Math.Sqrt(dx * dx + dz * dz));
                }
        }

        public static float Smooth01(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }

        // ---- frozen inputs, set by the generator as it goes ----

        public void SetPasses(List<WorldPass> passes)
        {
            int n = passes.Count;
            passX = new float[n]; passZ = new float[n]; passHalf = new float[n]; passSaddle = new float[n];
            for (int i = 0; i < n; i++)
            {
                passX[i] = passes[i].X;
                passZ[i] = passes[i].Z;
                passHalf[i] = passes[i].Width * 0.5f;
                passSaddle[i] = passes[i].Y;
            }
        }

        // Valley floors (mountain relief fades to nothing along them): quads of ax, az, bx, bz.
        public void SetValleys(List<float> quads, List<int> regions)
        {
            int n = regions.Count;
            valAx = new float[n]; valAz = new float[n]; valBx = new float[n]; valBz = new float[n];
            valRegion = regions.ToArray();
            for (int i = 0; i < n; i++)
            {
                valAx[i] = quads[i * 4];
                valAz[i] = quads[i * 4 + 1];
                valBx[i] = quads[i * 4 + 2];
                valBz[i] = quads[i * 4 + 3];
            }
        }

        public void SetCompounds(List<WorldSubZone> compounds)
        {
            int n = compounds.Count;
            discX = new float[n]; discZ = new float[n]; discR = new float[n]; discH = new float[n];
            discRegion = new int[n];
            for (int i = 0; i < n; i++)
            {
                discX[i] = compounds[i].X;
                discZ[i] = compounds[i].Z;
                discR[i] = compounds[i].Radius;
                discH[i] = compounds[i].Y;
                discRegion[i] = compounds[i].Region;
            }
        }

        public void SetRoads(List<WorldRoad> roads)
        {
            int total = 0;
            foreach (var road in roads) total += Math.Max(0, road.X.Length - 1);
            segAx = new float[total]; segAz = new float[total]; segAy = new float[total];
            segBx = new float[total]; segBz = new float[total]; segBy = new float[total];
            segHalf = new float[total];
            roadCells = (int)Math.Ceiling(size / GridCell) + 1;
            var lists = new List<int>[roadCells * roadCells];
            int s = 0;
            foreach (var road in roads)
            {
                float halfWidth = road.Width * 0.5f;
                float reach = halfWidth + RoadShoulder + 0.5f;
                for (int i = 0; i + 1 < road.X.Length; i++, s++)
                {
                    if (road.Bridge != null && i < road.Bridge.Length && road.Bridge[i]) continue;   // a deck: the ground under it is the riverbed, not the road
                    segAx[s] = road.X[i]; segAz[s] = road.Z[i]; segAy[s] = road.Y[i];
                    segBx[s] = road.X[i + 1]; segBz[s] = road.Z[i + 1]; segBy[s] = road.Y[i + 1];
                    segHalf[s] = halfWidth;
                    int x0 = Cell(Math.Min(segAx[s], segBx[s]) - reach), x1 = Cell(Math.Max(segAx[s], segBx[s]) + reach);
                    int z0 = Cell(Math.Min(segAz[s], segBz[s]) - reach), z1 = Cell(Math.Max(segAz[s], segBz[s]) + reach);
                    for (int gz = z0; gz <= z1; gz++)
                        for (int gx = x0; gx <= x1; gx++)
                        {
                            int index = gz * roadCells + gx;
                            if (lists[index] == null) lists[index] = new List<int>();
                            lists[index].Add(s);
                        }
                }
            }
            roadGrid = new int[lists.Length][];
            for (int i = 0; i < lists.Length; i++)
                if (lists[i] != null) roadGrid[i] = lists[i].ToArray();
        }

        int Cell(float v)
        {
            int c = (int)Math.Floor(v / GridCell);
            return c < 0 ? 0 : c >= roadCells ? roadCells - 1 : c;
        }

        public void AddRectPad(float x, float z, float halfX, float halfZ, float yawDeg, float height)
        {
            double yaw = yawDeg * Math.PI / 180.0;
            padX.Add(x); padZ.Add(z); padHalfX.Add(halfX); padHalfZ.Add(halfZ);
            padCos.Add((float)Math.Cos(yaw)); padSin.Add((float)Math.Sin(yaw));
            padBound.Add((float)Math.Sqrt(halfX * halfX + halfZ * halfZ));
            padH.Add(height); padRound.Add(false);
        }

        public void AddCirclePad(float x, float z, float radius, float height)
        {
            padX.Add(x); padZ.Add(z); padHalfX.Add(radius); padHalfZ.Add(radius);
            padCos.Add(1f); padSin.Add(0f); padBound.Add(radius);
            padH.Add(height); padRound.Add(true);
        }

        // ---- regions and coast ----

        // Rough distance inland from the waterline (negative at sea).
        public virtual float Coast(float x, float z)
        {
            double dx = x - half, dz = z - half;
            dx *= dx;
            dz *= dz;
            float c = islandRadius - (float)Math.Sqrt(Math.Sqrt(dx * dx + dz * dz));
            // Far offshore everything is flat seabed, so the noise would change nothing.
            if (c < -SeaRamp - coastNoise - coastBays) return c;
            if (coastBays > 0f)
            {
                // The rounded square alone reads as a made thing from above: bays and headlands bend the outline.
                double angle = Math.Atan2(z - half, x - half);
                float bays = 0f;
                for (int k = 0; k < BayShare.Length; k++) bays += BayShare[k] * (float)Math.Cos(BayWaves[k] * angle + bayPhase[k]);
                c += coastBays * bays;
            }
            if (coastNoise <= 0f) return c;
            return c + coastNoise * WorldNoise.Fbm(x / 140f, z / 140f, unchecked(seed + 11u), 2);
        }

        // Nearest region centre and the distance to the closest border of that region's cell.
        public virtual void Locate(float x, float z, out int region, out float border)
        {
            region = -1;
            border = float.MaxValue;
            float best = float.MaxValue;
            for (int i = 0; i < regionCount; i++)
            {
                float dx = x - cx[i], dz = z - cz[i];
                float d = dx * dx + dz * dz;
                if (d < best) { best = d; region = i; }
            }
            if (region < 0) return;
            for (int j = 0; j < regionCount; j++)
            {
                if (j == region) continue;
                float dx = x - cx[j], dz = z - cz[j];
                float b = (dx * dx + dz * dz - best) / (2f * centreDist[region, j]);
                if (b < border) border = b;
            }
        }

        // For a point on the border between regions i and j: how far the nearest third region's border is.
        public float BorderClearance(float x, float z, int i, int j)
        {
            float dix = x - cx[i], diz = z - cz[i];
            float di = dix * dix + diz * diz;
            float clearance = float.MaxValue;
            for (int k = 0; k < regionCount; k++)
            {
                if (k == i || k == j) continue;
                float dx = x - cx[k], dz = z - cz[k];
                float b = (dx * dx + dz * dz - di) / (2f * centreDist[i, k]);
                if (b < clearance) clearance = b;
            }
            return clearance;
        }

        // ---- height layers ----

        // Region ground in metres above sea level, before coast and border shaping.
        public float RegionHeight(int k, float x, float z)
        {
            switch (kind[k])
            {
                case RegionKind.Mountain:
                {
                    if (massif[k] != null)
                    {
                        // Level along roads and yards, then the massif: its own ridges, saddles and benches instead of a radial climb.
                        float lowland = baseH[k] + 4f * WorldNoise.Fbm(x / 120f, z / 120f, unchecked(seed + 25u), 2);
                        float climb = Smooth01((ValleyDistance(k, x, z) - 8f) / MassifRise);
                        return lowland + climb * massif[k].Height(x, z);
                    }
                    float foot = baseH[k] + 4f * WorldNoise.Fbm(x / 120f, z / 120f, unchecked(seed + 25u), 2);
                    // Level along roads and yards; the ground then climbs over 100 m: foothills first, the massif behind.
                    float away = ValleyDistance(k, x, z);
                    float rise = Smooth01((away - 8f) / MountainRise);
                    if (rise <= 0f) return foot;
                    // The ridged sum seldom gets past 0.65, so stretch it: the best crests come close to MaxHeight.
                    float crests = Smooth01(1.5f * WorldNoise.Ridged(x / 170f, z / 170f, unchecked(seed + 27u), 3));
                    // A broad swell joins the crests into massifs instead of leaving each one a cone on a plate,
                    // and small hills roll over the lower slopes.
                    float broad = 0.5f + 0.5f * WorldNoise.Fbm(x / 260f, z / 260f, unchecked(seed + 26u), 2);
                    float hills = Foothills * Smooth01((away - 8f) / 40f) * WorldNoise.Fbm(x / 42f, z / 42f, unchecked(seed + 28u), 2);
                    return foot + hills + rise * relief[k] * (0.22f + 0.23f * broad + 0.55f * crests * (0.6f + 0.4f * broad));
                }
                case RegionKind.Wetland:
                    return Math.Max(-MaxPoolDepth, baseH[k] + relief[k] * WorldNoise.Fbm(x / 38f, z / 38f, unchecked(seed + 23u), 2)) + Hills(k, x, z, WetlandHills);
                case RegionKind.Factory:
                    return baseH[k] + relief[k] * WorldNoise.Fbm(x / 90f, z / 90f, unchecked(seed + 29u), 2) + Hills(k, x, z, FactoryHills);
                default:
                    return baseH[k] + relief[k] * WorldNoise.Fbm(x / 70f, z / 70f, unchecked(seed + 21u), 3) + Hills(k, x, z, TownHills);
            }
        }

        // Rolling hills over the plains: none along the roads and yards (those are the valley floors), rising
        // away from them, so every road runs through a valley and no region is a flat table.
        float Hills(int k, float x, float z, float amplitude)
        {
            float rise = Smooth01((ValleyDistance(k, x, z) - 12f) / 70f);
            if (rise <= 0f) return 0f;
            // Lower beside a border, or the hills would bury the ridge that has to stay a wall (see FloorNear).
            Locate(x, z, out _, out float border);
            float nearRidge = HillsAtRidge + (1f - HillsAtRidge) * Smooth01((border - 40f) / 50f);
            float n = 0.5f + 0.5f * WorldNoise.Fbm(x / 135f, z / 135f, unchecked(seed + 41u), 3);
            return amplitude * rise * nearRidge * Smooth01(1.3f * n - 0.12f);
        }

        // Distance to the nearest valley floor or compound yard of a region.
        float ValleyDistance(int k, float x, float z)
        {
            float best = float.MaxValue;
            for (int i = 0; i < valRegion.Length; i++)
            {
                if (valRegion[i] != k) continue;
                float ax = valAx[i], az = valAz[i];
                float ex = valBx[i] - ax, ez = valBz[i] - az;
                float l2 = ex * ex + ez * ez;
                float t = l2 > 1e-6f ? ((x - ax) * ex + (z - az) * ez) / l2 : 0f;
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                float px = ax + ex * t - x, pz = az + ez * t - z;
                float d2 = px * px + pz * pz;
                if (d2 < best) best = d2;
            }
            best = best == float.MaxValue ? float.MaxValue : (float)Math.Sqrt(best);
            for (int i = 0; i < discRegion.Length; i++)
            {
                if (discRegion[i] != k) continue;
                float dx = x - discX[i], dz = z - discZ[i];
                float d = (float)Math.Sqrt(dx * dx + dz * dz) - discR[i];
                if (d < best) best = d;
            }
            return best;
        }

        // 1 inside a pass gap, 0 on the untouched ridge; saddle is the gap floor height.
        float PassMask(float x, float z, out float saddle)
        {
            float best = 0f, sumW = 0f, sumH = 0f;
            for (int i = 0; i < passX.Length; i++)
            {
                float dx = x - passX[i], dz = z - passZ[i];
                float reach = passHalf[i] + WorldPass.Shoulder;
                float d2 = dx * dx + dz * dz;
                if (d2 >= reach * reach) continue;
                float m = 1f - Smooth01(((float)Math.Sqrt(d2) - passHalf[i]) / WorldPass.Shoulder);
                sumW += m;
                sumH += m * passSaddle[i];
                if (m > best) best = m;
            }
            saddle = sumW > 0f ? sumH / sumW : 0f;
            return best;
        }

        // 0 at a pass, 1 where the ridge is free to wander. Roads are laid out against the straight ridge, so the
        // ridge keeps that shape wherever a road goes through it.
        float RidgeFreedom(float x, float z)
        {
            float free = 1f;
            for (int i = 0; i < passX.Length; i++)
            {
                float dx = x - passX[i], dz = z - passZ[i];
                float reach = passHalf[i] + WorldPass.Shoulder + PassCalm;
                float d2 = dx * dx + dz * dz;
                if (d2 >= reach * reach) continue;
                float f = Smooth01(((float)Math.Sqrt(d2) - passHalf[i] - WorldPass.Shoulder) / PassCalm);
                if (f < free) free = f;
            }
            return free;
        }

        // The highest floor (metres above the sea) among the regions whose cells are near: a ridge beside the
        // mountain region has to stand higher than one between the plains to stay a wall.
        float FloorNear(float x, float z)
        {
            float high = 0f;
            for (int i = 0; i < regionCount; i++)
            {
                // Signed distance to region i's cell (negative inside).
                float dix = x - cx[i], diz = z - cz[i];
                float di = dix * dix + diz * diz, outside = float.MinValue;
                for (int j = 0; j < regionCount; j++)
                {
                    if (j == i) continue;
                    float dx = x - cx[j], dz = z - cz[j];
                    float b = (di - (dx * dx + dz * dz)) / (2f * centreDist[i, j]);
                    if (b > outside) outside = b;
                }
                float floor = baseH[i] + (kind[i] == RegionKind.Mountain ? 9f
                    : kind[i] == RegionKind.Wetland ? WetlandHills * HillsAtRidge
                    : 2f + (kind[i] == RegionKind.Factory ? FactoryHills : TownHills) * HillsAtRidge);
                float v = floor * (1f - Smooth01((outside - 30f) / 60f));
                if (v > high) high = v;
            }
            return high;
        }

        // Cross-section of a ridge, 0 on the crest and 1 where it has met the ground: a steep core and a long foot.
        static float RidgeProfile(float t)
        {
            return 0.82f * Smooth01(t / 0.55f) + 0.18f * Smooth01((t - 0.3f) / 0.7f);
        }

        // Ground before anything is built on it.
        public virtual float Natural(float x, float z)
        {
            float c = Coast(x, z);
            if (c <= -SeaRamp) return seabedFloor;
            float seabed = c < 0f ? sea - (sea - seabedFloor) * Smooth01(-c / SeaRamp) : sea;

            // The ridge follows the border between two regions, but not in a straight line: the border is looked up
            // at a displaced position, so the ridge (and the ground of the regions with it) wanders either side.
            float free = RidgeFreedom(x, z);
            float wx = x, wz = z;
            if (free > 0f)
            {
                float m = RidgeMeander * free;
                wx += m * (WorldNoise.Fbm(x / 95f, z / 95f, unchecked(seed + 33u), 2) + 0.35f * WorldNoise.Value(x / 31f, z / 31f, unchecked(seed + 34u)));
                wz += m * (WorldNoise.Fbm(x / 95f, z / 95f, unchecked(seed + 35u), 2) + 0.35f * WorldNoise.Value(x / 31f, z / 31f, unchecked(seed + 36u)));
            }
            Locate(wx, wz, out int k, out float bd);
            if (k < 0) return seabed;

            float land = seabed;
            if (c > 0f) land = sea + RegionHeight(k, x, z) * Smooth01(c / ShoreRamp);
            if (bd >= RidgeMaxHalfWidth * 1.2f) return land;

            // Crest height and width change along the ridge; a low crest is a narrow ridge, so its flanks stay
            // too steep to climb whatever its height.
            float vary = 0.5f + 0.5f * WorldNoise.Fbm(x / 150f, z / 150f, unchecked(seed + 32u), 2);
            float rise = RidgeMinRise + RidgeCrestRange * vary;
            float wander = sea + FloorNear(x, z) + rise;
            float plain = sea + ridgeHeight + CrestNoise * (0.5f + 0.5f * WorldNoise.Value(x / 90f, z / 90f, unchecked(seed + 31u)));
            float crest = plain + (wander - plain) * free;
            float wide = Math.Min(RidgeMaxHalfWidth, (1.25f * rise + 4f) * (0.85f + 0.3f * (0.5f + 0.5f * WorldNoise.Value(x / 70f, z / 70f, unchecked(seed + 37u)))));
            float halfWidth = ridgeHalf + (wide - ridgeHalf) * free;
            float t = bd / halfWidth;
            if (t >= 1f) return land;

            float s = RidgeProfile(t);
            float mask = PassMask(x, z, out float saddle);
            float top = crest + (saddle - crest) * mask;
            if (c < 0f) top = seabed + (top - seabed) * Smooth01((c + SeaRamp) / (SeaRamp - RidgeSeaReach));
            float h = top + (land - top) * s;
            // Ribs and notches, strongest on the upper flanks, none at the foot, in a pass or under water.
            if (free > 0f && c > 0f)
            {
                float rough = WorldNoise.Ridged(x / 38f, z / 38f, unchecked(seed + 39u), 2) - 0.45f;
                h += RidgeRough * 2f * rough * (1f - s) * (1f - mask) * free * Smooth01(c / 20f);
            }
            return h;
        }

        // Natural ground with the compound yards flattened.
        public float Ground(float x, float z)
        {
            float h = Natural(x, z);
            for (int i = 0; i < discX.Length; i++)
            {
                float dx = x - discX[i], dz = z - discZ[i];
                float reach = discR[i] + DiscBlend;
                float d2 = dx * dx + dz * dz;
                if (d2 >= reach * reach) continue;
                float t = Smooth01(((float)Math.Sqrt(d2) - discR[i]) / DiscBlend);
                h = discH[i] + (h - discH[i]) * t;
            }
            return h;
        }

        // Is this point inside a compound yard? Gives the yard height.
        public bool CompoundAt(float x, float z, out float height)
        {
            for (int i = 0; i < discX.Length; i++)
            {
                float dx = x - discX[i], dz = z - discZ[i];
                if (dx * dx + dz * dz > discR[i] * discR[i]) continue;
                height = discH[i];
                return true;
            }
            height = 0f;
            return false;
        }

        // Nearby roads: distance to the nearest road edge (negative on the road) and the blended road height.
        public bool SampleRoad(float x, float z, out float edge, out float height)
        {
            edge = float.MaxValue;
            height = 0f;
            if (roadGrid == null || x < 0f || z < 0f) return false;
            int gx = (int)(x / GridCell), gz = (int)(z / GridCell);
            if (gx >= roadCells || gz >= roadCells) return false;
            var list = roadGrid[gz * roadCells + gx];
            if (list == null) return false;
            float sumW = 0f, sumH = 0f;
            for (int i = 0; i < list.Length; i++)
            {
                int s = list[i];
                float ax = segAx[s], az = segAz[s];
                float ex = segBx[s] - ax, ez = segBz[s] - az;
                float l2 = ex * ex + ez * ez;
                float t = l2 > 1e-6f ? ((x - ax) * ex + (z - az) * ez) / l2 : 0f;
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                float px = ax + ex * t - x, pz = az + ez * t - z;
                float e = (float)Math.Sqrt(px * px + pz * pz) - segHalf[s];
                if (e >= RoadShoulder) continue;
                if (e < edge) edge = e;
                // Where roads meet, their heights are averaged so the junction has no step.
                float w = 1f - (e > 0f ? e : 0f) / RoadShoulder;
                w *= w;
                w *= w;
                sumW += w;
                sumH += w * (segAy[s] + (segBy[s] - segAy[s]) * t);
            }
            if (sumW <= 0f) return false;
            height = sumH / sumW;
            return true;
        }

        // Distance to the nearest road edge; float.MaxValue when no road is within the shoulder.
        public float RoadEdge(float x, float z) => SampleRoad(x, z, out float edge, out _) ? edge : float.MaxValue;

        float PadEdge(int i, float dx, float dz, float d2)
        {
            if (padRound[i]) return Math.Max(0f, (float)Math.Sqrt(d2) - padHalfX[i]);
            float lx = Math.Abs(dx * padCos[i] - dz * padSin[i]) - padHalfX[i];
            float lz = Math.Abs(dx * padSin[i] + dz * padCos[i]) - padHalfZ[i];
            if (lx < 0f) lx = 0f;
            if (lz < 0f) lz = 0f;
            return (float)Math.Sqrt(lx * lx + lz * lz);
        }

        // Distance to the nearest pad within 8 m; float.MaxValue beyond that.
        public float PadDistance(float x, float z)
        {
            float best = float.MaxValue;
            for (int i = 0; i < padX.Count; i++)
            {
                float dx = x - padX[i], dz = z - padZ[i];
                float reach = padBound[i] + 8f;
                float d2 = dx * dx + dz * dz;
                if (d2 >= reach * reach) continue;
                float d = PadEdge(i, dx, dz, d2);
                if (d < best) best = d;
            }
            return best;
        }

        // Final ground height in metres.
        public float Height(float x, float z)
        {
            float h = Ground(x, z);
            if (SampleRoad(x, z, out float edge, out float roadH))
            {
                float t = Smooth01((edge - RoadCore) / (RoadShoulder - RoadCore));
                h = roadH + (h - roadH) * t;
            }
            for (int i = 0; i < padX.Count; i++)
            {
                float dx = x - padX[i], dz = z - padZ[i];
                float reach = padBound[i] + PadBlend;
                float d2 = dx * dx + dz * dz;
                if (d2 >= reach * reach) continue;
                float d = PadEdge(i, dx, dz, d2);
                if (d >= PadBlend) continue;
                h = padH[i] + (h - padH[i]) * Smooth01(d / PadBlend);
            }
            return h < 0f ? 0f : h > maxHeight ? maxHeight : h;
        }
    }
}
