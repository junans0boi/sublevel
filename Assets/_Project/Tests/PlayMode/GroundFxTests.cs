using System;
using System.Collections;
using System.IO;
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
    // Ground reaction, wildlife, storm and night in the real World (#205-#208, ADR-163): the behaviour is asserted and
    // each stage is saved as a PNG (Logs/fx, or the folder in FX_SHOTS) to be looked at. Also the A/B frame cost of the
    // whole set (effects on vs off) with the same probe as the render budget.
    [Category("Capture")]
    public class GroundFxTests
    {
        sealed class Walk : IPlayerInput
        {
            public bool Go;
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1, Move = Go ? Vector2.up : Vector2.zero };
        }

        readonly Walk _walk = new Walk();

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

        static string Folder()
        {
            string f = Environment.GetEnvironmentVariable("FX_SHOTS");
            f = Path.GetFullPath(string.IsNullOrEmpty(f) ? "Logs/fx" : f);
            Directory.CreateDirectory(f);
            return f;
        }

        IEnumerator Start()
        {
            _walk.Go = false;
            PlayerController.InputOverride = _walk;
            EnvironmentView.ForceTime = DayTimeChoice.Day;
            EnvironmentView.ForceWeather = WeatherChoice.Clear;
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(1.5f);
        }

        // A spot on the ground whose footstep surface is `want` (any region), or null.
        static Vector3? FindSurface(FootSurface want)
        {
            var world = WorldInfo.Current;
            foreach (var region in world.Regions)
                for (float radius = 6f; radius <= 140f; radius += 8f)
                    for (int a = 0; a < 24; a++)
                    {
                        float ang = a / 24f * Mathf.PI * 2f;
                        var p = region.Hub + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
                        if (!Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out var hit, 600f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                        if (hit.normal.y < 0.97f || hit.point.y < world.SeaLevel + 0.5f) continue;
                        if (CombatantAudio.SurfaceAt(hit.point) == want) return hit.point;
                    }
            return null;
        }

        // Shallow water along a coast: ground between 0.35 and 0.8 m under the sea surface, with dry land 6 m back.
        static bool FindWading(out Vector3 spot, out Vector3 toLand)
        {
            var world = WorldInfo.Current;
            foreach (var region in world.Regions)
                for (float radius = 20f; radius <= 400f; radius += 10f)
                    for (int a = 0; a < 48; a++)
                    {
                        float ang = a / 48f * Mathf.PI * 2f;
                        var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                        var p = region.Hub + dir * radius;
                        if (!Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out var hit, 600f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                        float depth = world.SeaLevel - hit.point.y;
                        if (depth < 0.35f || depth > 0.8f) continue;
                        var back = p - dir * 8f;
                        if (!Physics.Raycast(back + Vector3.up * 300f, Vector3.down, out var h2, 600f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                        if (h2.point.y > world.SeaLevel + 0.2f) { spot = hit.point; toLand = -dir; return true; }
                    }
            spot = default; toLand = default;
            return false;
        }

        static IEnumerator Shot(string name, string note)
        {
            yield return UiShot.Save(Path.Combine(Folder(), name), 1280, 720);
            Debug.Log($"[GroundFx] {name}: {note}");
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Footprints_GrassBend_Wading()
        {
            yield return Start();
            var pc = PlayerController.Local;
            var gfx = GroundFx.Current;
            Assert.IsNotNull(gfx, "the World has GroundFx");
            var soft = FindSurface(FootSurface.Dirt) ?? FindSurface(FootSurface.Grass);
            Assert.IsTrue(soft.HasValue, "a soft patch of ground exists");
            var surface = CombatantAudio.SurfaceAt(soft.Value);
            pc.Teleport(soft.Value + Vector3.up * 0.2f);
            yield return new WaitForSeconds(1.2f);
            int before = gfx.PrintsMade;
            var start = pc.transform.position;
            _walk.Go = true;
            yield return new WaitForSeconds(3.5f);
            _walk.Go = false;
            yield return new WaitForSeconds(0.4f);
            Assert.Greater(gfx.PrintsMade - before, 3, "walking on " + surface + " leaves prints");
            Assert.Greater(gfx.PrintsAlive, 3);
            Assert.LessOrEqual(gfx.PrintsAlive, GroundFxRules.MaxPrints, "capped pool");
            // A camera low behind the last prints, looking along the trail.
            var last = gfx.LastPrint;
            var cam = Camera.main.transform;
            var back = (start - pc.transform.position).normalized;
            back.y = 0f;
            var eye = last + Vector3.up * 0.8f;
            cam.SetPositionAndRotation(eye, Quaternion.LookRotation((last + back * 1.8f) - eye));
            yield return Shot("fx_01_footprints.png", $"{surface} prints made {gfx.PrintsMade - before} alive {gfx.PrintsAlive}");
            var bend = Shader.GetGlobalVectorArray("_GameBends");
            Assert.IsNotNull(bend);
            Assert.Greater(bend[0].z, 0f, "the local player bends plants");

            // A trail longer than the pool: it stays capped.
            _walk.Go = true;
            yield return new WaitForSeconds(6f);
            _walk.Go = false;
            Assert.LessOrEqual(gfx.PrintsAlive, GroundFxRules.MaxPrints);

            if (FindWading(out var water, out var toLand))
            {
                pc.Teleport(water + toLand * 3f + Vector3.up * 0.3f);
                yield return new WaitForSeconds(1.2f);
                pc.LookAt(water - toLand * 20f + Vector3.up * 0.3f);
                yield return new WaitForSeconds(0.4f);
                int rings = gfx.RingsMade;
                _walk.Go = true;
                yield return new WaitForSeconds(2.2f);
                Assert.Greater(gfx.RingsMade - rings, 3, "wading makes rings");
                yield return Shot("fx_02_wading.png", $"rings {gfx.RingsMade - rings}");
                _walk.Go = false;
            }
            else Debug.LogWarning("[GroundFx] no shallow coast found; wading not exercised");
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Rain_Splashes_WetGround_Lightning_Thunder()
        {
            yield return Start();
            var pc = PlayerController.Local;
            var view = EnvironmentView.Current;
            var gfx = GroundFx.Current;
            var storm = StormFx.Current;
            Assert.IsNotNull(storm, "the World has StormFx");
            var world = WorldInfo.Current;
            var el = world.Elevators[0];
            var dir = Quaternion.Euler(0f, el.FacingDeg, 0f) * Vector3.forward;
            var spot = el.Position + dir * 20f;
            if (Physics.Raycast(spot + Vector3.up * 200f, Vector3.down, out var hit, 400f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) spot = hit.point;
            pc.Teleport(spot + Vector3.up * 0.2f);
            yield return new WaitForSeconds(1.2f);
            pc.LookAt(spot + dir * 30f + Vector3.up * 0.2f);
            Assert.AreEqual(0f, gfx.Wet, 1e-3f, "dry before the rain");
            int none = gfx.SplashesMade;
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(none, gfx.SplashesMade, "no splashes in dry weather");

            view.Set(WorldSky.DayHour, WeatherKind.Rain);
            yield return new WaitForSeconds(3f);
            Assert.Greater(gfx.SplashesMade, 20, "rain splashes on the ground");
            Assert.Greater(gfx.Wet, 0.5f, "the ground is wet");
            Assert.AreEqual(Shader.GetGlobalFloat("_GameWet"), gfx.Wet, 1e-3f, "shaders see the wetness");
            Assert.LessOrEqual(gfx.RaysLastFrame, GroundFxRules.MaxSplashesPerFrame);
            yield return Shot("fx_03_rain_day.png", $"splashes {gfx.SplashesMade} wet {gfx.Wet:F2}");

            int thunders = storm.Thunders, strikes = storm.Strikes;
            storm.Strike(500f);
            // Frames may be long in batch mode: take the shot on the first frame where the flash is on.
            bool shot = false;
            float peak = 0f;
            for (int f = 0; f < 40; f++)
            {
                yield return null;
                peak = Mathf.Max(peak, storm.FlashNow);
                if (!shot && storm.FlashNow > 0.2f) { shot = true; yield return Shot("fx_04_lightning.png", $"flash {storm.FlashNow:F2}"); }
                if (storm.FlashNow == 0f && peak > 0f) break;
            }
            Assert.Greater(peak, 0.2f, "the flash came (peak " + peak + ", strikes " + storm.Strikes + ", dt " + Time.deltaTime + ")");
            Assert.IsTrue(shot, "a frame showed the flash");
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(0f, storm.FlashNow, 1e-3f, "the flash is over");
            yield return new WaitForSeconds(2.5f);
            Assert.AreEqual(thunders + 1, storm.Thunders, "the thunder came after the delay");
            Assert.AreEqual(strikes + 1, storm.Strikes);

            // Stop raining: it dries off slowly and the splashes stop.
            view.Set(WorldSky.DayHour, WeatherKind.Clear);
            int s = gfx.SplashesMade;
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(s, gfx.SplashesMade, "no splashes after the rain");
            Assert.Greater(gfx.Wet, 0.5f, "still wet a moment after");

            view.Set(WorldSky.NightHour, WeatherKind.Rain);
            yield return new WaitForSeconds(2f);
            yield return Shot("fx_05_rain_night.png", "night rain");
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Night_Stars_WindowLights_Wildlife()
        {
            yield return Start();
            var pc = PlayerController.Local;
            var view = EnvironmentView.Current;
            var night = NightFx.Current;
            var wild = Wildlife.Current;
            Assert.IsNotNull(night);
            Assert.IsNotNull(wild);
            var world = WorldInfo.Current;
            var el = world.Elevators[0];
            var dir = Quaternion.Euler(0f, el.FacingDeg, 0f) * Vector3.forward;
            var spot = el.Position + dir * 20f;
            if (Physics.Raycast(spot + Vector3.up * 200f, Vector3.down, out var hit, 400f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) spot = hit.point;
            pc.Teleport(spot + Vector3.up * 0.2f);
            yield return new WaitForSeconds(1.2f);

            // Day: butterflies, leaves, a flock.
            pc.LookAt(spot + dir * 12f + Vector3.up * 1f);
            yield return new WaitForSeconds(7f);
            Assert.Greater(wild.Levels.Butterflies, 1f, "levels at day");
            Assert.Greater(wild.ButterfliesAlive, 0, "butterflies by day");
            Assert.Greater(wild.LeavesAlive, 0, "leaves in the air");
            Assert.AreEqual(0, wild.FirefliesAlive, "no fireflies by day");
            wild.Flock(pc.transform.position);
            yield return new WaitForSeconds(1f);
            Assert.Greater(wild.BirdsAlive, 2, "a flock");
            Assert.IsTrue(wild.Nearest('t', pc.transform.position, out var butterfly));
            var cam = Camera.main.transform;
            var side = Vector3.Cross(Vector3.up, (butterfly - pc.transform.position).normalized);
            var eye = butterfly - (butterfly - pc.transform.position).normalized * 3.5f + Vector3.up * 0.3f;
            cam.SetPositionAndRotation(eye, Quaternion.LookRotation(butterfly - eye));
            yield return Shot("fx_06_day_wildlife.png", $"butterflies {wild.ButterfliesAlive} leaves {wild.LeavesAlive} birds {wild.BirdsAlive}");
            int scatters = wild.Scatters;
            wild.Scatter(pc.transform.position);
            yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(wild.Nearest('b', pc.transform.position, out var bird));
            var eye2 = bird - (bird - pc.transform.position).normalized * 7f;
            Camera.main.transform.SetPositionAndRotation(eye2, Quaternion.LookRotation(bird - eye2));
            yield return Shot("fx_07_birds_scatter.png", $"birds {wild.BirdsAlive}");
            Assert.Greater(wild.BirdsAlive, 4);

            // Night, clear: stars, a moon, fireflies, lit windows seen from outside.
            view.Set(WorldSky.NightHour, WeatherKind.Clear);
            yield return new WaitForSeconds(9f);
            Assert.Greater(night.StarAlpha, 0.9f, "stars at night");
            Assert.Greater(wild.FirefliesAlive, 0, "fireflies at night");
            Assert.IsTrue(wild.Nearest('f', pc.transform.position, out var firefly));
            var eye3 = firefly + Vector3.Cross(Vector3.up, (firefly - pc.transform.position).normalized) * 3.5f;
            Camera.main.transform.SetPositionAndRotation(eye3, Quaternion.LookRotation(firefly - eye3));
            yield return Shot("fx_08b_fireflies.png", $"fireflies {wild.FirefliesAlive}");
            pc.LookAt(pc.transform.position + dir * 40f + Vector3.up * 28f);
            pc.LookAt(pc.transform.position + dir * 40f + Vector3.up * 28f);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("fx_08_night_sky.png", $"star alpha {night.StarAlpha:F2} fireflies {wild.FirefliesAlive}");

            // A lit window: stand 7 m out from the nearest lit pane, looking at it.
            GlassPane best = null;
            float bestSq = float.MaxValue;
            foreach (var p in GlassPane.All)
            {
                if (p == null || !NightFx.IsLit(p.Id)) continue;
                float sq = (p.transform.position - pc.transform.position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = p; }
            }
            Assert.IsNotNull(best, "a lit pane exists");
            var stand = best.transform.position + best.transform.forward * 7f;
            if (Physics.Raycast(stand + Vector3.up * 50f, Vector3.down, out var h2, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) stand = h2.point;
            pc.Teleport(stand + Vector3.up * 0.2f);
            yield return new WaitForSeconds(1.5f);
            pc.LookAt(best.transform.position);
            yield return new WaitForSeconds(1.5f);
            Assert.Greater(night.LitWindows, 0, "lit windows drawn");
            Assert.Greater(night.WindowLightsOn, 0, "a window light on");
            yield return Shot("fx_09_night_window.png", $"lit {night.LitWindows} lights {night.WindowLightsOn}");

            // Day again: no stars, no windows.
            view.Set(WorldSky.DayHour, WeatherKind.Clear);
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(0f, night.StarAlpha, 1e-3f);
            Assert.AreEqual(0, night.LitWindows);
            Assert.AreEqual(0, night.WindowLightsOn);
        }

        // A/B: the render probe with every effect of this change on and off, at the heavy moments.
        [UnityTest, Timeout(900000), Category("Perf")]
        public IEnumerator FrameCost_EffectsOnVsOff()
        {
            yield return Start();
            var pc = PlayerController.Local;
            var view = EnvironmentView.Current;
            var world = WorldInfo.Current;
            var region = world.Regions[0];
            var station = world.ElevatorsIn(region.Index)[0].Position;
            var toward = region.Hub - station;
            toward.y = 0f;
            toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
            var spot = station + toward * 8f;
            pc.Teleport(spot + Vector3.up * 0.2f);
            yield return new WaitForSeconds(1f);
            var cam = new GameObject("ProbeCamera").AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 700f;
            cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.GetUniversalAdditionalCameraData().SetRenderer(world.RendererIndex);
            cam.transform.position = pc.Camera.transform.position;
            cam.transform.LookAt(region.Hub + Vector3.up * 2f);
            var parts = new Component[] { GroundFx.Current, Wildlife.Current, StormFx.Current, NightFx.Current };
            var roots = new Transform[] { GroundFx.Current.Root, Wildlife.Current.Root, StormFx.Current.Root, NightFx.Current.Root };
            var report = new System.Text.StringBuilder();
            foreach (var (name, hour, weather) in new[]
            {
                ("day_clear", WorldSky.DayHour, WeatherKind.Clear),
                ("night_clear", WorldSky.NightHour, WeatherKind.Clear),
                ("day_rain", WorldSky.DayHour, WeatherKind.Rain),
                ("night_rain", WorldSky.NightHour, WeatherKind.Rain),
            })
            {
                view.Set(hour, weather);
                RenderProbe.Result on = default, off = default;
                foreach (bool enabled in new[] { true, false, true, false })
                {
                    for (int i = 0; i < parts.Length; i++) { ((Behaviour)parts[i]).enabled = enabled; roots[i].gameObject.SetActive(enabled); }
                    Shader.SetGlobalFloat("_GameWet", enabled && weather == WeatherKind.Rain ? 1f : 0f);
                    yield return new WaitForSeconds(enabled ? 12f : 1f); // the wildlife fills in only when on; a second pass keeps the better of two
                    RenderProbe.Result r = default;
                    yield return RenderProbe.Measure(cam, x => r = x);
                    if (enabled) { if (on.Stats.AvgMs == 0f || r.Stats.AvgMs < on.Stats.AvgMs) on = r; }
                    else if (off.Stats.AvgMs == 0f || r.Stats.AvgMs < off.Stats.AvgMs) off = r;
                }
                string line = $"[GroundFx AB] {name}: ON {on} | OFF {off} | avg delta {on.Stats.AvgMs - off.Stats.AvgMs:F2} ms";
                Debug.Log(line);
                report.AppendLine(line);
                RenderProbe.AssertWithinBudget(on);
            }
            File.WriteAllText(Path.Combine(Folder(), "frame_cost_ab.txt"), report.ToString());
            UnityEngine.Object.Destroy(cam.gameObject);
        }
    }
}
