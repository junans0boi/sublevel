using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #419 (ADR-386): the bungee curve and the one-at-a-time line.
    public class BungeeTests
    {
        [Test] public void StartsAtThePad_EndsAtThePad()
        {
            Assert.AreEqual(0f, BungeeCurve.Height(0f), 1e-3f);
            Assert.AreEqual(0f, BungeeCurve.Height(BungeeCurve.Duration + 1f), 1e-3f);
            Assert.AreEqual(0f, BungeeCurve.Height(BungeeCurve.Duration - 0.001f), 0.05f, "the lowering ends on the pad");
        }

        [Test] public void RisesToThePlatform_AndWaitsThere()
        {
            Assert.AreEqual(BungeeCurve.Top, BungeeCurve.Height(BungeeCurve.AscentSeconds), 1e-3f);
            Assert.AreEqual(BungeeCurve.Top, BungeeCurve.Height(BungeeCurve.AscentSeconds + BungeeCurve.HoldSeconds * 0.5f), 1e-3f);
            float prev = 0f;
            for (float t = 0f; t <= BungeeCurve.AscentSeconds; t += 0.1f) { float h = BungeeCurve.Height(t); Assert.GreaterOrEqual(h, prev - 1e-4f); prev = h; }
        }

        [Test] public void FallsFreelyForTheRope_ThenBouncesAndNeverHitsTheFloor()
        {
            float tFall = BungeeCurve.AscentSeconds + BungeeCurve.HoldSeconds;
            Assert.AreEqual(BungeeCurve.Top - BungeeCurve.Rope, BungeeCurve.Height(tFall + BungeeCurve.FallSeconds), 0.05f);
            Assert.Greater(BungeeCurve.Bottom, 1f, "the lowest point clears the pad: nobody is hurt, nothing is touched");
            Assert.Less(BungeeCurve.Bottom, BungeeCurve.Top - BungeeCurve.Rope, "the rope stretches past its length");
        }

        [Test] public void ReboundApex_IsAboveTheBottom_AndBelowTheStart_AndNotFloatingAway()
        {
            Assert.Greater(BungeeCurve.ReboundApex, BungeeCurve.Bottom + 3f);
            Assert.Less(BungeeCurve.ReboundApex, BungeeCurve.Top);
            Assert.Greater(BungeeCurve.ReboundApex, BungeeCurve.Top - BungeeCurve.Rope - 3f, "it springs back about to where the rope began to pull");
        }

        [Test] public void BounceDiesOut()
        {
            float t3 = BungeeCurve.AscentSeconds + BungeeCurve.HoldSeconds + BungeeCurve.FallSeconds;
            float swingEarly = System.Math.Abs(BungeeCurve.Height(t3 + 3f) - BungeeCurve.RestHeight);
            float swingLate = System.Math.Abs(BungeeCurve.Height(t3 + BungeeCurve.BounceSeconds - 0.01f) - BungeeCurve.RestHeight);
            Assert.Less(swingLate, swingEarly + 1e-3f);
            Assert.LessOrEqual(swingLate, BungeeCurve.SettleBelow + 0.05f);
        }

        [Test] public void DurationIsAboutFifteenSeconds() { Assert.Greater(BungeeCurve.Duration, 10f); Assert.Less(BungeeCurve.Duration, 25f); }

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
    }
}
