using System.IO;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Pictures for the website's update posts, rendered from the saved World scene (editor render, like WaitingIslandCapture and RenderOverview).
// tools/unity.sh run-gfx WebNewsCapture.Shoot -> Logs/web/*.png (1920x1080). WEB_ONLY=island|world|cars limits the set.
public static class WebNewsCapture
{
    const string Dir = "Logs/web";

    public static void Shoot()
    {
        int code = 0;
        try
        {
            EditorSceneManager.OpenScene(WorldBuilder.ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            DynamicGI.UpdateEnvironment();
            Directory.CreateDirectory(Dir);
            var info = Object.FindFirstObjectByType<WorldInfo>();
            var cam = new GameObject("WebCam").AddComponent<Camera>();
            cam.fieldOfView = 60f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 700f;
            SceneLook.EnablePost(cam);
            UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).SetRenderer(info.RendererIndex);
            string only = System.Environment.GetEnvironmentVariable("WEB_ONLY");
            if (string.IsNullOrEmpty(only) || only == "island") Island(cam);
            if (string.IsNullOrEmpty(only) || only == "world") World(cam, info);
            if (string.IsNullOrEmpty(only) || only == "cars") Cars(cam);
        }
        catch (System.Exception e) { Debug.LogError("[WebNewsCapture] failed: " + e); code = 1; }
        EditorApplication.Exit(code);
    }

    static void Shot(Camera cam, string name, Vector3 from, Vector3 to) => SceneLook.Shot(cam, $"{Dir}/{name}.png", from, to);

