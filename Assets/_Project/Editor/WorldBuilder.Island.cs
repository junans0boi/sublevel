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
}
