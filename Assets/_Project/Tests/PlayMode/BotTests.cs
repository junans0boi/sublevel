using System;
using System.Collections;
using System.Reflection;
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
    // M2-2: bots in the zone D scene (it has a navmesh). There is no Bot prefab yet, so each test builds its bots
    // the way the prefab will be built: the training dummy's body (Combatant + stance + capsule) with the dummy's
    // own behaviour switched off and a BotController added. The controller is found by name because this test
    // assembly does not reference Game.AI.
    public class BotTests
    {
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Smg = 2; // "Ordo": automatic, light ammo

        static readonly Type BotType = Type.GetType("Game.AI.BotController, Game.AI");
        static readonly Vector3 Parking = new Vector3(0f, 300f, 0f);

        int _parked;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (BotType == null) Assert.Ignore("Game.AI.BotController is not in this build");
            // The human player in the scene is not part of these scenarios.
            BotRules.Current = new BotRules { TargetsPlayers = false };
            _parked = 0;
            SceneManager.LoadScene("ZoneD");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player in ZoneD");
            yield return WaitUntil(() => Combatant.All.Count > 1, 15f, "the scene's training dummies");
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

        // ---- Building bots ----

        // A spawned combatant with nothing driving it yet, parked out of everyone's sight.
        Combatant SpawnBody()
        {
            var director = MatchDirector.Instance;
            var prefab = (NetworkObject)typeof(MatchDirector).GetField("_dummyPrefab", Priv).GetValue(director);
            var network = (NetworkManager)typeof(MatchDirector).GetField("_networkManager", Priv).GetValue(director);
            var body = UnityEngine.Object.Instantiate(prefab, Parking + Vector3.right * (3f * _parked++), Quaternion.identity);
            SwitchOffDummy(body.gameObject);
            network.ServerManager.Spawn(body);
            return body.GetComponent<Combatant>();
        }

        static void SwitchOffDummy(GameObject go)
        {
            if (go.GetComponent("TrainingDummy") is Behaviour dummy) dummy.enabled = false;
        }

        // The scene's own dummies leave the match: switched off, moved far below the floor and killed.
        static void RetireOthers(params Combatant[] keep)
        {
            var all = Combatant.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var c = all[i];
                if (c == null || c.IsPlayer || Array.IndexOf(keep, c) >= 0) continue;
                SwitchOffDummy(c.gameObject);
                c.transform.position = new Vector3(3f * i, -200f, 0f);
                c.ServerTakeDamage(100000f);
            }
        }

        static void Place(Combatant body, Vector3 position, Vector3 lookAt)
        {
            var facing = lookAt - position;
            facing.y = 0f;
            body.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing));
        }

        static void Arm(Combatant body)
        {
            var weapon = GameData.Weapons.Find(Smg);
            Assert.IsTrue(body.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Smg, Amount = weapon.MagSize }));
            Assert.IsTrue(body.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = weapon.Ammo, Amount = 300 }));
        }

        static void MakeBot(Combatant body) => body.gameObject.AddComponent(BotType);

        // Two points on the navmesh `meters` apart, on the same level, with nothing between them from knee to eye.
        static bool FindOpenLine(float meters, out Vector3 from, out Vector3 to)
        {
            from = to = default;
            var zone = ZoneInfo.Current;
            if (zone == null) return false;
            foreach (var point in zone.ConvergencePoints)
            {
                if (!NavMesh.SamplePosition(point.position, out var start, 2f, NavMesh.AllAreas)) continue;
                for (int deg = 0; deg < 360; deg += 15)
                {
                    var dir = Quaternion.Euler(0f, deg, 0f) * Vector3.forward;
                    if (!NavMesh.SamplePosition(start.position + dir * meters, out var end, 1f, NavMesh.AllAreas)) continue;
                    if (Mathf.Abs(end.position.y - start.position.y) > 0.5f) continue;
                    if (!Clear(start.position, end.position, 0.5f) || !Clear(start.position, end.position, 1.1f) || !Clear(start.position, end.position, 1.6f)) continue;
                    from = start.position;
                    to = end.position;
                    return true;
                }
            }
            return false;
        }

        static bool Clear(Vector3 a, Vector3 b, float height) =>
            !Physics.Linecast(a + Vector3.up * height, b + Vector3.up * height, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        // ---- Scenarios ----

        [UnityTest]
        [Timeout(150000)]
        public IEnumerator TwoArmedBots_ThirtyMetresApartInSight_FightUntilOneDies()
        {
            var a = SpawnBody();
            var b = SpawnBody();
            yield return WaitUntil(() => a.IsServerInitialized && b.IsServerInitialized, 5f, "bot bodies spawned");
            RetireOthers(a, b);
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(FindOpenLine(30f, out var from, out var to), "zone D should have a 30 m open line from a convergence point");
            Place(a, from, to);
            Place(b, to, from);
            Arm(a);
            Arm(b);
            yield return new WaitForFixedUpdate();
            MakeBot(a);
            MakeBot(b);

            yield return WaitUntil(() => a.Dead.Value || b.Dead.Value, 100f, "one of the two bots to die");
            var winner = a.Dead.Value ? b : a;
            var loser = a.Dead.Value ? a : b;
            Assert.IsTrue(loser.Dead.Value);
            Assert.AreEqual(0f, loser.Hp.Value, 1e-3f);
            var weapon = GameData.Weapons.Find(Smg);
            int roundsLeft = winner.ActiveMag + winner.ReserveFor(weapon.Ammo);
            Assert.Less(roundsLeft, weapon.MagSize + 300, "the winner did the shooting");
            // Scored like any other kill (the count reaches the host's client side a network tick later).
            yield return WaitUntil(() => MatchDirector.Instance.State.KillsOf(winner) == 1, 5f, "the kill to be counted for the winner");
        }

        [UnityTest]
        public IEnumerator UnarmedBot_WalksToAWeaponOnTheGround_AndPicksItUp()
        {
            var bot = SpawnBody();
            yield return WaitUntil(() => bot.IsServerInitialized, 5f, "bot body spawned");
            RetireOthers(bot);
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(FindOpenLine(8f, out var from, out var to), "zone D should have 8 m of open floor at a convergence point");
            Place(bot, from, to);
            Assert.AreEqual(0, bot.ActiveWeaponId, "the bot starts with empty hands");
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Weapon, WeaponId = Smg, Amount = 30 }, to + Vector3.up * 0.3f);
            yield return new WaitForFixedUpdate();
            MakeBot(bot);

            yield return WaitUntil(() => bot.ActiveWeaponId != 0, 30f, "the bot to pick up a weapon");
            Assert.Greater(Vector3.Distance(bot.transform.position, from), 2f, "it had to walk there");
            Assert.Greater(bot.ActiveMag, 0, "the weapon came with its magazine");
        }

        [UnityTest]
        public IEnumerator BotWithNobodyAround_Roams_AndStaysOnTheNavMesh()
        {
            // Nothing to pick up on the way: pure wandering.
            BotRules.Current.LootRadius = 0f;
            var bot = SpawnBody();
            yield return WaitUntil(() => bot.IsServerInitialized, 5f, "bot body spawned");
            RetireOthers(bot);
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(NavMesh.SamplePosition(ZoneInfo.Current.ConvergencePoints[0].position, out var start, 2f, NavMesh.AllAreas));
            Place(bot, start.position, start.position + Vector3.forward);
            Arm(bot);
            yield return new WaitForFixedUpdate();
            MakeBot(bot);

            float began = Time.time;
            float farthest = 0f;
            while (Time.time < began + 10f)
            {
                var at = bot.transform.position;
                var moved = at - start.position;
                moved.y = 0f;
                farthest = Mathf.Max(farthest, moved.magnitude);
                if (Time.time > began + 0.5f)
                    Assert.IsTrue(NavMesh.SamplePosition(at, out _, 0.5f, NavMesh.AllAreas), "the bot left the navmesh at " + at);
                yield return null;
            }
            Assert.Greater(farthest, 10f, "a roaming bot should get more than 10 m away within 10 s");
            Assert.IsFalse(bot.Dead.Value);
        }
    }
}
