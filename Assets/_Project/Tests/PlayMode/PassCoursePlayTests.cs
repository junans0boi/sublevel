using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #451 (ADR-559): the mountain pass as a local host, uphill and downhill: the player starts at the wheel on the right end's grid, the run
    // from the start line to the finish line finishes the race, a car fallen off the edge goes back where it left the road, and the bots
    // drive the whole road on their own.
    public class PassCoursePlayTests
    {
        sealed class NoPlayerInput : IPlayerInput { public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 }; }

        static IEnumerator Load(string id)
        {
            MoveGuard.TrustTeleports = true;
            PlayerController.InputOverride = new NoPlayerInput();
            RaceEvent.Starting = new RaceStart { CourseId = id };
            SceneManager.LoadScene(RaceEvent.ById(id).Scene);
            yield return Until(() => PlayerController.Local != null && Combatant.LocalPlayer != null && RaceDirector.Current != null, 60f, "pass scene loaded");
            Assert.AreEqual(id, RaceDirector.Current.Info.Id, "the director runs the chosen direction");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MoveGuard.TrustTeleports = false;
            PlayerController.InputOverride = null;
            RaceEvent.Starting = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator Until(System.Func<bool> ok, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!ok()) { Assert.Less(Time.realtimeSinceStartup, end, what); yield return null; }
        }

        // Along the centre line at a steady pace from where the car is to `to` (s), on the road's surface.
        static IEnumerator Drive(Vehicle car, RaceCourse c, float to, float speed)
        {
            float s = c.Project(car.transform.position.x, car.transform.position.z).s;
            while (s < to)
            {
                s = Mathf.Min(to, s + speed * Time.deltaTime);
                var (x, z, yaw) = c.PointAt(s);
                car.Place(new Vector3(x, c.HeightAt(s) + 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
                yield return null;
            }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Uphill_StartToFinish_FinishesTheRace() => StartToFinish("pass_up");

        [UnityTest, Timeout(300000)]
        public IEnumerator Downhill_StartToFinish_FinishesTheRace() => StartToFinish("pass_down");

        static IEnumerator StartToFinish(string id)
        {
            yield return Load(id);
            var me = Combatant.LocalPlayer; var c = RaceDirector.Current.Info.Course;
            yield return Until(() => VehicleSeat.VehicleOf(me) != null, 10f, "seated at the wheel");
            var car = VehicleSeat.VehicleOf(me);
            var at = car.transform.position;
            var (s0, off) = c.Project(at.x, at.z);
            Assert.That(s0, Is.InRange(-60f, 0f), "on the grid behind the start line");
            Assert.Less(Mathf.Abs(off), c.Width * 0.5f);
            Assert.AreEqual(c.HeightAt(0f), at.y, 1f, "standing on the road at its end of the mountain");
            yield return Until(() => RaceHud.Phase == RacePhase.Racing, 20f, "GO");
            Assert.Less(Vector3.Distance(at, car.transform.position), 0.5f, "parked on the level grid: nothing rolled");

            float seconds = c.MinLapSeconds + 5f, speed = c.LapLength / seconds;
            yield return Drive(car, c, c.LapLength + 6f, speed);
            yield return Until(() => RaceHud.Phase == RacePhase.Finishing, 5f, "over the finish line: the rest get their while");
            var m = RaceHud.Last;
            Assert.AreEqual(me.ObjectId, m.Keys[0], "the local player first");
            Assert.IsTrue(m.Done[0]);
            Assert.AreEqual(1, m.LapsDone[0], "one run");
            Assert.AreEqual(1, m.LapsToGo[0]);
            Assert.AreEqual(seconds, m.Times[0], 4f, "the time from the start line");
        }

        // The loop (#451, the map joined up): once round from the foot's line, up the pass and down the other side.
        [UnityTest, Timeout(400000)]
        public IEnumerator Loop_OneLap_FinishesTheRace()
        {
            yield return Load("pass_loop");
            var me = Combatant.LocalPlayer; var c = RaceDirector.Current.Info.Course;
            yield return Until(() => RaceHud.Phase == RacePhase.Racing, 25f, "GO");
            var car = VehicleSeat.VehicleOf(me);
            float seconds = c.MinLapSeconds + 5f, speed = c.LapLength / seconds;
            float s = c.Project(car.transform.position.x, car.transform.position.z).s - c.LapLength;   // on the grid: behind the line
            while (s < c.LapLength + 6f)
            {
                s += speed * Time.deltaTime;
                var (x, z, yaw) = c.PointAt(s);
                car.Place(new Vector3(x, c.HeightAt(s) + 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
                yield return null;
            }
            yield return Until(() => RaceHud.Phase == RacePhase.Finishing, 5f, "round once: finished");
            var m = RaceHud.Last;
            Assert.AreEqual(me.ObjectId, m.Keys[0]); Assert.IsTrue(m.Done[0]); Assert.AreEqual(1, m.LapsDone[0]);
            Assert.AreEqual(seconds, m.Times[0], 5f);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FallenOffTheEdge_BackWhereItLeftTheRoad()
        {
            yield return Load("pass_down");
            var me = Combatant.LocalPlayer; var c = RaceDirector.Current.Info.Course;
            yield return Until(() => RaceHud.Phase == RacePhase.Racing, 25f, "the race is on");
            var car = VehicleSeat.VehicleOf(me);
            const float edgeAt = 600f;
            yield return Drive(car, c, edgeAt, 40f);
            yield return new WaitForSeconds(0.3f);
            // Landed on the leg below (as if over the guardrail): further down the mountain, ahead in the race.
            var (x, z, _) = c.PointAt(edgeAt);
            RaceCourse.Point below = default; float best = float.MaxValue;
            foreach (var p in c.Path)
                if (p.S > edgeAt + 250f && (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < best) { best = (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z); below = p; }
            car.Place(new Vector3(below.X, below.Y + 0.5f, below.Z), Quaternion.Euler(0f, c.PointAt(below.S).yaw, 0f));
            yield return new WaitForSeconds(0.5f);
            Assert.Greater(c.Project(car.transform.position.x, car.transform.position.z).s, edgeAt + 200f, "down there for a moment");
            yield return new WaitForSeconds(RaceEvent.FallResetSeconds + 1f);
            var q = car.transform.position;
            var (s, off) = c.Project(q.x, q.z);
            Assert.AreEqual(edgeAt, s, 6f, "back on the road where it left it: " + q);
            Assert.Less(Mathf.Abs(off), 1.5f, "on the centre line");
            Assert.AreEqual(c.HeightAt(s), q.y, 1.2f, "on the road's surface, not the leg below");
            Assert.Greater(Vector3.Dot(car.transform.forward, Quaternion.Euler(0f, c.PointAt(s).yaw, 0f) * Vector3.forward), 0.9f, "facing down the road");
        }

        // The bots drive the whole road by themselves, both ways (the hairpins, the grades, braking downhill), with few put-backs.
        [UnityTest, Timeout(600000)]
        public IEnumerator Bots_DriveUphill_ToTheFinish() => BotsFinish("pass_up");

        [UnityTest, Timeout(600000)]
        public IEnumerator Bots_DriveDownhill_ToTheFinish() => BotsFinish("pass_down");

        [UnityTest, Timeout(600000)]
        public IEnumerator Bots_DriveTheLoop_ToTheFinish() => BotsFinish("pass_loop");

        static IEnumerator BotsFinish(string id)
        {
            yield return Load(id);
            yield return Until(() => RaceHud.Phase == RacePhase.Racing, 25f, "the race is on");
            float start = Time.realtimeSinceStartup;
            // The first bot in gives the rest AfterWinnerSeconds (the player stays on the grid), then the results.
            yield return Until(() => RaceHud.Phase == RacePhase.Results, 360f, "a bot at the finish, then the results");
            var r = RaceHud.Last; var sb = new System.Text.StringBuilder();
            for (int i = 0; i < r.Keys.Length; i++) sb.Append($"{r.Names[i]} {r.Times[i]:0.0}; ");
            Debug.Log($"[PassBots] {id}: all bots finished in {Time.realtimeSinceStartup - start:0} s, {RaceDirector.Current.BotResets} resets: {sb}");
            int bots = 0, done = 0;
            for (int i = 0; i < r.Keys.Length; i++) if (r.Keys[i] < 0) { bots++; if (r.Done[i]) done++; }
            Assert.AreEqual(RaceEvent.Racers - 1, bots);
            Assert.GreaterOrEqual(done, bots - 2, "nearly every bot made it to the finish in time");
            Assert.LessOrEqual(RaceDirector.Current.BotResets, bots, "the bots keep to the road (about one put-back each at most)");
        }

        // PERF A (ADR-041): the pass from the views that see the most — each grid with the cars, into the first hairpin, the switchbacks
        // from the cliff side, high looks over the whole mountain and the summit, and the loop's forest (the trees and the terrain at their worst). Run in a quiet window.
        [UnityTest, Category("Perf"), Timeout(300000)]
        public IEnumerator Pass_RendersWithinBudget()
        {
            yield return Load("pass_up");
            // The cars all on the grid, standing (the bots spawn when the countdown starts; once racing they drive through the grid view).
            yield return Until(() => RaceHud.Phase == RacePhase.Countdown && RaceDirector.Current.CarCount >= RaceEvent.Racers, 25f, "the full grid, counting down");
            var up = RaceEvent.PassUp; var down = RaceEvent.PassDown; var loop = RaceEvent.PassLoop;
            Vector3 P(RaceCourse c, float s, float h, float side = 0f) { var (x, z, yaw) = c.PointAt(s); return new Vector3(x, c.HeightAt(s) + h, z) + Quaternion.Euler(0f, yaw, 0f) * new Vector3(side, 0f, 0f); }
            float hp = 0f; foreach (var p in up.Path) if (p.S > 0f && p.Radius > 0f && p.Radius <= 15f) { hp = p.S; break; }
            var cam = new GameObject("ProbeCamera").AddComponent<Camera>();
            cam.enabled = false; cam.nearClipPlane = 0.05f; cam.farClipPlane = 900f; cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var views = new (string id, Vector3 from, Vector3 to)[]
            {
                ("grid_foot", P(up, -60f, 2.5f, 2f), P(up, 40f, 1f)),
                ("start_up", P(up, -5f, 1.3f), P(up, 80f, 1f)),
                ("hairpin", P(up, hp - 45f, 1.4f), P(up, hp + 10f, 0f)),
                ("cliff_down", P(down, down.LapLength * 0.45f, 1.4f, 2f), P(down, down.LapLength * 0.45f + 60f, -6f, 9f)),
                ("grid_top", P(down, -60f, 2.5f, 2f), P(down, 40f, -2f)),
                ("over_mountain", P(down, 20f, 30f), P(down, 700f, -60f)),
                // The summit from above (the whole mountain and both hairpin blocks in view) and the densest forest on the loop.
                ("summit_aerial", P(loop, up.LapLength + 60f, 120f, 40f), P(loop, loop.LapLength * 0.75f, -40f)),
                ("forest_north", P(loop, loop.LapLength * 0.52f, 1.4f), P(loop, loop.LapLength * 0.52f + 70f, 0f)),
                ("esses_east", P(loop, loop.LapLength * 0.8f, 1.4f), P(loop, loop.LapLength * 0.8f + 70f, -3f)),
            };
            var results = new System.Collections.Generic.List<RenderProbe.Result>();
            foreach (var v in views)
            {
                // The grid while the cars stand on it; every other view once the race is under way (past GO and its first-use hitches).
                if (v.id == "start_up") { yield return Until(() => RaceHud.Phase == RacePhase.Racing, 10f, "GO"); yield return new WaitForSeconds(5f); }
                cam.transform.position = v.from; cam.transform.LookAt(v.to);
                RenderProbe.Result r = default;
                yield return RenderProbe.Measure(cam, x => r = x, screenshotPath: $"Logs/perf/pass_{v.id}.png");
                results.Add(r);
            }
            Object.Destroy(cam.gameObject);
            RenderProbe.WriteReport("Logs/perf/pass.json", "{" + string.Join(",", views.Select((v, i) => $"\"{v.id}\":{results[i].ToJson()}")) + "}");
            Debug.Log("[Perf] pass " + string.Join(" | ", views.Select((v, i) => v.id + ": " + results[i])));
            foreach (var r in results) RenderProbe.AssertWithinBudget(r);
        }
    }
}
