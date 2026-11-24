using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // #444 (ADR-513/515): the event mode's circuit with its fork, the lap judge by checkpoints on either route, and the race order.
    public class RaceEventTests
    {
        static RaceCourse C => RaceEvent.Course;

        static float Dist(RaceCourse.Point a, RaceCourse.Point b) => (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));

        // A rough time over a run of samples: each at the lower of the 90 km/h top speed and its corner's grip limit (car grip 1.05).
        static float Time(IReadOnlyList<RaceCourse.Point> pts, int from, int to)
        {
            float t = 0f;
            for (int i = from; i < to; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                float v = a.Radius > 0f ? Math.Min(25f, (float)Math.Sqrt(1.05 * 9.81 * a.Radius)) : 25f;
                t += Dist(a, b) / v;
            }
            return t;
        }

        // The main route's samples in driving order from the line, as (x, z, s).
        static List<RaceCourse.Point> MainFromLine()
        {
            var list = new List<RaceCourse.Point>(C.Path);
            list.Sort((a, b) => a.S.CompareTo(b.S));
            return list;
        }

        [Test]
        public void Circuit_IsClosed_AboutTwoAndAQuarterKilometres_AndAboutTwoAndAHalfMinutesALap()
        {
            var path = C.Path;
            float sum = 0f;
            for (int i = 0; i < path.Count; i++) sum += Dist(path[i], path[(i + 1) % path.Count]);
            Assert.AreEqual(C.LapLength, sum, 0.1f, "the loop closes on itself");
            Assert.That(C.LapLength, Is.InRange(2000f, 2600f));
            var main = MainFromLine();
            main.Add(main[0]);
            Assert.That(Time(main, 0, main.Count - 1), Is.InRange(110f, 170f), "about two and a half minutes a lap, two laps about five");
            Assert.Less(C.MinLapSeconds, C.LapLength / 29f, "a real flat-out lap is never refused");
            foreach (var c in RaceEvent.Corners) Assert.GreaterOrEqual(c.r, 12f, "no corner tighter than a 12 m hairpin");
            int hairpins = 0; foreach (var c in RaceEvent.Corners) if (c.r <= 12f) hairpins++;
            Assert.GreaterOrEqual(hairpins, 8, "a block of switchbacks");
            Assert.AreEqual(0f, C.Project(0f, RaceEvent.LineZ).s, 0.05f, "the line is s = 0");
            Assert.AreEqual(10f, C.Project(0f, RaceEvent.LineZ - 10f).s, 0.05f, "the race runs north on the main straight");
        }

        [Test]
        public void Sections_NeverShareRunoff_OutsideTheFork()
        {
            var path = C.Path;
            float L = C.LapLength, need = C.Width + 2f * C.Runoff + 2f, min = float.MaxValue;
            for (int i = 0; i < path.Count; i++)
                for (int j = i + 1; j < path.Count; j++)
                {
                    float ds = Math.Abs(path[i].S - path[j].S); ds = Math.Min(ds, L - ds);
                    if (ds < 70f) continue;
                    min = Math.Min(min, Dist(path[i], path[j]));
                }
            Assert.GreaterOrEqual(min, need, "two parts of the main route never share run-off or a barrier");
            // The inner cut stays clear of everything but the stretch of main route it replaces.
            var branch = C.BranchPath;
            for (int b = 0; b < branch.Count; b++)
                foreach (var p in path)
                {
                    if (p.S > C.ForkS - 40f && p.S < C.JoinS + 40f) continue;
                    Assert.GreaterOrEqual(Dist(branch[b], p), need, $"branch sample {b} clear of the main route at s {p.S:0}");
                }
        }

        [Test]
        public void Fork_LeavesAndJoinsTheMainRoute_BothWaysAboutAsFast()
        {
            var branch = C.BranchPath;
            Assert.Greater(branch.Count, 4);
            Assert.Less(Math.Abs(C.Project(branch[0].X, branch[0].Z).offset), 0.5f, "the fork starts on the main route");
            Assert.Less(Math.Abs(C.Project(branch[branch.Count - 1].X, branch[branch.Count - 1].Z).offset), 0.5f, "and ends on it");
            Assert.Greater(C.ForkS, 0f); Assert.Greater(C.JoinS, C.ForkS, "the fork is not over the line");
            Assert.AreEqual(C.ForkS, branch[0].S, 0.5f); Assert.AreEqual(C.JoinS, branch[branch.Count - 1].S, 0.5f);
            var main = MainFromLine();
            int a = main.FindIndex(p => p.S >= C.ForkS), b = main.FindLastIndex(p => p.S <= C.JoinS);
            float tMain = Time(main, a, b), tBranch = Time(branch, 0, branch.Count - 1);
            Assert.That(tBranch / tMain, Is.InRange(0.9f, 1.1f), $"the outer arc {tMain:0.0} s, the inner cut {tBranch:0.0} s: a real choice");
            foreach (float cp in C.Checkpoints) Assert.IsFalse(cp > C.ForkS - 25f && cp < C.JoinS + 25f, $"checkpoint at {cp:0} is outside the fork");
            Assert.GreaterOrEqual(C.Sectors, 8, "enough checkpoints that a lap cannot be cut short");
            // On the inner cut the distance runs from the fork to the join too.
            var (s, off, onBranch) = C.ProjectRoute(branch[branch.Count / 2].X, branch[branch.Count / 2].Z);
            Assert.IsTrue(onBranch); Assert.AreEqual(0f, off, 0.05f);
            Assert.That(s, Is.InRange(C.ForkS, C.JoinS));
        }

        // Drive one lap's samples (by the main route, or with the inner cut in the fork) through a lap judge from just before the line.
        static float DriveLap(RaceLap lap, bool cut, double t0, double seconds)
        {
            var route = new List<RaceCourse.Point>();
            foreach (var p in MainFromLine())
                if (!cut || p.S < C.ForkS || p.S > C.JoinS) route.Add(p);
            if (cut)
            {
                int at = route.FindIndex(p => p.S > C.JoinS);
                route.InsertRange(at, C.BranchPath);
            }
            route.Add(MainFromLine()[0]);
            float result = -1f;
            for (int i = 0; i < route.Count; i++)
            {
                float s = C.Project(route[i].X, route[i].Z).s;
                if (i == route.Count - 1 && s > C.LapLength * 0.5f) s = 0f;
                float r = lap.Step(s, t0 + seconds * i / (route.Count - 1));
                if (r > 0f) result = r;
            }
            return result;
        }

        [Test]
        public void Lap_CountsByEitherRoute_NotByCuttingAcross()
        {
            var lap = new RaceLap(C);
            lap.Step(C.LapLength - 5f, 0); lap.Step(1f, 0.2);            // off the grid over the line: the clock starts
            Assert.IsTrue(lap.Timing);
            Assert.AreEqual(150f, DriveLap(lap, false, 0.2, 150), 0.5f, "a lap by the outer arc");
            Assert.AreEqual(1, lap.Laps);
            Assert.AreEqual(152f, DriveLap(lap, true, 150.2, 152), 0.5f, "a lap by the inner cut");
            Assert.AreEqual(2, lap.Laps);

            var cheat = new RaceLap(C);
            cheat.Step(C.LapLength - 5f, 0); cheat.Step(1f, 0.2);
            cheat.Step(C.Checkpoints[1] + 5f, 20);
            cheat.Step(C.Checkpoints[4] + 5f, 30);                         // across the infield past three checkpoints
            Assert.IsFalse(cheat.Timing, "cutting across stops the clock");
            Assert.AreEqual(0, cheat.Laps);
        }

        [Test]
        public void Progress_GrowsRoundTheRace_AndTheGridIsBehindTheLine()
        {
            var lap = new RaceLap(C);
            float L = C.LapLength;
            lap.Step(L - 30f, 0);
            float grid = lap.Progress(L - 30f);
            Assert.Less(grid, 0f, "on the grid, before the line");
            lap.Step(2f, 1);
            float p0 = lap.Progress(2f);
            Assert.Greater(p0, grid, "over the line is ahead of the grid");
            Assert.AreEqual(2f, p0, 0.01f);
            Assert.Greater(lap.Progress(L * 0.5f), p0, "and further round is further ahead");
        }

        [Test]
        public void RaceOrder_FinishersByTime_ThenByHowFarRound()
        {
            var list = new List<RaceEvent.Standing>
            {
                new RaceEvent.Standing { Key = 1, Progress = 3000f },
                new RaceEvent.Standing { Key = 2, Finished = true, FinishTime = 250 },
                new RaceEvent.Standing { Key = 3, Progress = 3500f },
                new RaceEvent.Standing { Key = 4, Finished = true, FinishTime = 240 },
                new RaceEvent.Standing { Key = 5, Progress = -20f },
            };
            RaceEvent.Order(list);
            CollectionAssert.AreEqual(new[] { 4, 2, 3, 1, 5 }, list.ConvertAll(e => e.Key));
        }

        [Test]
        public void Grid_TwelveStaggeredSlots_OnTheMainStraight_BehindTheLine()
        {
            var spots = RaceEvent.GridSpots();
            Assert.AreEqual(RaceEvent.GridSlots, spots.Count);
            Assert.GreaterOrEqual(RaceEvent.GridSlots, RaceEvent.Racers);
            for (int i = 0; i < spots.Count; i++)
            {
                var (x, z, yaw) = spots[i];
                var (s, off) = C.Project(x, z);
                Assert.Less(Math.Abs(off), C.Width * 0.5f - 1.2f, $"slot {i} on the tarmac with room for the car");
                Assert.Greater(s, C.LapLength - 80f, $"slot {i} just behind the line");
                Assert.AreEqual(180f, yaw);
                for (int j = 0; j < i; j++)
                {
                    float dx = x - spots[j].x, dz = z - spots[j].z;
                    Assert.Greater(Math.Sqrt(dx * dx + dz * dz), 6f, $"slots {i} and {j} apart");
                }
            }
        }
    }
}

