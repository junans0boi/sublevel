using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-498 (#419, 준환 r5.2 "박진감이 필요함 · 줄도 안 달려 있음 · 내려오는 법이 안 정해짐"): the hard catch, the slow motion of the
    // jumper's own clock, the hang and the way down (F or on its own), the head-down pose and the cord.
    public class BungeeFeelTests
    {
        const float G = BungeeCurve.Gravity;

        [Test] public void Catch_IsTheHardestMoment_AboutTwoG_AtTheLowestPoint()
        {
            float hardest = 0f, at = 0f;
            for (float t = BungeeCurve.HoldSeconds; t < BungeeCurve.ReadyAt; t += 0.01f)
            {
                float a = BungeeCurve.Acceleration(t);
                if (a > hardest) { hardest = a; at = t; }
            }
            Assert.That(hardest / G, Is.InRange(1.8f, 3f), "a real catch, not a soft sag (was 1.3 g)");
            Assert.That(at, Is.InRange(BungeeCurve.TautAt, BungeeCurve.SnapAt), "the cord pulls hardest on the way down to the lowest point");
            Assert.Greater(BungeeCurve.Acceleration(BungeeCurve.SnapAt) / G, 1.8f, "still pulling about 2 g as it turns them round (the yank)");
            Assert.Greater(BungeeCurve.Acceleration(BungeeCurve.TautAt + 0.05f) / G, 1f, "the cord bites at once");
            Assert.AreEqual(BungeeCurve.Bottom, BungeeCurve.Height(BungeeCurve.SnapAt), 0.01f);
            Assert.Less(BungeeCurve.SnapAt - BungeeCurve.TautAt, 1.3f, "the rope bites quickly once it is taut");
            Assert.Greater(-BungeeCurve.Velocity(BungeeCurve.TautAt - 0.01f), 26f, "about 100 km/h when the rope starts to pull");
        }

        [Test] public void Phases_Board_Fall_Bounce_Hang_Lower_Done()
        {
            Assert.AreEqual(BungeePhase.Board, BungeeCurve.PhaseAt(0.1f));
            Assert.AreEqual(BungeePhase.Fall, BungeeCurve.PhaseAt(BungeeCurve.HoldSeconds + 0.5f));
            Assert.AreEqual(BungeePhase.Bounce, BungeeCurve.PhaseAt(BungeeCurve.SnapAt));
            Assert.AreEqual(BungeePhase.Hang, BungeeCurve.PhaseAt(BungeeCurve.ReadyAt + 0.1f));
            Assert.AreEqual(BungeePhase.Lower, BungeeCurve.PhaseAt(BungeeCurve.AutoLowerAt + 0.1f), "nobody pressed F: lowered on its own");
            Assert.AreEqual(BungeePhase.Done, BungeeCurve.PhaseAt(BungeeCurve.Duration + 0.01f));
            Assert.That(BungeeCurve.HangSeconds, Is.InRange(3f, 10f));
        }

        [Test] public void F_LowersFromWhereTheyHang_WithoutAJump_AndEndsOnThePad()
        {
            float press = BungeeCurve.ReadyAt + 1.3f;
            Assert.AreEqual(BungeePhase.Lower, BungeeCurve.PhaseAt(press + 0.01f, press));
            Assert.AreEqual(BungeeCurve.Height(press), BungeeCurve.Height(press, press), 1e-3f, "the lowering starts where the swing was");
            Assert.AreEqual(BungeeCurve.Height(press - 0.01f, press), BungeeCurve.Height(press + 0.01f, press), 0.1f, "no jump in the height");
            Assert.AreEqual(press + BungeeCurve.LowerSeconds, BungeeCurve.End(press), 1e-3f);
            Assert.Less(BungeeCurve.End(press), BungeeCurve.Duration, "pressing F is quicker than waiting");
            Assert.AreEqual(0f, BungeeCurve.Height(BungeeCurve.End(press) + 0.01f, press), 1e-3f);
            float prev = float.MaxValue;
            for (float t = press; t <= BungeeCurve.End(press); t += 0.05f)
            {
                float h = BungeeCurve.Height(t, press);
                Assert.LessOrEqual(h, prev + 1e-3f, "lowered steadily, never pulled back up");
                Assert.Less(-BungeeCurve.Velocity(t, press), 8f, "gently");
                prev = h;
            }
        }

        [Test] public void NeverStuckHanging_AnyPressOrNone_EndsOnThePadInTime()
        {
            foreach (float lowerAt in new[] { 0f, BungeeCurve.SnapAt, BungeeCurve.ReadyAt, BungeeCurve.ReadyAt + 3f, BungeeCurve.AutoLowerAt + 5f, float.PositiveInfinity })
            {
                Assert.LessOrEqual(BungeeCurve.End(lowerAt), BungeeCurve.Duration + 1e-3f, "lowerAt " + lowerAt);
                Assert.GreaterOrEqual(BungeeCurve.LowerStart(lowerAt), BungeeCurve.ReadyAt, "not lowered mid-bounce: " + lowerAt);
                Assert.AreEqual(0f, BungeeCurve.Height(BungeeCurve.End(lowerAt), lowerAt), 1e-3f);
                for (float t = BungeeCurve.HoldSeconds; t < BungeeCurve.End(lowerAt) - 0.6f; t += 0.05f)
                    Assert.Greater(BungeeCurve.Height(t, lowerAt), 1f, "never near the pad until the last of the lowering: " + t);
            }
        }

        [Test] public void Line_TakesFOnlyFromTheJumper_Once_AndEndsWhenTheLoweringEnds()
        {
            var line = new BungeeLine();
            line.TryStart(7, 100.0);
            Assert.IsFalse(line.RequestLower(8, 100.0 + BungeeCurve.ReadyAt + 1.0), "someone else cannot lower the jumper");
            Assert.IsTrue(line.RequestLower(7, 100.0 + BungeeCurve.ReadyAt + 1.0));
            Assert.AreEqual(BungeeCurve.ReadyAt + 1f, line.LowerAt, 1e-3f);
            Assert.IsFalse(line.RequestLower(7, 100.0 + BungeeCurve.ReadyAt + 2.0), "the first press counts");
            double end = 100.0 + BungeeCurve.End(line.LowerAt);
            Assert.IsFalse(line.Tick(end - 0.05));
            Assert.IsTrue(line.Tick(end + 0.01));
            Assert.IsFalse(line.Active);
        }

        [Test] public void Line_AnEarlyF_WaitsUntilTheBounceIsOver()
        {
            var line = new BungeeLine();
            line.TryStart(1, 0.0);
            Assert.IsTrue(line.RequestLower(1, BungeeCurve.SnapAt));
            Assert.AreEqual(BungeeCurve.ReadyAt, line.LowerAt, 1e-3f);
            line.Tick(BungeeCurve.End(line.LowerAt) + 0.01);
            line.TryStart(2, 1000.0);
            Assert.IsTrue(float.IsPositiveInfinity(line.LowerAt), "the next jump starts without a request");
        }

        [Test] public void SlowMotion_OnlyAroundTheCatch_NeverBackwards_BackInStepBeforeTheHang()
        {
            Assert.AreEqual(BungeeCurve.TautAt - 0.5f, BungeeCurve.FeelTime(BungeeCurve.TautAt - 0.5f), 1e-4f, "the fall itself is real time");
            Assert.AreEqual(BungeeCurve.ReadyAt, BungeeCurve.FeelTime(BungeeCurve.ReadyAt), 1e-4f);
            float slowest = 1f, fastest = 0f, lag = 0f, dt = 0.005f;
            for (float t = 0f; t < BungeeCurve.ReadyAt; t += dt)
            {
                float rate = (BungeeCurve.FeelTime(t + dt) - BungeeCurve.FeelTime(t)) / dt;
                slowest = Mathf.Min(slowest, rate); fastest = Mathf.Max(fastest, rate);
                lag = Mathf.Max(lag, t - BungeeCurve.FeelTime(t));
            }
            Assert.That(slowest, Is.InRange(0.3f, 0.5f), "about a third of real speed at the slowest");
            Assert.Less(fastest, 1.35f, "the catch-up is not a fast-forward");
            Assert.LessOrEqual(lag, BungeeCurve.SlowLag + 1e-3f);
            float slowAt = BungeeCurve.SnapAt - 0.2f;
            Assert.Less(BungeeCurve.FeelTime(slowAt + 0.05f) - BungeeCurve.FeelTime(slowAt), 0.035f, "slow just before the lowest point");
        }

        [Test] public void Pose_UprightOnTheBoardAndThePad_HeadDownInBetween()
        {
            Assert.AreEqual(0f, BungeePose.Flip(BungeeCurve.Top + 0.1f), 1e-3f, "standing on the board");
            Assert.AreEqual(1f, BungeePose.Flip(BungeeCurve.Top - 6f), 1e-3f, "head first a few metres into the fall");
            Assert.AreEqual(1f, BungeePose.Flip(BungeeCurve.Bottom), 1e-3f, "still head-down at the lowest point");
            Assert.AreEqual(1f, BungeePose.Flip(BungeeCurve.ReboundApex), 1e-3f);
            Assert.AreEqual(0f, BungeePose.Flip(0.1f), 1e-3f, "feet first onto the pad");
            BungeePose.Ankles(1f, out float up, out float forward);
            Assert.AreEqual(2f * BungeePose.PivotHeight - BungeePose.AnkleHeight, up, 1e-3f, "head-down the ankles are on top");
            Assert.AreEqual(0f, forward, 1e-3f);
            BungeePose.Ankles(0f, out up, out _);
            Assert.AreEqual(BungeePose.AnkleHeight, up, 1e-3f);
        }

        [Test] public void OnRope_OnlyOverThePad_AndNotAHopOnIt()
        {
            Assert.IsTrue(BungeePose.OnRope(0.2f, 0f, 20f));
            Assert.IsTrue(BungeePose.OnRope(0f, 0.3f, BungeeCurve.Top + 0.1f), "clipped in on the board");
            Assert.IsFalse(BungeePose.OnRope(0f, 0f, 1f), "a hop on the landing pad");
            Assert.IsFalse(BungeePose.OnRope(3f, 0f, 20f), "beside the tower");
        }

        [Test] public void Cord_Slack_HangsInALoopBelowTheJumper_Taut_IsStraightAndThinner()
        {
            var pts = new Vector3[40];
            var anchor = new Vector3(0f, 60f, 0f);
            const float length = 38f;
            // Early in the fall: ten metres down, the loop hangs below the jumper.
            var ankles = new Vector3(0.95f, 50f, 0f);
            float w = BungeeCordView.Shape(anchor, ankles, length, 0f, pts);
            Assert.AreEqual(BungeeCordView.Width, w, 1e-4f);
            Assert.AreEqual(anchor, pts[0]); Assert.Less(Vector3.Distance(ankles, pts[39]), 1e-3f);
            float lowest = float.MaxValue; foreach (var p in pts) lowest = Mathf.Min(lowest, p.y);
            Assert.AreEqual(anchor.y - 0.5f * (length + 10f), lowest, 0.6f, "the fold hangs halfway down the slack cord");
            // Past its length: a straight line, thinner.
            ankles = new Vector3(0.95f, 60f - 45f, 0f);
            w = BungeeCordView.Shape(anchor, ankles, length, 0f, pts);
            Assert.Less(w, BungeeCordView.Width * 0.95f);
            for (int i = 0; i < pts.Length; i++)
                Assert.Less(Vector3.Distance(pts[i], Vector3.Lerp(anchor, ankles, i / 39f)), 1e-3f);
        }

        [Test] public void Cord_StandingOnTheBoard_ComesUpBesideTheLip_NotThroughTheBoard()
        {
            var pts = new Vector3[40];
            var anchor = new Vector3(0f, 59.95f, 0f);
            var ankles = new Vector3(0.95f, 60.2f, 0f);   // standing on the board, east of its west lip
            BungeeCordView.Shape(anchor, ankles, 38f, 0f, pts);
            for (int i = 0; i < pts.Length - 1; i++)
                if (pts[i].y < 59.9f) Assert.Less(pts[i].x, 0.3f, $"point {i} hangs outside the board ({pts[i]})");
        }

        [Test] public void SnapSound_IsLoudAtOnce_AndDiesAway()
        {
            var data = BungeeRide.SynthSnap();
            float early = 0f, late = 0f;
            for (int i = 0; i < 2205; i++) early = Mathf.Max(early, Mathf.Abs(data[i]));
            for (int i = data.Length - 2205; i < data.Length; i++) late = Mathf.Max(late, Mathf.Abs(data[i]));
            Assert.Greater(early, 0.5f);
            Assert.Less(late, 0.1f);
        }
    }
}
