using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // ADR-072 (#92) in the World scene as a host: squads of four (the player and three bots) start together, a lethal
    // hit knocks down while a teammate stands, teammates do not hurt each other, F held revives, bleeding out and a
    // wiped team die and place as a team, the last team standing wins, bots revive. Solo stays as it was.
    public class TeamPlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() => Frame;
        }

        ScriptedInput _input;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator Load(TeamMode mode)
        {
            TeamMatch.Mode = mode;
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "local player in World");
            int expected = MatchDirector.Instance.BotCount + 1;
            if (mode != TeamMode.Solo)
                yield return WaitUntil(() => Combatant.All.Count(c => c != null && c.HasTeam) == expected, 10f, "every combatant in a team");
            yield return null;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Bots leave the player alone so nothing but the test hurts it.
            BotRules.Current = new BotRules { TargetsPlayers = false };
            TeamRules.Current = new TeamRules { ReviveSeconds = 1.5f };
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            TeamMatch.Mode = TeamMode.Solo;
            BotRules.Current = GameData.Rules.Bot;
            TeamRules.Current = GameData.Rules.Team;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static Combatant Me => Combatant.LocalPlayer;
        static List<Combatant> TeamOf(int team) => Combatant.All.Where(c => c != null && c.TeamId.Value == team).OrderBy(c => c.TeamSlot.Value).ToList();
        static List<Combatant> Mates => TeamOf(Me.TeamId.Value).Where(c => c != Me).ToList();
        static Combatant Enemy => Combatant.All.First(c => c != null && c.HasTeam && c.TeamId.Value != Me.TeamId.Value && !c.Dead.Value);
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static void EndProtection()
        {
            foreach (var c in Combatant.All)
            {
                var protection = c != null ? c.GetComponent<SpawnProtection>() : null;
                if (protection != null) protection.ServerEnd();
            }
        }

        static void Hit(Combatant victim, Combatant by, float amount) =>
            victim.ServerTakeDamage(new DamageInfo { Amount = amount, Attacker = by, Zone = HitZone.Body, WeaponId = 3, Cause = DamageCause.Shot });

        static void KillOutright(Combatant victim, Combatant by)
        {
            for (int i = 0; i < 4 && !victim.Dead.Value; i++) Hit(victim, by, 1000f);
        }

        [UnityTest]
        public IEnumerator Squad_PlayerAndThreeBots_ShareATeamAndAnElevator()
        {
            yield return Load(TeamMode.Squad);
            Assert.AreEqual(1, Me.TeamId.Value, "the player is counted first");
            Assert.AreEqual(1, Me.TeamSlot.Value);
            Assert.AreEqual(3, Mates.Count);
            CollectionAssert.AreEquivalent(new[] { 2, 3, 4 }, Mates.Select(c => c.TeamSlot.Value));
            foreach (var mate in Mates) Assert.Less(Flat(mate.transform.position, Me.transform.position), 3f, mate.TeamName.Value + " should start in the player's elevator");
            int teams = Combatant.All.Select(c => c.TeamId.Value).Distinct().Count();
            Assert.AreEqual(Teams.TeamCount(MatchDirector.Instance.BotCount + 1, 4), teams);
            yield return WaitUntil(() => Mates.All(c => !string.IsNullOrEmpty(c.TeamName.Value)), 3f, "teammates' names");

            // The team on the HUD: four rows, three teammates on the compass and the minimap.
            yield return WaitUntil(() => TeamHud.Instance != null && TeamHud.Instance.MembersShown == 4, 3f, "four member rows");
            yield return WaitUntil(() => TeamHud.Instance.CompassMembersShown == 3 && TeamHud.Instance.MinimapMembersShown == 3, 3f, "teammates on the compass and minimap");
        }

        [UnityTest]
        public IEnumerator LethalHit_KnocksDown_TeammatesDoNoDamage_KnockIsPosted()
        {
            yield return Load(TeamMode.Squad);
            EndProtection();
            var enemy = Enemy;
            var mate = Mates[0];
            int knocks = MatchState.Instance.KnocksPosted;

            Hit(Me, mate, 50f);
            Assert.AreEqual(100f, Me.Hp.Value, 1e-3f, "friendly fire is off");

            Hit(Me, enemy, 500f);
            Assert.IsTrue(Me.Knocked.Value, "a lethal hit knocks down while teammates stand");
            Assert.IsFalse(Me.Dead.Value);
            Assert.AreEqual(TeamRules.Current.KnockedHp, Me.Hp.Value, 1e-3f);
            Assert.IsTrue(Me.Holstered.Value, "the weapon is put away");
            Assert.AreEqual(Stance.Prone, Me.GetComponent<CombatantStance>().Current, "knocked lies down");
            Assert.IsFalse(Me.ServerFire(Vector3.forward), "no shooting while knocked");
            Assert.IsFalse(Me.ServerSelectSlot(0), "no drawing a weapon while knocked");
            yield return WaitUntil(() => MatchState.Instance.KnocksPosted > knocks, 3f, "the knock in the feed");
            yield return WaitUntil(() => TeamHud.Instance.KnockedShown, 3f, "the knocked band");
            Assert.AreEqual(0, MatchState.Instance.PlacementOf(Me));

            // Hit again while knocked: dead, credited to the attacker.
            int kills = MatchState.Instance.KillsOf(enemy);
            Hit(Me, enemy, 500f);
            Assert.IsTrue(Me.Dead.Value);
            yield return WaitUntil(() => MatchState.Instance.KillsOf(enemy) == kills + 1, 3f, "the kill credited");
            Assert.AreEqual(0, MatchState.Instance.PlacementOf(Me), "the team still plays: no placement yet");
            Assert.AreEqual(MatchOutcome.Playing, MatchState.Instance.LocalOutcome);
        }

        [UnityTest]
        public IEnumerator Knocked_BleedsOut_CreditedToTheKnocker()
        {
            TeamRules.Current = new TeamRules { BleedPerSecond = 50f };
            yield return Load(TeamMode.Squad);
            EndProtection();
            var enemy = Enemy;
            var mate = Mates[0];
            int kills = MatchState.Instance.KillsOf(enemy);
            Hit(mate, enemy, 500f);
            Assert.IsTrue(mate.Knocked.Value);
            Assert.Greater(mate.BleedSecondsLeft, 0f);
            yield return WaitUntil(() => mate.Dead.Value, 5f, "bleeding out");
            // TeamDirector clears Knocked on its next tick, a frame after Dead.
            yield return WaitUntil(() => !mate.Knocked.Value, 2f, "the dead are not knocked");
            yield return WaitUntil(() => MatchState.Instance.KillsOf(enemy) == kills + 1, 3f, "the bleed-out credited to the knocker");
            Assert.AreEqual((byte)DamageCause.Bleed, MatchState.Instance.Feed.Last(e => e.VictimId == mate.ObjectId).Cause);
        }

        [UnityTest]
        public IEnumerator HoldingF_RevivesAKnockedTeammate()
        {
            TeamRules.Current = new TeamRules { ReviveSeconds = 1.5f, BotReviveSearchMeters = 0f };
            yield return Load(TeamMode.Squad);
            // F reaches the character only once the doors are open.
            yield return WaitUntil(() => !SpawnProtection.InputBlocked, 30f, "the doors open");
            EndProtection();
            var mate = Mates[0];
            Hit(mate, Enemy, 500f);
            Assert.IsTrue(mate.Knocked.Value);
            // Right next to the player whatever the bot did meanwhile.
            var beside = Me.transform.position + Me.transform.right * 1f;
            var agent = mate.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.enabled) agent.Warp(beside);
            else mate.transform.position = beside;
            yield return null;
            Assert.AreEqual(mate, TeamHud.ReviveCandidate(Me));
            yield return WaitUntil(() => TeamHud.Instance.RevivePromptShown, 3f, "the revive prompt");

            _input.Frame.InteractHeld = true;
            yield return WaitUntil(() => Me.Reviving && mate.BeingRevived, 3f, "the revive under way");
            float hp = mate.Hp.Value;
            yield return WaitUntil(() => TeamHud.Instance.ReviveBarShown, 2f, "the revive bar");
            yield return WaitUntil(() => !mate.Knocked.Value, 5f, "the teammate back up");
            _input.Frame.InteractHeld = false;
            Assert.LessOrEqual(hp, TeamRules.Current.KnockedHp, "no bleeding while being revived");
            Assert.IsFalse(mate.Dead.Value);
            Assert.AreEqual(TeamRules.Current.ReviveHp, mate.Hp.Value, 1e-3f);
            Assert.IsFalse(Me.Reviving);
        }

        [UnityTest]
        public IEnumerator LettingGoOfF_StopsTheRevive()
        {
            TeamRules.Current = new TeamRules { ReviveSeconds = 5f, BotReviveSearchMeters = 0f };
            yield return Load(TeamMode.Squad);
            EndProtection();
            var mate = Mates[0];
            Hit(mate, Enemy, 500f);
            Assert.IsTrue(Me.ServerBeginRevive(mate), "teammates start next to each other in the elevator");
            Assert.IsFalse(Mates[1].ServerBeginRevive(mate), "one reviver at a time");
            Me.ServerCancelRevive();
            Assert.IsFalse(Me.Reviving);
            Assert.IsFalse(mate.BeingRevived);
            Assert.IsTrue(mate.Knocked.Value);
            Assert.IsFalse(Me.ServerBeginRevive(Enemy), "an enemy is never revived");
        }

        [UnityTest]
        public IEnumerator AWipedTeam_DiesAndPlacesTogether()
        {
            yield return Load(TeamMode.Squad);
            EndProtection();
            var enemyTeam = TeamOf(Enemy.TeamId.Value);
            Assert.AreEqual(4, enemyTeam.Count);
            for (int i = 0; i < 3; i++)
            {
                Hit(enemyTeam[i], Me, 500f);
                Assert.IsTrue(enemyTeam[i].Knocked.Value, "knocked while the last member stands");
            }
            // The last one on its feet dies at once, and nobody is left to revive the others.
            Hit(enemyTeam[3], Me, 500f);
            Assert.IsTrue(enemyTeam[3].Dead.Value, "the last member standing is not knocked");
            yield return WaitUntil(() => enemyTeam.All(c => c.Dead.Value), 3f, "the knocked members finished");
            int teams = Combatant.All.Select(c => c.TeamId.Value).Distinct().Count();
            yield return WaitUntil(() => MatchState.Instance.TeamPlacementOf(enemyTeam[0].TeamId.Value) == teams, 3f, "the team placed last");
            foreach (var c in enemyTeam) Assert.AreEqual(teams, MatchState.Instance.PlacementOf(c));
            Assert.Greater(MatchState.Instance.KillsOf(Me), 0);
        }

        [UnityTest]
        public IEnumerator LastTeamStanding_Wins_EvenWithTheTeamDown()
        {
            yield return Load(TeamMode.Squad);
            EndProtection();
            // The player is knocked; a teammate stands, so the team is still in it.
            Hit(Me, Enemy, 500f);
            Assert.IsTrue(Me.Knocked.Value);
            foreach (var c in Combatant.All.Where(c => c != null && c.TeamId.Value != Me.TeamId.Value).ToList()) KillOutright(c, Mates[0]);
            yield return WaitUntil(() => MatchState.Instance.IsOver, 5f, "the match decided");
            Assert.AreEqual(Me.TeamId.Value, MatchState.Instance.WinnerTeam);
            Assert.AreEqual(1, MatchState.Instance.PlacementOf(Me));
            Assert.AreEqual(MatchOutcome.Won, MatchState.Instance.LocalOutcome);
            yield return WaitUntil(() => ResultScreen.Instance != null && ResultScreen.Instance.IsShown, 3f, "the result screen");
            StringAssert.Contains("팀", ResultScreen.Instance.PlacementText);
        }

        [UnityTest]
        public IEnumerator Bots_ReviveAKnockedTeammate()
        {
            TeamRules.Current = new TeamRules { ReviveSeconds = 1.5f, BotReviveCalmSeconds = 0.5f };
            yield return Load(TeamMode.Squad);
            yield return WaitUntil(() => !SpawnProtection.InputBlocked, 30f, "the doors open");
            EndProtection();
            var down = Mates[0];
            Hit(down, Enemy, 500f);
            Assert.IsTrue(down.Knocked.Value);
            yield return WaitUntil(() => down.BeingRevived || !down.Knocked.Value, 20f, "a bot teammate reviving");
            yield return WaitUntil(() => !down.Knocked.Value, 10f, "the teammate back up");
            Assert.IsFalse(down.Dead.Value);
        }

        [UnityTest]
        public IEnumerator Solo_HasNoTeams_AndALethalHitKills()
        {
            yield return Load(TeamMode.Solo);
            yield return null;
            Assert.IsTrue(Combatant.All.All(c => c == null || !c.HasTeam), "solo leaves everyone without a team");
            EndProtection();
            var bot = Combatant.All.First(c => c != null && c != Me);
            Hit(Me, bot, 500f);
            Assert.IsTrue(Me.Dead.Value);
            Assert.IsFalse(Me.Knocked.Value);
            Assert.IsFalse(TeamHud.Instance != null && TeamHud.Instance.Active);
        }
    }
}
