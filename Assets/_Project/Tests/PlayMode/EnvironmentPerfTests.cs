using System.Collections;
using System.Collections.Generic;
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
    // ADR-119 (#150): the render budget (PERF_BUDGET A: p99 <= 10 ms, draw calls, triangles) holds in the World for every
    // time of day and weather, measured the way World_RendersWithinBudget does, from the viewpoint beside an elevator
    // with the rain falling around the player's camera.
    [Category("Perf")]
    public class EnvironmentPerfTests
    {
        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
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

        [UnityTest, Timeout(600000)]
        public IEnumerator World_EveryTimeOfDayAndWeather_RendersWithinBudget()
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
            var view = EnvironmentView.Current;
            var world = WorldInfo.Current;
            var region = world.Regions[0];
            var station = world.ElevatorsIn(region.Index)[0].Position;
            var toward = region.Hub - station;
            toward.y = 0f;
            toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
            // The player's own camera stands where the probe does, so the rain falls in front of it.
            var spot = station + toward * 8f;
            PlayerController.Local.Teleport(spot + Vector3.up * 0.2f);
            yield return new WaitForSeconds(1f);
            var cam = new GameObject("ProbeCamera").AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 700f;
            cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.GetUniversalAdditionalCameraData().SetRenderer(world.RendererIndex);
            cam.transform.position = PlayerController.Local.Camera.transform.position;
            cam.transform.LookAt(region.Hub + Vector3.up * 2f);

            var names = new List<string>();
            var results = new List<RenderProbe.Result>();
            foreach (var (name, hour, weather) in new[]
            {
                ("day_clear", WorldSky.DayHour, WeatherKind.Clear),
                ("dusk_clear", WorldSky.DuskHour, WeatherKind.Clear),
                ("night_clear", WorldSky.NightHour, WeatherKind.Clear),
                ("day_rain", WorldSky.DayHour, WeatherKind.Rain),
                ("night_rain", WorldSky.NightHour, WeatherKind.Rain),
                ("day_fog", WorldSky.DayHour, WeatherKind.Fog),
            })
            {
                view.Set(hour, weather);
                yield return new WaitForSeconds(1.5f); // the rain fills the air
                RenderProbe.Result result = default;
                yield return RenderProbe.Measure(cam, r => result = r, screenshotPath: $"Logs/perf/env_{name}.png");
                names.Add(name);
                results.Add(result);
                Debug.Log($"[Perf] env {name} (rain particles {view.RainAlive}): {result}");
            }
            Object.Destroy(cam.gameObject);
            for (int i = 0; i < results.Count; i++) RenderProbe.AssertWithinBudget(results[i]);
        }
    }
}