namespace Game.Tests
{
    // #445 (ADR-514/516): the race's phases, the course list, the grid on any course and where R puts a car back.
    public class RaceFlowTests
    {
        [Test]
        public void Flow_WaitsForEveryone_ThenCountsDown_ThenRaces_AndLocksInputUntilGo()
        {
            var f = new RaceFlow(100);
            Assert.AreEqual(RacePhase.Waiting, f.Tick(101, false, false));
            Assert.IsTrue(f.InputLocked);
            Assert.AreEqual(RacePhase.Countdown, f.Tick(102, true, false), "everyone is in: the countdown starts");
            Assert.AreEqual(RaceEvent.CountdownSeconds, f.CountdownLeft(102), 1e-3f);
            Assert.AreEqual(1.5f, f.CountdownLeft(102 + RaceEvent.CountdownSeconds - 1.5), 1e-3f);
            Assert.IsTrue(f.InputLocked, "no moving before GO");
            Assert.AreEqual(RacePhase.Racing, f.Tick(102 + RaceEvent.CountdownSeconds, true, false), "GO");
            Assert.IsFalse(f.InputLocked);
            Assert.AreEqual(102 + RaceEvent.CountdownSeconds, f.GoAt, 1e-6);
        }

        [Test]
        public void Flow_DoesNotWaitForeverForSomeoneLoading()
        {
            var f = new RaceFlow(0);
            Assert.AreEqual(RacePhase.Waiting, f.Tick(RaceEvent.WaitForLoadSeconds - 0.1, false, false));
            Assert.AreEqual(RacePhase.Countdown, f.Tick(RaceEvent.WaitForLoadSeconds, false, false));
        }

