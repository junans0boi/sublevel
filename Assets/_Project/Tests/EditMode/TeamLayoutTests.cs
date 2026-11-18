using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-332: a party is one team of its own size, bots make teams of 4, nobody is topped up.
    public class TeamLayoutTests
    {
        static readonly TeamRules Rules = new TeamRules();
        static readonly MatchRules Match = new MatchRules();

        static ElevatorSlot[] World()
        {
            var l = new List<ElevatorSlot>();
            int[] perRegion = { 3, 4, 3, 3, 4, 3 };
            for (int r = 0; r < perRegion.Length; r++)
                for (int i = 0; i < perRegion[r] * 3; i++) l.Add(new ElevatorSlot(r));
            return l.ToArray();
        }

        static int[] Sizes(TeamLayout l) => Enumerable.Range(1, l.TeamCount).Select(l.SizeOfTeam).ToArray();

        static TeamLayout Plan(params int[] parties)
        {
            var q = parties.Select((n, i) => new QueueParty(i + 1, n)).ToList();
            var plan = MatchComposer.Compose(TeamMode.Squad, q, Match, Rules);
            return new TeamLayout(plan.Teams.Select(t => t.Size).ToList());
        }

        [Test]
        public void SoloHuman_IsAloneAndBotsAreFours()
        {
            var l = TeamLayout.For(1, 79, 4);
            Assert.AreEqual(80, l.Participants);
            Assert.AreEqual(1, l.SizeOfTeam(1));
            Assert.AreEqual(1, l.TeamOf(0));
            Assert.AreEqual(2, l.TeamOf(1)); // first bot is not on the human's team
            Assert.AreEqual(1, l.SlotOf(0));
            Assert.AreEqual(21, l.TeamCount);
            Assert.AreEqual(19, Sizes(l).Count(s => s == 4));
            Assert.AreEqual(1, Sizes(l).Count(s => s == 3));
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void Party_IsOneTeamOfItsSize(int n)
        {
            var l = TeamLayout.ForParties(new[] { n }, 80 - n, 4);
            Assert.AreEqual(n, l.SizeOfTeam(1));
            for (int i = 0; i < n; i++) { Assert.AreEqual(1, l.TeamOf(i)); Assert.AreEqual(i + 1, l.SlotOf(i)); }
            Assert.AreEqual(2, l.TeamOf(n));
        }

        [Test]
        public void MixedParties_KeepTheirSizes_AndMatchTheComposerPlan()
        {
            var l = Plan(1, 3, 2, 4, 1);
            Assert.AreEqual(new[] { 1, 3, 2, 4, 1 }, Sizes(l).Take(5).ToArray());
            Assert.AreEqual(80, l.Participants);
            Assert.IsTrue(Sizes(l).Skip(5).All(s => s <= 4));
            Assert.AreEqual(new[] { 1, 2, 2, 2, 3, 3, 4, 4, 4, 4, 5 }, Enumerable.Range(0, 11).Select(l.TeamOf).ToArray());
        }

        [Test]
        public void SixtyFourPeople_AllSolo_AndSixteenBots()
        {
            var l = Plan(Enumerable.Repeat(1, 64).ToArray());
            Assert.AreEqual(80, l.Participants);
            Assert.AreEqual(68, l.TeamCount);
            Assert.AreEqual(64, Sizes(l).Take(64).Count(s => s == 1));
            Assert.AreEqual(Enumerable.Repeat(4, 4).ToArray(), Sizes(l).Skip(64).ToArray());
        }

        [Test]
        public void HumansWithoutPartyInfo_FillTeamsInOrder()
        {
            Assert.AreEqual(new[] { 4, 4, 1 }, Sizes(TeamLayout.For(9, 0, 4)));
        }

        [Test]
        public void Deploy_OneCarPerTeam_SoloTeamsToo()
        {
            var world = World();
            for (int seed = 0; seed < 50; seed++)
            {
                var l = Plan(1, 1, 3);
                var cars = l.Deploy(world, seed);
                Assert.AreEqual(l.Participants, cars.Length);
                var carOfTeam = new Dictionary<int, int>();
                for (int i = 0; i < cars.Length; i++)
                {
                    int team = l.TeamOf(i);
                    if (!carOfTeam.TryGetValue(team, out var car)) carOfTeam[team] = cars[i];
                    else Assert.AreEqual(car, cars[i], "a team shares its elevator");
                }
                var used = carOfTeam.Values.Where(c => c >= 0).ToList();
                Assert.AreEqual(used.Count, used.Distinct().Count(), "teams never share a car");
                Assert.IsTrue(used.GroupBy(c => world[c].Region).All(g => g.Count() <= (l.TeamCount + 5) / 6 + 1), "region cap");
            }
        }
    }
}
