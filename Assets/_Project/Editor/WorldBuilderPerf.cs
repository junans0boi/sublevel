using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Game.Core;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

// A1 (ADR-168): measures what 411 buildings on a 2.4 x 2.0 km island cost, BEFORE the generator is written.
// Nothing is saved: World.unity is opened, 411 copies of its real buildings go into a throw-away root, and the editor exits.
// Run: tools/unity.sh run-gfx WorldBuilderPerf.Run   (graphics device, no audio, no PlayMode)
public static class WorldBuilderPerf
{
    const float MapX = 2400f, MapZ = 2000f, MapY = 230f;
    const int W = 1920, H = 1080;
    static readonly string Out = "Logs/perf/a1.txt";
    static readonly StringBuilder Report = new StringBuilder();

    static void Log(string s) { Report.AppendLine(s); Debug.Log("[A1] " + s); }

    // Town centres on the 2.4 x 2.0 km box (towns.json order of tiers; positions are a plausible spread, not the final layout).
    struct Town { public string Name; public int Count; public float W, D, X, Z; }
    static readonly Town[] Towns =
    {
        new Town{Name="C 청람읍", Count=90, W=440, D=300, X=1100, Z=950},
        new Town{Name="L1 새재", Count=50, W=330, D=240, X=1750, Z=1000},
        new Town{Name="L2 해옥포", Count=38, W=300, D=230, X=350, Z=500},
        new Town{Name="L3 광석리", Count=39, W=320, D=240, X=1750, Z=350},
        new Town{Name="L4 북령읍", Count=42, W=330, D=210, X=1000, Z=1650},
        new Town{Name="M1 학전", Count=16, W=190, D=130, X=1000, Z=650},
        new Town{Name="M2 논실", Count=21, W=200, D=140, X=1950, Z=750},
        new Town{Name="M3 마루재", Count=21, W=190, D=130, X=1550, Z=1250},
        new Town{Name="M4 채석리", Count=19, W=200, D=140, X=2050, Z=1450},
        new Town{Name="M5 남포", Count=20, W=220, D=140, X=1450, Z=250},
        new Town{Name="M6 북해리", Count=19, W=200, D=140, X=1500, Z=1800},
        new Town{Name="H1 솔밭", Count=6, W=160, D=50, X=800, Z=1150},
        new Town{Name="H3 샘골", Count=6, W=160, D=50, X=700, Z=1500},
        new Town{Name="H4 양지", Count=6, W=170, D=50, X=2200, Z=1100},
        new Town{Name="H5 달뫼", Count=8, W=100, D=60, X=650, Z=900},
        new Town{Name="H6 하동", Count=4, W=150, D=50, X=1700, Z=600},
        new Town{Name="H7 갯말", Count=6, W=150, D=50, X=250, Z=900},
    };

    // Per-kind mix of the 411 (towns.json totals); template = which real building shape stands in, stacks = copies piled up
    // to reach the storey count (the real templates are 1-2 storeys). Estimate, not the final generator.
    struct Kind { public string Name; public int Count; public string Template; public int Stacks; }
    static readonly Kind[] Kinds =
    {
        new Kind{Name="주택", Count=178, Template="House", Stacks=1},
        new Kind{Name="상가", Count=138, Template="House", Stacks=1},
        new Kind{Name="빌라", Count=22, Template="House", Stacks=2},
        new Kind{Name="노점", Count=16, Template="Shed", Stacks=1},
        new Kind{Name="창고", Count=14, Template="Warehouse", Stacks=1},
        new Kind{Name="막사", Count=8, Template="Warehouse", Stacks=1},
        new Kind{Name="아파트", Count=8, Template="House", Stacks=3},
        new Kind{Name="헛간", Count=8, Template="Shed", Stacks=1},
        new Kind{Name="공장", Count=6, Template="FactoryHall", Stacks=1},
        new Kind{Name="학교", Count=6, Template="Warehouse", Stacks=1},
        new Kind{Name="감시탑", Count=5, Template="Shed", Stacks=2},
        new Kind{Name="시장", Count=2, Template="Warehouse", Stacks=1},
    };