        [Test]
        public void Flow_FirstFinisher_GivesTheRestAWhile_ThenResults_ThenOver()
        {
            var f = new RaceFlow(0);
            f.Tick(0, true, false); f.Tick(RaceEvent.CountdownSeconds, true, false);
            Assert.AreEqual(RacePhase.Racing, f.Tick(50, true, false));
            f.FirstFinish(60);
            Assert.AreEqual(RacePhase.Finishing, f.Tick(60, true, false));
            Assert.AreEqual(RacePhase.Finishing, f.Tick(60 + RaceEvent.AfterWinnerSeconds - 1, true, false));
            Assert.AreEqual(RacePhase.Results, f.Tick(60 + RaceEvent.AfterWinnerSeconds, true, false), "time is up: the rest did not finish");
            Assert.AreEqual(RacePhase.Over, f.Tick(60 + RaceEvent.AfterWinnerSeconds + RaceEvent.ResultsSeconds, true, false));

            var g = new RaceFlow(0);
            g.Tick(0, true, false); g.Tick(RaceEvent.CountdownSeconds, true, false);
            g.FirstFinish(70);
            Assert.AreEqual(RacePhase.Results, g.Tick(75, true, true), "everyone is in: no need to wait");
        }

        [Test]
        public void Courses_HaveANameAndAScene_EachItsOwn()
        {
            Assert.GreaterOrEqual(RaceEvent.Courses.Length, 1);
            var scenes = new HashSet<string>();
            foreach (var c in RaceEvent.Courses)
            {
                Assert.IsFalse(string.IsNullOrEmpty(c.Name));
                Assert.IsTrue(c.Scene.StartsWith("Race_"), c.Scene);
                // One scene per course, but for courses on one road (#451: the pass both ways and its loop): this one starts on that road.
                if (!scenes.Add(c.Scene))
                {
                    var other = RaceEvent.ByScene(c.Scene).Course;
                    Assert.IsTrue(c.Course.Hilly && other.Hilly, c.Scene + ": only the pass's courses share a scene");
                    Assert.Less(other.RouteDistance(false, c.Course.LineX, c.Course.LineZ), 0.5f, c.Name + " starts on the scene's road");
                }
                Assert.GreaterOrEqual(c.Laps, 1);
                Assert.IsNotNull(c.Course);
            }
            Assert.AreSame(RaceEvent.Courses[0], RaceEvent.ByScene(RaceEvent.Courses[0].Scene));
            Assert.IsNull(RaceEvent.ByScene("World"));
        }

