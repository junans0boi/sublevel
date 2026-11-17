using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Before/after pictures and numbers for the meadow mood work (ADR-282..285). The viewpoints come from the plan only (terrain, roads,
// buildings), never from the plants, so a run on an old scene and one on a new scene look at the same places.
// tools/unity.sh run-gfx WorldBuilder.MoodCheck, MOOD_TAG names the outputs: Logs/mood/{tag}_*.png and Logs/mood_{tag}.txt.
public static partial class WorldBuilder
{
    public static void MoodCheck()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        Physics.SyncTransforms();
        string tag = System.Environment.GetEnvironmentVariable("MOOD_TAG") ?? "now";
        const string dir = "Logs/mood";
        Directory.CreateDirectory(dir);
        DynamicGI.UpdateEnvironment();
        var plan = WorldGenerator.Generate(LoadSpec());
        var terrain = Object.FindFirstObjectByType<Terrain>();
        var info = Object.FindFirstObjectByType<WorldInfo>();
        Vector3 Ground(float x, float z) => new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.GetPosition().y, z);
        var log = new List<string>();
        void Say(string line) { Debug.Log("[MoodCheck:" + tag + "] " + line); log.Add(line); }

        // Open meadow in a region: flat, dry, off the roads and 25 m from any building, found on a spiral round the hub.
        bool Meadow(float x, float z) =>
            plan.RegionAt(x, z) >= 0 && plan.Height(x, z) > plan.SeaLevel + 4f && plan.SlopeTan(x, z) < 0.12f && plan.RoadDistance(x, z) > 14f
            && plan.Lots.All(l => l.Distance(x, z) > 25f) && plan.Regions[plan.RegionAt(x, z)].Kind != RegionKind.Mountain && plan.Regions[plan.RegionAt(x, z)].Kind != RegionKind.Wetland;
        var eyes = new List<Vector3>();
        Vector3? tallSpot = null;
        foreach (var region in info.Regions.Where(r => r.Kind != RegionKind.Mountain && r.Kind != RegionKind.Wetland))
        {
            bool added = false;
            for (int i = 0; i < 6000; i++)
            {
                float a = i * 0.618f * Mathf.PI * 2f, r = 60f + i * 0.2f;
                float x = region.Hub.x + Mathf.Cos(a) * r, z = region.Hub.z + Mathf.Sin(a) * r;
                if (!Meadow(x, z)) continue;
                if (!added && eyes.Count < 5) { eyes.Add(new Vector3(x, 0f, z)); added = true; }
                if (tallSpot == null && Vegetation.TallMeadow(plan, x, z, plan.Height(x, z)) > 0.9f) tallSpot = new Vector3(x, 0f, z);
                if (added && tallSpot != null) break;
            }
        }

        // Sight over meadow sample points (every 40 m), 8 rays at eye height: trees block, grass does not (it has no collider).
        double sum = 0; int n = 0, under30 = 0, points = 0, inTall = 0;
        for (float z = 20f; z < plan.Size; z += 40f)
            for (float x = 20f; x < plan.Size; x += 40f)
            {
                if (!Meadow(x, z)) continue;
                points++;
                if (Vegetation.TallMeadow(plan, x, z, plan.Height(x, z)) > 0.3f) inTall++;
                var eye = Ground(x, z) + Vector3.up * 1.7f;
                for (int d = 0; d < 8; d++)
                {
                    float ang = d * Mathf.PI / 4f + 0.2f;
                    float seen = Physics.Raycast(eye, new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)), out var hit, 150f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.distance : 150f;
                    sum += seen; n++;
                    if (seen < 30f) under30++;
                }
            }
        Say($"meadow: {points} sample points (open flat ground), {100.0 * inTall / System.Math.Max(1, points):F0}% in tall grass; mean sight {sum / System.Math.Max(1, n):F1} m, {100.0 * under30 / System.Math.Max(1, n):F0}% of rays blocked within 30 m");

        var veg = Object.FindFirstObjectByType<VegetationRenderer>();
        int trunks = Object.FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None).Count(c => c.transform.parent != null && c.transform.parent.name == "Trunks");
        var kinds = new int[3];
        for (int i = 0; i < veg.Count; i++) kinds[(int)veg.KindOf(i)]++;
        Say($"plants {veg.Count}: broadleaf {kinds[(int)PlantKind.Broadleaf]}, conifer {kinds[(int)PlantKind.Conifer]}, bush {kinds[(int)PlantKind.Bush]}; trunk colliders {trunks}");
        var grass = File.ReadAllBytes(GrassMapPath);
        int shortCells = 0, tallCells = 0;
        foreach (var b in grass) { if (b >= 128) tallCells++; else if (b > 0) shortCells++; }
        Say($"grass map cells (2 m): short {shortCells}, tall {tallCells} ({100.0 * tallCells / System.Math.Max(1, shortCells + tallCells):F1}%)");

        var cam = new GameObject("MoodCamera").AddComponent<Camera>();
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 1500f; cam.fieldOfView = 60f;
        SceneLook.EnablePost(cam);
        UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).SetRenderer(info.RendererIndex);
        for (int i = 0; i < eyes.Count; i++)
        {
            var e = Ground(eyes[i].x, eyes[i].z);
            // Looks toward the hub of the island so the same direction is used before and after.
            var toward = new Vector3(info.Size * 0.5f - e.x, 0f, info.Size * 0.5f - e.z).normalized;
            SceneLook.Shot(cam, $"{dir}/{tag}_eye{i}.png", e + Vector3.up * 1.7f, e + Vector3.up * 2.4f + toward * 40f);
        }
        if (tallSpot != null)
        {
            var t = Ground(tallSpot.Value.x, tallSpot.Value.z);
            SceneLook.Shot(cam, $"{dir}/{tag}_tall_stand.png", t + Vector3.up * 1.7f, t + new Vector3(12f, 1.4f, 9f));
            SceneLook.Shot(cam, $"{dir}/{tag}_tall_crouch.png", t + Vector3.up * 0.9f, t + new Vector3(12f, 0.9f, 9f));
            SceneLook.Shot(cam, $"{dir}/{tag}_tall_prone.png", t + Vector3.up * 0.4f, t + new Vector3(12f, 0.6f, 9f));
        }
        cam.fieldOfView = 50f;
        RenderSettings.fog = false; // the haze greys the colours out of the aerials; the eye views keep it
        int shot = 0;
        foreach (var region in info.Regions.Where(r => r.Kind != RegionKind.Wetland).Take(3))
        {
            var c = Ground(region.Hub.x, region.Hub.z);
            SceneLook.Shot(cam, $"{dir}/{tag}_air{shot++}_{region.Id}.png", c + new Vector3(0f, 230f, -380f), c + Vector3.up * 40f);
        }
        cam.fieldOfView = 55f;
        var mid = new Vector3(info.Size * 0.5f, 0f, info.Size * 0.5f);
        SceneLook.Shot(cam, $"{dir}/{tag}_air_wide.png", mid + new Vector3(-250f, 700f, -1000f), mid);

        RenderSettings.fog = true;
        if (eyes.Count > 0)
        {
            var e = Ground(eyes[0].x, eyes[0].z) + Vector3.up * 1.7f;
            Say(ViewStats(cam, e, e + new Vector3(0.7f, 0f, 0.7f) * 40f, tag));
        }
        File.WriteAllLines($"Logs/mood_{tag}.txt", log);
        EditorApplication.Exit(0);
    }
}