    [Serializable] class Row { public string variant, terrain, view; public float avgMs, p99Ms; public long drawCalls, tris, setPass; public int statDraws, statBatches, statSetPass; public long statTris; public int estDraws; public long estTris; public int lights; }

    public static void Run()
    {
        try { Measure(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogError("[A1] failed: " + e); EditorApplication.Exit(1); }
    }

    static void Measure()
    {
        Directory.CreateDirectory("Logs/perf");
        var sw = Stopwatch.StartNew();
        Log($"date {DateTime.Now:yyyy-MM-dd HH:mm} unity {Application.unityVersion} gpu {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}) cpu {SystemInfo.processorType} ram {SystemInfo.systemMemorySize}MB");
        foreach (var f in new[] { "Assets/_Project/Scenes/World.unity", "Assets/_Project/Prefabs/World/Generated/World_Meshes.asset", "Assets/_Project/Prefabs/World/Generated/World_NavMesh.asset", "Assets/_Project/Prefabs/World/Generated/World_Terrain.asset" })
            if (File.Exists(f)) Log($"file {f} {new FileInfo(f).Length / 1048576.0:F1} MB");

        long memBefore = Profiler.GetTotalAllocatedMemoryLong();
        var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/World.unity", OpenSceneMode.Single);
        Log($"open World.unity (editor, real 73 lots): {sw.Elapsed.TotalSeconds:F1} s, +{(Profiler.GetTotalAllocatedMemoryLong() - memBefore) / 1048576.0:F0} MB native allocated");

        var cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
        if (cam == null) throw new Exception("no camera in World.unity");
        var srcTerrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
        var world = GameObject.Find("World");

        // --- templates: the real buildings of the scene, by kind suffix (LotNN_Kind) ---
        var lots = world.GetComponentsInChildren<LODGroup>(true).Select(g => g.gameObject).Where(g => g.name.StartsWith("Lot")).ToList();
        var byKind = lots.GroupBy(g => g.name.Substring(g.name.LastIndexOf('_') + 1)).ToDictionary(g => g.Key, g => g.ToList());
        Log("real buildings by kind: " + string.Join(", ", byKind.Select(k => $"{k.Key} {k.Value.Count}")));
        int realRend = 0; long realTris = 0; var realMats = new HashSet<Material>(); var realMeshes = new HashSet<Mesh>(); int realLights = 0;
        foreach (var b in lots)
        {
            foreach (var r in b.GetComponentsInChildren<MeshRenderer>(true)) { realRend++; realMats.Add(r.sharedMaterial); var m = r.GetComponent<MeshFilter>().sharedMesh; realMeshes.Add(m); realTris += Tris(m); }
            realLights += b.GetComponentsInChildren<Light>(true).Length;
        }
        long realMeshBytes = realMeshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m));
        Log($"real {lots.Count} lots: {realRend} renderers ({realRend / (double)lots.Count:F1}/bldg), {realTris} tris ({realTris / (double)lots.Count:F0}/bldg), {realMats.Count} materials, {realLights} lights, mesh memory {realMeshBytes / 1048576.0:F1} MB ({realMeshBytes / 1048576.0 / lots.Count:F2} MB/bldg)");

        // --- big terrain (single, then 2x2) ---
        var layers = srcTerrain != null ? srcTerrain.terrainData.terrainLayers : new TerrainLayer[0];
        var rows = new List<Row>();
        world.SetActive(false);
        var island = new GameObject("A1_Island");
        SceneSize(island, byKind);

