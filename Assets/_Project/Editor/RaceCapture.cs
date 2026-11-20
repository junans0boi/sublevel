using System.IO;
using Game.Core;
using Game.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// #438..#440: screenshots of the waiting island's race hall from the World scene, with the race cars from the scene's own car models.
// tools/unity.sh run-gfx RaceCapture.Shoot -> Logs/lookdev/race_*.png
public static class RaceCapture
{
    public static void Shoot()
    {
        int code = 0;
        try
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/World.unity", OpenSceneMode.Single);
            var island = GameObject.Find("WaitingIsland").transform;
            var hall = island.Find("RaceTrack");
            if (hall == null) throw new System.Exception("World has no race hall: rebuild it (WorldBuilder.Build)");
            var cam = new GameObject("CaptureCam").AddComponent<Camera>();
            cam.fieldOfView = 60; cam.nearClipPlane = 0.05f; cam.farClipPlane = 700f;
            SceneLook.EnablePost(cam);
            Directory.CreateDirectory("Logs/lookdev");
            float y = island.position.y;
            Vector3 L(float lx, float h, float lz) => island.TransformPoint(new Vector3(lx, h, lz));

            // The cars on the grid, as the server spawns them (one of each car model).
            var prefabs = Object.FindAnyObjectByType<VehicleSpawner>().CarPrefabs();
            var spots = RaceTrack.GridSpots();
            var cars = new GameObject[spots.Count];
            for (int i = 0; i < spots.Count; i++)
            {
                var (lx, lz, yaw) = spots[i];
                cars[i] = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i % prefabs.Count].gameObject);
                cars[i].transform.SetPositionAndRotation(L(lx, 0.05f, lz), Quaternion.Euler(0f, yaw, 0f));
            }

            float mx = RaceTrack.MainX, lineZ = RaceTrack.LineZ;
            Shot(cam, "race_gate_from_island", L(6f, 1.7f, -12f), L(48f, 1.2f, -15f));
            Shot(cam, "race_grid", L(mx + 2f, 2.2f, lineZ - 6f), L(43f, 0.5f, -13f));
            Shot(cam, "race_pit_from_bollards", L(37.5f, 1.7f, -9f), L(52f, 1f, -18f));
            Shot(cam, "race_start_gantry", L(mx + 2f, 1.3f, lineZ + 40f), L(mx, 3f, lineZ - 20f));
            Shot(cam, "race_grandstand", L(mx + 4f, 3f, -40f), L(44f, 2f, -85f));
            Shot(cam, "race_hairpin", L(mx + 1f, 1.3f, -95f), L(80f, 1f, -135f));
            Shot(cam, "race_esses", L(92f, 1.3f, -100f), L(135f, 1f, -92f));
            Shot(cam, "race_fast_right", L(150f, 1.3f, -82f), L(180f, 1f, -30f));
            Shot(cam, "race_chicane", L(180f, 1.3f, -10f), L(130f, 1f, 40f));
            Shot(cam, "race_outside_sea", L(230f, 40f, 140f), L(110f, 8f, -30f));

            // Mid-race: three cars out on the circuit, placed on the centre line (the others stay on the grid).
            var path = RaceTrack.Path;
            foreach (var (car, s) in new[] { (cars[0], 40f), (cars[1], 70f), (cars[2], 115f) })
            {
                int i = 0; float best = float.MaxValue;
                for (int k = 0; k < path.Count; k++) { float d = Mathf.Abs(path[k].S - s); if (d < best) { best = d; i = k; } }
                var p = path[i]; var q = path[(i + 1) % path.Count];
                Put(car, L(p.X, 0.05f, p.Z), Mathf.Atan2(q.X - p.X, q.Z - p.Z) * Mathf.Rad2Deg);
            }
            Shot(cam, "race_midrace", L(mx - 3f, 4f, -60f), L(80f, 0.5f, -120f));

            // From above with the roof hidden: the whole layout (island, gate, pit, circuit).
            foreach (Transform t in hall) if (t.name.StartsWith("Roof")) t.gameObject.SetActive(false);
            var dome = island.GetComponentsInChildren<Transform>(true);
            foreach (var t in dome) if (t.name.StartsWith("Dome")) t.gameObject.SetActive(false);
            Shot(cam, "race_aerial", L(80f, 300f, -30f), L(80f, 0f, -29.9f));
        }
        catch (System.Exception e) { Debug.LogError("[RaceCapture] failed: " + e); code = 1; }
        EditorApplication.Exit(code);
    }

    static void Put(GameObject car, Vector3 at, float yaw) => car.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));

    static void Shot(Camera cam, string name, Vector3 from, Vector3 to) => SceneLook.Shot(cam, $"Logs/lookdev/{name}.png", from, to);
}
