using System;
using System.Collections;
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
    // ADR-332 (#424) in the World scene as a host: one person in squad mode is a squad of one, the bots are teams of four
    // (the smallest may be smaller), every team shares one elevator, and the team HUD works with no teammates.
    public class SoloSquadPlayTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            TeamMatch.Mode = TeamMode.Solo;
            BotRules.Current = GameData.Rules.Bot;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator SquadAlone_IsATeamOfOne_BotsInFours_OneCarPerTeam()
        {
            BotRules.Current = new BotRules { TargetsPlayers = false };
            TeamMatch.Mode = TeamMode.Squad;
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null || MatchDirector.Instance == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            int bots = MatchDirector.Instance.BotCount;
            end = Time.realtimeSinceStartup + 20f;
            while (Combatant.All.Count(c => c != null && c.HasTeam) < bots + 1)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("not everyone has a team");
                yield return null;
            }
            yield return new WaitForSeconds(1f);

            var me = Combatant.LocalPlayer;
            Assert.AreEqual(1, me.TeamId.Value);
            Assert.AreEqual(1, me.TeamSlot.Value);
            Assert.AreEqual(0, Combatant.All.Count(c => c != null && me.IsTeammateOf(c)), "nobody else is on my team");

            var teams = Combatant.All.Where(c => c != null).GroupBy(c => c.TeamId.Value).ToList();
            var expected = TeamLayout.For(1, bots, 4);
            Assert.AreEqual(expected.TeamCount, teams.Count, "one team per planned team");
            foreach (var g in teams) Assert.AreEqual(expected.SizeOfTeam(g.Key), g.Count(), "team " + g.Key + " size");
            Assert.AreEqual(1, teams.First(g => g.Key == 1).Count());
            Assert.LessOrEqual(teams.Where(g => g.Key != 1).Max(g => g.Count()), 4);
            Debug.Log($"[SoloSquad] bots {bots}, teams {teams.Count}, sizes {string.Join(",", teams.OrderBy(g => g.Key).Select(g => g.Count()))}");

            // One elevator per team, never shared between teams (ADR-260).
            foreach (var g in teams)
            {
                var first = g.First().transform.position;
                foreach (var c in g) Assert.Less(Vector2.Distance(new Vector2(c.transform.position.x, c.transform.position.z), new Vector2(first.x, first.z)), 3f, "team " + g.Key + " starts together");
            }

            // The team HUD with one member.
            Assert.IsNotNull(TeamHud.Instance);
            end = Time.realtimeSinceStartup + 5f;
            while (TeamHud.Instance.MembersShown != 1) { if (Time.realtimeSinceStartup > end) Assert.Fail("member rows " + TeamHud.Instance.MembersShown); yield return null; }
            Assert.AreEqual(0, TeamHud.Instance.CompassMembersShown);
            Assert.AreEqual(0, TeamHud.Instance.MinimapMembersShown);

            yield return UiShot.Save("Logs/squadshots/solo_squad_hud.png", 1920, 1080);
        }
    }
}
