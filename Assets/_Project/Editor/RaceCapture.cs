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

    // tools/unity.sh run-gfx RaceCapture.ShootEvent -> Logs/lookdev/event_*.png: each event course from above, the grid, and its sections.
    public static void ShootEvent()
    {
        int code = 0;
        try
        {
            foreach (var info in RaceEvent.Courses)
            {
                if (info.Course.Open) continue;   // the pass: ShootPass
                EditorSceneManager.OpenScene($"Assets/_Project/Scenes/{info.Scene}.unity", OpenSceneMode.Single);
                var c = info.Course;
                var cam = new GameObject("CaptureCam").AddComponent<Camera>();
                cam.fieldOfView = 60; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1500f;
                SceneLook.EnablePost(cam);
                var prefabs = Object.FindAnyObjectByType<VehicleSpawner>().CarPrefabs();
                var grid = c.GridSpots(RaceEvent.Racers);
                for (int i = 0; i < grid.Count; i++)
                {
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i % prefabs.Count].gameObject);
                    car.transform.SetPositionAndRotation(new Vector3(grid[i].x, 0.05f, grid[i].z), Quaternion.Euler(0f, grid[i].yaw, 0f));
                }
                string id = info.Id;
                Vector3 P(float s, float h, float side = 0f) { var (x, z, yaw) = c.PointAt(s); return new Vector3(x, h, z) + Quaternion.Euler(0f, yaw, 0f) * new Vector3(side, 0f, 0f); }
                Shot(cam, $"event_{id}_grid", P(-110f, 4f, 4f), P(-20f, 1f));
                Shot(cam, $"event_{id}_start", P(-12f, 1.3f, -2f), P(60f, 1.5f));
                Shot(cam, $"event_{id}_quarter", P(c.LapLength * 0.12f, 1.4f), P(c.LapLength * 0.12f + 60f, 1f));
                Shot(cam, $"event_{id}_half", P(c.LapLength * 0.45f, 1.4f), P(c.LapLength * 0.45f + 60f, 1f));
                if (c.BranchPath.Count > 0) Shot(cam, $"event_{id}_fork", P(c.ForkS - 40f, 6f), P(c.ForkS + 30f, 0f));
                Shot(cam, $"event_{id}_south", P(c.LapLength * 0.8f, 1.4f), P(c.LapLength * 0.8f + 60f, 1f));
                float cx = 0f, cz = 0f; foreach (var p in c.Path) { cx += p.X; cz += p.Z; } cx /= c.Path.Count; cz /= c.Path.Count;
                Shot(cam, $"event_{id}_aerial", new Vector3(cx, 520f, cz), new Vector3(cx, 0f, cz - 0.1f));
                Shot(cam, $"event_{id}_oblique", new Vector3(cx - 260f, 200f, cz + 330f), new Vector3(cx, 0f, cz));
            }
        }
        catch (System.Exception e) { Debug.LogError("[RaceCapture] event failed: " + e); code = 1; }
        EditorApplication.Exit(code);
    }

    // tools/unity.sh run-gfx RaceCapture.ShootPass -> Logs/lookdev/pass_*.png (#451): both grids, the hairpins, the cliff side, from above.
    public static void ShootPass()
    {
        int code = 0;
        try
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Race_Pass.unity", OpenSceneMode.Single);
            var up = RaceEvent.PassUp; var down = RaceEvent.PassDown;
            var cam = new GameObject("CaptureCam").AddComponent<Camera>();
            cam.fieldOfView = 60; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1500f;
            SceneLook.EnablePost(cam);
            var prefabs = Object.FindAnyObjectByType<VehicleSpawner>().CarPrefabs();
            foreach (var c in new[] { up, down })
            {
                var grid = c.GridSpots(RaceEvent.Racers);
                for (int i = 0; i < grid.Count; i++)
                {
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i % prefabs.Count].gameObject);
                    car.transform.SetPositionAndRotation(new Vector3(grid[i].x, c.RoadHeight(grid[i].x, grid[i].z) + 0.05f, grid[i].z), Quaternion.Euler(0f, grid[i].yaw, 0f));
                }
            }
            // A point on the course: s along, h above the road, `side` metres to the right.
            Vector3 P(RaceCourse c, float s, float h, float side = 0f) { var (x, z, yaw) = c.PointAt(s); return new Vector3(x, c.HeightAt(s) + h, z) + Quaternion.Euler(0f, yaw, 0f) * new Vector3(side, 0f, 0f); }
            Shot(cam, "pass_start_foot", P(up, -70f, 5f, 3f), P(up, 10f, 1f));
            Shot(cam, "pass_start_top", P(down, -70f, 5f, 3f), P(down, 10f, 1f));
            Shot(cam, "pass_drive_up", P(up, 120f, 1.4f), P(up, 180f, 1f));
            // The first hairpin (uphill): from the inside approach, then from above.
            float hp = 0f; foreach (var p in up.Path) if (p.S > 0f && p.Radius > 0f && p.Radius <= 15f) { hp = p.S; break; }
            Shot(cam, "pass_hairpin", P(up, hp - 45f, 1.6f, 1.5f), P(up, hp + 10f, 0f));
            Shot(cam, "pass_hairpin_above", P(up, hp + 12f, 45f, -40f), P(up, hp + 12f, 0f));
            // The same hairpin's chevrons up close from both ways (each way's boards point its own turn).
            int hpTurn = 0; foreach (var p in up.Path) if (Mathf.Abs(p.S - hp) < 0.01f) hpTurn = p.Turn;
            var (hx, hz, _) = up.PointAt(hp + 8f); float hd = down.Project(hx, hz).s;
            Shot(cam, "pass_chevrons_up", P(up, hp - 22f, 1.5f), P(up, hp + 8f, 1.3f, 6.6f * hpTurn));
            Shot(cam, "pass_chevrons_down", P(down, hd - 30f, 1.5f), P(down, hd, 1.3f, -6.6f * hpTurn));
            // Downhill in the switchbacks: the guardrail and the drop on the valley side.
            float mid = down.LapLength * 0.45f;
            Shot(cam, "pass_cliff", P(down, mid, 1.5f, 2.5f), P(down, mid + 50f, -6f, 9f));
            Shot(cam, "pass_drive_down", P(down, down.LapLength * 0.62f, 1.4f), P(down, down.LapLength * 0.62f + 60f, -2f));
            var loop = RaceEvent.PassLoop;
            // The way down the other side: its first hairpin (approach and from above), the esses.
            float eh = 0f; foreach (var p in loop.Path) if (p.S > up.LapLength + 200f && p.Radius > 0f && p.Radius <= 15f && (eh == 0f || p.S < eh)) eh = p.S;
            Shot(cam, "pass_loop_hairpin", P(loop, eh - 45f, 1.6f, 1.5f), P(loop, eh + 10f, -1f));
            Shot(cam, "pass_loop_hairpins_above", P(loop, eh + 90f, 80f, -60f), P(loop, eh + 90f, 0f));
            float es = 0f; foreach (var p in loop.Path) if (p.S > eh + 300f && p.Radius >= 30f && p.Radius <= 40f && (es == 0f || p.S < es)) es = p.S;
            Shot(cam, "pass_loop_esses", P(loop, es - 40f, 1.4f), P(loop, es + 40f, -3f));
            float cx = 0f, cz = 0f, cy = 0f; foreach (var p in loop.Path) { cx += p.X; cz += p.Z; cy += p.Y; } cx /= loop.Path.Count; cz /= loop.Path.Count; cy /= loop.Path.Count;
            Shot(cam, "pass_aerial", new Vector3(cx, cy + 950f, cz), new Vector3(cx, cy, cz - 0.1f));
            Shot(cam, "pass_oblique", new Vector3(cx + 560f, cy + 380f, cz + 640f), new Vector3(cx, cy, cz));
        }
        catch (System.Exception e) { Debug.LogError("[RaceCapture] pass failed: " + e); code = 1; }
        EditorApplication.Exit(code);
    }

    static void Put(GameObject car, Vector3 at, float yaw) => car.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));

    static void Shot(Camera cam, string name, Vector3 from, Vector3 to) => SceneLook.Shot(cam, $"Logs/lookdev/{name}.png", from, to);
}
