using System;
using System.Collections;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // ADR-258: the solo start of the menu is a match of 80 (the player and 79 bots) when the data says so.
    public class MenuMatchFillPlayTests
    {
        MatchRules _saved;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _saved = MatchRules.Current;
            MatchRules.Current = new MatchRules { FillToParticipants = true };
            NetSession.Reset();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MatchRules.Current = _saved;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator SoloStart_FillsTheMatchToEighty_WithBots()
        {
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 120f;
            // One car per team (ADR-260): the map's elevators are the slots, so the match is as big as they hold (80 once the map has 20 x 4).
            yield return new WaitUntil(() => MatchDirector.Instance != null && MatchDirector.Instance.Elevators != null);
            int expectedBots = Mathf.Min(79, MatchDirector.Instance.Elevators.Sites.Count - 1);
            while (Combatant.All.Count < expectedBots + 1 && Time.realtimeSinceStartup < end) yield return null;
            int people = 0, bots = 0;
            foreach (var c in Combatant.All) if (c != null) { if (c.IsPlayer) people++; else bots++; }
            Debug.Log($"[MenuFill] combatants {Combatant.All.Count}: {people} people, {bots} bots");
            Assert.AreEqual(1, people, "one person");
            Assert.AreEqual(expectedBots, bots, "the rest are bots, as many as the elevators hold");
        }
    }
}
