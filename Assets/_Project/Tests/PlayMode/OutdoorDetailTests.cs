using System.Collections;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #192 ground, #193 vegetation, #194 ambient audio in the World as a player meets them: the beds follow the
    // time of day, a roof and gunfire; foliage moves in the wind; daylight shots at eye height in every region.
    [Category("Capture")]
    public class OutdoorDetailTests
    {
        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PlayerController.InputOverride = new Idle();
            EnvironmentView.ForceTime = DayTimeChoice.Day;
            EnvironmentView.ForceWeather = WeatherChoice.Clear;
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(1f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            EnvironmentView.ForceTime = null;
            EnvironmentView.ForceWeather = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // What the new ground and grass cost: the same views timed with the ground untiling and the dense grass switched
        // off and on, interleaved so a noisy machine hits every setting alike. Reported, and bounded loosely.
        [UnityTest, Timeout(900000)]
        public IEnumerator GroundAndGrass_CostLittle()
        {
            var world = WorldInfo.Current;
            var grass = Object.FindFirstObjectByType<GrassRenderer>();
            var cam = new GameObject("ProbeCamera").AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 700f;
            cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.GetUniversalAdditionalCameraData().SetRenderer(world.RendererIndex);
            var sums = new float[4];
            var p99s = new float[4];
            string[] names = { "ground off, grass off", "ground on, grass off", "ground off, grass on", "ground on, grass on" };
            int views = 0;
            foreach (var region in world.Regions)
            {
                var spot = StandOutside(region, out var look);
                cam.transform.position = spot + Vector3.up * 1.7f;
                cam.transform.LookAt(look);
                PlayerController.Local.Teleport(spot + Vector3.up * 0.2f); // so the grass is built around the view
                yield return new WaitForSeconds(1.5f);
                for (int round = 0; round < 2; round++)
                    for (int mode = 0; mode < 4; mode++)
                    {
                        Shader.SetGlobalFloat("_GameGroundOff", (mode & 1) == 0 ? 1f : 0f);
                        grass.enabled = (mode & 2) != 0;
                        yield return null;
                        RenderProbe.Result r = default;
                        yield return RenderProbe.Measure(cam, x => r = x);
                        sums[mode] += r.Stats.AvgMs;
                        p99s[mode] = Mathf.Max(p99s[mode], r.Stats.P99Ms);
                    }
                views++;
            }
            Shader.SetGlobalFloat("_GameGroundOff", 0f);
            grass.enabled = true;
            Object.Destroy(cam.gameObject);
            for (int m = 0; m < 4; m++) Debug.Log($"[Outdoor] cost {names[m]}: mean avg {sums[m] / (views * 2):F2} ms, worst p99 {p99s[m]:F2} ms");
            Assert.Less(sums[3] / (views * 2) - sums[0] / (views * 2), 2.5f, "the new ground and grass add well under 2.5 ms on average");
        }

        // Open ground a few metres out of an elevator station, the player's eyes toward the region's hub.
        static Vector3 StandOutside(WorldInfo.Region region, out Vector3 look)
        {
            var world = WorldInfo.Current;
            var el = world.ElevatorsIn(region.Index)[0];
            var dir = Quaternion.Euler(0f, el.FacingDeg, 0f) * Vector3.forward;
            var spot = el.Position + dir * 16f;
            if (Physics.Raycast(spot + Vector3.up * 200f, Vector3.down, out var hit, 400f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) spot = hit.point;
            var toward = region.Hub - spot;
            toward.y = 0f;
            look = spot + (toward.sqrMagnitude > 1f ? toward.normalized : dir) * 50f + Vector3.up * 1.2f;
            return spot;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator AmbientBeds_FollowTimeOfDay_ARoof_AndGunfire()
        {
            var beds = Object.FindFirstObjectByType<AmbientBeds>();
            Assert.IsNotNull(beds, "the World gets ambient beds");
            var view = EnvironmentView.Current;
            var world = WorldInfo.Current;
            var pc = PlayerController.Local;
            var spot = StandOutside(world.Regions[0], out var look);
            pc.Teleport(spot + Vector3.up * 0.2f);
            yield return new WaitForSeconds(1.5f);
            pc.LookAt(look);

            view.Set(WorldSky.DayHour, WeatherKind.Clear);
            yield return new WaitForSeconds(3f);
            Assert.IsFalse(beds.Input.Indoors, "in the open");
            Assert.Greater(beds.Levels.Wind, 0.2f, "wind by day");
            Assert.Greater(beds.WindLoopVolume, 0.02f, "the wind loop is playing");
            Assert.Greater(beds.Levels.BirdsPerMinute, 2f, "birds by day");
            Assert.AreEqual(0f, beds.Levels.Crickets, 1e-3f);
            float windDay = beds.Levels.Wind;

            // Birds are scheduled (spatial one-shots): give them a minute of game time at a high rate? No: just check they come.
            float until = Time.time + 25f;
            while (beds.BirdsPlayed == 0 && Time.time < until) yield return null;
            Assert.Greater(beds.BirdsPlayed, 0, "a bird sang within 25 s");

            SpatialAudio.NoteLoud(1f);
            yield return new WaitForSeconds(0.6f);
            Assert.Less(beds.Levels.Wind, windDay * 0.6f, "wind ducks under gunfire");
            yield return new WaitForSeconds(3.5f);
            Assert.Greater(beds.Levels.Wind, windDay * 0.9f, "and comes back");

            view.Set(WorldSky.NightHour, WeatherKind.Clear);
            yield return new WaitForSeconds(3f);
            Assert.Greater(beds.Levels.Crickets, 0.1f, "crickets at night");
            Assert.AreEqual(0f, beds.Levels.BirdsPerMinute, 1e-3f, "no birds at night");

            // Inside a hall: under a roof, room tone replaces the wind.
            var hall = GameObject.Find("World/Buildings").transform.Cast<Transform>().First(t => t.name.Contains("FactoryHall") || t.name.Contains("Warehouse"));
            pc.Teleport(hall.position + Vector3.up * 0.4f);
            yield return new WaitForSeconds(2f);
            Assert.IsTrue(beds.Input.Indoors, "the hall has a roof");
            Assert.Greater(beds.Levels.RoomTone, 0.2f);
            Assert.Less(beds.Levels.Wind, windDay * 0.4f);
        }

        // Two renders of the same view a second and a half apart: with only the wind moving, the trees and grass move.
        [UnityTest, Timeout(300000)]
        public IEnumerator Foliage_MovesInTheWind()
        {
            var world = WorldInfo.Current;
            var town = world.Regions.First(r => r.Kind == RegionKind.Town);
            var pc = PlayerController.Local;
            var spot = StandOutside(town, out var look);
            pc.Teleport(spot + Vector3.up * 0.2f);
            yield return new WaitForSeconds(1.5f);
            pc.LookAt(look);
            yield return new WaitForSeconds(0.5f);
            var a = Render(pc.Camera);
            yield return new WaitForSeconds(1.5f);
            var b = Render(pc.Camera);
            int changed = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 0.06f) changed++;
            Debug.Log($"[Outdoor] foliage pixels changed in 1.5 s: {changed} of {a.Length}");
            Assert.Greater(changed, 300, "leaves and grass sway");
        }

        static Color[] Render(Camera source)
        {
            var go = new GameObject("WindProbe");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(source);
            cam.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            var rt = new RenderTexture(480, 270, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(480, 270, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 480, 270), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var pixels = tex.GetPixels();
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(go);
            return pixels;
        }

        // Daylight shots as the player's own camera sees them, a few metres out of an elevator in every region.
        // ENV_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.OutdoorDetailTests.CaptureRegions
        [UnityTest, Explicit, Timeout(300000)]
        public IEnumerator CaptureRegions()
        {
            string folder = System.Environment.GetEnvironmentVariable("ENV_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/b192/play";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            var world = WorldInfo.Current;
            var pc = PlayerController.Local;
            foreach (var region in world.Regions)
            {
                var spot = StandOutside(region, out var look);
                pc.Teleport(spot + Vector3.up * 0.2f);
                yield return new WaitForSeconds(2f);
                pc.LookAt(look);
                yield return new WaitForSeconds(0.6f);
                yield return UiShot.Save(Path.Combine(folder, $"play_{region.Id}_{region.Kind}.png".ToLowerInvariant()), 1600, 900);
                // And the ground at the feet, looking down the slope a little.
                pc.LookAt(spot + (look - spot).normalized * 6f + Vector3.down * 0.4f);
                yield return new WaitForSeconds(0.4f);
                yield return UiShot.Save(Path.Combine(folder, $"play_{region.Id}_{region.Kind}_ground.png".ToLowerInvariant()), 1600, 900);
            }
        }
    }
}