        [Test]
        public void Grid_OnAnyCourse_FollowsTheTrackBehindTheLine()
        {
            foreach (var info in RaceEvent.Courses)
            {
                var c = info.Course;
                var spots = c.GridSpots(RaceEvent.GridSlots);
                Assert.AreEqual(RaceEvent.GridSlots, spots.Count);
                for (int i = 0; i < spots.Count; i++)
                {
                    var (s, off) = c.Project(spots[i].x, spots[i].z);
                    Assert.Less(Math.Abs(off), c.Width * 0.5f - 1.2f, $"{info.Name} slot {i} on the tarmac");
                    float behind = c.Open ? -s : c.LapLength - s;   // an open course's grid is at negative s
                    Assert.That(behind, Is.InRange(0f, 120f), $"{info.Name} slot {i} behind the line");
                    if (i > 0) Assert.Greater(Math.Abs(s - c.Project(spots[i - 1].x, spots[i - 1].z).s), 3f, "staggered");
                }
            }
        }

        [Test]
        public void Reset_PutsTheCarOnTheNearestRoute_FacingTheWayOfTheRace()
        {
            var c = RaceEvent.Course;
            // Off to the side of the main straight (north-bound): back on its centre line, facing north (yaw 180).
            var (x, z, yaw) = c.ResetPose(9f, 0f);
            Assert.AreEqual(0f, x, 0.05f); Assert.AreEqual(0f, z, 0.05f);
            Assert.AreEqual(180f, Mathf360(yaw), 1f);
            // In the fork, near the inner cut: on the inner cut.
            var b = c.BranchPath[c.BranchPath.Count / 2];
            var r = c.ResetPose(b.X - 4f, b.Z);
            Assert.IsTrue(c.ProjectRoute(r.x, r.z).onBranch);
            Assert.AreEqual(0f, c.ProjectRoute(r.x, r.z).offset, 0.05f);
        }

        static float Mathf360(float a) { a %= 360f; return a < 0f ? a + 360f : a; }
    }
}

namespace Game.Tests
{
    // #446 prep (ADR-515): a spin backwards past a checkpoint costs the clean lap time, not the lap itself.
    public class RaceLapCountTests
    {
        [Test]
        public void ReversingAndGoingOn_StillCountsTheLap_ButNotItsTime()
        {
            var c = RaceEvent.Course; float L = c.LapLength;
            var lap = new RaceLap(c);
            lap.Step(L - 5f, 0); lap.Step(1f, 1);                                   // over the line: the race is on
            float cp2 = c.Checkpoints[2];
            lap.Step(c.Checkpoints[1] + 1f, 10); lap.Step(cp2 + 1f, 20);
            lap.Step(cp2 - 1f, 21);                                                 // spun back over checkpoint 2
            Assert.IsFalse(lap.Timing);
            float t = -1f;
            lap.Step(cp2 + 1f, 22);
            for (int k = 3; k < c.Sectors; k++) lap.Step(c.Checkpoints[k] + 1f, 20 + k * 10);
            t = lap.Step(1f, 200);
            Assert.AreEqual(1, lap.Laps, "the lap still counts for the race");
            Assert.AreEqual(-1f, t, 1e-3f, "but its time is not a clean lap");
            Assert.AreEqual(L + 1f, lap.Progress(1f), 0.01f, "one lap round");
        }

