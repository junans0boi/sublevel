using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #166: the timer rules of a dropped player's combatant.
    public class ReconnectTests
    {
        [Test]
        public void SameToken_WithinTheTime_GetsTheCombatantBack_Once()
        {
            var holds = new SessionHolds();
            holds.Begin("a", 7, 100, 60);
            Assert.IsTrue(holds.TryResume("a", 159.9, out int id));
            Assert.AreEqual(7, id);
            Assert.IsFalse(holds.TryResume("a", 160, out _), "a hold is used up by the first return");
            Assert.AreEqual(0, holds.Count);
        }

        [Test]
        public void Deadline_IsExclusive_AndAfterItTheCombatantGoesToABot()
        {
            var holds = new SessionHolds();
            holds.Begin("a", 7, 100, 60);
            Assert.IsFalse(holds.TryResume("a", 160, out _), "too late exactly at the deadline");
            Assert.AreEqual(1, holds.Count, "a late return does not free the hold; the bot hand-over does");
            Assert.IsEmpty(holds.TakeExpired(159.9));
            CollectionAssert.AreEqual(new[] { 7 }, holds.TakeExpired(160));
            Assert.AreEqual(0, holds.Count);
            Assert.IsEmpty(holds.TakeExpired(500), "handed over only once");
        }

        [Test]
        public void OtherToken_DoesNotGetSomeoneElsesCombatant()
        {
            var holds = new SessionHolds();
            holds.Begin("a", 7, 0, 60);
            holds.Begin("b", 8, 0, 30);
            Assert.IsFalse(holds.TryResume("c", 1, out _));
            Assert.IsFalse(holds.TryResume(null, 1, out _));
            Assert.IsFalse(holds.TryResume("", 1, out _));
            Assert.IsTrue(holds.TryResume("b", 1, out int id));
            Assert.AreEqual(8, id);
            CollectionAssert.AreEqual(new[] { 7 }, holds.TakeExpired(61));
        }

        [Test]
        public void SeveralExpire_AtTheirOwnTimes()
        {
            var holds = new SessionHolds();
            holds.Begin("a", 1, 0, 10);
            holds.Begin("b", 2, 5, 10);
            CollectionAssert.AreEqual(new[] { 1 }, holds.TakeExpired(12));
            CollectionAssert.AreEqual(new[] { 2 }, holds.TakeExpired(15));
        }

        [Test]
        public void EmptyToken_IsNeverHeld()
        {
            var holds = new SessionHolds();
            holds.Begin("", 1, 0, 10);
            holds.Begin(null, 2, 0, 10);
            Assert.AreEqual(0, holds.Count);
            Assert.IsFalse(holds.Has(""));
        }

        [Test]
        public void ReconnectSeconds_IsDataDriven_AndDefaultsToAMinute()
        {
            Assert.AreEqual(60f, new MatchRules().ReconnectSeconds);
            Assert.AreEqual(60f, MatchRules.Current.ReconnectSeconds);
        }
    }
}
