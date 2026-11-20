using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of the time of day and weather in the World (#150, ADR-119): day, dusk, night, fog, night fog.
    // Run on purpose with
    //   ENV_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.EnvironmentCaptureTests
    [Explicit, Category("Capture")]
    public class EnvironmentCaptureTests
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

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureEnvironment()
        {
            string folder = Environment.GetEnvironmentVariable("ENV_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/envshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
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
            Assert.IsNotNull(view, "the World has an EnvironmentView");
            var pc = PlayerController.Local;
            var world = WorldInfo.Current;
            // Out of the station, onto open ground, looking along the way to the region's centre.
            var el = world.Elevators[0];
            var dir = Quaternion.Euler(0f, el.FacingDeg, 0f) * Vector3.forward;
            var spot = el.Position + dir * 20f;
            if (Physics.Raycast(spot + Vector3.up * 200f, Vector3.down, out var hit, 400f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) spot = hit.point;
            pc.Teleport(spot + Vector3.up * 0.2f);
            yield return new WaitForSeconds(1.5f);
            pc.LookAt(spot + dir * 60f + Vector3.up * 10f);
            yield return new WaitForSeconds(0.5f);

            IEnumerator Shot(string name, float hour, WeatherKind weather, float settle = 1.2f)
            {
                view.Set(hour, weather);
                yield return new WaitForSeconds(settle);
                yield return UiShot.Save(Path.Combine(folder, name), 1280, 720);
                Debug.Log($"[EnvCapture] {name}: fog end {view.Look.FogEnd}");
            }
            yield return Shot("env_01_day_clear.png", WorldSky.DayHour, WeatherKind.Clear);
            yield return Shot("env_02_dusk_clear.png", WorldSky.DuskHour, WeatherKind.Clear);
            yield return Shot("env_03_night_clear.png", WorldSky.NightHour, WeatherKind.Clear);
            yield return Shot("env_05_day_fog.png", WorldSky.DayHour, WeatherKind.Fog);
            yield return Shot("env_06_night_fog.png", WorldSky.NightHour, WeatherKind.Fog);
            yield return Shot("env_07_dawn_clear.png", 6.5f, WeatherKind.Clear);
            Debug.Log("[EnvCapture] shots in " + folder);
        }
    }
}
