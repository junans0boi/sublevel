using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #413 (ADR-380): the waiting countdown. Max 90 s; a full room (people + bots at Participants, or people at MaxHumans)
    // shortens it to 15 s when more than 15 s are left; at the end the missing seats are filled with bots.
    public class WaitingCountdownTests
    {
        static MatchRules Rules() => new MatchRules { Participants = 80, MinBots = 16, WaitMaxSeconds = 90f, WaitFullSeconds = 15f };

        [Test]
        public void Nobody_NoClockRuns()
        {
            var c = new WaitingCountdown(Rules());
            var s = c.Update(0.0, 0, 0);
            Assert.IsFalse(s.Running);
            Assert.IsFalse(s.Done);
            Assert.AreEqual(90f, s.SecondsLeft);
            s = c.Update(500.0, 0, 0);
            Assert.IsFalse(s.Done, "nobody there, nothing to start");
        }

        [Test]
        public void FirstPerson_OpensWindow_EndsAtMax_TimeUp()
        {
            var c = new WaitingCountdown(Rules());
            Assert.AreEqual(90f, c.Update(10.0, 1, 0).SecondsLeft, 1e-3f);
            var mid = c.Update(40.0, 1, 5);
            Assert.AreEqual(60f, mid.SecondsLeft, 1e-3f);
            Assert.IsFalse(mid.Done);
            var end = c.Update(100.0, 2, 5);
            Assert.IsTrue(end.Done);
            Assert.AreEqual(StartReason.TimeUp, end.Reason);
            Assert.AreEqual(0f, end.SecondsLeft);
            Assert.AreEqual(78, end.BotsTotal, "80 - 2 people: the match total of bots, the 5 waiting ones included");
        }

        [Test]
        public void FullRoom_ShortensTo15Seconds_StartsWithFullReason()
        {
            var c = new WaitingCountdown(Rules());
            c.Update(0.0, 1, 0);
            var full = c.Update(20.0, 1, 79); // 80 present
            Assert.AreEqual(15f, full.SecondsLeft, 1e-3f);
            Assert.IsFalse(full.Done);
            Assert.AreEqual(5f, c.Update(30.0, 1, 79).SecondsLeft, 1e-3f);
            var end = c.Update(35.0, 1, 79);
            Assert.IsTrue(end.Done);
            Assert.AreEqual(StartReason.Full, end.Reason);
            Assert.AreEqual(79, end.BotsTotal);
        }

        [Test]
        public void FullWithLessThan15Left_KeepsTheShorterTime()
        {
            var c = new WaitingCountdown(Rules());
            c.Update(0.0, 1, 0);
            var s = c.Update(80.0, 1, 79); // 10 s left, full now: stays 10, not 15
            Assert.AreEqual(10f, s.SecondsLeft, 1e-3f);
            var end = c.Update(90.0, 1, 79);
            Assert.IsTrue(end.Done);
            Assert.AreEqual(StartReason.TimeUp, end.Reason, "the 90 s ran out first");
        }

        [Test]
        public void PeopleAtMaxHumans_IsFull_EvenWithoutBots()
        {
            var r = Rules(); // MaxHumans = 64
            var c = new WaitingCountdown(r);
            c.Update(0.0, 63, 0);
            Assert.AreEqual(90f, c.Update(1.0, 63, 0).SecondsLeft, 1e-3f);
            Assert.AreEqual(15f, c.Update(2.0, 64, 0).SecondsLeft, 1e-3f);
        }

        [Test]
        public void Dropping_OutOfFull_DoesNotLengthenTheClock()
        {
            var c = new WaitingCountdown(Rules());
            c.Update(0.0, 1, 0);
            c.Update(20.0, 1, 79);                       // full: ends at 35
            Assert.AreEqual(10f, c.Update(25.0, 1, 40).SecondsLeft, 1e-3f); // not full any more, the short deadline stays
        }

        [Test]
        public void EveryoneLeaves_ResetsTheClock()
        {
            var c = new WaitingCountdown(Rules());
            c.Update(0.0, 1, 0);
            c.Update(50.0, 0, 0);
            Assert.AreEqual(90f, c.Update(60.0, 1, 0).SecondsLeft, 1e-3f, "a new first person opens a fresh window");
        }

        [Test]
        public void Reset_ClearsDeadline()
        {
            var c = new WaitingCountdown(Rules());
            c.Update(0.0, 1, 0);
            c.Reset();
            Assert.AreEqual(90f, c.Update(500.0, 1, 0).SecondsLeft, 1e-3f);
        }

        [Test]
        public void StartSecondsOverride_ReplacesTheMax_ZeroMeansNever()
        {
            var r = Rules();
            var c = new WaitingCountdown(r, 12.5f);
            c.Update(0.0, 1, 0);
            Assert.IsTrue(c.Update(12.5, 1, 0).Done);
            var never = new WaitingCountdown(r, 0f);
            never.Update(0.0, 1, 0);
            Assert.IsFalse(never.Update(9999.0, 1, 0).Done);
        }

        [Test]
        public void Defaults_AreNinetyAndFifteen_AndFromData()
        {
            var r = new MatchRules();
            Assert.AreEqual(90f, r.WaitMaxSeconds);
            Assert.AreEqual(15f, r.WaitFullSeconds);
            var g = UnityEngine.JsonUtility.FromJson<MatchRules>("{\"WaitMaxSeconds\":45,\"WaitFullSeconds\":5}");
            Assert.AreEqual(45f, g.WaitMaxSeconds);
            Assert.AreEqual(5f, g.WaitFullSeconds);
        }
    }
}