        [Test]
        public void BackOverTheLine_GoesBackALap_AndDoesNotCountItTwice()
        {
            var c = RaceEvent.Course; float L = c.LapLength;
            var lap = new RaceLap(c);
            lap.Step(L - 5f, 0); lap.Step(1f, 1);
            for (int k = 1; k < c.Sectors; k++) lap.Step(c.Checkpoints[k] + 1f, k * 10);
            lap.Step(1f, 200);
            Assert.AreEqual(1, lap.Laps);
            lap.Step(L - 2f, 201);                                                  // backwards over the line
            Assert.AreEqual(L - 2f, lap.Progress(L - 2f), 0.01f, "just short of one lap round");
            lap.Step(1f, 202);                                                      // and over it again
            Assert.AreEqual(1, lap.Laps, "not a second lap");
            Assert.AreEqual(L + 1f, lap.Progress(1f), 0.01f);
        }
    }
}

namespace Game.Tests
{
    // #447 (ADR-517): how a bot racer drives — look ahead along its route, steer at that point, slow for the corners coming up.
    public class RaceBotDriverTests
    {
        static RaceCourse C => RaceEvent.Course;

        [Test]
        public void OnTheMainStraight_FacingTheWay_ItGoesFlatOutAndStraight()
        {
            var (x, z, yaw) = C.PointAt(10f);
            var d = RaceBotDriver.Drive(C, x, z, yaw, 10f, 0.9f, false);
            Assert.AreEqual(1f, d.throttle, 1e-3f);
            Assert.AreEqual(0f, d.steer, 0.05f);
        }

        [Test]
        public void OffToTheLeft_ItSteersBackRight_AndOffToTheRight_Left()
        {
            var (x, z, yaw) = C.PointAt(30f);
            float rad = yaw * Mathf.Deg2Rad;
            float rx = Mathf.Cos(rad), rz = -Mathf.Sin(rad);    // right of the heading
            Assert.Greater(RaceBotDriver.Drive(C, x - rx * 4f, z - rz * 4f, yaw, 15f, 0.9f, false).steer, 0.05f, "left of the line: steer right");
            Assert.Less(RaceBotDriver.Drive(C, x + rx * 4f, z + rz * 4f, yaw, 15f, 0.9f, false).steer, -0.05f, "right of the line: steer left");
        }

        [Test]
        public void BeforeAHairpin_AtSpeed_ItBrakes()
        {
            // The end of the main straight runs into the first corner: at full speed close to it, brake.
            float s = 0f;
            for (; s < C.LapLength; s += 2f)
            {
                var (px, pz, _) = C.PointAt(s);
                var (_, off) = C.Project(px, pz);
                bool corner = false;
                foreach (var p in C.Path) if (p.Radius > 0f && p.Radius <= 20f && Mathf.Abs(p.X - px) < 1f && Mathf.Abs(p.Z - pz) < 1f) corner = true;
                if (corner) break;
            }
            var (x, z, yaw) = C.PointAt(s - 25f);
            Assert.Less(RaceBotDriver.Drive(C, x, z, yaw, 25f, 0.9f, false).throttle, 0f, "25 m before the first corner at 90 km/h: brake");
            Assert.Greater(RaceBotDriver.TargetSpeed(C, s - 25f, 0.95f), RaceBotDriver.TargetSpeed(C, s - 25f, 0.8f), "a better bot corners faster");
        }

        [Test]
        public void Skills_SpreadOverTheField_QuickAndSlow()
        {
            float lo = 1f, hi = 0f;
            for (int i = 0; i < 7; i++) { float k = RaceBotDriver.Skill(i, 7, 42); lo = Mathf.Min(lo, k); hi = Mathf.Max(hi, k); }
            Assert.GreaterOrEqual(lo, 0.8f); Assert.LessOrEqual(hi, 0.95f);
            Assert.Greater(hi - lo, 0.1f, "quick and slow bots in every race");
        }

        [Test]
        public void ABotTakingTheCut_SlowsForTheCutsCorner_NotTheArcs()
        {
            float apex = -1f; foreach (var p in C.BranchPath) if (p.Radius > 0f) { apex = p.S; break; }
            Assert.Greater(apex, 0f, "the cut has a corner");
            float s = apex - 5f;   // just before the V
            Assert.Less(RaceBotDriver.TargetSpeed(C, s, 0.9f, true), RaceBotDriver.TargetSpeed(C, s, 0.9f, false) - 1f, "the V's apex is tighter than the arc");
        }

