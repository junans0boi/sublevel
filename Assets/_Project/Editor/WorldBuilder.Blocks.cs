using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Pictures of the tall kinds (#249 T8): a shop, a villa and an apartment block are generated in memory beside the town hub of the saved
// World scene and shot from outside, inside, on the stairs and on the roof. Nothing is saved.
// tools/unity.sh run-gfx WorldBuilder.RenderBlocks   ->   Logs/world/block_<Kind>_*.png
public static partial class WorldBuilder
{
    public static void RenderBlocks()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var info = Object.FindFirstObjectByType<WorldInfo>();
            var terrain = Object.FindFirstObjectByType<Terrain>();
            if (info == null || terrain == null) throw new Exception("the World scene has no WorldInfo or terrain (run WorldBuilder.Build first)");
            Directory.CreateDirectory(OverviewDir);
            DynamicGI.UpdateEnvironment();

            // the real ground as a height field, for FootY
            int n = 129;
            var grid = new float[n, n];
            float maxH = terrain.terrainData.size.y;
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) grid[z, x] = Mathf.Clamp01(terrain.SampleHeight(new Vector3(x * info.Size / (n - 1), 0f, z * info.Size / (n - 1))) / maxH);
            var ground = new WorldMeshes.HeightField(grid, info.Size, maxH);

            var cam = new GameObject("BlockCamera").AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 400f; cam.fieldOfView = 60f;
            SceneLook.EnablePost(cam);
            UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).SetRenderer(info.RendererIndex);

            var town = info.Regions.First(r => r.Kind == RegionKind.Town);
            var root = new GameObject("BlockShowcase").transform;
            var store = new WorldMeshes.Store("Assets/_Project/Prefabs/World/_showcase.asset", "Assets/_Project/Prefabs/World/_showcase.bytes");
            var nav = new List<NavMeshBuildSource>();
            int renderers = 0;
            var kinds = new[] { BuildingKind.Shop, BuildingKind.Villa, BuildingKind.Apartment };
            for (int i = 0; i < kinds.Length; i++)
            {
                var kind = kinds[i];
                Buildings.FootprintLimits(kind, out float w0, out float d0, out float w1, out float d1);
                float w = (w0 + w1) * 0.5f, d = (d0 + d1) * 0.5f;
                var at = town.Hub + new Vector3(40f + i * 35f, 0f, 30f);
                var placed = PlaceBuilding(root, "Showcase_" + kind, kind, at.x, ground.Height(at.x, at.z), at.z, w, d, 0f, 3 + i, ground, store, nav, ref renderers);
                var plan = placed.Plan;
                Vector3 At(float x, float y, float z) => placed.Origin + placed.Rotation * new Vector3(x, y, z);
                Vector3 Eye(Vector3 v) => new Vector3(v.x, ground.Height(v.x, v.z) + 1.7f, v.z);
                string tag = OverviewDir + "/block_" + kind;
                Debug.Log($"[Blocks] {kind}: {plan.Storeys} storeys, {plan.Pieces.Count} pieces, {plan.Decor.Count} decor, {plan.LootPoints.Count} loot, height {plan.Height:F1} m");

                var corner = placed.Origin + placed.Rotation * new Vector3(plan.Width * 0.5f + 9f, 0f, plan.Depth * 0.5f + 11f);
                SceneLook.Shot(cam, tag + "_outside.png", Eye(corner), placed.Origin + Vector3.up * (plan.Height * 0.45f));
                var back = placed.Origin + placed.Rotation * new Vector3(-plan.Width * 0.5f - 9f, 0f, -plan.Depth * 0.5f - 11f);
                SceneLook.Shot(cam, tag + "_outside_back.png", Eye(back) + Vector3.up * 1.5f, placed.Origin + Vector3.up * (plan.Height * 0.45f));

                int shots = 0;
                foreach (var room in plan.Rooms.OrderBy(r => r.Storey).ThenByDescending(r => r.Area))
                {
                    if (room.Storey != 0 && room.Storey != plan.Storeys - 1) continue;
                    if (shots >= 4) break;
                    SceneLook.Shot(cam, $"{tag}_room{shots}.png", At(room.MinX + 0.45f, room.FloorY + 1.65f, room.MinZ + 0.45f), At(room.MaxX - 0.6f, room.FloorY + 1.0f, room.MaxZ - 0.6f));
                    shots++;
                }
                if (plan.Stairs.Count > 1)
                {
                    var stair = plan.Stairs[1];
                    var climb = Quaternion.Euler(0f, stair.FacingDeg, 0f) * Vector3.forward;
                    var foot = new Vector3(stair.X, stair.BaseY, stair.Z);
                    SceneLook.Shot(cam, tag + "_stairs.png", At(foot.x - climb.x * 2.2f + 0.9f, foot.y + 1.65f, foot.z - climb.z * 2.2f), At(foot.x + climb.x * stair.Length * 0.6f, foot.y + (stair.TopY - stair.BaseY) * 0.6f, foot.z + climb.z * stair.Length * 0.6f));
                }
                // the roof: from the deck level beside the stair house toward the far edge, and from the street up at the parapet
                var last = plan.Stairs[plan.Stairs.Count - 1];
                SceneLook.Shot(cam, tag + "_roof.png", At(0f, last.TopY + 1.65f, 0f), At(plan.Width * 0.5f, last.TopY + 0.8f, plan.Depth * 0.5f));
            }
            Debug.Log("[Blocks] pictures written to " + OverviewDir);
        }
        catch (Exception e)
        {
            Debug.LogError("[Blocks] failed: " + e);
            EditorApplication.Exit(1);
        }
    }
}
