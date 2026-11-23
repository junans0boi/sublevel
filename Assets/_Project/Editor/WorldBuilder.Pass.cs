using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

// The mountain pass of the event race (#451, ADR-559): course 3 "고개", an open two-lane road from the foot to the summit raced uphill or
// downhill (RaceEvent.PassUp / PassDown, one scene). Built from the uphill course's centre line and heights:
//   - the mountain: a Unity terrain shaped from the road itself (a plane through the road's heights plus a smooth interpolation of what is
//     left, a little noise away from the road), cut level under the road; rock on the steep faces between the legs;
//   - the road: smooth ribbons (asphalt, gravel shoulders, a skirt down each side hiding the cut) with mesh colliders, white edge lines and a
//     yellow double centre line;
//   - guardrails (rail, posts, box colliders following the slope) on every side where the ground falls away from the road, a barrier across
//     each end of the road;
//   - the start/finish of both directions (the shared start builder, raised to each line's height);
//   - a conifer forest on the slopes (the world's vegetation kit), trunk colliders near the road.
// No new assets.
public static partial class WorldBuilder
{
    const float PassMargin = 350f;          // terrain beyond the road's bounds (the valley and the far hills fill the view)
    const float ForestReach = 150f;         // the forest's full density this far from the road, a quarter of it beyond (PERF A: 260 m was 12k trees)
    const int PassHeightmap = 1025, PassAlphamap = 512;
    const float PassCut = 0.3f;             // the ground under the road sits this far below it
    const float PassBlend0 = 1.5f, PassBlend1 = 14f;   // past the shoulder: from the cut to the mountain's own shape
    const float PassRailHeight = 1.1f;
    const string PassTerrainPath = WorldMaterials.GeneratedDir + "/Race_Pass_Terrain.asset";

    // The mountain's shape away from the road: a rounded cone round a peak inside the loop north of the summit straight, its height and slope fitted
    // to the road's heights (least squares), down to a valley floor; plus inverse-distance interpolation of the road's own heights above or
    // below that cone.
    sealed class PassField
    {
        const float PeakX = -20f, PeakZ = -560f, PeakRound = 120f;   // inside the loop, north of the summit straight
        readonly RaceCourse _c;
        readonly float _a, _b, _floor;
        readonly float[] _x, _z, _r;
        static float Reach(float x, float z) => Mathf.Sqrt((x - PeakX) * (x - PeakX) + (z - PeakZ) * (z - PeakZ) + PeakRound * PeakRound);
        float Cone(float x, float z) { float h = _a + _b * Reach(x, z), d = h - _floor; return _floor + (d + Mathf.Sqrt(d * d + 400f)) * 0.5f; }   // a soft max with the floor
        public PassField(RaceCourse c)
        {
            _c = c;
            var p = c.Path; int n = p.Count;
            double sd = 0, sy = 0, sdd = 0, sdy = 0; float low = float.MaxValue;
            foreach (var q in p) { float d = Reach(q.X, q.Z); sd += d; sy += q.Y; sdd += d * d; sdy += d * q.Y; low = Mathf.Min(low, q.Y); }
            _b = (float)((n * sdy - sd * sy) / (n * sdd - sd * sd)); _a = (float)((sy - _b * sd) / n);   // y = a + b * reach
            _floor = low - 45f;
            _x = new float[n]; _z = new float[n]; _r = new float[n];
            for (int i = 0; i < n; i++) { _x[i] = p[i].X; _z[i] = p[i].Z; _r[i] = p[i].Y - Cone(p[i].X, p[i].Z); }
        }

