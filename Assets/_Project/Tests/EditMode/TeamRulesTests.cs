using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-072: team assignment, deployment by team, the knock / bleed / revive rules and the last team standing.
    public class TeamRulesTests
    {
        static readonly ElevatorSlot[] World = Layout(3, 3, 4, 3);

        static ElevatorSlot[] Layout(params int[] perRegion)
        {
            var slots = new List<ElevatorSlot>();
            for (int r = 0; r < perRegion.Length; r++)
                for (int i = 0; i < perRegion[r]; i++) slots.Add(new ElevatorSlot(r));
            return slots.ToArray();
        }

        static TeamMember Up(int team) => new TeamMember(team, false, false);
        static TeamMember Down(int team) => new TeamMember(team, false, true);
        static TeamMember Gone(int team) => new TeamMember(team, true, false);

        [Test]
        public void Defaults_MatchTheDecision()
        {
            var rules = new TeamRules();
            Assert.AreEqual(1, rules.SizeOf(TeamMode.Solo));
            Assert.AreEqual(2, rules.SizeOf(TeamMode.Duo));
            Assert.AreEqual(4, rules.SizeOf(TeamMode.Squad));
            Assert.IsFalse(rules.FriendlyFire);
            Assert.AreEqual(100f, rules.KnockedHp, 1e-6f);
            Assert.AreEqual(10f, rules.ReviveSeconds, 1e-6f);
            Assert.AreEqual(10f, rules.ReviveHp, 1e-6f);
            Assert.AreEqual(90f, Knocks.SecondsLeft(100f, 1, rules), 1e-3f, "first knock bleeds out in 90 s (PUBG table)");
            Assert.AreEqual(30f, Knocks.SecondsLeft(100f, 2, rules), 1e-3f);
            rules.BleedSeconds = null;
            Assert.AreEqual(40f, Knocks.SecondsLeft(100f, 1, rules), 1e-4f, "without a table: the older 40 s");
            Assert.AreEqual(100f / 3.75f, Knocks.SecondsLeft(100f, 2, rules), 1e-3f, "the second bleeds 50 % faster");
        }

        [Test]
        public void DataFile_HasTheTeamSection_WithTheDefaults()
        {
            var text = Resources.Load<TextAsset>("GameData/rules");
            Assert.IsNotNull(text);
            var rules = JsonUtility.FromJson<GameRules>(text.text).Team;
            var defaults = new TeamRules();
            Assert.AreEqual(defaults.SquadSize, rules.SquadSize);
            Assert.AreEqual(defaults.DuoSize, rules.DuoSize);
            Assert.AreEqual(defaults.FriendlyFire, rules.FriendlyFire);
            Assert.AreEqual(defaults.BleedPerSecond, rules.BleedPerSecond, 1e-6f);
            Assert.AreEqual(defaults.ReviveSeconds, rules.ReviveSeconds, 1e-6f);
            Assert.AreEqual(defaults.ReviveRangeMeters, rules.ReviveRangeMeters, 1e-6f);
        }

        [Test]
        public void TeamOf_FillsTeamsInOrder_PlayerFirst()
        {
            // 1 player + 15 bots in squads: the player is member 1 of team 1 with three bots.
            Assert.AreEqual(new[] { 1, 1, 1, 1, 2, 2, 2, 2, 3 }, Enumerable.Range(0, 9).Select(i => Teams.TeamOf(i, 4)).ToArray());
            Assert.AreEqual(new[] { 1, 2, 3, 4, 1, 2 }, Enumerable.Range(0, 6).Select(i => Teams.SlotOf(i, 4)).ToArray());
            Assert.AreEqual(new[] { 1, 1, 2, 2, 3 }, Enumerable.Range(0, 5).Select(i => Teams.TeamOf(i, 2)).ToArray());
            Assert.AreEqual(4, Teams.TeamCount(16, 4));
            Assert.AreEqual(8, Teams.TeamCount(16, 2));
            Assert.AreEqual(5, Teams.TeamCount(17, 4), "a short last team");
            Assert.AreEqual(Teams.None, Teams.TeamOf(-1, 4));
        }

        [Test]
        public void Same_NeverForNoTeam()
        {
            Assert.IsTrue(Teams.Same(3, 3));
            Assert.IsFalse(Teams.Same(3, 4));
            Assert.IsFalse(Teams.Same(Teams.None, Teams.None));
        }

        [Test]
        public void ModeNames_Parse_AndCycle()
        {
            Assert.IsTrue(Teams.TryParse("SQUAD", out var squad));
            Assert.AreEqual(TeamMode.Squad, squad);
            Assert.IsTrue(Teams.TryParse("duo", out var duo));
            Assert.AreEqual(TeamMode.Duo, duo);
            Assert.IsFalse(Teams.TryParse("trio", out _));
            Assert.AreEqual(TeamMode.Duo, Teams.Next(TeamMode.Solo));
            Assert.AreEqual(TeamMode.Squad, Teams.Next(TeamMode.Duo));
            Assert.AreEqual(TeamMode.Solo, Teams.Next(TeamMode.Squad));
            Assert.AreEqual("스쿼드", Teams.ModeName(TeamMode.Squad));
        }

        [Test]
        public void Deploy_PutsEachTeamInOneElevator_AndTeamsApart()
        {
            for (int seed = 0; seed < 100; seed++)
                foreach (int size in new[] { 2, 4 })
                {
                    var assigned = Teams.Deploy(16, size, World, seed * 7919 + 13);
                    Assert.AreEqual(16, assigned.Length);
                    var perTeam = new Dictionary<int, int>();
                    for (int i = 0; i < assigned.Length; i++)
                    {
                        int team = Teams.TeamOf(i, size);
                        if (perTeam.TryGetValue(team, out int elevator)) Assert.AreEqual(elevator, assigned[i], $"seed {seed}: team {team} split");
                        else perTeam[team] = assigned[i];
                    }
                    // Fewer teams than elevators: no two teams share one.
                    Assert.AreEqual(perTeam.Count, perTeam.Values.Distinct().Count(), $"seed {seed}, size {size}");
                }
        }

        [Test]
        public void Deploy_Solo_IsThePlainAssignment()
        {
            for (int seed = 0; seed < 20; seed++)
                Assert.AreEqual(Deployment.Assign(16, World, seed, false), Teams.Deploy(16, 1, World, seed)); // ADR-260: strangers never share a car
        }

        [Test]
        public void Knock_OnlyInTeamModes_WithATeammateStanding_Once()
        {
            Assert.IsFalse(Knocks.KnocksDown(TeamMode.Solo, false, true), "solo never knocks");
            Assert.IsTrue(Knocks.KnocksDown(TeamMode.Duo, false, true));
            Assert.IsTrue(Knocks.KnocksDown(TeamMode.Squad, false, true));
            Assert.IsFalse(Knocks.KnocksDown(TeamMode.Squad, false, false), "the last one standing dies");
            Assert.IsFalse(Knocks.KnocksDown(TeamMode.Squad, true, true), "a knocked one dies");
        }

        [Test]
        public void FriendlyFire_Off_BlocksTeammates_NotSelfOrEnemies()
        {
            var rules = new TeamRules();
            Assert.AreEqual(0f, Knocks.DamageBetween(30f, 2, 2, false, false, rules));
            Assert.AreEqual(30f, Knocks.DamageBetween(30f, 2, 2, true, false, rules), "one's own grenade");
            Assert.AreEqual(30f, Knocks.DamageBetween(30f, 1, 2, false, false, rules));
            Assert.AreEqual(30f, Knocks.DamageBetween(30f, Teams.None, Teams.None, false, false, rules), "solo: everyone is an enemy");
            rules.FriendlyFire = true;
            Assert.AreEqual(30f, Knocks.DamageBetween(30f, 2, 2, false, false, rules));
            rules.KnockedDamageFactor = 0.5f;
            Assert.AreEqual(15f, Knocks.DamageBetween(30f, 1, 2, false, true, rules));
        }

        [Test]
        public void Bleed_FollowsThePubgTable_ByNthKnock()
        {
            var rules = new TeamRules();
            var expected = new[] { 90f, 30f, 30f, 10f, 7f, 5f, 4f, 3f, 2f };
            for (int n = 1; n <= expected.Length; n++)
                Assert.AreEqual(expected[n - 1], Knocks.SecondsLeft(rules.KnockedHp, n, rules), 1e-3f, $"knock {n}");
            Assert.AreEqual(2f, Knocks.SecondsLeft(rules.KnockedHp, 12, rules), 1e-3f, "past the table the last value stays");
            Assert.AreEqual(90f, Knocks.SecondsLeft(rules.KnockedHp, 0, rules), 1e-3f, "never slower than the first");
            Assert.AreEqual(1.5f, rules.DownInvulnerableSeconds, 1e-6f, "1.5 s untouchable after going down");
        }

        [Test]
        public void DataFile_HasTheBleedTable()
        {
            var rules = JsonUtility.FromJson<GameRules>(Resources.Load<TextAsset>("GameData/rules").text).Team;
            CollectionAssert.AreEqual(new TeamRules().BleedSeconds, rules.BleedSeconds);
            Assert.AreEqual(1.5f, rules.DownInvulnerableSeconds, 1e-6f);
        }

        [Test]
        public void Carry_NeedsAFreeKnockedTeammateInReach_AndEndsWhenFarOrDown()
        {
            var rules = new TeamRules();
            Assert.IsTrue(KnockCarry.CanStart(1f, true, false, true, true, true, rules));
            Assert.IsFalse(KnockCarry.CanStart(rules.CarryRangeMeters + 0.1f, true, false, true, true, true, rules), "too far");
            Assert.IsFalse(KnockCarry.CanStart(1f, false, false, true, true, true, rules), "the carrier is down");
            Assert.IsFalse(KnockCarry.CanStart(1f, true, true, true, true, true, rules), "the carrier is busy");
            Assert.IsFalse(KnockCarry.CanStart(1f, true, false, false, true, true, rules), "the teammate is standing");
            Assert.IsFalse(KnockCarry.CanStart(1f, true, false, true, false, true, rules), "someone already revives or carries it");
            Assert.IsFalse(KnockCarry.CanStart(1f, true, false, true, true, false, rules), "an enemy");
            Assert.IsTrue(KnockCarry.KeepGoing(rules.CarryKeepMeters - 0.1f, true, true, rules));
            Assert.IsFalse(KnockCarry.KeepGoing(rules.CarryKeepMeters + 0.1f, true, true, rules), "left behind");
            Assert.IsFalse(KnockCarry.KeepGoing(1f, false, true, rules), "the carrier went down");
            Assert.IsFalse(KnockCarry.KeepGoing(1f, true, false, rules), "the carried one died or got up");
            Assert.Less(rules.CarryMoveFactor, 1f, "carrying is slow");
            var (x, y, z) = KnockCarry.Offset(0f);
            Assert.AreEqual(-0.3f, z, 1e-4f, "behind the carrier");
            Assert.Greater(y, 0f);
            Assert.AreEqual(0f, x, 1e-4f);
            // #173 piggyback: with a ~0.95 m standing hip height, the rider's hips sit at the carrier's lower back.
            float riderHips = y - KnockCarry.RideDrop + 0.95f;
            Assert.That(riderHips, Is.InRange(0.9f, 1.2f));
        }

        [Test]
        public void LastTeammateDown_WholeTeamIsFinished()
        {
            // The last one standing of a squad goes down: nobody can revive the knocked ones, so all four are finished.
            var members = new List<TeamMember> { Down(1), Down(1), Down(1), Down(1), Up(2) };
            for (int i = 0; i < 4; i++) Assert.IsTrue(TeamStandings.MustFinish(members, i), "member " + i);
            Assert.IsFalse(TeamStandings.MustFinish(members, 4));
            members[2] = Up(1);
            for (int i = 0; i < 4; i++) Assert.IsFalse(TeamStandings.MustFinish(members, i), "one stands: nobody is finished");
        }

        [Test]
        public void Bleed_GrowsWithEveryKnock()
        {
            var rules = new TeamRules { BleedSeconds = null };
            Assert.AreEqual(2.5f, rules.BleedFor(1), 1e-6f);
            Assert.AreEqual(3.75f, rules.BleedFor(2), 1e-6f);
            Assert.AreEqual(5f, rules.BleedFor(3), 1e-6f);
            Assert.AreEqual(2.5f, rules.BleedFor(0), 1e-6f, "never slower than the first");
            Assert.AreEqual(10f, Knocks.SecondsLeft(25f, 1, rules), 1e-4f);
        }

        [Test]
        public void Revive_TakesTheFullTime_InRange()
        {
            var rules = new TeamRules();
            var revive = new ReviveTimer();
            Assert.IsFalse(revive.CanStart(rules.ReviveRangeMeters + 0.1f, true, true, true, rules), "too far");
            Assert.IsFalse(revive.CanStart(1f, true, true, false, rules), "not a teammate");
            Assert.IsFalse(revive.CanStart(1f, false, true, true, rules), "the reviver is down");
            Assert.IsFalse(revive.CanStart(1f, true, false, true, rules), "nobody to revive");
            Assert.IsTrue(revive.CanStart(1f, true, true, true, rules));
            revive.Start();
            for (int i = 0; i < 99; i++) Assert.AreEqual(ReviveStep.Going, revive.Tick(0.1f, 1f, true, true, rules), $"step {i}");
            Assert.AreEqual(0.99f, revive.Progress01(rules), 1e-3f);
            Assert.AreEqual(ReviveStep.Done, revive.Tick(0.1f, 1f, true, true, rules));
            Assert.IsFalse(revive.Active);
        }

        [Test]
        public void Revive_BreaksOff_WhenApart_OrEitherIsDown()
        {
            var rules = new TeamRules();
            var revive = new ReviveTimer();
            revive.Start();
            Assert.AreEqual(ReviveStep.Going, revive.Tick(1f, rules.ReviveKeepMeters - 0.01f, true, true, rules), "a little drift is fine");
            Assert.AreEqual(ReviveStep.Cancelled, revive.Tick(1f, rules.ReviveKeepMeters + 0.01f, true, true, rules));
            Assert.AreEqual(0f, revive.Elapsed);
            revive.Start();
            Assert.AreEqual(ReviveStep.Cancelled, revive.Tick(0.1f, 1f, false, true, rules), "reviver knocked");
            revive.Start();
            Assert.AreEqual(ReviveStep.Cancelled, revive.Tick(0.1f, 1f, true, false, rules), "target died");
            Assert.AreEqual(ReviveStep.Idle, revive.Tick(0.1f, 1f, true, true, rules));
        }

        [Test]
        public void KnockedMembers_AreFinished_OnlyWhenNobodyOfTheirTeamStands()
        {
            var members = new List<TeamMember> { Down(1), Up(1), Down(2), Gone(2), Up(3) };
            Assert.IsFalse(TeamStandings.MustFinish(members, 0), "a teammate stands");
            Assert.IsTrue(TeamStandings.MustFinish(members, 2), "nobody of team 2 stands");
            Assert.IsFalse(TeamStandings.MustFinish(members, 1), "standing members are never finished");
            Assert.IsTrue(TeamStandings.HasStanding(members, 1));
            Assert.IsFalse(TeamStandings.HasStanding(members, 1, except: 1));
        }

        [Test]
        public void LastTeamStanding_Wins_KnockedStillCountsAsAlive()
        {
            var members = new List<TeamMember> { Up(1), Down(1), Gone(2), Down(2), Up(3) };
            Assert.AreEqual(3, TeamStandings.AliveTeams(members));
            Assert.IsFalse(TeamStandings.Decided(members, out _));

            members = new List<TeamMember> { Up(1), Down(1), Gone(2), Gone(2), Gone(3) };
            Assert.AreEqual(1, TeamStandings.AliveTeams(members));
            Assert.IsTrue(TeamStandings.Decided(members, out int winner));
            Assert.AreEqual(1, winner);

            members = new List<TeamMember> { Gone(1), Gone(2) };
            Assert.IsTrue(TeamStandings.Decided(members, out winner));
            Assert.AreEqual(Teams.None, winner, "nobody left");
        }

        [Test]
        public void Placement_IsOneBehindTheTeamsStillAlive()
        {
            // Team 2 just went out with teams 1 and 3 left: third.
            var members = new List<TeamMember> { Up(1), Gone(2), Gone(2), Up(3), Down(3) };
            Assert.IsFalse(TeamStandings.TeamAlive(members, 2));
            Assert.IsTrue(TeamStandings.TeamAlive(members, 3));
            Assert.AreEqual(3, TeamStandings.PlacementOnElimination(members));
        }

        [Test]
        public void MembersWithoutATeam_CountOneEach()
        {
            var members = new List<TeamMember> { Up(Teams.None), Up(Teams.None), Gone(Teams.None) };
            Assert.AreEqual(2, TeamStandings.AliveTeams(members));
        }
    }
}
