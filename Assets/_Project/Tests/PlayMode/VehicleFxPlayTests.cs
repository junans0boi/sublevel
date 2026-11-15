using System;
using System.Collections;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #202 / #203 (ADR-155) in the TestBox as a host: lamps follow the dark, the engine and the L key; brake / reverse lamps;
    // real lights stay within budget; tyre dust is made on dirt-like ground only when moving; the layered engine sounds play.
    public class VehicleFxPlayTests
    {
        sealed class NoPlayerInput : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        sealed class Keys : IVehicleInput
        {
            public VehicleInputFrame Frame = new VehicleInputFrame { Seat = -1 };

            public VehicleInputFrame Read()
            {
                var f = Frame;
                Frame.Interact = Frame.EngineToggle = Frame.ToggleView = Frame.Lights = false;
                Frame.Seat = -1;
                return f;
            }
        }

        Keys _keys;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            VehicleInput.InputOverride = null;
            VehicleLights.AmbientOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static Vehicle Find(VehicleKind kind) => Vehicle.All.FirstOrDefault(v => v != null && v.Kind == kind);

        [UnityTest, Timeout(120000)]
        public IEnumerator Lamps_FollowDarkEngineAndSwitch_BrakeAndReverse_WithinBudget()
        {
            _keys = new Keys();
            PlayerController.InputOverride = new NoPlayerInput();
            VehicleInput.InputOverride = _keys;
            VehicleLights.AmbientOverride = 1f;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && Find(VehicleKind.Car) != null && Find(VehicleKind.Motorcycle) != null, 20f, "player and vehicles");
            yield return new WaitForSeconds(1f);
            var car = Find(VehicleKind.Car);
            var bike = Find(VehicleKind.Motorcycle);
            var lights = car.GetComponent<VehicleLights>();
            var bikeLights = bike.GetComponent<VehicleLights>();
            Assert.IsNotNull(lights, "VehicleAudio adds the lamps");
            Assert.IsNotNull(car.GetComponent<VehicleTyreFx>(), "and the tyre effects");
            yield return WaitUntil(() => lights.LampCount > 0 && bikeLights.LampCount > 0, 5f, "lamps built");
            Assert.AreEqual(2, lights.SpotCount, "a car has two headlights");
            Assert.AreEqual(1, bikeLights.SpotCount, "a motorcycle has one");
            Assert.Greater(lights.LampCount, bikeLights.LampCount, "the car also has reverse lamps and indicators");

            // Engine off: dark whatever the sky.
            VehicleLights.AmbientOverride = 0.1f;
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(lights.Lit, "parked and off at night: dark");

            var me = Combatant.LocalPlayer;
            PlayerController.Local.Teleport(car.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            _keys.Frame.Interact = true;
            yield return WaitUntil(() => VehicleSeat.IsAttached(me), 3f, "in the car");
            yield return new WaitForSeconds(car.Rules.EnterSeconds + 0.4f);
            Assert.IsTrue(car.EngineOn.Value);
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(lights.Lit, "night + engine: auto lamps");
            Assert.AreEqual(2, lights.SpotsOn, "both headlights light");
            Assert.AreEqual(VehicleFxRules.LightsAuto, lights.Mode);

            // L: off by hand, then back to auto.
            _keys.Frame.Lights = true;
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(lights.Lit, "L switches them off");
            Assert.AreEqual(VehicleFxRules.LightsOff, lights.Mode);
            Assert.AreEqual(0, lights.SpotsOn);
            _keys.Frame.Lights = true;
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(lights.Lit, "L again");
            Assert.AreEqual(VehicleFxRules.LightsAuto, lights.Mode);

            // By day the same press turns them on; the sky alone turns them off.
            VehicleLights.AmbientOverride = 1f;
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(lights.Lit, "day: dark lamps");
            _keys.Frame.Lights = true;
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(lights.Lit, "forced on by day");
            _keys.Frame.Lights = true;
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(lights.Lit);
            VehicleLights.AmbientOverride = 0.1f;

            // Brake and reverse lamps.
            _keys.Frame.Throttle = 1f;
            yield return new WaitForSeconds(2.5f);
            Assert.IsFalse(lights.Braking, "driving");
            _keys.Frame.Throttle = -1f;
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(lights.Braking, "slowing hard shows the brake lamps");
            yield return new WaitForSeconds(3.5f);
            Assert.IsTrue(lights.Reversing, "backing up shows the reverse lamps");
            Assert.IsFalse(lights.Braking);
            _keys.Frame.Throttle = 0f;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator EngineLayers_Play_AndAScreechOnlyWhenSliding()
        {
            _keys = new Keys();
            PlayerController.InputOverride = new NoPlayerInput();
            VehicleInput.InputOverride = _keys;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && Find(VehicleKind.Car) != null, 20f, "player and car");
            yield return new WaitForSeconds(1f);
            var car = Find(VehicleKind.Car);
            var audio = car.GetComponent<VehicleAudio>();
            var me = Combatant.LocalPlayer;
            PlayerController.Local.Teleport(car.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            _keys.Frame.Interact = true;
            yield return WaitUntil(() => VehicleSeat.IsAttached(me), 3f, "in the car");
            yield return new WaitForSeconds(car.Rules.EnterSeconds + 0.4f);
            _keys.Frame.Throttle = 1f;
            yield return new WaitForSeconds(3f);
            var sources = audio.GetComponentsInChildren<AudioSource>().ToDictionary(s => s.name);
            Assert.IsTrue(sources["Engine"].isPlaying, "engine body");
            Assert.IsTrue(sources["EngineWhine"].isPlaying, "the whine layer plays under load");
            Assert.Greater(sources["EngineWhine"].volume, 0.01f);
            Assert.Greater(sources["Wind"].volume, 0f, "wind at speed (" + car.SpeedKmh + " km/h)");
            Assert.IsFalse(sources["TyreScreech"].isPlaying, "no screech while gripping");
            _keys.Frame.Throttle = 0f;
        }
    }
}