        public float Mountain(float x, float z)
        {
            float sw = 0f, sr = 0f;
            for (int i = 0; i < _x.Length; i++)
            {
                float dx = x - _x[i], dz = z - _z[i], w = 1f / (dx * dx + dz * dz + 400f);
                sw += w; sr += w * _r[i];
            }
            float noise = (Mathf.PerlinNoise(x / 90f + 3.7f, z / 90f + 8.1f) - 0.5f) * 14f + (Mathf.PerlinNoise(x / 31f + 1.3f, z / 31f + 4.4f) - 0.5f) * 4f;
            float edge = Mathf.Abs(_c.Project(x, z).offset) - (_c.Width * 0.5f + _c.Runoff);
            return Cone(x, z) + sr / sw + noise * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 45f, edge));
        }

        // The finished ground: cut level under the road and its shoulders, blending into the mountain past them.
        public float Ground(float x, float z)
        {
            var (s, off) = _c.Project(x, z);
            float edge = Mathf.Abs(off) - (_c.Width * 0.5f + _c.Runoff);
            float road = _c.HeightAt(s) - PassCut;
            if (edge <= PassBlend0) return road;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PassBlend0, PassBlend1, edge));
            return Mathf.Lerp(road, Mountain(x, z), t);
        }
    }

    // `road`: the whole road (the loop, which runs along the pass); `up`/`down`: the sprints whose start lines and grids are drawn.
    static void BuildPass(Transform root, Transform cols, WorldMeshes.Store store, RaceCourse road, RaceCourse up, RaceCourse down)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // The mountain's roads climb its south faces: the sun comes from the south here (the look-dev set's sun is in the north, which left
        // every slope between the legs in shade, black from afar). This scene only.
        foreach (var name in new[] { "Sun", "Fill" })
        {
            var light = GameObject.Find(name);
            if (light != null) light.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * light.transform.rotation;
        }
        var field = new PassField(road);
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var p in road.Path) { x0 = Mathf.Min(x0, p.X); x1 = Mathf.Max(x1, p.X); z0 = Mathf.Min(z0, p.Z); z1 = Mathf.Max(z1, p.Z); }
        x0 -= PassMargin; x1 += PassMargin; z0 -= PassMargin; z1 += PassMargin;
        var terrain = BuildPassTerrain(root, field, road, x0, x1, z0, z1);
        BuildPassRoad(root, store, road);
        int rails = BuildPassRails(root, cols, store, road, field);
        foreach (var (c, name) in new[] { (up, "StartFoot"), (down, "StartTop") })
        {
            var at = Child(root, name);
            at.localPosition = new Vector3(0f, c.HeightAt(0f), 0f);
            var kit = new CircuitKit(Child(at, "Colliders"));
            BuildStart(kit, c, RaceEvent.GridSlots);
            kit.Emit(at, store);
        }
        int trees = BuildPassForest(root, store, road, terrain);
        var (boards, mirrors) = BuildPassDressing(root, store, road, field);
        Debug.Log($"[WorldBuilder] pass: road {(road.Open ? road.Path[road.Path.Count - 1].S - road.Path[0].S : road.LapLength):F0} m, sprint {up.LapLength:F0} m, climb " +
            $"{up.HeightAt(up.LapLength) - up.HeightAt(0f):F0} m, {rails} guardrail pieces, {boards} chevrons, {mirrors} curve mirrors, {trees} trees, terrain {x1 - x0:F0} x {z1 - z0:F0} m, {watch.Elapsed.TotalSeconds:F1} s");
    }

    static Terrain BuildPassTerrain(Transform root, PassField field, RaceCourse c, float x0, float x1, float z0, float z1)
    {
        int res = PassHeightmap;
        var h = new float[res, res];
        float lo = float.MaxValue, hi = float.MinValue;
        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float v = field.Ground(Mathf.Lerp(x0, x1, i / (res - 1f)), Mathf.Lerp(z0, z1, j / (res - 1f)));
                h[j, i] = v; lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
            }
        lo -= 2f; hi += 2f;
        for (int j = 0; j < res; j++) for (int i = 0; i < res; i++) h[j, i] = (h[j, i] - lo) / (hi - lo);

        var data = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainData>(PassTerrainPath);
        if (data == null) { data = new TerrainData(); UnityEditor.AssetDatabase.CreateAsset(data, PassTerrainPath); }
        data.heightmapResolution = res;
        data.size = new Vector3(x1 - x0, hi - lo, z1 - z0);
        data.SetHeights(0, 0, h);
        data.terrainLayers = WorldMaterials.TerrainLayers();
        data.alphamapResolution = PassAlphamap;
        data.baseMapResolution = 512;
        // Grass, worn dirt along the road's edges and under the forest's edge, rock on the steep faces.
        int ar = PassAlphamap;
        var alpha = new float[ar, ar, 4];
        for (int j = 0; j < ar; j++)
            for (int i = 0; i < ar; i++)
            {
                float u = (i + 0.5f) / ar, v = (j + 0.5f) / ar;
                float steep = data.GetSteepness(u, v);
                float x = Mathf.Lerp(x0, x1, u), z = Mathf.Lerp(z0, z1, v);
                float edge = Mathf.Abs(c.Project(x, z).offset) - (c.Width * 0.5f + c.Runoff);
                // Bare rock only on the steepest faces; the cuts below that are earth (the dark rock read as black smears from afar).
                float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(46f, 58f, steep));
                float dirt = (1f - rock) * Mathf.Max(Mathf.Max(0.55f * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(PassBlend0, PassBlend0 + 3f, edge)),
                    0.75f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(32f, 44f, steep))),
                    0.2f * Mathf.SmoothStep(0.6f, 0.8f, Mathf.PerlinNoise(x / 40f + 7.7f, z / 40f + 2.2f)));
                float grass = Mathf.Max(0f, 1f - rock - dirt);
                alpha[j, i, 0] = grass; alpha[j, i, 1] = dirt; alpha[j, i, 2] = rock; alpha[j, i, 3] = 0f;
            }
        data.SetAlphamaps(0, 0, alpha);
        UnityEditor.EditorUtility.SetDirty(data);

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(x0, lo, z0);
        go.isStatic = true;
        var terrain = go.GetComponent<Terrain>();
        terrain.materialTemplate = WorldMaterials.Terrain();
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 5f;
        terrain.basemapDistance = 2000f;   // the base map has no side-projected rock (the steep faces drew black from afar)
        terrain.shadowCastingMode = ShadowCastingMode.Off;
        terrain.allowAutoConnect = false;
        terrain.drawTreesAndFoliage = false;
        return terrain;
    }

    // The road's ribbons along the centre line, cut into pieces of ChunkSamples samples (one mesh each, for culling).
    static void BuildPassRoad(Transform root, WorldMeshes.Store store, RaceCourse c)
    {
        const int ChunkSamples = 24;
        float half = c.Width * 0.5f, edge = half + c.Runoff;
        var asphalt = WorldMaterials.Asphalt; var gravel = WorldMaterials.DirtRoad; var white = WorldMaterials.RaceWhite;
        var yellow = WorldMaterials.DoorStripe();
        var p = PassRun(c); int n = p.Count; bool closed = !c.Open;
        var parent = Child(root, "Road");
        for (int from = 0, part = 0; from < n - 1; from += ChunkSamples, part++)
        {
            int to = Mathf.Min(n - 1, from + ChunkSamples);
            var road = new Ribbon(); var shoulder = new Ribbon(); var lines = new Ribbon(); var centre = new Ribbon();
            road.Strip(p, closed, from, to, -half, half, 0f, 0f, asphalt.Tile);
            foreach (int sgn in new[] { -1, 1 })
            {
                shoulder.Strip(p, closed, from, to, sgn < 0 ? -edge : half, sgn < 0 ? -half : edge, 0f, 0f, gravel.Tile);
                // The skirt: from the shoulder's edge down and out under the ground, so no gap shows where the ground is cut.
                shoulder.Strip(p, closed, from, to, sgn * (edge + 0.8f), sgn * edge, -2.2f, 0f, gravel.Tile, sgn > 0);
                lines.Strip(p, closed, from, to, sgn * (half - 0.35f) - 0.075f, sgn * (half - 0.35f) + 0.075f, 0.012f, 0.012f, white.Tile);
                centre.Strip(p, closed, from, to, sgn * 0.13f - 0.05f, sgn * 0.13f + 0.05f, 0.014f, 0.014f, Vector2.one);
            }
            road.Emit(parent, store, "Asphalt" + part, asphalt.Material, true);
            shoulder.Emit(parent, store, "Shoulder" + part, gravel.Material, true);
            lines.Emit(parent, store, "EdgeLine" + part, white.Material, false);
            centre.Emit(parent, store, "CentreLine" + part, yellow, false);
        }
    }

    // The centre line's samples in driving order as one run (a circuit's first sample again at the end), S = distance along the run.
    static List<RaceCourse.Point> PassRun(RaceCourse c)
    {
        var list = new List<RaceCourse.Point>(c.Path);
        if (!c.Open) list.Add(c.Path[0]);
        float s = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) s += Mathf.Sqrt((list[i].X - list[i - 1].X) * (list[i].X - list[i - 1].X) + (list[i].Z - list[i - 1].Z) * (list[i].Z - list[i - 1].Z));
            var q = list[i]; q.S = s; list[i] = q;
        }
        return list;
    }

    // A strip along the centre line between two offsets (left of the driving direction positive), each side at its own height above the
    // road; uv across in metres / tile and along by distance.
    sealed class Ribbon
    {
        readonly List<Vector3> _v = new List<Vector3>(); readonly List<Vector2> _uv = new List<Vector2>(); readonly List<int> _t = new List<int>();

        public void Strip(List<RaceCourse.Point> p, bool closed, int from, int to, float a, float b, float ya, float yb, Vector2 tile, bool flip = false)
        {
            int n = p.Count, start = _v.Count;
            for (int i = from; i <= to; i++)
            {
                // a circuit's run: before the first is the one before the repeat at the end, after the last is the second
                var prev = p[i > 0 ? i - 1 : closed ? n - 2 : 0]; var next = p[i < n - 1 ? i + 1 : closed ? 1 : n - 1];
                var d = new Vector2(next.X - prev.X, next.Z - prev.Z).normalized;
                var left = new Vector3(-d.y, 0f, d.x);
                var mid = new Vector3(p[i].X, p[i].Y, p[i].Z);
                _v.Add(mid + left * a + Vector3.up * ya); _v.Add(mid + left * b + Vector3.up * yb);
                _uv.Add(new Vector2(a / tile.x, p[i].S / tile.y)); _uv.Add(new Vector2(b / tile.x, p[i].S / tile.y));
            }
            for (int k = 0; k < to - from; k++)
            {
                int q = start + k * 2;
                // a right of b: facing up (Unity's clockwise front); `flip` for a strip given left to right
                if (!flip) { _t.Add(q); _t.Add(q + 1); _t.Add(q + 2); _t.Add(q + 1); _t.Add(q + 3); _t.Add(q + 2); }
                else { _t.Add(q); _t.Add(q + 2); _t.Add(q + 1); _t.Add(q + 1); _t.Add(q + 2); _t.Add(q + 3); }
            }
        }

        public void Emit(Transform parent, WorldMeshes.Store store, string name, Material material, bool collide)
        {
            if (_t.Count == 0) return;
            var mesh = new Mesh { name = "Race_Pass_" + name };
            mesh.SetVertices(_v); mesh.SetUVs(0, _uv); mesh.SetTriangles(_t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = store.AddBlob(mesh);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;   // flat on the road: its shadow pass was draws and set-passes for nothing (PERF A)
            if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;   // re-assigned from the blob at load (WorldMeshBank)
            go.isStatic = true;
        }
    }

    // Guardrails where the ground falls away from the road (the cliff side), a barrier across each end of an open road. Returns the pieces.
    static int BuildPassRails(Transform root, Transform cols, WorldMeshes.Store store, RaceCourse c, PassField field)
    {
        var steel = WorldMaterials.RaceWhite; var rails = new RaceTiles(steel.Tile, PassDressCell);   // painted white
        var p = PassRun(c); int n = p.Count, pieces = 0;
        float edge = c.Width * 0.5f + c.Runoff + 0.25f;
        var railCols = Child(cols, "Guardrail");
        for (int i = 0; i < n - 1; i++)
        {
            var a = p[i]; var b = p[i + 1];
            var d = new Vector2(b.X - a.X, b.Z - a.Z).normalized; var left = new Vector3(-d.y, 0f, d.x);
            var ma = new Vector3(a.X, a.Y, a.Z); var mb = new Vector3(b.X, b.Y, b.Z);
            foreach (int sgn in new[] { -1, 1 })
            {
                if (!PassCliff(field, ma, mb, left, sgn, edge)) continue;   // ground level or rising: the mountain side
                var ea = ma + left * sgn * edge; var eb = mb + left * sgn * edge;
                var rot = Quaternion.LookRotation(eb - ea, Vector3.up); float len = Vector3.Distance(ea, eb);
                var m = (ea + eb) * 0.5f;
                rails.Add(m + Vector3.up * 0.65f, new Vector3(0.12f, 0.34f, len + 0.15f), rot);                 // the W-beam
                rails.Add(ea + Vector3.up * 0.45f - left * sgn * 0.12f, new Vector3(0.14f, 0.9f, 0.14f), rot);   // a post at each sample
                RaceCollider(railCols, m + Vector3.up * (PassRailHeight * 0.5f), new Vector3(0.4f, PassRailHeight, len + 0.15f), rot);
                pieces++;
            }
        }
        // Across both ends of an open road: the road stops at a barrier.
        foreach (int i in c.Open ? new[] { 0, n - 1 } : new int[0])
        {
            var q = p[i]; var o = p[i == 0 ? 1 : n - 2];
            var dir = new Vector3(q.X - o.X, 0f, q.Z - o.Z).normalized;
            var at = new Vector3(q.X, q.Y, q.Z) - dir * 1f;
            var rot = Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir), Vector3.up);
            rails.Add(at + Vector3.up * 0.65f, new Vector3(0.12f, 0.34f, edge * 2f + 0.5f), rot);
            for (float k = -edge; k <= edge + 0.01f; k += edge / 3f) rails.Add(at + rot * new Vector3(0f, 0.45f, k), new Vector3(0.14f, 0.9f, 0.14f), rot);
            RaceCollider(railCols, at + Vector3.up * (PassRailHeight * 0.5f), new Vector3(0.4f, PassRailHeight, edge * 2f + 0.5f), rot);
            pieces++;
        }
        rails.Emit(root, store, "Guardrail", steel.Material);
        return pieces;
    }

    const float PassDressCell = 160f;

    // Where the ground falls away beside the road (9 m beyond the shoulder 1.2 m or more under it): a guardrail there.
    static bool PassCliff(PassField field, Vector3 ma, Vector3 mb, Vector3 left, int sgn, float edge)
    {
        var probe = (ma + mb) * 0.5f + left * sgn * (edge + 9f);
        return field.Mountain(probe.x, probe.z) <= (ma.y + mb.y) * 0.5f - 1.2f;
    }

    // The roadside of a real mountain road: on every tight corner chevron boards behind the outside edge for both ways (a V of two boards
    // per post) and a curve mirror at the apex; white delineator posts with a red reflector along the sides without a guardrail and a reflector
    // on the guardrail every 8 m; a concrete gutter along the mountain side. Boxes batched in big cells (few draws), no colliders (behind
    // the rail or in the gutter). Returns the boards and mirrors.
    static (int boards, int mirrors) BuildPassDressing(Transform root, WorldMeshes.Store store, RaceCourse c, PassField field)
    {
        var white = WorldMaterials.RaceWhite; var red = WorldMaterials.RaceRed; var black = WorldMaterials.Wire; var concrete = WorldMaterials.ConcreteDark;
        var metal = WorldMaterials.PaintedMetal; var yellow = WorldMaterials.DoorStripe();
        var W = new RaceTiles(white.Tile, PassDressCell); var R = new RaceTiles(red.Tile, PassDressCell); var K = new RaceTiles(black.Tile, PassDressCell);
        var Y = new RaceTiles(Vector2.one, PassDressCell); var G = new RaceTiles(concrete.Tile, PassDressCell); var M = new RaceTiles(metal.Tile, PassDressCell);
        var p = PassRun(c); int n = p.Count, boards = 0, mirrors = 0;
        float edge = c.Width * 0.5f + c.Runoff + 0.25f;
        bool Crosses(float s0, float s1, float every) => Mathf.Floor(s0 / every) != Mathf.Floor(s1 / every);
        Vector3 Mid(int i) => new Vector3(p[i].X, p[i].Y, p[i].Z);
        Vector3 Left(int i) { int j = Mathf.Min(n - 1, i + 1), k = j == i ? i - 1 : i; var d = new Vector2(p[j].X - p[k].X, p[j].Z - p[k].Z).normalized; return new Vector3(-d.y, 0f, d.x); }
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 ma = Mid(i), mb = Mid(i + 1), left = Left(i);
            float len = Vector3.Distance(ma, mb);
            foreach (int sgn in new[] { -1, 1 })
            {
                bool rail = PassCliff(field, ma, mb, left, sgn, edge);
                var ea = ma + left * sgn * edge; var eb = mb + left * sgn * edge; var side = left * sgn;
                var rot = Quaternion.LookRotation(eb - ea, Vector3.up);
                if (rail)
                {
                    if (Crosses(p[i].S, p[i + 1].S, 8f)) R.Add(ea - side * 0.08f + Vector3.up * 0.65f, new Vector3(0.03f, 0.1f, 0.14f), rot);
                    continue;
                }
                G.Add((ea + eb) * 0.5f + side * 0.2f + Vector3.up * -0.07f, new Vector3(0.7f, 0.14f, len + 0.05f), rot);   // the gutter
                if (!Crosses(p[i].S + (sgn > 0 ? 10f : 0f), p[i + 1].S + (sgn > 0 ? 10f : 0f), 20f)) continue;
                var post = ea + side * 0.75f;
                W.Add(post + Vector3.up * 0.55f, new Vector3(0.1f, 1.1f, 0.1f), rot);
                R.Add(post + Vector3.up * 0.95f - side * 0.055f, new Vector3(0.02f, 0.14f, 0.07f), rot);
            }
        }
        // The tight corners (radius under 20 m), each a run of arc samples.
        for (int i = 0; i < n - 1; i++)
        {
            if (!(p[i].Radius > 0f && p[i].Radius < 20f)) continue;
            int first = i; while (i < n - 1 && p[i + 1].Radius == p[first].Radius && p[i + 1].Turn == p[first].Turn) i++;
            int last = i, outside = -p[first].Turn;
            if (last - first < 2) continue;
            float s0 = p[first].S, s1 = p[last].S;
            for (int k = first; k <= last; k++)
            {
                bool apex = k == (first + last) / 2;
                if (!apex && k != first && k != last && (k - first) % 2 != 0) continue;
                var m = Mid(k); var outward = Left(k) * outside; var inward = -outward;
                var brot = Quaternion.LookRotation(inward, Vector3.up);
                if (apex)
                {
                    // The curve mirror: a pole, an orange octagonal frame, the glass, facing the road.
                    var foot = m + outward * (edge + 0.9f);
                    M.Add(foot + Vector3.up * 1.5f, new Vector3(0.09f, 3f, 0.09f), brot);
                    var face = foot + Vector3.up * 2.9f + inward * 0.08f;
                    Y.Add(face, new Vector3(0.9f, 0.9f, 0.05f), brot); Y.Add(face, new Vector3(0.9f, 0.9f, 0.05f), brot * Quaternion.Euler(0f, 0f, 45f));
                    M.Add(face + inward * 0.03f, new Vector3(0.74f, 0.74f, 0.02f), brot); M.Add(face + inward * 0.03f, new Vector3(0.74f, 0.74f, 0.02f), brot * Quaternion.Euler(0f, 0f, 45f));
                    mirrors++;
                    continue;
                }
                // Chevron boards on a post behind the outside edge (the outside of a bend is the same side both ways): two yellow boards in
                // a V, each turned towards the cars coming one way, its black arrow (two bars) pointing the way they turn — uphill and
                // downhill alike (#451).
                var at = m + outward * (edge + 0.6f);
                M.Add(at + Vector3.up * 0.75f, new Vector3(0.07f, 1.5f, 0.07f), brot);
                var lap = (Mid(Mathf.Min(n - 1, k + 1)) - Mid(Mathf.Max(0, k - 1))); lap.y = 0f; lap.Normalize();
                foreach (int way in new[] { 1, -1 })
                {
                    var t = lap * way;                                                   // the way these cars go
                    var face = Quaternion.LookRotation((inward - t * 0.6f).normalized, Vector3.up);   // turned towards them
                    float dir = Mathf.Sign(Vector3.Dot(face * Vector3.right, t));
                    var board = at + Vector3.up * 1.5f - t * 0.2f + inward * 0.05f;
                    Y.Add(board, new Vector3(0.5f, 0.7f, 0.03f), face);
                    foreach (int up in new[] { 1, -1 })
                        K.Add(board + face * new Vector3(-0.01f * dir, 0.1f * up, 0.025f), new Vector3(0.3f, 0.09f, 0.02f), face * Quaternion.Euler(0f, 0f, -42f * up * dir));
                }
                boards++;
            }
        }
        W.Emit(root, store, "Delineators", white.Material); R.Emit(root, store, "Reflectors", red.Material); K.Emit(root, store, "Arrows", black.Material);
        Y.Emit(root, store, "Signs", yellow); G.Emit(root, store, "Gutter", concrete.Material); M.Emit(root, store, "Poles", metal.Material);
        // Small or flat: no shadows (a shadow pass per material and cell for slivers nobody sees, PERF A).
        foreach (Transform t in root)
            foreach (var prefix in new[] { "Delineators", "Reflectors", "Arrows", "Signs", "Gutter", "Poles" })
                if (t.name.StartsWith(prefix) && t.TryGetComponent<MeshRenderer>(out var r)) r.shadowCastingMode = ShadowCastingMode.Off;
        return (boards, mirrors);
    }

    // Conifers (a few broadleaf trees among them) on the slopes: a jittered grid thinned by noise, clear of the road, none on the rock faces.
    static int BuildPassForest(Transform root, WorldMeshes.Store store, RaceCourse c, Terrain terrain)
    {
        const float Spacing = 10f, Clear = 6f, ColliderReach = 40f, Cell = 32f;
        var kit = WorldVegetation.Build(store);
        var data = terrain.terrainData; var origin = terrain.transform.position;
        var rng = new System.Random(451);
        var proto = new List<int>(); var place = new List<Vector4>(); var yaw = new List<float>();
        var veg = Child(root, "Vegetation"); var trunks = Child(veg, "Trunks");
        var cells = new Dictionary<long, GameObject>();
        float edge = c.Width * 0.5f + c.Runoff;
        for (float z = origin.z + 5f; z < origin.z + data.size.z - 5f; z += Spacing)
            for (float x = origin.x + 5f; x < origin.x + data.size.x - 5f; x += Spacing)
            {
                float px = x + ((float)rng.NextDouble() - 0.5f) * Spacing * 0.8f, pz = z + ((float)rng.NextDouble() - 0.5f) * Spacing * 0.8f;
                double roll = rng.NextDouble();
                if (Mathf.PerlinNoise(px / 70f + 11.3f, pz / 70f + 5.9f) < 0.38f) continue;   // clearings
                float off = Mathf.Abs(c.Project(px, pz).offset) - edge;
                if (off < Clear || (off > ForestReach && rng.NextDouble() > 0.25)) continue;   // beyond the reach: a quarter as dense
                float u = (px - origin.x) / data.size.x, v = (pz - origin.z) / data.size.z;
                if (data.GetSteepness(u, v) > 38f) continue;
                var kind = roll < 0.18 ? PlantKind.Broadleaf : PlantKind.Conifer;
                // Broadleaf trees only in two looks: each look is its own leaf and billboard material (set-passes at the grid, PERF A).
                int pr = kit.Prototype(kind, rng.Next(kind == PlantKind.Broadleaf ? 2 : Vegetation.Variants));
                float scale = 0.85f + (float)rng.NextDouble() * 0.4f;
                float y = data.GetInterpolatedHeight(u, v) + origin.y - 0.08f;
                proto.Add(pr); place.Add(new Vector4(px, y, pz, scale)); yaw.Add((float)rng.NextDouble() * 360f);
                if (off > ColliderReach || kit.TrunkOf[pr] < 0) continue;
                int cx = Mathf.FloorToInt(px / Cell), cz = Mathf.FloorToInt(pz / Cell);
                long key = ((long)cx << 32) ^ (uint)cz;
                if (!cells.TryGetValue(key, out var cell))
                {
                    cell = new GameObject($"TrunkCell_{cx}_{cz}"); cell.transform.SetParent(trunks, false);
                    cell.transform.position = new Vector3(cx * Cell, 0f, cz * Cell); cell.isStatic = true; cells[key] = cell;
                }
                var cap = cell.AddComponent<CapsuleCollider>();
                cap.radius = kit.TrunkRadius[kit.TrunkOf[pr]] * scale + 0.05f;
                cap.height = Mathf.Min(kit.TrunkHeight[kit.TrunkOf[pr]] * scale, 7f);
                cap.center = new Vector3(px - cx * Cell, y + cap.height * 0.5f, pz - cz * Cell);
            }
        veg.gameObject.AddComponent<VegetationRenderer>().Fill(kit.Prototypes, proto.ToArray(), place.ToArray(), yaw.ToArray());
        return proto.Count;
    }
}