        long memT0 = Profiler.GetTotalAllocatedMemoryLong();
        var swT = Stopwatch.StartNew();
        var single = MakeTerrains(island.transform, 1, layers, srcTerrain);
        Log($"terrain single 2049 heightmap + 2048 alpha ({layers.Length} layers): build {swT.Elapsed.TotalSeconds:F1} s, +{(Profiler.GetTotalAllocatedMemoryLong() - memT0) / 1048576.0:F0} MB native");

        var capital = Towns[0];
        // the highest ground 400-800 m from the capital: "ridge" looks down at the town from there
        Vector3 ridgePos = default; float best = -1f;
        for (float a = 0; a < 360f; a += 5f) for (float d = 400f; d <= 800f; d += 50f)
        {
            float rx = capital.X + Mathf.Cos(a * Mathf.Deg2Rad) * d, rz = capital.Z + Mathf.Sin(a * Mathf.Deg2Rad) * d;
            if (rx < 20 || rz < 20 || rx > MapX - 20 || rz > MapZ - 20) continue;
            float hh = Height01(rx, rz); if (hh > best) { best = hh; ridgePos = At(single, rx, rz, 2f); }
        }
        Log($"ridge viewpoint {ridgePos} ({Vector3.Distance(new Vector3(ridgePos.x, 0, ridgePos.z), new Vector3(capital.X, 0, capital.Z)):F0} m from capital, {ridgePos.y:F0} m high)");
        var views = new (string name, Vector3 pos, Vector3 look)[]
        {
            ("street", At(single, capital.X - 200f, capital.Z, 1.7f), Vector3.right),
            ("roof", At(single, capital.X, capital.Z, 18f), Vector3.right),
            ("ridge", ridgePos, (new Vector3(capital.X, At(single, capital.X, capital.Z, 8f).y, capital.Z) - ridgePos).normalized),
            ("aerial", At(single, capital.X, capital.Z - 500f, 250f), (new Vector3(0f, -0.5f, 1f)).normalized),
        };

        // terrain only, single vs 2x2
        foreach (var v in views) rows.Add(Shoot(cam, v.pos, v.look, "none", "single", v.name, island));
        foreach (var t in single) t.gameObject.SetActive(false);
        long memS0 = Profiler.GetTotalAllocatedMemoryLong();
        var quad = MakeTerrains(island.transform, 2, layers, srcTerrain);
        Log($"terrain 2x2 (4 x 1025 heightmap): +{(Profiler.GetTotalAllocatedMemoryLong() - memS0) / 1048576.0:F0} MB native");
        foreach (var v in views) rows.Add(Shoot(cam, v.pos, v.look, "none", "2x2", v.name, island));
        foreach (var t in quad) UnityEngine.Object.DestroyImmediate(t.terrainData == null ? t.gameObject : t.gameObject);
        foreach (var t in single) t.gameObject.SetActive(true);

        // --- buildings ---
        foreach (var (variant, dense) in new[] { ("detail", false), ("shell", false), ("box", false), ("detail", true), ("shell", true) })
        {
            string layout = dense ? "dense" : "spec";
            long m0 = Profiler.GetTotalAllocatedMemoryLong();
            var swB = Stopwatch.StartNew();
            var root = new GameObject("A1_" + variant); root.transform.SetParent(island.transform);
            var stats = Populate(root.transform, byKind, single, variant, dense);
            Log($"[{variant}/{layout}] 411 buildings placed in {swB.Elapsed.TotalSeconds:F1} s: {stats}, instantiate +{(Profiler.GetTotalAllocatedMemoryLong() - m0) / 1048576.0:F0} MB native (meshes shared with the originals)");
            foreach (var v in views) rows.Add(Shoot(cam, v.pos, v.look, variant, layout, v.name, island));

            if (variant == "detail" && !dense) NavBake(island, single, capital);
            UnityEngine.Object.DestroyImmediate(root);
            Resources.UnloadUnusedAssets();
        }

