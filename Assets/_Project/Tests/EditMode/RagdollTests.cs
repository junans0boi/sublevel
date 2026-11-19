using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-395 (#196): the ragdoll cap, the switch from the death clip, the settle and the push.
    public class RagdollTests
    {
        static readonly RagdollRules R = new RagdollRules();

        [Test]
        public void Budget_FreeSlotThenEvictsFarthestThenRejects()
        {
            var active = new List<float>();
            Assert.AreEqual(RagdollBudget.Free, RagdollBudget.Admit(active, 10f, R));
            for (int i = 0; i < R.MaxActive; i++) active.Add(5f + i * 5f); // 5, 10, ... 30
            Assert.AreEqual(R.MaxActive - 1, RagdollBudget.Admit(active, 12f, R), "nearer than the farthest: that one is frozen");
            Assert.AreEqual(RagdollBudget.Reject, RagdollBudget.Admit(active, 40f, R), "farther than every simulated body");
        }

        [Test]
        public void Budget_RejectsBeyondRangeAndWithoutCap()
        {
            Assert.AreEqual(RagdollBudget.Reject, RagdollBudget.Admit(new List<float>(), R.MaxMeters + 1f, R));
            Assert.AreEqual(RagdollBudget.Reject, RagdollBudget.Admit(new List<float>(), float.NaN, R));
            Assert.AreEqual(RagdollBudget.Reject, RagdollBudget.Admit(new List<float>(), 1f, new RagdollRules { MaxActive = 0 }));
        }

        [Test]
        public void Switch_OnlyForASeenDeathOnDryGroundAfterTheDelay()
        {
            Assert.IsTrue(RagdollSwitch.MayStart(true, false, false, true, R.DelaySeconds, R));
            Assert.IsFalse(RagdollSwitch.MayStart(true, false, false, true, R.DelaySeconds * 0.5f, R), "death clip first");
            Assert.IsFalse(RagdollSwitch.MayStart(false, false, false, true, 1f, R), "already dead when first seen");
            Assert.IsFalse(RagdollSwitch.MayStart(true, true, false, true, 1f, R), "in the water");
            Assert.IsFalse(RagdollSwitch.MayStart(true, false, true, true, 1f, R), "seated");
            Assert.IsFalse(RagdollSwitch.MayStart(true, false, false, false, 1f, R), "no rig");
        }

        [Test]
        public void Settle_FreezesWhenStillOrTooLongOrTooFar()
        {
            var s = new RagdollSettle();
            Assert.IsFalse(s.Tick(2f, 0.5f, 0.1f, R), "moving");
            Assert.IsFalse(s.Tick(0.05f, 0.5f, R.StillSeconds * 0.5f, R));
            Assert.IsFalse(s.Tick(1f, 0.5f, 0.02f, R), "moved again: still time restarts");
            Assert.IsFalse(s.Tick(0.05f, 0.5f, R.StillSeconds * 0.9f, R));
            Assert.IsTrue(s.Tick(0.05f, 0.5f, R.StillSeconds * 0.2f, R), "still long enough");

            var slow = new RagdollSettle();
            Assert.IsTrue(slow.Tick(3f, 0.5f, R.MaxSeconds, R), "time cap");
            var far = new RagdollSettle();
            Assert.IsTrue(far.Tick(3f, R.MaxDriftMeters + 0.1f, 0.02f, R), "drifted from the death position");
            var broken = new RagdollSettle();
            Assert.IsTrue(broken.Tick(3f, float.NaN, 0.02f, R), "NaN pose");
        }

        [Test]
        public void Impulse_AlongTheShotCappedWithLift()
        {
            RagdollImpulse.Hit(0f, 0f, 10f, 1, 0f, 1f, R, out float x, out float y, out float z);
            Assert.AreEqual(0f, x, 1e-4f);
            Assert.AreEqual(R.ImpulsePerHit, z, 1e-4f);
            Assert.AreEqual(R.ImpulsePerHit * R.Lift, y, 1e-4f);
            RagdollImpulse.Hit(0f, 0f, 1f, 100, 0f, 1f, R, out _, out _, out z);
            Assert.AreEqual(R.MaxImpulse, z, 1e-4f, "a shotgun's pellets are capped");
        }

        [Test]
        public void Impulse_WithoutAHitPushesBackFromTheFacing()
        {
            RagdollImpulse.Hit(0f, 0f, 0f, 0, 1f, 0f, R, out float x, out _, out float z);
            Assert.AreEqual(-R.FallbackImpulse, x, 1e-4f);
            Assert.AreEqual(0f, z, 1e-4f);
            RagdollImpulse.Hit(float.NaN, 0f, 0f, 1, 0f, 0f, R, out x, out _, out z);
            Assert.AreEqual(-R.FallbackImpulse, z, 1e-4f, "no facing either: backwards along -z");
        }

        [Test]
        public void Blast_FallsOffToNothing()
        {
            Assert.AreEqual(R.BlastImpulse, RagdollImpulse.Blast(0f, R), 1e-4f);
            Assert.AreEqual(R.BlastImpulse * 0.5f, RagdollImpulse.Blast(R.BlastMeters * 0.5f, R), 1e-4f);
            Assert.AreEqual(0f, RagdollImpulse.Blast(R.BlastMeters, R));
            Assert.AreEqual(0f, RagdollImpulse.Blast(float.NaN, R));
        }
    }
}
