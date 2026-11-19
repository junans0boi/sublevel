using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #419 (ADR-386): the bungee curve and the one-at-a-time line.
    public class BungeeTests
    {
        [Test] public void StartsAtTheTop_EndsAtThePad()
        {
            Assert.AreEqual(BungeeCurve.Top, BungeeCurve.Height(0f), 1e-3f);
            Assert.AreEqual(BungeeCurve.Top, BungeeCurve.Height(BungeeCurve.HoldSeconds * 0.5f), 1e-3f, "a beat on the board");
            Assert.AreEqual(0f, BungeeCurve.Height(BungeeCurve.Duration + 1f), 1e-3f);
            Assert.AreEqual(0f, BungeeCurve.Height(BungeeCurve.Duration - 0.001f), 0.05f, "the lowering ends on the pad");
        }

        [Test] public void FallsFreelyForTheRope_ThenBouncesAndNeverHitsTheFloor()
        {
            float tFall = BungeeCurve.HoldSeconds;
            Assert.AreEqual(BungeeCurve.Top - BungeeCurve.Rope, BungeeCurve.Height(tFall + BungeeCurve.FallSeconds), 0.05f);
            Assert.Greater(BungeeCurve.Bottom, 1f, "the lowest point clears the pad: nobody is hurt, nothing is touched");
            Assert.Less(BungeeCurve.Bottom, BungeeCurve.Top - BungeeCurve.Rope, "the rope stretches past its length");
        }

        [Test] public void ReboundApex_IsAboveTheBottom_AndBelowTheStart_AndNotFloatingAway()
        {
            Assert.Greater(BungeeCurve.ReboundApex, BungeeCurve.Bottom + 3f);
            Assert.Less(BungeeCurve.ReboundApex, BungeeCurve.Top);
            Assert.Greater(BungeeCurve.ReboundApex, BungeeCurve.Top - BungeeCurve.Rope - 8f, "it springs back most of the way to where the rope began to pull");
        }

        [Test] public void BounceDiesOut()
        {
            float t3 = BungeeCurve.HoldSeconds + BungeeCurve.FallSeconds;
            float swingEarly = System.Math.Abs(BungeeCurve.Height(t3 + 3f) - BungeeCurve.RestHeight);
            float swingLate = System.Math.Abs(BungeeCurve.Height(t3 + BungeeCurve.BounceSeconds - 0.01f) - BungeeCurve.RestHeight);
            Assert.Less(swingLate, swingEarly + 1e-3f);
            Assert.LessOrEqual(swingLate, BungeeCurve.SettleBelow + 0.05f);
        }

        [Test] public void DurationIsUnderTwentyFiveSeconds() { Assert.Greater(BungeeCurve.Duration, 12f); Assert.LessOrEqual(BungeeCurve.Duration, 25f); }

        // Juhwan: "too low". The platform is about 60 m up, the free fall about 35 m, and the lowest point 3..5 m above the pad.
        [Test] public void HighJump_SixtyMetresUp_LowestPointThreeToFiveMetres()
        {
            Assert.GreaterOrEqual(BungeeCurve.Top, 60f);
            Assert.GreaterOrEqual(BungeeCurve.Rope, 35f);
            Assert.That(BungeeCurve.Bottom, Is.InRange(3f, 5f));
        }

        [Test] public void OneAtATime_ThenACooldown()
        {
            var line = new BungeeLine();
            Assert.AreEqual(BungeeLine.Result.Started, line.TryStart(1, 0.0));
            Assert.AreEqual(BungeeLine.Result.Busy, line.TryStart(2, 1.0));
            Assert.IsFalse(line.Tick(5.0));
            Assert.IsTrue(line.Tick(BungeeCurve.Duration + 0.1));
            double end = BungeeCurve.Duration + 0.1;
            Assert.AreEqual(BungeeLine.Result.Cooling, line.TryStart(2, end + 1.0));
            Assert.AreEqual(BungeeLine.Result.Started, line.TryStart(2, end + BungeeLine.CooldownSeconds + 0.01));
            Assert.Greater(line.Best, 0f, "the rebound of the first jump is recorded");
        }

        [Test] public void Leaver_FreesTheLine_WithoutARecord()
        {
            var line = new BungeeLine();
            line.TryStart(1, 0.0);
            line.Abort(1, 3.0);
            Assert.IsFalse(line.Active);
            Assert.AreEqual(0f, line.Best);
            Assert.AreEqual(BungeeLine.Result.Cooling, line.TryStart(2, 4.0));
        }

        // ---- the lift to the top (ADR-387) ----

        [Test] public void Lift_IsAFastExpress_SixtyMetresInAboutFiveSeconds()
        {
            Assert.AreEqual(BungeeCurve.Top, BungeeLift.Height);
            Assert.That(BungeeLift.TravelSeconds, Is.InRange(4f, 5f));
            Assert.AreEqual(0f, BungeeLift.Travelled(0f), 1e-4f);
            Assert.AreEqual(BungeeLift.Height, BungeeLift.Travelled(BungeeLift.TravelSeconds), 1e-3f);
            float top = 0f, prevH = 0f, prevV = 0f;
            for (float t = 0f; t <= BungeeLift.TravelSeconds; t += 0.01f)
            {
                float h = BungeeLift.Travelled(t), v = BungeeLift.Speed(t);
                Assert.GreaterOrEqual(h, prevH - 1e-4f, "never goes back");
                Assert.Less(System.Math.Abs(v - prevV), 0.4f, "no jolt: the speed changes smoothly (S-curve)");
                if (t > 0.02f) Assert.AreEqual(v, (h - prevH) / 0.01f, 0.35f, "the speed is the slope of the height");
                top = System.Math.Max(top, v); prevH = h; prevV = v;
            }
            Assert.AreEqual(BungeeLift.CruiseSpeed, top, 0.01f);
            Assert.AreEqual(0f, BungeeLift.Speed(0f)); Assert.AreEqual(0f, BungeeLift.Speed(BungeeLift.TravelSeconds));
        }

        [Test] public void Lift_Cycle_OpenBottom_Up_OpenTop_Down()
        {
            Assert.AreEqual(LiftPhase.OpenBottom, BungeeLift.Phase(0.1, out _));
            Assert.IsTrue(BungeeLift.DoorsOpen(0.1));
            Assert.AreEqual(LiftPhase.Rising, BungeeLift.Phase(BungeeLift.DoorSeconds + 0.1, out _));
            Assert.IsFalse(BungeeLift.DoorsOpen(BungeeLift.DoorSeconds + 0.1), "doors shut while moving");
            double top = BungeeLift.DoorSeconds + BungeeLift.TravelSeconds + 0.1;
            Assert.AreEqual(LiftPhase.OpenTop, BungeeLift.Phase(top, out _));
            Assert.AreEqual(BungeeLift.Height, BungeeLift.CarHeight(top), 1e-3f);
            Assert.AreEqual(LiftPhase.Descending, BungeeLift.Phase(top + BungeeLift.DoorSeconds, out _));
            Assert.AreEqual(0f, BungeeLift.CarHeight(BungeeLift.Cycle + 0.1), 1e-3f, "and round again");
            Assert.AreEqual(BungeeLift.Height * 0.5f, BungeeLift.CarHeight(BungeeLift.DoorSeconds + BungeeLift.TravelSeconds * 0.5), 0.01f);
        }

        [Test] public void Lift_TakesAtMostFour_FirstComeFirstServed()
        {
            var riders = BungeeLift.Board(new[] { 5, 6, 7, 8, 9, 10 });
            CollectionAssert.AreEqual(new[] { 5, 6, 7, 8 }, riders);
            Assert.AreEqual(2, BungeeLift.Board(new[] { 1, 2 }).Count);
        }

        [Test] public void Falling_FromTheTop_IsSentBackToTheDeck_ButNotStandingOnTheTopFloor()
        {
            Assert.IsTrue(BungeeLift.FellOff(30f, false));
            Assert.IsFalse(BungeeLift.FellOff(60f, true), "on the platform");
            Assert.IsFalse(BungeeLift.FellOff(1.2f, false), "a hop on the deck");
        }

        [Test] public void TopFloor_IsThePlatform_TheBridgeAndTheLanding_NotTheAirBeside_AndTheJumpBoardIsOverThePad()
        {
            var t = WaitingIsland.Tower; var l = WaitingIsland.Lift; var pad = WaitingIsland.BungeePad;
            Assert.IsTrue(WaitingIsland.OnBungeeTop(t.x, t.z));
            Assert.IsTrue(WaitingIsland.OnBungeeTop(l.x, l.z));
            Assert.IsTrue(WaitingIsland.OnBungeeTop(pad.x + 1f, pad.z), "the board end of the bridge");
            Assert.IsFalse(WaitingIsland.OnBungeeTop(t.x, t.z + 15f));
            Assert.IsFalse(WaitingIsland.OnBungeeTop(pad.x - 3f, pad.z), "past the board is air");
            Assert.IsTrue(WaitingIsland.Inside(WaitingIsland.WorldX(pad.x), WaitingIsland.WorldZ(pad.z), 6f), "the landing pad is well inside the island");
        }
    }
}