        // --- report ---
        Log("variant | layout | view | avg ms | p99 | draw(profiler) | tris(profiler) | setpass | unitystats draw/batch/setpass/tris | draw(est) | tris(est) | lights");
        foreach (var r in rows)
            Log($"{r.variant} | {r.terrain} | {r.view} | {r.avgMs:F2} | {r.p99Ms:F2} | {r.drawCalls} | {r.tris} | {r.setPass} | {r.statDraws}/{r.statBatches}/{r.statSetPass}/{r.statTris} | {r.estDraws} | {r.estTris} | {r.lights}");
        File.WriteAllText(Out, Report.ToString());
        File.WriteAllText("Logs/perf/a1.json", "[" + string.Join(",", rows.Select(r => JsonUtility.ToJson(r))) + "]");
        Log($"total {sw.Elapsed.TotalSeconds:F0} s; written {Out}");
    }

    static Vector3 At(Terrain[] ts, float x, float z, float up)
    {
        float y = 0f;
        foreach (var t in ts) { var p = t.transform.position; var d = t.terrainData; if (x >= p.x && x <= p.x + d.size.x && z >= p.z && z <= p.z + d.size.z) y = t.SampleHeight(new Vector3(x, 0, z)); }
        return new Vector3(x, y + up, z);
    }

    static long Tris(Mesh m) { long n = 0; for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3; return n; }

    // ---------- terrain ----------
    static float[] townBase;

    static float Height01(float x, float z)
    {
        if (townBase == null)
        {
            townBase = new float[Towns.Length];
            for (int i = 0; i < Towns.Length; i++) townBase[i] = Raw01(Towns[i].X, Towns[i].Z);
        }
        float h = Raw01(x, z);
        for (int i = 0; i < Towns.Length; i++)
        {
            var t = Towns[i];
            float dx = Mathf.Max(0f, Mathf.Abs(x - t.X) - t.W * 0.5f - 30f), dz = Mathf.Max(0f, Mathf.Abs(z - t.Z) - t.D * 0.5f - 30f);
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < 120f) h = Mathf.Lerp(townBase[i], h, Mathf.SmoothStep(0f, 1f, d / 120f));
        }
        return h;
    }

    static float Raw01(float x, float z)
    {
        // island mask, two ridges to the north and east, valley for the river; enough for a heightfield of the right size and roughness
        float nx = x / MapX, nz = z / MapZ;
        float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(nz, 1f - nz)) * 6f);
        float h = 0f, amp = 1f, f = 3f;
        for (int o = 0; o < 5; o++) { h += (Mathf.PerlinNoise(nx * f + 11f, nz * f + 7f) - 0.5f) * amp; amp *= 0.5f; f *= 2f; }
        float ridge = Mathf.Exp(-Mathf.Pow((nz - 0.85f) * 4f, 2f)) * 0.5f + Mathf.Exp(-Mathf.Pow((nx - 0.85f) * 4f, 2f)) * 0.4f;
        return Mathf.Clamp01((0.22f + h * 0.6f + ridge) * edge);
    }

    static Terrain[] MakeTerrains(Transform parent, int split, TerrainLayer[] layers, Terrain template)
    {
        var list = new List<Terrain>();
        int res = split == 1 ? 2049 : 1025;
        int alpha = split == 1 ? 2048 : 1024;
        float sx = MapX / split, sz = MapZ / split;
        int nl = Mathf.Clamp(layers.Length, 0, 4);
        for (int tz = 0; tz < split; tz++)
            for (int tx = 0; tx < split; tx++)
            {
                var td = new TerrainData { heightmapResolution = res, alphamapResolution = alpha, baseMapResolution = 1024 };
                td.size = new Vector3(sx, MapY, sz);
                var hs = new float[res, res];
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                        hs[z, x] = Height01(tx * sx + x / (float)(res - 1) * sx, tz * sz + z / (float)(res - 1) * sz);
                td.SetHeights(0, 0, hs);
                if (nl > 0)
                {
                    td.terrainLayers = layers.Take(nl).ToArray();
                    var a = new float[alpha, alpha, nl];
                    for (int z = 0; z < alpha; z++)
                        for (int x = 0; x < alpha; x++)
                        {
                            float n = Mathf.PerlinNoise(x * 0.01f + tx, z * 0.01f + tz);
                            int l = Mathf.Min(nl - 1, (int)(n * nl)); a[z, x, l] = 0.7f; a[z, x, (l + 1) % nl] = 0.3f;
                        }
                    td.SetAlphamaps(0, 0, a);
                }
                var go = Terrain.CreateTerrainGameObject(td);
                go.name = $"A1_Terrain_{tx}_{tz}";
                go.transform.SetParent(parent);
                go.transform.position = new Vector3(tx * sx, 0, tz * sz);
                var t = go.GetComponent<Terrain>();
                t.heightmapPixelError = 4f; t.basemapDistance = 110f;
                if (template != null) { t.materialTemplate = template.materialTemplate; t.shadowCastingMode = template.shadowCastingMode; }
                list.Add(t);
            }
        return list.ToArray();
    }

    // ---------- buildings ----------
    static string Populate(Transform root, Dictionary<string, List<GameObject>> byKind, Terrain[] terrains, string variant, bool dense = false)
    {
        var rng = new System.Random(20261116);
        // kind list shuffled across towns
        var pool = new List<Kind>();
        foreach (var k in Kinds) for (int i = 0; i < k.Count; i++) pool.Add(k);
        pool = pool.OrderBy(_ => rng.Next()).ToList();
        int placed = 0, rends = 0, lights = 0; long tris = 0;
        var meshesUsed = new List<Mesh>();
        foreach (var town0 in Towns)
        {
            var town = town0;
            if (dense && town.Count >= 90) { town.W = 240f; town.D = 200f; }
            int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(town.Count * town.W / town.D)));
            int rowsN = Mathf.CeilToInt(town.Count / (float)cols);
            float px = Mathf.Max(14f, town.W / cols), pz = Mathf.Max(14f, town.D / rowsN);
            for (int i = 0; i < town.Count; i++)
            {
                var kind = pool[placed++ % pool.Count];
                if (!byKind.TryGetValue(kind.Template, out var temps) || temps.Count == 0) temps = byKind.Values.First();
                var tpl = temps[rng.Next(temps.Count)];
                float x = town.X - town.W / 2 + (i % cols + 0.5f) * px + (float)(rng.NextDouble() - 0.5) * 3f;
                float z = town.Z - town.D / 2 + (i / cols + 0.5f) * pz + (float)(rng.NextDouble() - 0.5) * 3f;
                var pos = At(terrains, x, z, 0f);
                var rot = Quaternion.Euler(0, rng.Next(4) * 90, 0);
                float stackH = 0f;
                for (int s = 0; s < kind.Stacks; s++)
                {
                    var c = BuildOne(variant, tpl, root, pos + Vector3.up * stackH, rot, out var h);
                    stackH += h;
                    foreach (var r in c.GetComponentsInChildren<MeshRenderer>(true)) { rends++; var mf = r.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) { tris += Tris(mf.sharedMesh); meshesUsed.Add(mf.sharedMesh); } }
                    lights += c.GetComponentsInChildren<Light>(true).Length;
                }
            }
        }
        long meshBytes = meshesUsed.Sum(m => Profiler.GetRuntimeMemorySizeLong(m));
        return $"{rends} renderers, {tris} tris total, {lights} lights, mesh memory if every building owned its meshes {meshBytes / 1048576.0:F0} MB";
    }

    static GameObject BuildOne(string variant, GameObject tpl, Transform root, Vector3 pos, Quaternion rot, out float height)
    {
        var bounds = new Bounds(tpl.transform.position, Vector3.zero);
        foreach (var r in tpl.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(r.bounds);
        height = Mathf.Max(3f, bounds.size.y - 1.2f);
        if (variant == "box")
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(root);
            cube.transform.position = pos + Vector3.up * height * 0.5f;
            cube.transform.rotation = rot;
            cube.transform.localScale = new Vector3(bounds.size.x, height, bounds.size.z);
            var shell = tpl.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name.StartsWith("Shell_"));
            cube.GetComponent<MeshRenderer>().sharedMaterial = shell.sharedMaterial;
            return cube;
        }
        var go = UnityEngine.Object.Instantiate(tpl, pos, rot, root);
        if (variant == "shell")
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true).Where(t => t != null && t.name.StartsWith("Inside_")).ToList())
                UnityEngine.Object.DestroyImmediate(t.gameObject);
            foreach (var l in go.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(l.gameObject);
            var g = go.GetComponent<LODGroup>();
            if (g != null) UnityEngine.Object.DestroyImmediate(g);
            var shellRenderers = go.GetComponentsInChildren<Renderer>(true);
            var lg = go.AddComponent<LODGroup>();
            lg.SetLODs(new[] { new LOD(0.02f, shellRenderers) });
            lg.RecalculateBounds();
        }
        return go;
    }

    // ---------- rendering ----------
    static Row Shoot(Camera cam, Vector3 pos, Vector3 forward, string variant, string terrain, string view, GameObject island)
    {
        var oldPos = cam.transform.position; var oldRot = cam.transform.rotation; var oldTarget = cam.targetTexture;
        cam.transform.position = pos; cam.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
        var probe = new Texture2D(1, 1, TextureFormat.RGB24, false);
        var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        const int frames = 40, warm = 10;
        var times = new List<float>();
        var sw = new Stopwatch();
        for (int i = 0; i < frames; i++)
        {
            sw.Restart();
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = prev;
            sw.Stop();
            if (i >= warm) times.Add((float)sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        var row = new Row
        {
            variant = variant, terrain = terrain, view = view,
            avgMs = times.Average(), p99Ms = times[Mathf.Min(times.Count - 1, (int)(times.Count * 0.99f))],
            drawCalls = draws.LastValue, tris = tris.LastValue, setPass = setPass.LastValue,
        };
        row.statDraws = UnityStats.drawCalls; row.statBatches = UnityStats.batches; row.statSetPass = UnityStats.setPassCalls; row.statTris = UnityStats.triangles;
        var shot = new Texture2D(W, H, TextureFormat.RGB24, false);
        RenderTexture.active = rt; shot.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
        File.WriteAllBytes($"Logs/perf/a1_{variant}_{terrain}_{view}.png", shot.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(shot);
        draws.Dispose(); tris.Dispose(); setPass.Dispose();
        cam.targetTexture = oldTarget; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(probe);
        Estimate(cam, island, row);
        cam.transform.position = oldPos; cam.transform.rotation = oldRot;
        return row;
    }

    // Independent CPU estimate: renderers inside the frustum, LOD chosen from screen height, far clip applied.
    static void Estimate(Camera cam, GameObject island, Row row)
    {
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        int draws = 0; long tris = 0; int lights = 0;
        foreach (var l in island.GetComponentsInChildren<Light>()) if (l.enabled && l.type != LightType.Directional && Vector3.Distance(l.transform.position, cam.transform.position) < l.range + 60f) lights++;
        row.lights = lights;
        var counted = new HashSet<Renderer>();
        foreach (var g in island.GetComponentsInChildren<LODGroup>())
        {
            var lods = g.GetLODs();
            var center = g.transform.TransformPoint(g.localReferencePoint);
            float dist = Vector3.Distance(center, cam.transform.position);
            if (dist > cam.farClipPlane) { foreach (var lod in lods) foreach (var r in lod.renderers) if (r) counted.Add(r); continue; }
            if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(center, Vector3.one * g.size * g.transform.lossyScale.x))) { foreach (var lod in lods) foreach (var r in lod.renderers) if (r) counted.Add(r); continue; }
            float rel = g.size * g.transform.lossyScale.x / (2f * Mathf.Max(0.01f, dist) * tan);
            int pick = -1;
            for (int i = 0; i < lods.Length; i++) if (rel >= lods[i].screenRelativeTransitionHeight) { pick = i; break; }
            foreach (var lod in lods) foreach (var r in lod.renderers) if (r) counted.Add(r);
            if (pick < 0) continue;
            foreach (var r in lods[pick].renderers) { if (r == null || !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue; var mf = r.GetComponent<MeshFilter>(); if (mf == null) continue; draws += mf.sharedMesh.subMeshCount; tris += Tris(mf.sharedMesh); }
        }
        foreach (var r in island.GetComponentsInChildren<MeshRenderer>())
        {
            if (counted.Contains(r) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, r.bounds) || Vector3.Distance(r.bounds.center, cam.transform.position) > cam.farClipPlane) continue;
            var mf = r.GetComponent<MeshFilter>(); if (mf == null) continue;
            draws += mf.sharedMesh.subMeshCount; tris += Tris(mf.sharedMesh);
        }
        row.estDraws = draws; row.estTris = tris;
    }

    // ---------- navmesh ----------
    static void NavBake(GameObject island, Terrain[] terrain, Town capital)
    {
        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = 0.35f; settings.agentHeight = 1.8f; settings.agentSlope = new WorldRuleSpec().WalkSlopeDeg; settings.agentClimb = 0.3f;
        settings.overrideVoxelSize = true; settings.voxelSize = 0.35f / 3f; settings.overrideTileSize = true; settings.tileSize = 256; settings.minRegionArea = 4f;
        foreach (var (label, bounds) in new[]
        {
            ("capital block 600x500 m", new Bounds(new Vector3(capital.X, 100, capital.Z), new Vector3(600, 260, 500))),
            ("whole island 2400x2000 m", new Bounds(new Vector3(MapX / 2, 100, MapZ / 2), new Vector3(MapX, 260, MapZ))),
        })
        {
            var sources = new List<NavMeshBuildSource>();
            var sw = Stopwatch.StartNew();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.RenderMeshes, 0, new List<NavMeshBuildMarkup>(), sources);
            foreach (var t in terrain)
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Terrain, sourceObject = t.terrainData, transform = Matrix4x4.TRS(t.transform.position, Quaternion.identity, Vector3.one), area = 0 });
            double collect = sw.Elapsed.TotalSeconds;
            sw.Restart();
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            Log($"navmesh {label}: {sources.Count} sources (all render meshes = upper bound; the real builder lists floors+stairs only), collect {collect:F1} s, bake {sw.Elapsed.TotalSeconds:F1} s, data {(data == null ? 0 : Profiler.GetRuntimeMemorySizeLong(data)) / 1048576.0:F1} MB runtime");
        }
    }

    // Scene files: World alone (World inactive but still saved) vs plus 411 detailed clones, saved as copies under Logs/perf.
    static void SceneSize(GameObject island, Dictionary<string, List<GameObject>> byKind)
    {
        var baseline = "Logs/perf/a1_world_only.unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), baseline, true);
        var root = new GameObject("A1_tmp"); root.transform.SetParent(island.transform);
        Populate(root.transform, byKind, new Terrain[0], "detail");
        var path = "Logs/perf/a1_detail_411.unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path, true);
        double a = new FileInfo(baseline).Length / 1048576.0, b = new FileInfo(path).Length / 1048576.0;
        Log($"scene YAML: World only {a:F1} MB, with 411 detailed clones {b:F1} MB (+{b - a:F1} MB, {(b - a) / 411 * 1024:F0} KB/bldg; meshes are shared asset references, so the real per-building mesh cost comes on top)");
        UnityEngine.Object.DestroyImmediate(root);
    }
}
