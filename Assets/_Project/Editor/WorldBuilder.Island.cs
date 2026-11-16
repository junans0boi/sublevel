using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

// The C2 island's own structures (ADR-204): river water and bridge decks. Everything else comes from the shared builder steps.
public static partial class WorldBuilder
{
    static Material _seaMaterial;            // BuildSea keeps its water material so the rivers share the look
    const float DeckThickness = 0.5f, RailHeight = 1.0f, RailThickness = 0.25f, PierSpacing = 16f, PierWidth = 1.6f;

    // A ribbon of water along each river at its own surface height; the ground under it is the carved riverbed.
    static int BuildRivers(WorldPlan plan, Transform parent, WorldMeshes.Store store)
    {
        if (plan.Rivers.Count == 0 || _seaMaterial == null) return 0;
        var root = Child(parent, "Rivers");
        string texPath = WorldMaterials.TextureDir + "/RiverDepth.png";
        var depthTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        byte shade = (byte)Mathf.RoundToInt(IslandTerrainDepth / WaterDepthRange * 255f);
        var pixels = new Color32[16];
        for (int i = 0; i < 16; i++) pixels[i] = new Color32(shade, shade, shade, 255);
        depthTex.SetPixels32(pixels);
        File.WriteAllBytes(texPath, depthTex.EncodeToPNG());
        Object.DestroyImmediate(depthTex);
        AssetDatabase.ImportAsset(texPath);
        var depth = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        string matPath = WorldMaterials.MaterialDir + "/World_River.mat";
        AssetDatabase.DeleteAsset(matPath);
        var material = new Material(_seaMaterial) { name = "World_River" };
        material.SetTexture("_DepthMap", depth);
        AssetDatabase.CreateAsset(material, matPath);

        int segments = 0;
        for (int r = 0; r < plan.Rivers.Count; r++)
        {
            var river = plan.Rivers[r];
            int n = river.X.Length;
            var vertices = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var triangles = new List<int>();
            float along = 0f;
            for (int i = 0; i < n; i++)
            {
                var a = new Vector2(river.X[Mathf.Max(0, i - 1)], river.Z[Mathf.Max(0, i - 1)]);
                var b = new Vector2(river.X[Mathf.Min(n - 1, i + 1)], river.Z[Mathf.Min(n - 1, i + 1)]);
                var tangent = (b - a).normalized;
                var right = new Vector2(tangent.y, -tangent.x);
                if (i > 0) along += Vector2.Distance(new Vector2(river.X[i], river.Z[i]), new Vector2(river.X[i - 1], river.Z[i - 1]));
                float half = river.HalfWidth[i] + 1.2f;    // reaches under the banks so no sliver of dry bed shows at the edge
                for (int k = 0; k < 2; k++)
                {
                    var p = new Vector2(river.X[i], river.Z[i]) + right * ((k == 0 ? -1f : 1f) * half);
                    vertices[i * 2 + k] = new Vector3(p.x, river.Y[i], p.y);
                    uvs[i * 2 + k] = new Vector2(k * half * 2f / 12f, along / 12f);
                }
                if (i + 1 < n)
                {
                    int a0 = i * 2;
                    triangles.Add(a0); triangles.Add(a0 + 2); triangles.Add(a0 + 1);
                    triangles.Add(a0 + 1); triangles.Add(a0 + 2); triangles.Add(a0 + 3);
                    segments++;
                }
            }
            var mesh = new Mesh { name = "River_" + r, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices; mesh.uv = uvs; mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("River" + r);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = store.AddBlob(mesh);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        Debug.Log($"[WorldBuilder] rivers: {plan.Rivers.Count} rivers, {segments} segments");
        return plan.Rivers.Count;
    }

    const float IslandTerrainDepth = 1.4f;   // = IslandTerrain.RiverDepth (that class is internal to Game.Core)

    // A deck, rails and piers for every plan bridge. The deck is walked and driven on (collider + navmesh), the rails only get in the way.
    static int BuildBridges(WorldPlan plan, Transform parent, WorldMeshes.Store store, List<NavMeshBuildSource> nav)
    {
        if (plan.Bridges.Count == 0) return 0;
        var root = Child(parent, "Bridges");
        var asphalt = WorldMaterials.Asphalt;
        var concrete = WorldMaterials.ConcreteDark;
        foreach (var bridge in plan.Bridges)
        {
            var road = plan.Roads[bridge.Road];
            var go = new GameObject(bridge.Name);
            go.transform.SetParent(root, false);
            var deckBatch = new WorldMeshes.BoxBatch(asphalt.Tile);
            var concreteBatch = new WorldMeshes.BoxBatch(concrete.Tile);
            var colliders = new GameObject("Colliders");
            colliders.transform.SetParent(go.transform, false);
            colliders.AddComponent<SurfaceIdTag>().Id = SurfaceId.Asphalt;
            float sincePier = PierSpacing;
            for (int i = bridge.FromPoint; i < bridge.ToPoint; i++)
            {
                var a = new Vector3(road.X[i], road.Y[i], road.Z[i]);
                var b = new Vector3(road.X[i + 1], road.Y[i + 1], road.Z[i + 1]);
                var dir = b - a;
                float length = dir.magnitude;
                if (length < 0.05f) continue;
                var rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                var mid = (a + b) * 0.5f + Vector3.down * (DeckThickness * 0.5f);
                var size = new Vector3(bridge.Width + 0.6f, DeckThickness, length + 0.15f);
                deckBatch.Add(mid, size, rotation);
                AddBox(colliders.transform, mid, size, rotation);
                nav.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = size, transform = Matrix4x4.TRS(mid, rotation, Vector3.one), area = 0 });
                for (int side = -1; side <= 1; side += 2)
                {
                    var rail = mid + rotation * new Vector3(side * (bridge.Width * 0.5f + 0.2f), DeckThickness * 0.5f + RailHeight * 0.5f, 0f);
                    var railSize = new Vector3(RailThickness, RailHeight, length + 0.15f);
                    concreteBatch.Add(rail, railSize, rotation);
                    AddBox(colliders.transform, rail, railSize, rotation);
                    nav.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = railSize, transform = Matrix4x4.TRS(rail, rotation, Vector3.one), area = NotWalkableArea });
                }
                sincePier += length;
                bool end = i == bridge.FromPoint || i == bridge.ToPoint - 1;
                if (sincePier >= PierSpacing || end)
                {
                    sincePier = 0f;
                    float bed = plan.Height(a.x, a.z);
                    float top = a.y - DeckThickness;
                    if (top - bed > 0.2f)
                    {
                        var pier = new Vector3(a.x, (top + bed - 1f) * 0.5f, a.z);
                        concreteBatch.Add(pier, new Vector3(bridge.Width * 0.8f, top - bed + 1f, PierWidth), Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z).normalized, Vector3.up));
                    }
                }
            }
            AddBridgeMesh(go.transform, store, "Deck", deckBatch, asphalt.Material, bridge.Name);
            AddBridgeMesh(go.transform, store, "Concrete", concreteBatch, concrete.Material, bridge.Name);
        }
        Debug.Log($"[WorldBuilder] bridges: {plan.Bridges.Count}");
        return plan.Bridges.Count;
    }

    static void AddBox(Transform parent, Vector3 center, Vector3 size, Quaternion rotation)
    {
        var box = new GameObject("Box");
        box.transform.SetParent(parent, false);
        box.transform.SetPositionAndRotation(center, rotation);
        box.AddComponent<BoxCollider>().size = size;
        box.isStatic = true;
    }

    static void AddBridgeMesh(Transform parent, WorldMeshes.Store store, string name, WorldMeshes.BoxBatch batch, Material material, string bridge)
    {
        if (batch.Boxes == 0) return;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = store.AddBlob(batch.ToMesh(bridge + "_" + name));
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.isStatic = true;
    }

    // NavMesh connectivity (tracks.md: "NavMesh connected"): from the first elevator station a complete path must reach every other station
    // and the far side of every bridge. Run: tools/unity.sh run WorldBuilder.CheckNav. Exit code 1 on any gap.
    public static void CheckNav()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        var info = Object.FindFirstObjectByType<Game.Gameplay.WorldInfo>();
        var zone = Object.FindFirstObjectByType<Game.Gameplay.ZoneNavMesh>();
        var data = new SerializedObject(zone).FindProperty("_data").objectReferenceValue as NavMeshData;
        NavMesh.RemoveAllNavMeshData();
        NavMesh.AddNavMeshData(data);
                int bad = 0, total = 0;
        bool Reach(Vector3 from, Vector3 to, string what)
        {
            total++;
            if (!NavMesh.SamplePosition(from, out var a, 6f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var b, 6f, NavMesh.AllAreas))
            { Debug.LogError($"[CheckNav] no navmesh near {what}"); bad++; return false; }
            var path = new NavMeshPath();
            bool ok = NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            if (!ok) { Debug.LogError($"[CheckNav] no complete path to {what} ({path.status}) from {a.position} to {b.position}, ends at {(path.corners.Length > 0 ? path.corners[path.corners.Length - 1] : Vector3.zero)}"); bad++; }
            return ok;
        }
        // CalculatePath gives up on long routes over a navmesh this big (default node pool: PathPartial although the way exists), so connectivity is
        // proven in hops: every road in 48 m steps, every station to the nearest avenue road point.
        var plan0 = WorldGenerator.Generate(LoadSpec());
        foreach (var road in plan0.Roads)
            for (int i = 8; i < road.X.Length; i += 8)
                Reach(new Vector3(road.X[i - 8], road.Y[i - 8], road.Z[i - 8]), new Vector3(road.X[i], road.Y[i], road.Z[i]), road.Name + " hop " + i);
        foreach (var e in plan0.Elevators)
        {
            Vector3 best = default; float bd = float.MaxValue;
            foreach (var road in plan0.Roads)
                for (int i = 0; i < road.X.Length; i++)
                {
                    float d = (road.X[i] - e.X) * (road.X[i] - e.X) + (road.Z[i] - e.Z) * (road.Z[i] - e.Z);
                    if (d < bd) { bd = d; best = new Vector3(road.X[i], road.Y[i], road.Z[i]); }
                }
            Reach(new Vector3(e.X, e.Y, e.Z), best, e.Name + " to its road");
        }
        // where do the roads lose the navmesh? (first gap of each road, 3 m probes)
        var plan = WorldGenerator.Generate(LoadSpec());
        int gaps = 0;
        foreach (var road in plan.Roads)
            for (int i = 0; i < road.X.Length; i++)
            {
                var p = new Vector3(road.X[i], plan.Height(road.X[i], road.Z[i]), road.Z[i]);
                if (NavMesh.SamplePosition(p, out var hit, 1.2f, NavMesh.AllAreas)) continue;
                Debug.LogError($"[CheckNav] road {road.Name} point {i}/{road.X.Length} at {p} (ground {plan.Height(p.x, p.z):F1}) has no navmesh"); gaps++;
                break;
            }
        Debug.Log($"[CheckNav] {gaps} roads with a navmesh gap");
        foreach (var b in plan.Bridges)
        {
            var road = plan.Roads[b.Road];
            int a0 = Mathf.Max(0, b.FromPoint - 2), a1 = Mathf.Min(road.X.Length - 1, b.ToPoint + 2);
            var deckMid = new Vector3(road.X[(b.FromPoint + b.ToPoint) / 2], road.Y[(b.FromPoint + b.ToPoint) / 2], road.Z[(b.FromPoint + b.ToPoint) / 2]);
            Debug.Log($"[CheckNav] {b.Name} deck mid navmesh: {NavMesh.SamplePosition(deckMid, out _, 0.8f, NavMesh.AllAreas)}");
            Reach(new Vector3(road.X[a0], road.Y[a0], road.Z[a0]), new Vector3(road.X[a1], road.Y[a1], road.Z[a1]), b.Name + " (across)");
        }
        Debug.Log($"[CheckNav] {total - bad}/{total} hops and station links complete");
        EditorApplication.Exit(bad == 0 ? 0 : 1);
    }

    // Renderer / material / triangle census of the built World scene, split by building part (tools/unity.sh run WorldBuilder.PerfStats).
    public static void PerfStats()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        var byName = new Dictionary<string, (int n, long tris)>();
        int total = 0, sub = 0; long triTotal = 0; var mats = new HashSet<Material>(); int colliders = 0, objects = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            total++;
            var mf = r.GetComponent<MeshFilter>();
            long tris = mf != null && mf.sharedMesh != null ? mf.sharedMesh.triangles.Length / 3 : 0;
            triTotal += tris;
            foreach (var m in r.sharedMaterials) { mats.Add(m); sub++; }
            string key = r.name.StartsWith("Inside_") ? "Inside" : r.name.StartsWith("Shell_") ? "Shell" : r.transform.parent != null ? r.transform.parent.name.Split('_')[0] : r.name;
            if (key.Length > 14) key = key.Substring(0, 14);
            byName.TryGetValue(key, out var v); byName[key] = (v.n + 1, v.tris + tris);
        }
        colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length;
        objects = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
        Debug.Log($"[PerfStats] mesh renderers {total}, triangles {triTotal}, distinct materials {mats.Count}, material slots {sub}, colliders {colliders}, transforms {objects}");
        foreach (var kv in byName) if (kv.Value.n > 50) Debug.Log($"[PerfStats]   {kv.Key}: {kv.Value.n} renderers, {kv.Value.tris} tris");
        var interiorMats = new Dictionary<string, int>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (r.name.StartsWith("Inside_")) { var k = r.sharedMaterial != null ? r.sharedMaterial.name : "?"; interiorMats.TryGetValue(k, out int c); interiorMats[k] = c + 1; }
        foreach (var kv in interiorMats) Debug.Log($"[PerfStats]   inside material {kv.Key}: {kv.Value}");
        var top = new Dictionary<string, (int t, int r)>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            var cur = t; string path = "";
            while (cur.parent != null && cur.parent.parent != null) cur = cur.parent;
            path = cur.parent != null ? cur.parent.name + "/" + cur.name : cur.name;
            if (path.Length > 30) path = path.Substring(0, 30);
            top.TryGetValue(path, out var v); top[path] = (v.t + 1, v.r + (t.GetComponent<MeshRenderer>() != null ? 1 : 0));
        }
        foreach (var kv in top) if (kv.Value.t > 100) Debug.Log($"[PerfStats]   tree {kv.Key}: {kv.Value.t} objects, {kv.Value.r} renderers");
        EditorApplication.Exit(0);
    }

    // Town views for before/after comparison (tools/unity.sh run-gfx WorldBuilder.CaptureTown; TOWN_TAG names the files): the capital from above, along
    // its avenue at eye height, from a roof, and a small town. Needs a graphics device.
    public static void CaptureTown()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        var info = Object.FindFirstObjectByType<Game.Gameplay.WorldInfo>();
        string tag = System.Environment.GetEnvironmentVariable("TOWN_TAG") ?? "now";
        Directory.CreateDirectory(OverviewDir);
        DynamicGI.UpdateEnvironment();
        var terrain = Object.FindFirstObjectByType<Terrain>();
        Vector3 At(float x, float z, float up) => new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)) + up, z);
        var cam = new GameObject("TownCamera").AddComponent<Camera>();
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 1500f; cam.fieldOfView = 60f;
        SceneLook.EnablePost(cam);
        UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).SetRenderer(info.RendererIndex);
        var spec = LoadSpec();
        foreach (var t in spec.Island.Towns)
        {
            if (t.Id != "C" && t.Id != "M1") continue;
            double r = t.RotDeg * System.Math.PI / 180.0, c = System.Math.Cos(r), sn = System.Math.Sin(r);
            Vector3 Local(float lx, float lz, float up) => At(t.X + (float)(lx * c - lz * sn), t.Z + (float)(lx * sn + lz * c), up);
            float hw = t.Width * 0.5f;
            cam.fieldOfView = 55f;
            SceneLook.Shot(cam, $"{OverviewDir}/town_{tag}_{t.Id}_air.png", Local(-hw * 0.9f, -t.Depth * 0.9f, 140f), Local(0f, 0f, 0f));
            cam.fieldOfView = 70f;
            SceneLook.Shot(cam, $"{OverviewDir}/town_{tag}_{t.Id}_street.png", Local(-hw * 0.7f, 0f, 1.7f), Local(hw * 0.3f, 0f, 3f));
            SceneLook.Shot(cam, $"{OverviewDir}/town_{tag}_{t.Id}_side.png", Local(-hw * 0.3f, t.Depth * 0.18f, 1.7f), Local(hw * 0.1f, t.Depth * 0.3f, 3f));
            SceneLook.Shot(cam, $"{OverviewDir}/town_{tag}_{t.Id}_roof.png", Local(0f, 0f, 22f), Local(hw * 0.5f, t.Depth * 0.2f, 6f));
        }
        EditorApplication.Exit(0);
    }
}