        [Test]
        public void InTheFork_ABotChoosingTheCut_AimsAtTheCut()
        {
            var b = C.BranchPath[2];
            var (s, _) = C.Project(b.X, b.Z);
            var (x, z, yaw) = C.PointAt(C.ForkS - 6f);
            var inner = RaceBotDriver.Aim(C, x, z, C.ForkS - 6f, 15f, true);
            var outer = RaceBotDriver.Aim(C, x, z, C.ForkS - 6f, 15f, false);
            Assert.IsTrue(C.ProjectRoute(inner.x, inner.z).onBranch, "the cut");
            Assert.IsFalse(C.ProjectRoute(outer.x, outer.z).onBranch, "the arc");
        }
    }
}

namespace Game.Tests
{
    // #448 (ADR-512): a lobby's start message can send the party to an event race course.
    public class RaceLobbyEventTests
    {
        [Test]
        public void TheStartMessagesEvent_NamesTheCourseScene_OrNothing()
        {
            Assert.IsNull(RaceEvent.SceneForLobby(0), "0 = the battle royale");
            Assert.AreEqual(RaceEvent.Courses[0].Scene, RaceEvent.SceneForLobby(1));
            Assert.IsNull(RaceEvent.SceneForLobby((byte)(RaceEvent.Courses.Length + 1)), "an unknown course: the battle royale");
        }
    }
}

namespace Game.Tests
{
    // #510 (ADR-562): gaps to the leader from the race times at fixed marks of progress.
    public class RaceGapTests
    {
        // A racer driving at a steady `speed` from the line (progress 0 at t = start), sampled every 0.1 s up to `until`.
        static RaceSplits Steady(float speed, float start, float until)
        {
            var s = new RaceSplits();
            for (float t = 0f; t <= until; t += 0.1f) s.Record((t - start) * speed - 0.01f, t);   // -0.01: on the grid just behind the line
            return s;
        }

        [Test]
        public void TheLeaderIsZero_TheRestTheirTimeBehindAtTheSameMark()
        {
            var lead = Steady(25f, 0f, 30f);
            var second = Steady(25f, 1.5f, 30f);   // the same pace, 1.5 s later
            var third = Steady(20f, 0f, 30f);      // slower: after 600 m it is 6 s behind
            var g = RaceSplits.Gaps(new[] { lead, second, third }, new[] { false, false, false }, new[] { 0f, 0f, 0f });
            Assert.AreEqual(0f, g[0]);
            Assert.AreEqual(1.5f, g[1], 0.11f);
            int k = third.Marks - 1;   // its furthest mark: k * 50 m at 20 vs 25 m/s
            Assert.AreEqual(k * RaceSplits.MarkMeters / 20f - k * RaceSplits.MarkMeters / 25f, g[2], 0.21f);
            Assert.Greater(g[2], 5f, "about 6 s behind after 600 m");
        }

        [Test]
        public void FinishedCars_AreByFinishTime_AndANotYetMovingCarIsUnknown()
        {
            var a = Steady(25f, 0f, 30f); var b = Steady(25f, 2f, 30f);
            var still = new RaceSplits(); still.Record(-30f, 5f);   // still on the grid: no mark
            var g = RaceSplits.Gaps(new[] { a, b, still }, new[] { true, true, false }, new[] { 100f, 103.25f, 0f });
            Assert.AreEqual(3.25f, g[1], 1e-4f, "by finish times");
            Assert.AreEqual(-1f, g[2], "no mark yet");
        }

        [Test]
        public void AJumpOverSeveralMarks_GivesThemAllThatTime_AndNoGapIsNegative()
        {
            var s = new RaceSplits();
            s.Record(0f, 1f); s.Record(160f, 2f);
            Assert.AreEqual(Mathf.FloorToInt(160f / RaceSplits.MarkMeters) + 1, s.Marks, "every mark from 0 to 160 m");
            Assert.AreEqual(2f, s.At(s.Marks - 1));
            var behind = new RaceSplits(); behind.Record(0f, 0.5f);   // ordered behind but reached the line first
            var g = RaceSplits.Gaps(new[] { s, behind }, new[] { false, false }, new[] { 0f, 0f });
            Assert.AreEqual(0f, g[1]);
        }

        [Test]
        public void AGap_IsNeverLessThanTheCarAheads()
        {
            // Third in the order (further round now) but its last mark was better than second's: shown level with second, not ahead.
            var lead = Steady(25f, 0f, 30f);
            var second = Steady(25f, 2f, 30f);
            var third = Steady(25f, 1f, 28f);
            var g = RaceSplits.Gaps(new[] { lead, second, third }, new[] { false, false, false }, new[] { 0f, 0f, 0f });
            Assert.AreEqual(g[1], g[2], 1e-4f);
            Assert.Greater(g[1], 1.5f);
        }
    }
}
