using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Integration wiring of the armour, death crate, grenade and bot features (the builders add them to the
    // prefabs and scenes). These fail until TestBoxBuilder.Build and ZoneDBuilder.Build have been run again.
    // The test assembly does not reference Game.AI, so the bot component is looked up by name.
    public class WiringTests
    {
        static readonly Type BotType = Type.GetType("Game.AI.BotController, Game.AI");

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        static IEnumerator Load(string scene)
        {
            SceneManager.LoadScene(scene);
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 20f, "local player in " + scene);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            BotRules.Current = new BotRules();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static void AssertGear(GameObject go, string what)
        {
            Assert.IsNotNull(go.GetComponent<CombatantGear>(), what + " has no CombatantGear");
            Assert.IsNotNull(go.GetComponent<DeathCrate>(), what + " has no DeathCrate");
            Assert.IsNotNull(go.GetComponent<ThrowableUser>(), what + " has no ThrowableUser");
            Assert.IsNotNull(go.GetComponent<ThrowableView>(), what + " has no ThrowableView");
        }

        [UnityTest]
        public IEnumerator TestBox_PlayerAndDummies_CarryGearCrateAndGrenadeComponents()
        {
            yield return Load("TestBox");
            AssertGear(Combatant.LocalPlayer.gameObject, "the player");
            int dummies = 0;
            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsPlayer) continue;
                AssertGear(c.gameObject, "a training dummy");
                dummies++;
            }
            Assert.Greater(dummies, 0, "the test box should have training dummies");
        }

        [UnityTest]
        public IEnumerator SpawnablePrefabs_IncludeTheThrownGrenadeAndTheBot()
        {
            yield return Load("TestBox");
            var network = UnityEngine.Object.FindFirstObjectByType<NetworkManager>();
            Assert.IsNotNull(network, "no NetworkManager in the scene");
            var prefabs = network.SpawnablePrefabs;
            Assert.IsNotNull(prefabs, "the NetworkManager has no spawnable prefab list");
            bool grenade = false, bot = false;
            for (int i = 0; i < prefabs.GetObjectCount(); i++)
            {
                var prefab = prefabs.GetObject(true, i);
                if (prefab == null) continue;
                if (prefab.GetComponent<ThrownItem>() != null) grenade = true;
                if (BotType != null && prefab.GetComponent(BotType) != null) bot = true;
            }
            Assert.IsTrue(grenade, "the ThrownGrenade prefab (with a ThrownItem) is not in DefaultPrefabObjects");
            Assert.IsTrue(bot || BotType == null, "the Bot prefab is not in DefaultPrefabObjects");
        }

        [UnityTest]
        public IEnumerator ZoneD_Match_SpawnsThreeBotsOnTheNavMesh_AndCountsThem()
        {
            if (BotType == null) Assert.Ignore("Game.AI.BotController is not in this build");
            // The bots leave the human player alone: this is about where they are, not what they do.
            BotRules.Current = new BotRules { TargetsPlayers = false };
            yield return Load("ZoneD");

            var bots = new List<Combatant>();
            yield return WaitUntil(() =>
            {
                bots.Clear();
                foreach (var c in Combatant.All)
                    if (c != null && !c.IsPlayer && c.GetComponent(BotType) != null) bots.Add(c);
                return bots.Count == 3;
            }, 15f, "three bots spawned in ZoneD");

            foreach (var bot in bots)
                Assert.IsNull(bot.GetComponent("TrainingDummy"), "bots are not training dummies");
            foreach (var c in Combatant.All)
                if (c != null && !c.IsPlayer) Assert.IsNotNull(c.GetComponent(BotType), "ZoneD should hold bots only, found " + c.name);

            // The controller snaps its agent onto the navmesh on its first updates.
            yield return WaitUntil(() =>
            {
                foreach (var bot in bots)
                    if (!NavMesh.SamplePosition(bot.transform.position, out var hit, 0.6f, NavMesh.AllAreas)) return false;
                return true;
            }, 10f, "all bots standing on the navmesh");

            var state = MatchState.Instance;
            Assert.IsNotNull(state, "no MatchState");
            yield return WaitUntil(() => state.TotalCount == 4 && state.AliveCount == 4, 10f, "the player and three bots counted by the match");
            for (int i = 0; i < bots.Count; i++)
                StringAssert.StartsWith("봇 ", state.ServerNameOf(bots[i]), "a bot is named for the kill feed");
        }
    }
}