    // ---- waiting island: bungee and lift ----
    static void Island(Camera cam)
    {
        float cx = WaitingIsland.CentreX, cz = WaitingIsland.CentreZ, y = 10.4f;   // deck top (WaitingIslandCapture)
        float L(float lx) => cx + lx; float Z(float lz) => cz + lz;
        var lift = WaitingIsland.Lift; var pad = WaitingIsland.BungeePad; var tw = WaitingIsland.Tower; float top = y + BungeeCurve.Top;
        // The board, the island's own materials, the call buttons and the hoist are built by the client HUD (ADR-490/495/496): drive its preview.
        var msg = new Game.Net.WaitingBroadcast { Active = true, SecondsLeft = 47f, People = 6, Total = 52, Target = 80, BungeeBest = 58.4f };
        Game.Net.WaitingHud.Preview(msg);
        var hud = Object.FindFirstObjectByType<Game.Net.WaitingHud>();
        // shortcut: reaches WaitingHud._last/Refresh by reflection (capture-only); give WaitingHud a preview setter if this breaks.
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        void LiftAt(LiftPhase phase, float into, byte calls)
        {
            msg.LiftPhase = (byte)phase; msg.LiftInto = into; msg.LiftCalls = calls;
            typeof(Game.Net.WaitingHud).GetField("_last", flags).SetValue(hud, msg);
            typeof(Game.Net.WaitingHud).GetMethod("Refresh", flags).Invoke(hud, null);
        }
        LiftAt(LiftPhase.OpenBottom, 0f, 0);

        // A few people on the deck for scale (bots from the real prefab, posed with the idle the game plays).
        var botPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/M1/Bot_MixamoAdam.prefab");
        var idle = CharacterAnimClips.LoadClip("U_Idle_Loop");
        GameObject Person(Vector3 at, float yaw, float t)
        {
            if (botPrefab == null) return null;
            var bot = (GameObject)PrefabUtility.InstantiatePrefab(botPrefab);
            bot.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            var anim = bot.GetComponentInChildren<Animator>();
            if (idle != null && anim != null) idle.SampleAnimation(anim.gameObject, t % idle.length);
            return bot;
        }
        var spots = WaitingIsland.SpawnPoints();
        for (int i = 0; i < 12; i++) { var s = spots[i * 4 % spots.Count]; Person(new Vector3(s.x, y + 0.1f, s.z), i * 53f, i * 0.37f); }

        cam.farClipPlane = 1500f;
        Shot(cam, "island_aerial", new Vector3(cx + 95f, y + 55f, cz + 95f), new Vector3(cx, y + 18f, cz));
        cam.farClipPlane = 700f;
        Shot(cam, "bungee_tower", new Vector3(L(-26f), y + 1.7f, Z(-16f)), new Vector3(L(tw.x), y + 30f, Z(tw.z)));

        // Top: someone at the end of the jump board, seen from the tower top behind, and the jumper's own view from the end.
        float end = WaitingIsland.BridgeEndX;
        var jumper = Person(new Vector3(L(end + 0.5f), top + 0.05f, Z(pad.z)), -90f, 0.5f);
        var jumperAt = new Vector3(L(end + 0.5f), top + 1f, Z(pad.z));
        Shot(cam, "bungee_board", new Vector3(L(end + 4.5f), top + 3f, Z(pad.z + 3f)), jumperAt + new Vector3(-3f, -6f, 0f));
        Shot(cam, "bungee_board_wide", new Vector3(L(end + 6f), top + 2.2f, Z(pad.z - 3.5f)), jumperAt + new Vector3(-4f, -4f, 1f));
        if (jumper != null) Object.DestroyImmediate(jumper);
        Shot(cam, "bungee_top_view", new Vector3(L(end - 1.2f), top + 0.6f, Z(pad.z)), new Vector3(L(end - 20f), y, Z(pad.z + 3f)));   // just off the board's end

        // Mid-fall: looking down past the feet, and looking out over the hall while dropping.
        float mid = y + BungeeCurve.Top - 22f;
        Shot(cam, "bungee_fall_down", new Vector3(L(pad.x), mid, Z(pad.z + 0.3f)), new Vector3(L(pad.x - 3f), y, Z(pad.z + 2f)));
        Shot(cam, "bungee_fall_out", new Vector3(L(pad.x), mid, Z(pad.z)), new Vector3(L(pad.x - 30f), y + 4f, Z(pad.z + 8f)));

        // Lift: the gate with its call button, the cage on the way up (from the deck and close by, two riders in it), the top.
        var cb = WaitingIsland.CallBottom; var ct = WaitingIsland.CallTop;
        Shot(cam, "lift_gate", new Vector3(L(cb.x + 3.2f), y + 1.7f, Z(cb.z + 4.5f)), new Vector3(L(lift.x), y + 1.6f, Z(lift.z)));
        Shot(cam, "lift_button", new Vector3(L(cb.x + 1.4f), y + 1.6f, Z(cb.z + 1.6f)), new Vector3(L(cb.x), y + cb.y, Z(cb.z)));
        LiftAt(LiftPhase.Rising, BungeeLift.TravelSeconds * 0.5f, 2);
        Shot(cam, "lift_rising", new Vector3(L(lift.x - 14f), y + 2.5f, Z(lift.z + 16f)), new Vector3(L(lift.x), y + BungeeCurve.Top * 0.5f + 2f, Z(lift.z)));
        var carT = GameObject.Find("WaitingIsland")?.transform.Find("BungeeLiftCar");
        if (carT != null)
        {
            var c = carT.position;
            Person(c + new Vector3(-0.5f, 0.1f, 0.4f), 200f, 0.2f); Person(c + new Vector3(0.5f, 0.1f, -0.3f), 160f, 1.1f);
            Shot(cam, "lift_cage", c + new Vector3(-9f, 3.5f, 8f), c + Vector3.up * 1.3f);
            Shot(cam, "lift_cage_low", c + new Vector3(-7f, -2.5f, 9f), c + Vector3.up * 1.6f);
        }
        LiftAt(LiftPhase.OpenTop, 0f, 0);
        Shot(cam, "lift_top", new Vector3(L(ct.x + 2.5f), top + 1.7f, Z(ct.z - 3.5f)), new Vector3(L(lift.x), top + 1.4f, Z(lift.z)));
    }

    // ---- the island map: aerial, forest, river, town ----
    static void World(Camera cam, WorldInfo info)
    {
        var plan = WorldGenerator.Generate(WorldBuilder.LoadSpec());
        var terrain = Object.FindFirstObjectByType<Terrain>();
        Vector3 Ground(float x, float z) => new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.GetPosition().y, z);
        float size = info.Size;
        var centre = new Vector3(size * 0.5f, info.SeaLevel, size * 0.5f);

        float lodBias = QualitySettings.lodBias;
        QualitySettings.lodBias = 100f;
        RenderSettings.fog = false;
        cam.fieldOfView = 50f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 3000f;
        Shot(cam, "map_aerial", centre + new Vector3(-260f, 420f, -820f), centre + new Vector3(0f, 0f, 40f));
        Shot(cam, "map_aerial_low", centre + new Vector3(-120f, 260f, -700f), centre + new Vector3(0f, 0f, 160f));
        QualitySettings.lodBias = lodBias;
        RenderSettings.fog = true;
        cam.fieldOfView = 60f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 700f;

