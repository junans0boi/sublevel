using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // PUBG_GAP 4-3 batch I, the smoke grenade: where a cloud blocks the way. (SmokeTests.cs is the older assembly
    // smoke check, so these live in their own file.)
    public class SmokeZoneTests
    {
        [SetUp]
        public void Empty() => SmokeZones.Clear();

        [TearDown]
        public void EmptyAgain() => SmokeZones.Clear();

        [Test]
        public void Defaults_AreTheAgreedNumbers()
        {
            var rules = new SmokeRules();
            Assert.AreEqual(1f, rules.FuseSeconds, 1e-4f);
            Assert.AreEqual(5f, rules.Radius, 1e-4f);
            Assert.AreEqual(25f, rules.Seconds, 1e-4f);
            Assert.AreEqual(3, rules.MaxCarried);
        }

        [Test]
        public void ALineThroughACloud_IsBlocked()
        {
            SmokeZones.Add(0f, 1f, 0f, 5f);
            Assert.IsTrue(SmokeZones.Blocks(-20f, 1f, 0f, 20f, 1f, 0f), "straight through the middle");
            Assert.IsTrue(SmokeZones.Blocks(-20f, 1f, 4f, 20f, 1f, 4f, 1.5f), "through the edge: a chord of 6 m");
        }

        [Test]
        public void ALineBesideOrShortOfACloud_IsFree()
        {
            SmokeZones.Add(0f, 1f, 0f, 5f);
            Assert.IsFalse(SmokeZones.Blocks(-20f, 1f, 8f, 20f, 1f, 8f), "passes 8 m away");
            Assert.IsFalse(SmokeZones.Blocks(-20f, 1f, 0f, -10f, 1f, 0f), "ends before the cloud");
            Assert.IsFalse(SmokeZones.Blocks(-20f, 1f, 0f, -5.5f, 1f, 0f), "ends just outside");
            Assert.IsFalse(SmokeZones.Blocks(-20f, 14f, 0f, 20f, 14f, 0f), "high above it");
            Assert.IsFalse(SmokeZones.Blocks(0f, 1f, 0f, 0f, 1f, 0f), "no length, no sight line");
        }

        [Test]
        public void AShortLookInsideTheCloud_StillSees()
        {
            SmokeZones.Add(0f, 1f, 0f, 5f);
            // Two combatants one metre apart in the smoke: the chord is under the minimum.
            Assert.IsFalse(SmokeZones.Blocks(0f, 1f, 0f, 1f, 1f, 0f));
            // ... but several metres inside it is blocked.
            Assert.IsTrue(SmokeZones.Blocks(-2f, 1f, 0f, 2f, 1f, 0f));
            // A line that only grazes the cloud has a short chord.
            Assert.IsFalse(SmokeZones.Blocks(-20f, 1f, 4.99f, 20f, 1f, 4.99f), "a graze: chord well under 1.5 m");
        }

        [Test]
        public void Chord_IsTheLengthInsideTheSphere()
        {
            Assert.AreEqual(10f, SmokeZones.Chord(-20f, 0f, 0f, 20f, 0f, 0f, 0f, 0f, 0f, 5f), 1e-3f);
            Assert.AreEqual(8f, SmokeZones.Chord(-20f, 3f, 0f, 20f, 3f, 0f, 0f, 0f, 0f, 5f), 1e-3f, "3-4-5: half chord 4");
            Assert.AreEqual(5f, SmokeZones.Chord(0f, 0f, 0f, 20f, 0f, 0f, 0f, 0f, 0f, 5f), 1e-3f, "starts in the middle");
            Assert.AreEqual(2f, SmokeZones.Chord(0f, 0f, 0f, 2f, 0f, 0f, 0f, 0f, 0f, 5f), 1e-3f, "wholly inside");
            Assert.AreEqual(0f, SmokeZones.Chord(-20f, 6f, 0f, 20f, 6f, 0f, 0f, 0f, 0f, 5f), 1e-3f);
        }

        [Test]
        public void ClearedCloud_BlocksNothing()
        {
            int id = SmokeZones.Add(0f, 1f, 0f, 5f);
            Assert.AreEqual(1, SmokeZones.Count);
            Assert.IsTrue(SmokeZones.Blocks(-20f, 1f, 0f, 20f, 1f, 0f));
            Assert.IsTrue(SmokeZones.Remove(id));
            Assert.IsFalse(SmokeZones.Remove(id), "once");
            Assert.AreEqual(0, SmokeZones.Count);
            Assert.IsFalse(SmokeZones.Blocks(-20f, 1f, 0f, 20f, 1f, 0f));
        }

        [Test]
        public void SeveralClouds_AnyOneBlocks()
        {
            int first = SmokeZones.Add(0f, 1f, 0f, 5f);
            SmokeZones.Add(40f, 1f, 0f, 5f);
            Assert.IsTrue(SmokeZones.Blocks(30f, 1f, 0f, 50f, 1f, 0f), "the second one");
            SmokeZones.Remove(first);
            Assert.IsFalse(SmokeZones.Blocks(-20f, 1f, 0f, 20f, 1f, 0f));
            Assert.IsTrue(SmokeZones.Blocks(30f, 1f, 0f, 50f, 1f, 0f));
        }

        [Test]
        public void AFullTable_DropsTheOldestCloud()
        {
            int oldest = SmokeZones.Add(1000f, 1f, 0f, 5f);
            for (int i = 1; i < SmokeZones.Capacity; i++) SmokeZones.Add(i * 100f, 1f, 500f, 5f);
            Assert.AreEqual(SmokeZones.Capacity, SmokeZones.Count);
            SmokeZones.Add(0f, 1f, 0f, 5f);
            Assert.AreEqual(SmokeZones.Capacity, SmokeZones.Count);
            Assert.IsFalse(SmokeZones.Remove(oldest), "it made room");
            Assert.IsTrue(SmokeZones.Blocks(-20f, 1f, 0f, 20f, 1f, 0f), "the newest is there");
        }

        [Test]
        public void SelectionCycles_BetweenTheKinds()
        {
            Assert.AreEqual(ThrowableKind.Smoke, Throwables.Next(ThrowableKind.Frag));
            Assert.AreEqual(ThrowableKind.Flash, Throwables.Next(ThrowableKind.Smoke));
            Assert.AreEqual(1f, Throwables.FuseOf(ThrowableKind.Smoke, new ThrowableRules(), new SmokeRules()), 1e-4f);
            Assert.AreEqual(5f, Throwables.FuseOf(ThrowableKind.Frag, new ThrowableRules(), new SmokeRules()), 1e-4f);
            Assert.AreEqual("연막탄", Throwables.NameOf(ThrowableKind.Smoke));
            Assert.AreEqual(ThrowableRules.SmokeLootKind, Throwables.LootKindOf(ThrowableKind.Smoke));
            Assert.AreEqual(ThrowableRules.GrenadeLootKind, Throwables.LootKindOf(ThrowableKind.Frag));
        }
    }
}
