using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Space (handbrake) and the brake as numbers, car and motorcycle, in the TestBox as a host. Silent: the listener volume is 0.
    // Handbrake + full steer at 40+ km/h must slide: slip angle and yaw rate above the limits below. S brakes at about 0.8 g.
    public class VehicleDriftTests
    {
        public const float DriftSpeedKmh = 45f;
        // Car: a real drift (slip and yaw above the minimums). Motorcycle: a controlled slide, not a spin (yaw capped).
        public const float MinSlipDeg = 22f, MinYawDegPerSecond = 60f, BikeMaxYawDegPerSecond = 220f;
        public const float MinBrakeG = 0.7f, MaxBrakeG = 1.0f;

        sealed class NoPlayerInput : IPlayerInput { public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 }; }

        sealed class Keys : IVehicleInput
        {
            public VehicleInputFrame Frame = new VehicleInputFrame { Seat = -1 };
            public VehicleInputFrame Read() { var f = Frame; Frame.Interact = Frame.EngineToggle = Frame.ToggleView = false; Frame.Seat = -1; return f; }
        }

        Keys _keys;

        static IEnumerator WaitUntil(Func<bool> c, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!c()) { if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what); yield return null; }
        }

        IEnumerator Load()
        {
            _keys = new Keys();
            PlayerController.InputOverride = new NoPlayerInput();
            VehicleInput.InputOverride = _keys;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player");
            yield return WaitUntil(() => Find(VehicleKind.Car) != null && Find(VehicleKind.Motorcycle) != null, 10f, "vehicles");
            AudioListener.volume = 0f;
            // Open ground beside the test box: a full-lock slide from 45 km/h reached the box's walls within the second.
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "DriftPad";
            pad.transform.position = Pad + Vector3.down * 0.5f;
            pad.transform.localScale = new Vector3(300f, 1f, 300f);
            yield return new WaitForSeconds(1f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null; VehicleInput.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null; yield return null;
        }

        static Vehicle Find(VehicleKind k) { foreach (var v in Vehicle.All) if (v != null && v.Kind == k) return v; return null; }
        static int SeatOf(Combatant c) => VehicleSeat.TryGet(c, out _, out int s) ? s : -1;

        IEnumerator Board(Vehicle v)
        {
            var me = Combatant.LocalPlayer;
            if (SeatOf(me) == 0) yield break;
            PlayerController.Local.Teleport(v.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null; yield return null;
            _keys.Frame.Interact = true;
            yield return WaitUntil(() => SeatOf(me) == 0, 3f, "driver's seat");
            yield return new WaitForSeconds(v.Rules.EnterSeconds + 0.3f);
            me.ServerSetHolstered(true);
        }

        static readonly Vector3 Pad = new Vector3(-600f, 0f, 0f);

        // The open pad, facing +X, rolling at `kmh` with the engine on and nothing pressed.
        IEnumerator Roll(Vehicle v, float kmh)
        {
            v.Body.position = Pad + new Vector3(-100f, 0.3f, v.IsMotorcycle ? 40f : -40f);
            v.Body.rotation = Quaternion.Euler(0f, 90f, 0f);
            v.Body.angularVelocity = Vector3.zero;
            _keys.Frame = new VehicleInputFrame { Seat = -1 };
            yield return new WaitForSeconds(0.3f);
            v.Body.linearVelocity = Vector3.right * (kmh / 3.6f);
            yield return new WaitForFixedUpdate();
        }

        struct Result { public float PeakSlipDeg, PeakYawDeg, EndSpeedKmh, DecelG, Seconds; }

        IEnumerator Run(Vehicle v, VehicleInputFrame press, float seconds, Action<Result> done)
        {
            _keys.Frame = press;
            var r = new Result { Seconds = seconds };
            float v0 = v.Body.linearVelocity.magnitude, t = 0f;
            while (t < seconds)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                var flatV = Vector3.ProjectOnPlane(v.Body.linearVelocity, Vector3.up);
                var flatF = Vector3.ProjectOnPlane(v.transform.forward, Vector3.up);
                if (flatV.magnitude > 2f) r.PeakSlipDeg = Mathf.Max(r.PeakSlipDeg, Mathf.Abs(Vector3.SignedAngle(flatF, flatV, Vector3.up)));
                r.PeakYawDeg = Mathf.Max(r.PeakYawDeg, Mathf.Abs(v.Body.angularVelocity.y * Mathf.Rad2Deg));
            }
            float v1 = v.Body.linearVelocity.magnitude;
            r.EndSpeedKmh = v1 * 3.6f;
            r.DecelG = (v0 - v1) / seconds / 9.81f;
            _keys.Frame = new VehicleInputFrame { Seat = -1 };
            done(r);
        }

        static string Fmt(string what, Result r) =>
            $"[drift] {what}: peak slip {r.PeakSlipDeg:F1} deg, peak yaw {r.PeakYawDeg:F0} deg/s, end {r.EndSpeedKmh:F0} km/h, mean decel {r.DecelG:F2} g over {r.Seconds:F1} s";

        static IEnumerable<VehicleKind> Kinds => new[] { VehicleKind.Car, VehicleKind.Motorcycle };

        [UnityTest]
        public IEnumerator HandbrakeWithFullSteer_Slides_AtSpeed([ValueSource(nameof(Kinds))] VehicleKind kind)
        {
            yield return Load();
            var v = Find(kind);
            yield return Board(v);
            Result plain = default, hb = default;
            yield return Roll(v, DriftSpeedKmh);
            yield return Run(v, new VehicleInputFrame { Seat = -1, Steer = 1f }, 1.0f, r => plain = r);
            Debug.Log(Fmt(kind + " steer only", plain));
            yield return Roll(v, DriftSpeedKmh);
            yield return Run(v, new VehicleInputFrame { Seat = -1, Steer = 1f, Handbrake = true }, 1.0f, r => hb = r);
            Debug.Log(Fmt(kind + " handbrake + full steer", hb));
            Assert.Greater(hb.PeakSlipDeg, MinSlipDeg, Fmt("slip angle", hb));
            Assert.Greater(hb.PeakYawDeg, MinYawDegPerSecond, Fmt("yaw rate", hb));
            if (kind == VehicleKind.Motorcycle)
            {
                Assert.Less(hb.PeakYawDeg, BikeMaxYawDegPerSecond, Fmt("the bike slides, it does not spin", hb));
                Assert.Less(hb.PeakSlipDeg, 100f, Fmt("the bike slides, it does not spin", hb));
            }
            Assert.Greater(hb.PeakSlipDeg, plain.PeakSlipDeg * 1.5f, "the handbrake is what makes it slide");
        }

        [UnityTest]
        public IEnumerator BrakeKey_DeceleratesAboutPointEightG([ValueSource(nameof(Kinds))] VehicleKind kind)
        {
            yield return Load();
            var v = Find(kind);
            yield return Board(v);
            yield return Roll(v, 60f);
            Result r = default;
            yield return Run(v, new VehicleInputFrame { Seat = -1, Throttle = -1f }, 0.8f, x => r = x);
            Debug.Log(Fmt(kind + " S brake from 60 km/h", r));
            Assert.That(r.DecelG, Is.InRange(MinBrakeG, MaxBrakeG), Fmt("brake", r));
        }

        // Tuning aid, not part of the suite: Handbrake variants for one kind in one run (Rules are shared, restored at the end).
        [UnityTest, Explicit("tuning sweep")]
        public IEnumerator Sweep([ValueSource(nameof(Kinds))] VehicleKind kind)
        {
            if (kind == VehicleKind.Car) yield break;
            foreach (float grip in new[] { 0.8f, 0.7f, 0.6f, 0.5f })
            foreach (float thr in new[] { 0f, 1f })
            {
                yield return Load();   // a fresh scene each time: a bike that fell over would spoil the next rows
                var v = Find(kind);
                yield return Board(v);
                var rules = v.Rules;
                float g0 = rules.HandbrakeRearGrip, d0 = rules.HandbrakeDecel;
                rules.HandbrakeRearGrip = grip; rules.HandbrakeDecel = 6f;
                yield return Roll(v, DriftSpeedKmh);
                Result r = default;
                yield return Run(v, new VehicleInputFrame { Seat = -1, Steer = 1f, Handbrake = true, Throttle = thr }, 1.0f, x => r = x);
                Debug.Log(Fmt($"{kind} grip {grip} throttle {thr} upY {v.transform.up.y:F2}", r));
                rules.HandbrakeRearGrip = g0; rules.HandbrakeDecel = d0;
            }
        }
    }
}