        // Forest (ADR-487): a sedan parked in the wood seen as the car camera sees it, and the eye-level view across the same slope.
        bool IsTrunk(Collider c) => c.transform.parent != null && c.transform.parent.name == "Trunks";
        var sedan = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleModels.OutDir + "/FairheavenLT80.prefab");
        int woodShots = 0;
        for (float z = 20f; z < plan.Size && woodShots < 3; z += 37f)
            for (float x = 20f; x < plan.Size && woodShots < 3; x += 37f)
            {
                if (Vegetation.ForestDensity(plan, x, z) < 0.6f || plan.SlopeTan(x, z) > 0.25f) continue;
                var at = Ground(x, z);
                int near = Physics.OverlapSphere(at + Vector3.up, 15f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Count(IsTrunk);
                if (near < 6 || Physics.OverlapSphere(at + Vector3.up, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(IsTrunk)) continue;
                float e = 1f, gx = plan.Height(x + e, z) - plan.Height(x - e, z), gz = plan.Height(x, z + e) - plan.Height(x, z - e);
                var contour = new Vector3(-gz, 0f, gx).sqrMagnitude > 1e-4f ? new Vector3(-gz, 0f, gx).normalized : Vector3.forward;
                if (sedan != null)
                {
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(sedan);
                    car.transform.SetPositionAndRotation(at + Vector3.up * 0.05f, Quaternion.LookRotation(contour));
                    foreach (var body in car.GetComponentsInChildren<Rigidbody>()) body.isKinematic = true;
                    Physics.SyncTransforms();
                    var eye = VehicleCamera.ChasePose(car.GetComponent<Game.Net.Vehicle>(), Quaternion.LookRotation(contour), out var pivot);
                    VegetationRenderer.ClearAround = cam; VegetationRenderer.ClearPivot = pivot;
                    cam.fieldOfView = Game.Net.GameData.Rules.Aim.Hip.Fov;
                    Shot(cam, $"forest_car{woodShots}", eye, eye + contour * 30f);
                    VegetationRenderer.ClearAround = null;
                    cam.fieldOfView = 60f;
                    Shot(cam, $"forest_car_side{woodShots}", at + Vector3.Cross(Vector3.up, contour) * 7f + contour * 4f + Vector3.up * 2.2f, at + Vector3.up * 0.8f);
                    Object.DestroyImmediate(car);
                }
                Shot(cam, $"forest_eye{woodShots}", at + Vector3.up * 1.7f, at + Vector3.up * 2.2f - contour * 40f);
                woodShots++;
            }

        // River: from a bank, looking along the water, at a few points along it.
        if (plan.Rivers.Count > 0)
        {
            var river = plan.Rivers[0];
            int n = river.X.Length;
            for (int k = 0; k < 3; k++)
            {
                int i = n * (k + 1) / 4, j = Mathf.Min(n - 1, i + 6);
                var along = new Vector3(river.X[j] - river.X[i], 0f, river.Z[j] - river.Z[i]).normalized;
                var side = new Vector3(along.z, 0f, -along.x);
                var bank = new Vector3(river.X[i], 0f, river.Z[i]) + side * (river.HalfWidth[i] + 10f);
                var eye = Ground(bank.x, bank.z) + Vector3.up * 3f;
                var look = new Vector3(river.X[j], river.Y[j] + 1f, river.Z[j]) + along * 30f;
                Shot(cam, $"river_{k}", eye, look);
            }
        }

        // Towns: down the main street and from above, for the two biggest towns.
        var spec = WorldBuilder.LoadSpec();
        foreach (var t in spec.Island.Towns.OrderByDescending(t => t.Width * t.Depth).Take(2))
        {
            double r = t.RotDeg * System.Math.PI / 180.0, c = System.Math.Cos(r), sn = System.Math.Sin(r);
            Vector3 Local(float lx, float lz, float up) { var g = Ground(t.X + (float)(lx * c - lz * sn), t.Z + (float)(lx * sn + lz * c)); return g + Vector3.up * up; }
            float hw = t.Width * 0.5f;
            cam.fieldOfView = 55f;
            RenderSettings.fog = false;   // the haze greys the aerials out
            Shot(cam, $"town_{t.Id}_air", Local(-hw * 0.9f, -t.Depth * 0.9f, 120f), Local(0f, 0f, 0f));
            RenderSettings.fog = true;
            cam.fieldOfView = 65f;
            Shot(cam, $"town_{t.Id}_street", Local(-hw * 0.7f, 0f, 1.7f), Local(hw * 0.3f, 0f, 3f));
            Shot(cam, $"town_{t.Id}_roof", Local(0f, 0f, 22f), Local(hw * 0.5f, t.Depth * 0.2f, 6f));
        }
        cam.fieldOfView = 60f;
    }

    // ---- each vehicle, parked on an open straight road, seen from the front three-quarter ----
    static void Cars(Camera cam)
    {
        var plan = WorldGenerator.Generate(WorldBuilder.LoadSpec());
        var terrain = Object.FindFirstObjectByType<Terrain>();
        Vector3 Ground(float x, float z) => new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.GetPosition().y, z);
        Vector3? spot = null; var along = Vector3.forward;
        for (float z = 40f; z < plan.Size - 40f && spot == null; z += 23f)
            for (float x = 40f; x < plan.Size - 40f && spot == null; x += 23f)
            {
                if (plan.RoadDistance(x, z) > 0.5f || plan.SlopeTan(x, z) > 0.05f) continue;
                float e = 1f, gx = plan.RoadDistance(x + e, z) - plan.RoadDistance(x - e, z), gz = plan.RoadDistance(x, z + e) - plan.RoadDistance(x, z - e);
                if (gx * gx + gz * gz < 1e-4f) continue;
                var a = new Vector3(-gz, 0f, gx).normalized;
                bool straight = true;
                for (float d = -24f; d <= 24f && straight; d += 8f)
                    straight = plan.RoadDistance(x + a.x * d, z + a.z * d) < 1.5f && plan.SlopeTan(x + a.x * d, z + a.z * d) < 0.08f;
                var at = Ground(x, z);
                bool open = true;
                for (int k = 0; k < 8 && open; k++)
                    open = !Physics.Raycast(at + Vector3.up * 1.6f, Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward, 14f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                // no tree within 28 m: their crowns (no collider) would shade the car
                open = open && !Physics.OverlapSphere(at, 28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(c => c.transform.parent != null && c.transform.parent.name == "Trunks");
                if (straight && open) { spot = at; along = a; }
            }
        if (spot == null) { Debug.LogWarning("[WebNewsCapture] no open road for the cars"); return; }
        var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { VehicleModels.OutDir })
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null && p.GetComponent<Game.Net.Vehicle>() != null).ToList();
        // Face the car and put the camera so the sun is behind the camera (the lit side of the car in view).
        var sun = GameObject.Find("Sun")?.transform.forward ?? Vector3.down;
        var toSun = new Vector3(-sun.x, 0f, -sun.z).normalized;
        var perp = new Vector3(along.z, 0f, -along.x);
        float bestDot = -2f; Vector3 heading = along, sideDir = perp;
        foreach (float sa in new[] { 1f, -1f })
            foreach (float sb in new[] { 1f, -1f })
            {
                float d = Vector3.Dot((along * sa + perp * sb * 0.75f).normalized, toSun);
                if (d > bestDot) { bestDot = d; heading = along * sa; sideDir = perp * sb; }
            }
        foreach (var prefab in prefabs)
        {
            var at = spot.Value;
            var up = Physics.Raycast(at + Vector3.up * 5f, Vector3.down, out var floor, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? floor.normal : Vector3.up;
            var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            car.transform.SetPositionAndRotation(at + up * 0.05f, Quaternion.LookRotation(Vector3.ProjectOnPlane(heading, up), up));
            foreach (var body in car.GetComponentsInChildren<Rigidbody>()) body.isKinematic = true;
            Physics.SyncTransforms();
            var b = SceneLook.WorldBounds(car);
            float reach = b.extents.magnitude;
            var eye = b.center + (heading * 1.0f + sideDir * 0.75f).normalized * reach * 2.3f + Vector3.up * (reach * 0.35f);
            cam.fieldOfView = 40f;
            Shot(cam, $"car_{prefab.name}", eye, b.center - Vector3.up * b.extents.y * 0.2f);
            Object.DestroyImmediate(car);
        }
        cam.fieldOfView = 60f;
    }
}
