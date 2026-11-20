using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #423 race on the waiting island (ADR-505..): the circuit beside the island, who may stand where, the lap judge and the room's records.
    public class RaceTrackTests
    {
        static float Wx(float lx) => WaitingIsland.WorldX(lx);
        static float Wz(float lz) => WaitingIsland.WorldZ(lz);

        [Test]
        public void Circuit_IsClosed_AboutHalfAKilometre_AndAboutHalfAMinuteALap()
        {
            var path = RaceTrack.Path;
            float sum = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                var a = path[i]; var b = path[(i + 1) % path.Count];
                float d = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
                Assert.LessOrEqual(d, 10.01f, $"samples {i}..{i + 1} are close together");
                sum += d;
            }
            Assert.AreEqual(RaceTrack.LapLength, sum, 0.05f, "the loop closes on itself");
            Assert.That(RaceTrack.LapLength, Is.InRange(450f, 650f));
            // A rough lap: every sample at the lower of the 90 km/h top speed and the corner's grip limit (v = sqrt(mu g r), car grip 1.05).
            float t = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                var a = path[i]; var b = path[(i + 1) % path.Count];
                float v = a.Radius > 0f ? Math.Min(25f, (float)Math.Sqrt(1.05 * 9.81 * a.Radius)) : 25f;
                t += (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z)) / v;
            }
            Assert.That(t, Is.InRange(25f, 45f), "about half a minute: two or three laps fit the 90 s wait");
            Assert.Less(RaceLap.MinLapSeconds, RaceTrack.LapLength / 29f, "a real flat-out lap is never refused");
            foreach (var c in RaceTrack.Corners) Assert.GreaterOrEqual(c.r, 12f, "no corner tighter than a hairpin of 12 m");
        }

        [Test]
        public void Sections_StayApart_AndInsideTheHall_AwayFromTheIsland()
        {
            var path = RaceTrack.Path;
            float L = RaceTrack.LapLength, edge = RaceTrack.Width * 0.5f + RaceTrack.Runoff, min = float.MaxValue;
            for (int i = 0; i < path.Count; i++)
            {
                var a = path[i];
                Assert.That(a.X, Is.InRange(RaceTrack.HallX0 + edge + 1f, RaceTrack.HallX1 - edge - 1f), $"sample {i} inside the hall (x)");
                Assert.That(a.Z, Is.InRange(RaceTrack.HallZ0 + edge + 1f, RaceTrack.HallZ1 - edge - 1f), $"sample {i} inside the hall (z)");
                Assert.Greater(Math.Sqrt(a.X * a.X + a.Z * a.Z), WaitingIsland.Radius + edge, $"sample {i} clear of the island");
                for (int j = i + 1; j < path.Count; j++)
                {
                    var b = path[j];
                    float ds = Math.Abs(a.S - b.S); ds = Math.Min(ds, L - ds);
                    if (ds < 70f) continue;
                    min = Math.Min(min, (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z)));
                }
            }
            Assert.GreaterOrEqual(min, 2f * edge + 2f, "two sections never share run-off or a barrier");
            Assert.GreaterOrEqual(RaceTrack.GapToMap(2400f), 1400f, "past the far clip twice over (ADR-381)");
            Assert.IsTrue(RaceTrack.InArea(Wx(RaceTrack.MainX), Wz(RaceTrack.LineZ)), "the line is in the race area");
            Assert.IsTrue(RaceTrack.InArea(Wx(44f), Wz(-14f)), "the pit is in the race area");
            Assert.IsFalse(RaceTrack.InArea(Wx(0f), Wz(0f)), "the island's middle is not");
            Assert.IsFalse(RaceTrack.InArea(Wx(30f), Wz(-14f)), "the gate west of the bollards is island, not race hall");
            Assert.IsFalse(RaceTrack.InArea(1200f, 1200f), "the map is not");
        }

        [Test]
        public void Project_GivesDistanceAlongTheLapFromTheLine_AndTheSideOffset()
        {
            var (s0, off0) = RaceTrack.Project(RaceTrack.MainX, RaceTrack.LineZ);
            Assert.AreEqual(0f, s0, 0.01f, "the line is s = 0");
            Assert.AreEqual(0f, off0, 0.01f);
            var (s1, _) = RaceTrack.Project(RaceTrack.MainX, RaceTrack.LineZ - 10f);
            Assert.AreEqual(10f, s1, 0.01f, "the race runs north on the main straight");
            var (s2, off2) = RaceTrack.Project(RaceTrack.MainX + 3f, RaceTrack.LineZ + 5f);
            Assert.AreEqual(RaceTrack.LapLength - 5f, s2, 0.01f, "just before the line");
            Assert.AreEqual(3f, Math.Abs(off2), 0.01f);
            Assert.IsTrue(RaceTrack.OnTrack(RaceTrack.MainX + 5.9f, 0f));
            Assert.IsFalse(RaceTrack.OnTrack(RaceTrack.MainX + 6.1f, 0f));
            Assert.IsFalse(RaceTrack.OnTrack(RaceTrack.PitX1 - 0.5f, -14f), "the pit is not track");
            Assert.IsFalse(RaceTrack.OnTrack(120f, -20f), "the infield is not track");
            // Every sample is on the track, and the distance grows along the samples (one way round).
            var path = RaceTrack.Path;
            for (int i = 0; i < path.Count; i++)
            {
                var (s, off) = RaceTrack.Project(path[i].X, path[i].Z);
                Assert.AreEqual(0f, off, 0.05f);
                Assert.AreEqual(path[i].S, s, 0.05f);
            }
        }

        [Test]
        public void Walker_StaysOnTheIslandOrInThePit_NeverOnTheTrack()
        {
            float x = Wx(0f), z = Wz(0f);
            RaceTrack.ClampWalker(ref x, ref z);
            Assert.AreEqual(Wx(0f), x, 1e-3f, "the island's middle is fine");
            x = Wx(44f); z = Wz(-14f);
            RaceTrack.ClampWalker(ref x, ref z);
            Assert.AreEqual(Wx(44f), x, 1e-3f, "the pit is fine");
            Assert.AreEqual(Wz(-14f), z, 1e-3f);
            // on the track beside the pit: back to the pit's edge
            x = Wx(RaceTrack.MainX); z = Wz(-14f);
            RaceTrack.ClampWalker(ref x, ref z);
            Assert.LessOrEqual(x - WaitingIsland.CentreX, RaceTrack.PitX1, "out of the track, into the pit");
            Assert.IsFalse(RaceTrack.OnTrack(x - WaitingIsland.CentreX, z - WaitingIsland.CentreZ));
            // far down the east straight: to the nearest walkable place, off the track
            x = Wx(180f); z = Wz(0f);
            RaceTrack.ClampWalker(ref x, ref z);
            Assert.IsTrue(RaceTrack.Walkable(x - WaitingIsland.CentreX, z - WaitingIsland.CentreZ), $"walkable at {x - WaitingIsland.CentreX}, {z - WaitingIsland.CentreZ}");
            // outside the island on the far side: back onto the island circle, as before
            x = Wx(-70f); z = Wz(0f);
            RaceTrack.ClampWalker(ref x, ref z);
            Assert.AreEqual(Wx(-WaitingIsland.WalkRadius), x, 1e-3f);
        }

        [Test]
        public void GridSpots_AreInThePit_FacingTheTrack_ApartFromEachOther()
        {
            var spots = RaceTrack.GridSpots();
            Assert.AreEqual(RaceTrack.Cars, spots.Count);
            for (int i = 0; i < spots.Count; i++)
            {
                var (lx, lz, yaw) = spots[i];
                Assert.IsTrue(RaceTrack.InPit(lx, lz, 2.5f), $"spot {i} inside the pit with room for the car: {lx}, {lz}");
                Assert.AreEqual(90f, yaw, 1e-3f, "facing east, out to the track");
                for (int j = 0; j < i; j++)
                {
                    // a car is about 4.5 x 1.9 m: the footprints keep a 0.6 m gap
                    float dx = lx - spots[j].x, dz = lz - spots[j].z;
                    Assert.IsTrue(Math.Abs(dx) > 5.1f || Math.Abs(dz) > 2.5f, $"spots {i} and {j} apart");
                }
            }
        }

        // ---- lap judge ----

        static float Run(RaceLap lap, float fromS, float toS, double t0, double t1, int steps = 400)
        {
            float result = -1f;
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps;
                float s = fromS + (toS - fromS) * u;
                s = ((s % RaceTrack.LapLength) + RaceTrack.LapLength) % RaceTrack.LapLength;
                float r = lap.Step(s, t0 + (t1 - t0) * u);
                if (r > 0f) result = r;
            }
            return result;
        }

        [Test]
        public void Lap_StartsAtTheLine_EndsAtTheLineAfterAllFourSectors()
        {
            var lap = new RaceLap();
            float L = RaceTrack.LapLength;
            Assert.AreEqual(-1f, Run(lap, L - 15f, L - 1f, 0, 1), 1e-3f, "out of the pit, before the line: nothing yet");
            Assert.IsFalse(lap.Timing);
            Assert.AreEqual(-1f, Run(lap, L - 1f, L + 2f, 1, 1.2), 1e-3f, "crossing the line starts the clock");
            Assert.IsTrue(lap.Timing);
            float t = Run(lap, 2f, L + 2f, 1.2, 21.1);
            Assert.AreEqual(20f, t, 0.1f, "a full lap: the time from line to line");
            Assert.IsTrue(lap.Timing, "the next lap runs on");
        }

        [Test]
        public void Lap_Reversing_OrJumping_DoesNotCount()
        {
            float L = RaceTrack.LapLength;
            var lap = new RaceLap();
            Run(lap, L - 5f, L + 5f, 0, 1);
            Run(lap, 5f, L * 0.4f, 1, 8);
            Assert.IsTrue(lap.Timing, "the lap is running");
            Run(lap, L * 0.4f, L * 0.2f, 8, 10);          // back the wrong way
            Assert.IsFalse(lap.Timing, "reversing past a sector stops the clock");
            Assert.AreEqual(-1f, Run(lap, L * 0.2f, L + 1f, 10, 25), 1e-3f, "and that lap never counts");

            var jump = new RaceLap();
            Run(jump, L - 5f, L + 5f, 0, 1);
            Assert.IsTrue(jump.Timing);
            jump.Step(L * 0.6f, 2);                        // a teleport across two sectors
            Assert.IsFalse(jump.Timing);
        }

        [Test]
        public void Lap_TooFastToBeReal_IsRefused()
        {
            float L = RaceTrack.LapLength;
            var lap = new RaceLap();
            Run(lap, L - 5f, L + 5f, 0, 0.1);
            Assert.AreEqual(-1f, Run(lap, 5f, L + 5f, 0.1, 0.1 + RaceLap.MinLapSeconds - 1f), 1e-3f);
        }

        // ---- records and the board ----

        [Test]
        public void Records_KeepEachPersonsBest_TopThreeFastestFirst_TiesByWhoSetItFirst()
        {
            var r = new RaceRecords();
            Assert.AreEqual(0, r.Top(3).Count, "empty");
            Assert.IsTrue(r.Submit(1, "Ann", 21f));
            Assert.IsFalse(r.Submit(1, "Ann", 22f), "slower than her best");
            Assert.IsTrue(r.Submit(1, "Ann", 19.5f));
            r.Submit(2, "Bo", 20f); r.Submit(3, "Cy", 20f); r.Submit(4, "Di", 25f);
            var top = r.Top(3);
            Assert.AreEqual(3, top.Count);
            Assert.AreEqual("Ann", top[0].Name); Assert.AreEqual(19.5f, top[0].Seconds, 1e-4f);
            Assert.AreEqual("Bo", top[1].Name, "a tie goes to who set it first");
            Assert.AreEqual("Cy", top[2].Name);
            Assert.AreEqual(19.5f, r.BestOf(1), 1e-4f);
            Assert.AreEqual(0f, r.BestOf(9), 1e-4f, "no time yet");
        }

        [Test]
        public void Board_ShowsTheLapPage_EveryOtherPeriod_OnlyWithRecords()
        {
            Assert.IsFalse(RaceBoard.ShowLapPage(7.0, false));
            Assert.IsFalse(RaceBoard.ShowLapPage(1.0, true), "the first period is the usual page");
            Assert.IsTrue(RaceBoard.ShowLapPage(RaceBoard.PageSeconds + 1.0, true));
            Assert.IsFalse(RaceBoard.ShowLapPage(RaceBoard.PageSeconds * 2 + 1.0, true));
            Assert.AreEqual("1  Ann  19.50\n2  Bo  20.00", RaceBoard.Lines(new[] { ("Ann", 19.5f), ("Bo", 20f), ("", 0f) }));
            Assert.AreEqual("1:05.30", RaceBoard.Time(65.3f));
        }
    }
}
