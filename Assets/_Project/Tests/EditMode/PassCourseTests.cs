using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #451 (ADR-559): course 3 "고개" — an open mountain road from the foot to the summit, raced uphill or downhill from a start line to a
    // finish line: the road's shape and heights, the sprint judge, the fall off the edge and where the car goes back.
    public class PassCourseTests
    {
        static RaceCourse Up => RaceEvent.PassUp;
        static RaceCourse Down => RaceEvent.PassDown;

        static float Dist(RaceCourse.Point a, RaceCourse.Point b) => (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));

        [Test]
        public void OpenRoad_StartToFinish_AboutTwoKilometres_ClimbingOverAHundredMetres()
        {
            Assert.IsTrue(Up.Open && Down.Open);
            Assert.That(Up.LapLength, Is.InRange(1600f, 2200f), "start to finish");
            Assert.AreEqual(Up.LapLength, Down.LapLength, 0.5f, "the same stretch of road either way");
            float climb = Up.HeightAt(Up.LapLength) - Up.HeightAt(0f);
            Assert.Greater(climb, 100f, "a real climb");
            Assert.AreEqual(-climb, Down.HeightAt(Down.LapLength) - Down.HeightAt(0f), 0.1f, "downhill falls as far");
            Assert.AreEqual(0f, Up.Project(RaceEvent.PassFoot.x, RaceEvent.PassFoot.z).s, 0.05f, "uphill starts at the foot");
            Assert.AreEqual(Up.LapLength, Up.Project(RaceEvent.PassTop.x, RaceEvent.PassTop.z).s, 0.05f, "and finishes at the top");
            Assert.AreEqual(0f, Down.Project(RaceEvent.PassTop.x, RaceEvent.PassTop.z).s, 0.05f, "downhill starts at the top");
            Assert.Less(Up.MinLapSeconds, Up.LapLength / 29f, "a real flat-out run is never refused");
            for (int i = 1; i < Up.Checkpoints.Count; i++) Assert.Greater(Up.Checkpoints[i], Up.Checkpoints[i - 1]);
            Assert.Less(Up.Checkpoints[Up.Checkpoints.Count - 1], Up.LapLength);
            Assert.Greater(Up.Path[0].S, -200f); Assert.Less(Up.Path[0].S, -60f, "a run-up behind the start for the grid");
            Assert.Greater(Up.Path[Up.Path.Count - 1].S - Up.LapLength, 60f, "room to stop after the finish");
        }

        [Test]
        public void Profile_NoSteeperThanTwelvePercent_EvenOnTheInsideOfAHairpin()
        {
            foreach (var c in new[] { Up, Down })
            {
                var p = c.Path;
                float max = 0f, inner = 0f;
                for (int i = 0; i < p.Count - 1; i++)
                {
                    float run = p[i + 1].S - p[i].S, g = Math.Abs(p[i + 1].Y - p[i].Y) / run;
                    max = Math.Max(max, g);
                    if (p[i].Radius > 0f && p[i + 1].Radius > 0f) inner = Math.Max(inner, g * p[i].Radius / (p[i].Radius - c.Width * 0.5f));
                }
                Assert.LessOrEqual(max, 0.12f, "centre line");
                Assert.LessOrEqual(inner, 0.12f, "the inside edge of the tightest corner");
                Assert.Greater(max, 0.06f, "it is a hill road");
                // The grids and the line are level (cars stand there before GO).
                for (float s = -60f; s <= 0f; s += 5f) Assert.AreEqual(c.HeightAt(0f), c.HeightAt(s), 0.05f, $"level at s {s}");
            }
        }

        [Test]
        public void Hairpins_FourInARow_NoneTighterThanTheCarTurns()
        {
            var k = RaceEvent.PassCorners;
            int hairpins = 0;
            for (int i = 1; i < k.Length - 2; i++)
            {
                if (k[i].r <= 0f) continue;
                Assert.GreaterOrEqual(k[i].r, 12f, "no corner tighter than the snake circuit's 12 m hairpins");
                Assert.GreaterOrEqual(k[i].r - RaceEvent.PassWidth * 0.5f, 8f, "the inside edge still turnable");
                if (k[i].r > 15f || k[i + 1].r > 15f || k[i + 1].r <= 0f) continue;
                // A hairpin: two tight corners close together that turn the road round.
                double hin = Math.Atan2(k[i].x - k[i - 1].x, k[i].z - k[i - 1].z), hout = Math.Atan2(k[i + 2].x - k[i + 1].x, k[i + 2].z - k[i + 1].z);
                double turn = Math.Abs(((hout - hin) * 180.0 / Math.PI % 360.0 + 540.0) % 360.0 - 180.0);
                Assert.Greater(turn, 150.0, $"corners {i}-{i + 1} turn the road round");
                hairpins++; i++;
            }
            Assert.AreEqual(4, hairpins, "a block of four hairpins");
        }

        [Test]
        public void Legs_StayApart_NeverShareAShoulderOrCross()
        {
            var p = Up.Path;
            float min = float.MaxValue;
            for (int i = 0; i < p.Count; i++)
                for (int j = i + 1; j < p.Count; j++)
                    if (p[j].S - p[i].S >= 70f) min = Math.Min(min, Dist(p[i], p[j]));
            Assert.GreaterOrEqual(min, 30f, "different legs at least 30 m apart (room for the slope between them)");
            Assert.GreaterOrEqual(min, Up.Width + 2f * Up.Runoff + 2f);
        }

        [Test]
        public void BothDirections_AreTheSameRoad()
        {
            foreach (var p in Up.Path)
            {
                var (s, off) = Down.Project(p.X, p.Z);
                Assert.AreEqual(0f, off, 0.05f, "on the downhill centre line");
                Assert.AreEqual(p.Y, Down.HeightAt(s), 0.05f, $"the same height at uphill s {p.S:0}");
                Assert.AreEqual(Up.LapLength, p.S + s, 0.5f, "distances run the other way");
            }
        }

        [Test]
        public void Sprint_ClockStartsAtTheLine_FinishesAtTheFinishLine_Once()
        {
            var lap = new RaceLap(Up);
            double t = 0; float finished = -1f; int times = 0;
            for (float s = -40f; s < Up.LapLength + 30f; s += 1.25f, t += 0.05)
            {
                if (s < 0f) Assert.IsFalse(lap.Started, "not before the line");
                if (s < Up.LapLength - 1f) Assert.AreEqual(0, lap.Laps, $"not done at {s:0}");
                float r = lap.Step(s, t);
                if (r > 0f) { finished = r; times++; }
            }
            Assert.AreEqual(1, lap.Laps, "done");
            Assert.AreEqual(1, times, "its time given once");
            Assert.AreEqual(Up.LapLength / 25f, finished, 0.2f, "from the start line to the finish line at 25 m/s");
        }

        [Test]
        public void Sprint_AJumpAhead_DoesNotCount_DrivingOnDoes()
        {
            var lap = new RaceLap(Down);
            double t = 0;
            float s = -10f;
            for (; s < 300f; s += 1f, t += 0.05) lap.Step(s, t);
            // Off the edge onto the leg below (ahead on a downhill): nothing from there counts.
            for (float j = 800f; j < Down.LapLength + 20f; j += 1f, t += 0.05) lap.Step(j, t);
            Assert.AreEqual(0, lap.Laps, "not finished by falling down the mountain");
            Assert.AreEqual(300f, lap.Progress(820f), 1.5f, "the order counts where it drove to");
            // Put back where it left the road and driven on: it finishes.
            for (s = 299f; s < Down.LapLength + 5f; s += 1f, t += 0.05) lap.Step(s, t);
            Assert.AreEqual(1, lap.Laps);

            var cheat = new RaceLap(Up);
            cheat.Step(-2f, 0); cheat.Step(1f, 0.1); cheat.Step(Up.LapLength + 1f, 50);
            Assert.AreEqual(0, cheat.Laps, "a teleport to the finish is no run");
            var early = new RaceLap(Up);
            early.Step(-2f, 0); early.Step(40f, 0.1);
            Assert.IsFalse(early.Started, "crossing the line by a jump does not start the clock");
        }

        [Test]
        public void Sprint_RaceOrder_ByHowFarDownTheRoad_GridLast()
        {
            RaceLap Run(float to) { var l = new RaceLap(Up); double t = 0; for (float s = -5f; s <= to; s += 1f, t += 0.05) l.Step(s, t); return l; }
            var a = Run(500f); var b = Run(300f); var grid = new RaceLap(Up); grid.Step(-20f, 0);
            var list = new System.Collections.Generic.List<RaceEvent.Standing>
            {
                new RaceEvent.Standing { Key = 0, Progress = grid.Progress(-20f) },
                new RaceEvent.Standing { Key = 1, Progress = b.Progress(300f) },
                new RaceEvent.Standing { Key = 2, Progress = a.Progress(500f) },
            };
            RaceEvent.Order(list);
            Assert.AreEqual(new[] { 2, 1, 0 }, new[] { list[0].Key, list[1].Key, list[2].Key });
        }

        [Test]
        public void Fall_OffTheEdge_IsSeen_AndTheCarGoesBackWhereItLeftTheRoad()
        {
            var c = Down;
            const float at = 600f;
            var (x, z, yaw) = c.PointAt(at); float y = c.HeightAt(at);
            float good = c.Follow(float.NaN, x, y + 0.5f, z);
            Assert.AreEqual(at, good, 0.5f, "on the road: followed");
            Assert.IsFalse(c.Fell(good, y + 0.5f));
            // The nearest point of a leg further down the mountain.
            RaceCourse.Point below = default; float best = float.MaxValue;
            foreach (var p in c.Path)
            {
                if (p.S < at + 250f) continue;
                float d = (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z);
                if (d < best) { best = d; below = p; }
            }
            Assert.Less(Math.Sqrt(best), 120.0, "a leg below within reach");
            Assert.Greater(y - below.Y, RaceCourse.FallDepth, "well below");
            float kept = c.Follow(good, below.X, below.Y + 0.5f, below.Z);
            Assert.AreEqual(good, kept, "landed on the leg below: not the way on");
            Assert.IsTrue(c.Fell(kept, below.Y + 0.5f), "fallen");
            var (rx, ry, rz, ryaw) = c.ResetTo(below.X, below.Z, kept);
            Assert.AreEqual(x, rx, 0.1f); Assert.AreEqual(z, rz, 0.1f); Assert.AreEqual(y, ry, 0.05f, "back on the road where it left it");
            Assert.AreEqual(yaw, ryaw, 1f, "facing the way of the race");
            // Driving on along the road keeps it followed; a car up the slope beside the road is not on it.
            var (x2, z2, _) = c.PointAt(at + 8f);
            Assert.AreEqual(at + 8f, c.Follow(good, x2, c.HeightAt(at + 8f) + 0.4f, z2), 0.5f);
            Assert.AreEqual(good, c.Follow(good, x, y + 4f, z), "flying high over the road: not on it");

            // A flat circuit: nothing to fall off; R goes to the nearest point at ground level.
            var flat = RaceEvent.Course;
            Assert.IsFalse(flat.Fell(10f, -20f));
            var r = flat.ResetTo(9f, 0f, float.NaN);
            Assert.AreEqual(0f, r.x, 0.05f); Assert.AreEqual(0f, r.y); Assert.AreEqual(0f, r.z, 0.05f);
        }

        [Test]
        public void Bots_OnAnOpenRoad_DoNotBrakeForCornersBehindTheStart()
        {
            // On the summit straight past the last corner nothing lies ahead (a circuit would see its first corners there).
            Assert.AreEqual(RaceBotDriver.TopSpeed, RaceBotDriver.TargetSpeed(Up, Up.LapLength - 5f, 0.9f), 0.01f);
            // Before the first hairpin it slows.
            float hairpin = 0f;
            foreach (var p in Up.Path) if (p.S > 0f && p.Radius > 0f && p.Radius <= 15f) { hairpin = p.S; break; }
            Assert.Less(RaceBotDriver.TargetSpeed(Up, hairpin - 10f, 0.9f), 16f, "slows for the hairpin");
        }

        // The loop (the map joined up): up the pass, over the top, round the peak and down the other side back to the foot.
        [Test]
        public void Loop_IsClosed_RunsAlongThePass_AndComesBackDownTheOtherSide()
        {
            var c = RaceEvent.PassLoop;
            Assert.IsFalse(c.Open); Assert.IsTrue(c.Hilly);
            Assert.That(c.LapLength, Is.InRange(4300f, 5400f), "a lap");
            Assert.AreEqual(0f, c.Project(RaceEvent.PassFoot.x, RaceEvent.PassFoot.z).s, 0.05f, "starts at the foot's line");
            foreach (var p in Up.Path)
            {
                if (p.S < -50f || p.S > Up.LapLength + 50f) continue;
                var (s, off) = c.Project(p.X, p.Z);
                Assert.AreEqual(0f, off, 0.05f, $"the pass is part of the loop (uphill s {p.S:0})");
                Assert.AreEqual(p.Y, c.HeightAt(s), 0.05f, "at the pass's heights");
            }
            var q = c.Path; int n = q.Count; float max = 0f, lo = float.MaxValue, hi = float.MinValue, gap = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var a = q[i]; var b = q[(i + 1) % n];
                float run = Dist(a, b); max = Math.Max(max, Math.Abs(b.Y - a.Y) / run);
                if (a.Radius > 0f && b.Radius > 0f) max = Math.Max(max, Math.Abs(b.Y - a.Y) / run * a.Radius / (a.Radius - c.Width * 0.5f));
                lo = Math.Min(lo, a.Y); hi = Math.Max(hi, a.Y);
                for (int j = i + 1; j < n; j++)
                {
                    float ds = Math.Abs(q[j].S - a.S); ds = Math.Min(ds, c.LapLength - ds);
                    if (ds >= 70f) gap = Math.Min(gap, Dist(a, q[j]));
                }
            }
            Assert.LessOrEqual(max, 0.12f, "no steeper than 12%, the way down the other side included");
            Assert.AreEqual(Up.HeightAt(Up.LapLength) - Up.HeightAt(0f), hi - lo, 1f, "the loop climbs the pass and comes all the way down");
            Assert.GreaterOrEqual(gap, 30f, "no two parts of the loop closer than 30 m (no crossing)");
            for (float s = -60f; s <= 0f; s += 5f) Assert.AreEqual(c.HeightAt(0f), c.HeightAt(s), 0.05f, "level grid");
            // A lap of the loop counts like a circuit's.
            var lap = new RaceLap(c); double t = 0; float time = -1f;
            for (float s = -30f; s <= c.LapLength * 2f + 5f; s += 2f, t += 0.08) { float r = lap.Step(((s % c.LapLength) + c.LapLength) % c.LapLength, t); if (r > 0f) time = r; }
            Assert.AreEqual(2, lap.Laps); Assert.AreEqual(c.LapLength / 25f, time, 0.5f);
            // Fallen off the pass's edge on the loop: back where it left the road.
            var (x, z, _) = c.PointAt(900f); float good = c.Follow(float.NaN, x, c.HeightAt(900f) + 0.5f, z);
            Assert.IsTrue(c.Fell(good, c.HeightAt(900f) - 8f));
            Assert.AreEqual(c.HeightAt(900f), c.ResetTo(0f, 0f, good).y, 0.05f);
        }

        [Test]
        public void BothDirections_ShareTheScene_AndTheLobbyNamesWhich()
        {
            var up = RaceEvent.ById("pass_up"); var down = RaceEvent.ById("pass_down");
            Assert.AreEqual("Race_Pass", up.Scene); Assert.AreEqual(up.Scene, down.Scene);
            Assert.AreSame(Up, up.Course); Assert.AreSame(Down, down.Course);
            Assert.AreEqual(1, up.Laps); Assert.AreEqual(1, down.Laps);
            int n = Array.IndexOf(RaceEvent.Courses, down) + 1;
            Assert.AreEqual("Race_Pass", RaceEvent.SceneForLobby((byte)n));
            Assert.AreEqual("pass_down", RaceEvent.IdForLobby((byte)n));
            Assert.IsNull(RaceEvent.IdForLobby(0));
            foreach (var (x, z, _) in Down.GridSpots(RaceEvent.GridSlots)) Assert.AreEqual(Down.HeightAt(0f), Down.RoadHeight(x, z), 0.05f, "the downhill grid at the top, level");
        }

        // The sprint's HUD line in place of "LAP 1/1", and its results column (#451).
        [Test]
        public void SprintHud_ShowsMetresLeftAndHowFar_ResultsTheAverageSpeed()
        {
            Assert.AreEqual("남은 1,873 m · 0%", RaceEvent.SprintLine(-20f, 1873f), "on the grid behind the line");
            Assert.AreEqual("남은 937 m · 49%", RaceEvent.SprintLine(936.4f, 1873f));
            Assert.AreEqual("남은 0 m · 100%", RaceEvent.SprintLine(1900f, 1873f), "past the finish");
            Assert.AreEqual("72 km/h", RaceEvent.AverageKmh(1800f, 90f));
            Assert.AreEqual("--", RaceEvent.AverageKmh(1800f, 0f));
        }
    }
}
