using System.IO;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// #414: screenshots of the waiting island from the World scene, and the two "cannot see each other" checks.
// tools/unity.sh run-gfx WaitingIslandCapture.Shoot -> Logs/lookdev/island_*.png
public static class WaitingIslandCapture
{
    public static void Shoot()
    {
        int code = 0;
        try
        {
            EditorSceneManager.OpenScene(WorldBuilderScenePath, OpenSceneMode.Single);
            var cam = new GameObject("CaptureCam").AddComponent<Camera>();
            cam.fieldOfView = 60; cam.nearClipPlane = 0.05f; cam.farClipPlane = 700f;  // the game's far clip (world.json Build.FarClip)
            SceneLook.EnablePost(cam);
            Directory.CreateDirectory("Logs/lookdev");
            float cx = WaitingIsland.CentreX, cz = WaitingIsland.CentreZ, y = 10.4f;
            // The board as the match shows it, and a crowd standing on the island for scale (bots from the real prefab).
            Game.Net.WaitingHud.Preview(new Game.Net.WaitingBroadcast { Active = true, SecondsLeft = 47f, People = 2, Total = 38, Target = 80, BungeeBest = 12.7f });
            var botPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/M1/Bot_MixamoAdam.prefab");
            var spots = WaitingIsland.SpawnPoints();
            var crowd = new GameObject("CaptureCrowd");
            if (botPrefab != null)
                for (int i = 0; i < 14; i++)
                {
                    var spot = spots[i * 3 % spots.Count];
                    var bot = (GameObject)PrefabUtility.InstantiatePrefab(botPrefab, crowd.transform);
                    bot.transform.SetPositionAndRotation(new Vector3(spot.x, y + 0.1f, spot.z), Quaternion.Euler(0f, i * 53f, 0f));
                }
            Shot(cam, "island_outside", new Vector3(cx + 105f, y + 42f, cz + 105f), new Vector3(cx, y + 30f, cz));
            Shot(cam, "island_inside_board", new Vector3(cx, y + 1.7f, cz + 18f), new Vector3(cx, y + 7f, cz - 33f));
            Shot(cam, "island_inside_tower", new Vector3(cx - 8f, y + 1.7f, cz + 12f), new Vector3(cx + 22f, y + 12f, cz + 4f));
            Shot(cam, "island_inside_lift", new Vector3(cx, y + 1.7f, cz - 10f), new Vector3(cx, y + 2.4f, cz + 34f));
            Shot(cam, "island_inside_booths", new Vector3(cx + 4f, y + 1.7f, cz), new Vector3(cx - 27f, y + 1.8f, cz));
            Shot(cam, "island_dome_up", new Vector3(cx, y + 1.7f, cz), new Vector3(cx, y + 30f, cz + 1f));
            Shot(cam, "island_crowd", new Vector3(cx - 6f, y + 1.7f, cz + 16f), new Vector3(cx + 2f, y + 1.4f, cz - 2f));
            Shot(cam, "island_board_live", new Vector3(cx, y + 3f, cz - 8f), new Vector3(cx, y + 7f, cz - 33f));
            Shot(cam, "island_board_close", new Vector3(cx, y + 6.5f, cz - 12f), new Vector3(cx, y + 7f, cz - 33f));
            // The bungee (ADR-386/387): the lift gate on the deck, the view from the car on its way up, the top platform, the board looking
            // down, the fall, the lowest point, and the whole tower from across the hall.
            float L(float lx) => cx + lx; float Z(float lz) => cz + lz;
            var lift = WaitingIsland.Lift; var pad = WaitingIsland.BungeePad; var tw = WaitingIsland.Tower; float top = y + BungeeCurve.Top;
            var car = GameObject.Find("WaitingIsland")?.transform.Find("BungeeLiftCar");
            Shot(cam, "bungee_tower_wide", new Vector3(L(-24f), y + 1.7f, Z(-14f)), new Vector3(L(tw.x), y + 28f, Z(tw.z)));
            Shot(cam, "bungee_lift_gate", new Vector3(L(lift.x - 2f), y + 1.7f, Z(lift.z + 7f)), new Vector3(L(lift.x), y + 1.6f, Z(lift.z)));
            if (car != null) { var cp = car.localPosition; cp.y = BungeeCurve.Top * 0.55f; car.localPosition = cp; car.Find("Doors")?.gameObject.SetActive(true); }
            Shot(cam, "bungee_lift_rising", new Vector3(L(lift.x - 0.6f), y + BungeeCurve.Top * 0.55f + 1.7f, Z(lift.z + 0.6f)), new Vector3(L(-10f), y + 14f, Z(-8f)));
            if (car != null) { var cp = car.localPosition; cp.y = BungeeCurve.Top; car.localPosition = cp; car.Find("Doors")?.gameObject.SetActive(false); }
            Shot(cam, "bungee_top_platform", new Vector3(L(lift.x + 0.5f), top + 1.7f, Z(lift.z - 1f)), new Vector3(L(pad.x), top + 0.5f, Z(pad.z)));
            Shot(cam, "bungee_jump_board", new Vector3(L(pad.x + 2.8f), top + 1.7f, Z(pad.z)), new Vector3(L(pad.x - 2f), y, Z(pad.z + 0.5f)));
            Shot(cam, "bungee_falling", new Vector3(L(pad.x), y + BungeeCurve.Top - 22f, Z(pad.z + 0.3f)), new Vector3(L(pad.x + 0.5f), y, Z(pad.z)));
            Shot(cam, "bungee_bottom", new Vector3(L(pad.x), y + BungeeCurve.Bottom + 0.3f, Z(pad.z)), new Vector3(L(pad.x + 12f), y + 0.5f, Z(pad.z + 6f)));
            Shot(cam, "bungee_up_from_pad", new Vector3(L(pad.x + 4f), y + 1.7f, Z(pad.z + 3f)), new Vector3(L(pad.x), top, Z(pad.z)));
            // From the island's rim looking at the map (south): only the wall. From the map's north edge looking at the island (north): only sea and fog.
            Shot(cam, "island_to_map", new Vector3(cx, y + 1.7f, cz + 30f), new Vector3(cx, y + 1.7f, cz + 800f));
            Shot(cam, "map_to_island", new Vector3(1200f, 40f, 60f), new Vector3(cx, y + 10f, cz));
            Shot(cam, "map_edge_wide", new Vector3(1200f, 120f, 40f), new Vector3(1200f, 10f, -1000f));
        }
        catch (System.Exception e) { Debug.LogError("[WaitingIslandCapture] failed: " + e); code = 1; }
        EditorApplication.Exit(code);
    }

    const string WorldBuilderScenePath = "Assets/_Project/Scenes/World.unity";

    static void Shot(Camera cam, string name, Vector3 from, Vector3 to) => SceneLook.Shot(cam, $"Logs/lookdev/{name}.png", from, to);
}
