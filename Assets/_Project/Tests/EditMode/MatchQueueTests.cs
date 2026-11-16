using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-247..249: who plays a match for N people in the queue, solo and squad, and the queue's window.
    public class MatchQueueTests
    {
        static readonly TeamRules Teams = new TeamRules();
        static MatchRules Rules() => new MatchRules(); // 50 participants, 10 bots at least, 30 s window

        static List<QueueParty> Solos(int n)
        {
            var l = new List<QueueParty>();
            for (int i = 0; i < n; i++) l.Add(new QueueParty(i + 1, 1));
            return l;
        }

        // humans, mode -> (humans placed, bots, teams, human teams, bot teams, overflow tickets)
        [TestCase(1, TeamMode.Solo, 1, 49, 50, 1, 49, 0)]
        [TestCase(11, TeamMode.Solo, 11, 39, 50, 11, 39, 0)]
        [TestCase(40, TeamMode.Solo, 40, 10, 50, 40, 10, 0)]
        [TestCase(60, TeamMode.Solo, 40, 10, 50, 40, 10, 20)]
        [TestCase(1, TeamMode.Squad, 1, 49, 14, 1, 13, 0)]
        [TestCase(11, TeamMode.Squad, 11, 39, 21, 11, 10, 0)]
        [TestCase(40, TeamMode.Squad, 40, 10, 43, 40, 3, 0)]
        [TestCase(60, TeamMode.Squad, 40, 10, 43, 40, 3, 20)]
        public void SoloTickets_Composition(int people, TeamMode mode, int humans, int bots, int teams, int humanTeams, int botTeams, int overflow)
        {
            var plan = MatchComposer.Compose(mode, Solos(people), Rules(), Teams);
            Assert.AreEqual(humans, plan.Humans, "humans");
            Assert.AreEqual(bots, plan.Bots, "bots");
            Assert.AreEqual(50, plan.Participants);
            Assert.AreEqual(teams, plan.Teams.Count, "teams");
            Assert.AreEqual(humanTeams, plan.HumanTeams, "human teams");
            Assert.AreEqual(botTeams, plan.BotTeams, "bot teams");
            Assert.AreEqual(overflow, plan.Overflow.Count, "overflow");
            int sum = 0;
            foreach (var t in plan.Teams) sum += t.Size;
            Assert.AreEqual(50, sum, "every participant is on exactly one team");
        }

        [Test]
        public void Squad_BotTeamsAreBalancedFours_NeverALoneLeftover()
        {
            var plan = MatchComposer.Compose(TeamMode.Squad, Solos(1), Rules(), Teams);
            var sizes = new List<int>();
            foreach (var t in plan.Teams) if (t.IsBot) sizes.Add(t.Size);
            Assert.AreEqual(13, sizes.Count);
            foreach (int s in sizes) Assert.That(s == 3 || s == 4, "size " + s);
            Assert.AreEqual(49, sizes.Sum());
        }

        [Test]
        public void Parties_OneToFour_StayWholeAsTeams()
        {
            var parties = new List<QueueParty> { new QueueParty(1, 4), new QueueParty(2, 3), new QueueParty(3, 2), new QueueParty(4, 1) };
            var plan = MatchComposer.Compose(TeamMode.Squad, parties, Rules(), Teams);
            Assert.AreEqual(10, plan.Humans);
            Assert.AreEqual(40, plan.Bots);
            Assert.AreEqual(4, plan.HumanTeams);
            Assert.AreEqual(new[] { 4, 3, 2, 1 }, plan.Teams.GetRange(0, 4).ConvertAll(t => t.Size).ToArray());
            Assert.AreEqual(10, plan.BotTeams);
        }

        [Test]
        public void SoloMode_PartySizeIsIgnored_EveryoneIsAlone()
        {
            var plan = MatchComposer.Compose(TeamMode.Solo, new List<QueueParty> { new QueueParty(1, 4) }, Rules(), Teams);
            Assert.AreEqual(1, plan.Humans);
            Assert.AreEqual(50, plan.Teams.Count);
        }

        [Test]
        public void APartyThatDoesNotFitWhole_IsLeftOver_AndSoIsEveryoneBehindIt()
        {
            var parties = new List<QueueParty>();
            for (int i = 0; i < 9; i++) parties.Add(new QueueParty(i + 1, 4)); // 36
            parties.Add(new QueueParty(100, 4)); // 40: fits exactly
            parties.Add(new QueueParty(101, 1)); // 41: no
            var plan = MatchComposer.Compose(TeamMode.Squad, parties, Rules(), Teams);
            Assert.AreEqual(40, plan.Humans);
            CollectionAssert.AreEqual(new[] { 101 }, plan.Overflow);
            var behind = new List<QueueParty>(parties) { new QueueParty(102, 1) };
            behind.Insert(9, new QueueParty(99, 4)); // 9 parties of 4 + 4 more = 40; then 100 does not fit
            var p2 = MatchComposer.Compose(TeamMode.Squad, behind, Rules(), Teams);
            CollectionAssert.AreEqual(new[] { 100, 101, 102 }, p2.Overflow);
        }

        [Test]
        public void Window_OpensWithTheFirstTicket_ClosesAfterThirtySeconds()
        {
            var q = new MatchQueue(Rules());
            Assert.IsFalse(q.ShouldStart(TeamMode.Solo, 0, Teams), "empty");
            q.Enqueue(1, 1, 100.0);
            Assert.AreEqual(30f, q.SecondsLeft(100.0), 1e-3f);
            q.Enqueue(2, 1, 110.0);
            Assert.IsFalse(q.ShouldStart(TeamMode.Solo, 129.9, Teams));
            Assert.AreEqual(0.1f, q.SecondsLeft(129.9), 1e-2f);
            Assert.IsTrue(q.ShouldStart(TeamMode.Solo, 130.0, Teams), "the window ended: fill with bots");
            var plan = q.Start(TeamMode.Solo, 130.0, Teams);
            Assert.AreEqual(2, plan.Humans);
            Assert.AreEqual(48, plan.Bots);
            Assert.AreEqual(0, q.Waiting);
        }

        [Test]
        public void FullRoom_StartsAtOnce_AndTheOverflowOpensANewWindow()
        {
            var q = new MatchQueue(Rules());
            for (int i = 0; i < 41; i++) q.Enqueue(i + 1, 1, 10.0 + i * 0.1);
            Assert.IsTrue(q.ShouldStart(TeamMode.Solo, 11.0, Teams), "41 people: 40 fit, no reason to wait");
            var plan = q.Start(TeamMode.Solo, 11.0, Teams);
            Assert.AreEqual(40, plan.Humans);
            Assert.AreEqual(1, q.Waiting, "one stays for the next match");
            Assert.AreEqual(30f, q.SecondsLeft(11.0), 1e-3f, "a fresh window for the one left");
        }

        [Test]
        public void Leaving_Empties_ThenTheNextTicketOpensAFreshWindow_AndDuplicatesAreRefused()
        {
            var q = new MatchQueue(Rules());
            Assert.IsTrue(q.Enqueue(1, 2, 0.0));
            Assert.IsFalse(q.Enqueue(1, 2, 1.0), "same client twice");
            Assert.IsTrue(q.Leave(1, 5.0));
            Assert.AreEqual(0f, q.SecondsLeft(5.0));
            q.Enqueue(2, 1, 50.0);
            Assert.AreEqual(30f, q.SecondsLeft(50.0), 1e-3f);
        }

        [Test]
        public void State_ShowsWaitingTargetAndTimeLeft()
        {
            var q = new MatchQueue(Rules());
            q.Enqueue(1, 3, 0.0);
            q.Enqueue(2, 1, 4.0);
            var s = q.State(10.0);
            Assert.IsTrue(s.Open);
            Assert.AreEqual(4, s.Waiting);
            Assert.AreEqual(2, s.Parties);
            Assert.AreEqual(50, s.Target);
            Assert.AreEqual(20f, s.SecondsLeft, 1e-3f);
            Assert.IsFalse(new MatchQueue(Rules()).State(0).Open);
        }

        [Test]
        public void MenuMatch_FillsToFiftyWithBots_WhenTheDataSaysSo_AndKeepsTheSliceCountWhenNot()
        {
            var r = new MatchRules { FillToParticipants = true };
            Assert.AreEqual(49, r.BotsFor(1));
            Assert.AreEqual(39, r.BotsFor(11));
            Assert.AreEqual(10, r.BotsFor(40));
            Assert.AreEqual(50, r.TargetParticipants);
            var slice = new MatchRules { BotCount = 3 };
            Assert.AreEqual(3, slice.BotsFor(1), "tests that set BotCount keep it");
            Assert.AreEqual(4, slice.TargetParticipants);
        }
    }
}
