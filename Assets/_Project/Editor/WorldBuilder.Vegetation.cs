using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Trees, bushes, grass and the sea stacks off the coast (#71, ADR-070). Where every plant stands is Vegetation.Plan
// (Game.Core); the meshes and materials are WorldVegetation's. Plants are drawn instanced by one VegetationRenderer,
// grass by one GrassRenderer from a baked map; only the trunks are GameObjects (a capsule each, for bodies, shots and
// the navmesh).
public static partial class WorldBuilder
{
    const string GrassMapPath = WorldMaterials.GeneratedDir + "/World_Grass.bytes";
    const float PlantSink = 0.08f;          // feet a little into the ground
    const float MaxTrunkCollider = 7f;      // capsule height cap: the crown above it does not stop anything

    // Sea stacks: rocks standing in the sea beyond the wading line, so the view out from the shore has something in
    // it and the outline reads as a coast rather than the edge of a plate.
    const int OffshoreRays = 44;
    const float OffshoreChance = 0.45f, OffshoreDepth = 3f;

    static void BuildVegetation(WorldPlan plan, WorldMeshes.HeightField ground, Terrain terrain, Transform parent, WorldMeshes.Store store,
        List<NavMeshBuildSource> nav)
    {
        var watch = Stopwatch.StartNew();
        var kit = WorldVegetation.Build(store);
        var plants = Vegetation.Plan(plan);
        var root = Child(parent, "Vegetation");
        var trunks = Child(root, "Trunks");
        trunks.gameObject.isStatic = true;

        var prototype = new int[plants.Count];
        var placement = new Vector4[plants.Count];
        var yaw = new float[plants.Count];
        int colliders = 0;
        for (int i = 0; i < plants.Count; i++)
        {
            var p = plants[i];
            int proto = kit.Prototype(p.Kind, p.Variant);
            float y = ground.Height(p.X, p.Z) - PlantSink;
            prototype[i] = proto;
            placement[i] = new Vector4(p.X, y, p.Z, p.Scale);
            yaw[i] = p.YawDeg;
            if (!p.HasTrunk) continue;

            // Bushes stop nothing; trunks stop bodies and shots and are holes in the navmesh.
            float radius = kit.TrunkRadius[proto] * p.Scale + 0.05f;
            float height = Mathf.Min(kit.TrunkHeight[proto] * p.Scale, MaxTrunkCollider);
            var go = new GameObject("Trunk" + colliders++);
            go.transform.SetParent(trunks, false);
            go.transform.position = new Vector3(p.X, y, p.Z);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = Vector3.up * (height * 0.5f);
            go.isStatic = true;
            float side = radius * 2f + 0.3f;
            nav.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.ModifierBox,
                size = new Vector3(side, height + 2f, side),
                transform = Matrix4x4.TRS(new Vector3(p.X, y + height * 0.5f, p.Z), Quaternion.identity, Vector3.one),
                area = NotWalkableArea,
            });
        }
        root.gameObject.AddComponent<VegetationRenderer>().Fill(kit.Prototypes, prototype, placement, yaw);

        var map = BakeGrass(plan, ground, terrain.terrainData, out int grassy, out int reeds);
        var grass = Child(root, "Grass");
        grass.gameObject.AddComponent<GrassRenderer>().Fill(map, Vegetation.GrassMapResolution(plan.Size), Vegetation.GrassMapCell, plan.Seed,
            terrain, kit.GrassShort, kit.GrassTall, kit.Grass, kit.FlowerClump, kit.Flowers);

        long nearTriangles = 0;
        foreach (var p in plants) nearTriangles += Triangles(kit.Prototypes[kit.Prototype(p.Kind, p.Variant)].Lods[0].Mesh);
        Debug.Log($"[WorldBuilder] vegetation in {watch.Elapsed.TotalSeconds:F1} s: {plants.Count(p => p.Kind == PlantKind.Broadleaf)} broadleaf, " +
            $"{plants.Count(p => p.Kind == PlantKind.Conifer)} conifer ({colliders} trunk colliders, plan {plan.Trees.Count}), " +
            $"{plants.Count(p => p.Kind == PlantKind.Bush)} bushes; {nearTriangles} triangles if all were at full detail; " +
            $"grass map {grassy} grass and {reeds} reed cells of {Vegetation.GrassMapCell} m");
    }

    static long Triangles(Mesh mesh)
    {
        long n = 0;
        for (int s = 0; s < mesh.subMeshCount; s++) n += mesh.GetIndexCount(s) / 3;
        return n;
    }

    // Where grass grows, one byte per GrassMapCell (Vegetation.EncodeGrass): the terrain's own grass layer as it was
    // painted (so grass stands where the ground looks grassy), thinned in patches, kept off roads and pads; reeds in
    // the wetland shallows.
    static TextAsset BakeGrass(WorldPlan plan, WorldMeshes.HeightField ground, TerrainData data, out int grassy, out int reeds)
    {
        int res = Vegetation.GrassMapResolution(plan.Size);
        float cell = Vegetation.GrassMapCell;
        int ares = data.alphamapResolution;
        var alpha = data.GetAlphamaps(0, 0, ares, ares);
        var bytes = new byte[res * res];
        grassy = 0;
        reeds = 0;
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float wx = (x + 0.5f) * cell, wz = (z + 0.5f) * cell;
                float h = ground.Height(wx, wz);
                float allowed = Vegetation.GrassAllowed(plan, wx, wz, h);
                if (allowed <= 0f) continue;
                float patch = Mathf.Clamp01(0.45f + 0.75f * Mathf.PerlinNoise(wx / 19f + 3.3f, wz / 19f + 8.1f) - 0.2f * Mathf.PerlinNoise(wx / 5f + 1.7f, wz / 5f + 6.2f));
                bool tall = Vegetation.TallGrass(plan, wx, wz, h);
                float density;
                if (tall) density = allowed * (0.5f + 0.5f * patch);
                else
                {
                    int ax = Mathf.Clamp((int)(wx / plan.Size * ares), 0, ares - 1), az = Mathf.Clamp((int)(wz / plan.Size * ares), 0, ares - 1);
                    int region = plan.RegionAt(wx, wz);
                    var kind = region >= 0 ? plan.Regions[region].Kind : RegionKind.Town;
                    float regionShare = kind == RegionKind.Factory ? 0.6f : kind == RegionKind.Mountain ? 0.85f : 1f;
                    density = allowed * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.55f, alpha[az, ax, 0])) * regionShare * patch;
                }
                byte v = Vegetation.EncodeGrass(density, tall);
                bytes[z * res + x] = v;
                if (v >= 128) reeds++;
                else if (v > 0) grassy++;
            }
        }
        File.WriteAllBytes(GrassMapPath, bytes);
        AssetDatabase.ImportAsset(GrassMapPath);
        return SceneLook.Load<TextAsset>(GrassMapPath);
    }

    // Big rocks set in the sea a little way out, beyond where anyone can wade: stretched upward into stacks.
    static int BuildOffshore(WorldPlan plan, WorldMeshes.HeightField ground, Transform parent)
    {
        var withLods = new List<RockVariant>();
        var single = new List<RockVariant>();
        for (int set = 0; set < WorldMaterials.RockSets.Length; set++) LoadRockSet(set, withLods, single);
        if (withLods.Count == 0) return 0;
        var root = Child(parent, "Offshore");
        var unused = new List<NavMeshBuildSource>(); // out of reach: nothing to cut out of the navmesh
        var centre = new Vector2(plan.Size * 0.5f, plan.Size * 0.5f);
        int placed = 0;
        for (int i = 0; i < OffshoreRays; i++)
        {
            uint h = Hash(plan.Seed, 950000 + i);
            if (Unit(h, 0) > OffshoreChance) continue;
            float angle = (i + 0.8f * Unit(h, 10)) / OffshoreRays * Mathf.PI * 2f;
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            // Out from the middle to where the sea is OffshoreDepth deep, then a little further.
            float r = 200f;
            bool found = false;
            for (; r < plan.Size * 0.72f; r += 2f)
            {
                var p = centre + dir * r;
                if (ground.Height(p.x, p.y) < plan.SeaLevel - OffshoreDepth) { found = true; break; }
            }
            if (!found) continue;
            r += 6f + 50f * Unit(h, 20);
            int count = 1 + (int)(Unit(h, 5) * 2.99f);
            for (int k = 0; k < count; k++)
            {
                uint kh = Hash((int)h, 17 + k);
                var side = new Vector2(-dir.y, dir.x);
                var at = centre + dir * (r + 8f * k) + side * (Unit(kh, 0) - 0.5f) * 18f;
                if (at.x < 6f || at.y < 6f || at.x > plan.Size - 6f || at.y > plan.Size - 6f) continue;
                float floor = ground.Height(at.x, at.y);
                if (floor > plan.SeaLevel - OffshoreDepth + 0.5f) continue;
                var variant = withLods[(int)((kh >> 8) % (uint)withLods.Count)];
                float width = (k == 0 ? 9f : 5f) + 12f * Unit(kh, 10) * Unit(kh, 20);
                PlaceRock(root, variant, placed, new Vector3(at.x, floor, at.y), (kh >> 3) % 360u, width, 0.05f, unused);
                // Tall enough to stand 2..9 m out of the water whatever the depth.
                var rock = root.GetChild(root.childCount - 1);
                float standing = rock.localScale.y * variant.Size.y;
                float wanted = plan.SeaLevel - floor + 2f + 7f * Unit(kh, 25);
                float stretch = Mathf.Clamp(wanted / Mathf.Max(0.5f, standing), 1f, 3.2f);
                var scale = rock.localScale;
                scale.y *= stretch;
                rock.localScale = scale;
                rock.GetComponent<LODGroup>().RecalculateBounds();
                placed++;
            }
        }
        Debug.Log($"[WorldBuilder] offshore: {placed} sea stacks");
        return placed;
    }

    // ---- pictures for review (called from RenderOverview) ----

    // In a mountain wood, a town street with its trees, the wetland reeds, a meadow at grass height, and out to sea
    // from the shore past the stacks.
    static void RenderVegetationViews(Camera cam, WorldInfo info, Terrain terrain)
    {
        Vector3 Ground(float x, float z) => new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)), z);
        var plan = WorldGenerator.Generate(LoadSpec());
        var plants = Vegetation.Plan(plan);
        var trees = plants.Where(p => p.HasTrunk).ToList();
        int Around(Plant at, float r) => trees.Count(t => (t.X - at.X) * (t.X - at.X) + (t.Z - at.Z) * (t.Z - at.Z) < r * r);

        int mountain = plan.Regions.First(r => r.Kind == RegionKind.Mountain).Index;
        var wood = trees.Where(t => t.Region == mountain).OrderByDescending(t => Around(t, 18f)).FirstOrDefault();
        if (wood.Scale > 0f)
        {
            // A step off the densest tree, looking across the wood.
            var eye = Ground(wood.X + 2.2f, wood.Z + 1.4f) + Vector3.up * 1.7f;
            var dense = trees.Where(t => t.Region == mountain && Around(t, 14f) > 3).OrderBy(t => (t.X - wood.X) * (t.X - wood.X) + (t.Z - wood.Z) * (t.Z - wood.Z)).Skip(6).FirstOrDefault();
            var look = dense.Scale > 0f ? Ground(dense.X, dense.Z) + Vector3.up * 3f : eye + Vector3.forward * 20f;
            SceneLook.Shot(cam, OverviewDir + "/world_forest.png", eye, look);
            SceneLook.Shot(cam, OverviewDir + "/world_forest_far.png", Ground(wood.X, wood.Z - 90f) + Vector3.up * 1.7f, Ground(wood.X, wood.Z) + Vector3.up * 6f);
        }

        int town = plan.Regions.First(r => r.Kind == RegionKind.Town).Index;
        var street = plants.Skip(plan.Trees.Count).Where(p => p.HasTrunk && p.Region == town).FirstOrDefault();
        if (street.Scale > 0f)
            SceneLook.Shot(cam, OverviewDir + "/world_town_trees.png", Ground(street.X + 14f, street.Z - 10f) + Vector3.up * 1.7f, Ground(street.X, street.Z) + Vector3.up * 3f);

        var wetland = info.Regions.First(r => r.Kind == RegionKind.Wetland);
        Vector3? reeds = null;
        for (int i = 0; i < 4000 && reeds == null; i++)
        {
            float a = i * 0.618f * Mathf.PI * 2f, r = 6f + i * 0.06f;
            float x = wetland.Hub.x + Mathf.Cos(a) * r, z = wetland.Hub.z + Mathf.Sin(a) * r;
            float h = terrain.SampleHeight(new Vector3(x, 0f, z));
            if (h > plan.SeaLevel + 0.15f && Vegetation.TallGrass(plan, x, z, h) && Vegetation.GrassAllowed(plan, x, z, h) > 0.8f) reeds = new Vector3(x, h, z);
        }
        if (reeds != null)
            SceneLook.Shot(cam, OverviewDir + "/world_reeds.png", reeds.Value + new Vector3(-6f, 1.6f, -6f), reeds.Value + new Vector3(4f, 0.4f, 4f));

        var townHub = info.Regions.First(r => r.Kind == RegionKind.Town).Hub;
        Vector3? meadow = null;
        for (int i = 0; i < 4000 && meadow == null; i++)
        {
            float a = i * 0.618f * Mathf.PI * 2f, r = 30f + i * 0.08f;
            float x = townHub.x + Mathf.Cos(a) * r, z = townHub.z + Mathf.Sin(a) * r;
            float h = terrain.SampleHeight(new Vector3(x, 0f, z));
            if (Vegetation.GrassAllowed(plan, x, z, h) > 0.95f && plan.RoadDistance(x, z) > 12f && plan.SlopeTan(x, z) < 0.15f) meadow = new Vector3(x, h, z);
        }
        if (meadow != null)
        {
            SceneLook.Shot(cam, OverviewDir + "/world_grass.png", meadow.Value + Vector3.up * 1.7f, meadow.Value + new Vector3(12f, 0.2f, 9f));
            SceneLook.Shot(cam, OverviewDir + "/world_grass_prone.png", meadow.Value + Vector3.up * 0.4f, meadow.Value + new Vector3(12f, 0.3f, 9f));
        }

        // The cell with the most wild flowers, seen from a step away at eye height.
        {
            var bytes = File.ReadAllBytes(GrassMapPath);
            int res = Vegetation.GrassMapResolution(plan.Size);
            var scratch = new List<GrassClump>();
            Vector3? flowers = null;
            int most = 0;
            for (int cz = 10; cz < (int)(plan.Size / Vegetation.GrassCell) - 10; cz += 2)
                for (int cx = 10; cx < (int)(plan.Size / Vegetation.GrassCell) - 10; cx += 2)
                {
                    Vegetation.ScatterGrass(bytes, res, Vegetation.GrassMapCell, cx, cz, plan.Seed, scratch);
                    int n = scratch.Count(c => c.Flower);
                    if (n <= most) continue;
                    var mid = scratch.Where(c => c.Flower).Aggregate(Vector2.zero, (sum, c) => sum + new Vector2(c.X, c.Z)) / n;
                    most = n;
                    flowers = Ground(mid.x, mid.y);
                }
            Debug.Log("[WorldBuilder] flower view: most flowers in one cell " + most);
            if (flowers != null)
            {
                SceneLook.Shot(cam, OverviewDir + "/world_flowers.png", flowers.Value + new Vector3(-3.5f, 1.5f, -3.5f), flowers.Value + new Vector3(0f, 0.2f, 0f));
                SceneLook.Shot(cam, OverviewDir + "/world_flowers_wide.png", flowers.Value + new Vector3(-9f, 1.7f, -9f), flowers.Value + new Vector3(4f, 0.6f, 4f));
            }
        }

        var offshore = GameObject.Find("World/Offshore");
        if (offshore != null && offshore.transform.childCount > 0)
        {
            var stack = offshore.transform.GetChild(0).position;
            var centre = new Vector3(info.Size * 0.5f, 0f, info.Size * 0.5f);
            var inward = new Vector3(centre.x - stack.x, 0f, centre.z - stack.z).normalized;
            var p = stack;
            for (int i = 0; i < 400 && terrain.SampleHeight(p) < plan.SeaLevel + 0.6f; i++) p += inward * 2f;
            p += inward * 6f;
            SceneLook.Shot(cam, OverviewDir + "/world_offshore.png", Ground(p.x, p.z) + Vector3.up * 1.7f, new Vector3(stack.x, plan.SeaLevel + 3f, stack.z));
        }
    }
}
