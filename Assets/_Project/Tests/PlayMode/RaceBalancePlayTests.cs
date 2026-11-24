using System.Collections;
using System.Collections.Generic;
using FishNet;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #517 (ADR-562): balance check of the race rows. On each event course every car model runs a time trial with the same fixed-skill
    // driver that uses its own car (top speed on boost, tyre grip, brakes), alone on the road: a flying lap on a circuit, a standing run on
    // a sprint. Logs a [RaceBalance] line per course; asserts the spec's targets (no car fastest everywhere is checked across the four logs
    // by hand in the worklog; per course best-to-worst within ~6 %). Measurement, not a gate — run on purpose:
    //   tools/unity.sh test playmode Game.Tests.RaceBalancePlayTests
    // The event bots (RaceBotDriver) aim at a fixed 90 km/h with a fixed grip, so they cannot show a car's character: this driver can.
    [Explicit, Category("Balance")]
    public class RaceBalancePlayTests
    {
        sealed class NoPlayerInput : IPlayerInput { public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 }; }

        const float Skill = 0.9f;            // fraction of the grip limit in corners (the event bots: 0.80..0.95)
        const float TimeScale = 4f;          // physics steps are fixed, inputs set every step: the same run, quicker
        const float SprintGap = 12f;         // s between starts on a sprint (no car catches the one before)
        public static float MaxSpread = 0.06f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            MoveGuard.TrustTeleports = true;
            PlayerController.InputOverride = new NoPlayerInput();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            MoveGuard.TrustTeleports = false;
            PlayerController.InputOverride = null;
            RaceEvent.Chosen = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(900000)] public IEnumerator Snake() => Run("snake");
        [UnityTest, Timeout(900000)] public IEnumerator PassUp() => Run("pass_up");
        [UnityTest, Timeout(900000)] public IEnumerator PassDown() => Run("pass_down");
        [UnityTest, Timeout(900000)] public IEnumerator PassLoop() => Run("pass_loop");

        sealed class Entry { public Vehicle Car; public string Model; public float GoodS = float.NaN, PrevS, StartAt = -1f, Time = -1f, StuckSince = -1f; public int Crossings, Resets; public bool Out; }

        static IEnumerator Run(string id)
        {
            var info = RaceEvent.ById(id);
            RaceEvent.Chosen = id;
            SceneManager.LoadScene(info.Scene);
            float end = Time.realtimeSinceStartup + 90f;
            Vehicle mine = null;
            while (RaceDirector.Current == null || Combatant.LocalPlayer == null || (mine = RaceDirector.Current.CarOf(Combatant.LocalPlayer)) == null)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "race scene loaded and the player's grid car there");
                yield return null;
            }
            Assert.AreEqual(id, RaceDirector.Current.Info.Id);
            // The director's race (bots, lap judge) is not needed: only its scene, its course and the race rules for every car.
            RaceDirector.Current.enabled = false;
            var body = mine.GetComponent<Rigidbody>();
            body.isKinematic = true;
            mine.transform.position = new Vector3(0f, -2000f, 0f);   // the player's grid car out of the way

            var c = info.Course;
            var prefabs = VehicleSpawner.Instance.CarPrefabs();
            Assert.AreEqual(5, prefabs.Count, "the five race models");
            var cars = new List<Entry>();
            for (int k = 0; k < prefabs.Count; k++) cars.Add(new Entry { Model = prefabs[k].name });

            Time.timeScale = TimeScale;
            float t0 = Time.fixedTime;
            int launched = 0;
            end = Time.realtimeSinceStartup + 600f;
            while (true)
            {
                yield return new WaitForFixedUpdate();
                float now = Time.fixedTime - t0;
                // Circuits: every car on the road at once, spread round the lap. Sprints: one after another from the start.
                while (launched < cars.Count && (!c.Open || now >= launched * SprintGap))
                {
                    float s = c.Open ? 12f : c.LapLength * (0.05f + 0.18f * launched);
                    cars[launched].Car = Spawn(prefabs[launched], c, s, launched);
                    cars[launched].PrevS = s; cars[launched].GoodS = s;
                    if (c.Open) cars[launched].StartAt = now;
                    launched++;
                }
                bool all = launched == cars.Count;
                foreach (var e in cars)
                {
                    if (e.Car == null || e.Time >= 0f || e.Out) continue;
                    all = false;
                    Step(e, c, now);
                }
                if (all) break;
                Assert.Less(Time.realtimeSinceStartup, end, "every car through its run");
            }
            Time.timeScale = 1f;

            float best = float.MaxValue, worst = 0f;
            var sb = new System.Text.StringBuilder($"[RaceBalance] {id}:");
            foreach (var e in cars)
            {
                sb.Append($" {e.Model} {(e.Out ? "DNF" : e.Time.ToString("0.00"))} s ({e.Resets} resets);");
                if (e.Out) continue;
                best = Mathf.Min(best, e.Time); worst = Mathf.Max(worst, e.Time);
            }
            sb.Append($" spread {(worst / best - 1f) * 100f:0.0} %");
            Debug.Log(sb.ToString());
            foreach (var e in cars) Assert.IsFalse(e.Out, e.Model + " finished its run");
            Assert.LessOrEqual(worst / best - 1f, MaxSpread, "best-to-worst within ~6 %");
        }

        static Vehicle Spawn(FishNet.Object.NetworkObject prefab, RaceCourse c, float s, int k)
        {
            var (x, z, yaw) = c.PointAt(s);
            var nob = Object.Instantiate(prefab, new Vector3(x, c.HeightAt(s) + 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
            var v = nob.GetComponent<Vehicle>();
            v.ServerSetPaint(k);
            InstanceFinder.ServerManager.Spawn(nob);
            v.Autopilot = true;
            v.ServerSetEngine(true);
            return v;
        }

        // One physics step of a car's run: drive, time it, put it back on the road when stuck (as the event bots are).
        static void Step(Entry e, RaceCourse c, float now)
        {
            var t = e.Car.transform; var pos = t.position;
            float s = c.Project(pos.x, pos.z).s;
            if (c.Open) { if (s >= c.LapLength - 1f) { e.Time = now - e.StartAt; e.Car.SetDriverInput(new DriverInput { Handbrake = true }); return; } }
            else if (e.PrevS > c.LapLength * 0.75f && s < c.LapLength * 0.25f)
            {
                // Over the line: the first crossing starts the flying lap, the second ends it.
                if (++e.Crossings == 1) e.StartAt = now;
                else { e.Time = now - e.StartAt; e.Car.SetDriverInput(new DriverInput { Handbrake = true }); return; }
            }
            e.PrevS = s;
            e.Car.SetDriverInput(Drive(c, e.Car, pos.x, pos.z, t.eulerAngles.y, s));

            e.GoodS = c.Follow(e.GoodS, pos.x, pos.y, pos.z);
            bool fell = c.Fell(e.GoodS, pos.y);
            bool stuck = fell || t.up.y < 0.3f || Mathf.Abs(c.Project(pos.x, pos.z).offset) > c.Width * 0.5f + c.Runoff + 2f || Mathf.Abs(e.Car.Speed) < 1f;
            if (!stuck) { e.StuckSince = -1f; return; }
            if (e.StuckSince < 0f) { e.StuckSince = now; return; }
            if (now - e.StuckSince < (fell ? RaceEvent.FallResetSeconds : RaceEvent.StuckResetSeconds)) return;
            var (x, y, z, yaw) = c.ResetTo(pos.x, pos.z, e.GoodS);
            e.Car.ServerPlace(new Vector3(x, y + 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
            e.StuckSince = -1f;
            if (++e.Resets > 5) e.Out = true;
        }

        // RaceBotDriver's line and corner plan, but with this car's own limits: its boost top speed, its tyre grip (the bot counts on 1.05
        // of the base car's 1.15) and its brakes (the bot plans 6 of the base car's 9). Boost held whenever it pulls.
        static DriverInput Drive(RaceCourse c, Vehicle car, float x, float z, float yaw, float s)
        {
            var r = car.Rules;
            float speed = car.Speed, top = r.BoostMaxKmh / 3.6f, grip = r.TyreGrip * (RaceBotDriver.Grip / 1.15f), plan = r.BrakeDecel * (6f / 9f);
            var (ax, az) = RaceBotDriver.Aim(c, x, z, s, speed, false);
            float want = Mathf.Atan2(ax - x, az - z) * Mathf.Rad2Deg;
            float diff = Mathf.DeltaAngle(yaw, want);
            float target = top;
            foreach (var p in c.Path)
            {
                if (p.Radius <= 0f) continue;
                float L = c.LapLength, d = c.Open ? p.S - s : ((p.S - s) % L + L) % L;
                if (d < 0f || d > 80f) continue;
                float corner = Mathf.Sqrt(grip * RaceBotDriver.Gravity * p.Radius) * Skill;
                target = Mathf.Min(target, Mathf.Sqrt(corner * corner + 2f * plan * d));
            }
            float throttle = speed > target + 1f ? -1f : speed < target - 0.5f ? 1f : 0.3f;
            return new DriverInput { Throttle = throttle, Steer = Mathf.Clamp(diff / 25f, -1f, 1f), Boost = throttle > 0f };
        }
    }
}
