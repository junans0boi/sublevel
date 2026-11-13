using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // ADR-055 (M2-3, #57) in the World scene as a host: everyone starts in an elevator station behind a closed door,
    // nobody leaves or fires before it opens, and spawn protection holds until it runs out or the player fires.
    // The input hold needs PlayerController to read SpawnProtection.InputBlocked (one line, see the issue).
    public class DeploymentPlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = false;
                return f;
            }
        }

        const int Rifle = 3;             // weapons.json
        const float SiteTolerance = 1.5f;
        const float BotSiteTolerance = 2.5f; // bots are snapped onto the navmesh

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

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "local player in World");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static int NearestSite(Vector3 position, out float distance)
        {
            var sites = ElevatorDoors.Instance.Sites;
            int best = -1;
            distance = float.MaxValue;
            for (int i = 0; i < sites.Count; i++)
            {
                float d = Flat(position, sites[i].Spawn);
                if (d < distance) { distance = d; best = i; }
            }
            return best;
        }

        static SpawnProtection ServerProtection(Combatant c)
        {
            var protection = c.GetComponent<SpawnProtection>();
            Assert.IsNotNull(protection, c.name + " should have spawn protection (added by MatchDirector on the server)");
            return protection;
        }

        IEnumerator GiveRifle(Combatant player)
        {
            player.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => player.ActiveWeaponId == Rifle && player.ActiveMag > 0, 3f, "rifle in hand");
        }

        [UnityTest]
        public IEnumerator LocalPlayer_StartsInAStation_BehindAClosedDoor()
        {
            Assert.IsNotNull(ElevatorDoors.Instance, "the World scene should start its match from the elevators");
            Assert.AreEqual(WorldInfo.Current.Elevators.Count, ElevatorDoors.Instance.Sites.Count);
            Assert.AreEqual(ElevatorDoors.Instance.Sites.Count, ElevatorDoors.Instance.Doors.Count, "one door per station");

            var player = Combatant.LocalPlayer;
            int site = NearestSite(player.transform.position, out float distance);
            Assert.Less(distance, SiteTolerance, "the player should start at an elevator's spawn point");
            Assert.IsTrue(SpawnProtection.Deployed);
            Assert.IsTrue(SpawnProtection.InputBlocked, "the doors should still be closed right after the start");
            Assert.Greater(SpawnProtection.DoorSecondsLeft, 0f);
            Assert.LessOrEqual(SpawnProtection.DoorSecondsLeft, DeploymentRules.Current.DoorDelaySeconds + 0.01f);

            // Looking out of the station, the door is the first thing in the way.
            var door = ElevatorDoors.Instance.Doors[site];
            Assert.IsTrue(door.Blocking);
            var s = ElevatorDoors.Instance.Sites[site];
            var eye = s.Spawn + Vector3.up * 1.5f;
            var outward = Quaternion.Euler(0f, s.YawDeg, 0f) * Vector3.forward;
            var first = FirstHit(eye, outward, player.transform);
            Assert.IsNotNull(first, "something should close the station's front");
            Assert.AreEqual(door, first.GetComponentInParent<ElevatorDoor>(), "the station's front should be its door, not " + first.name);

            // Then it opens on its own, sinks away and stops blocking.
            yield return WaitUntil(() => !SpawnProtection.InputBlocked, DeploymentRules.Current.DoorDelaySeconds + 2f, "the doors to open");
            yield return WaitUntil(() => !door.Blocking, DeploymentRules.Current.DoorSlideSeconds + 2f, "the door to sink into the floor");
            first = FirstHit(eye, outward, player.transform);
            Assert.IsTrue(first == null || first.GetComponentInParent<ElevatorDoor>() == null, "the open door should be out of the way");
        }

        // The nearest collider along a ray within 5 m, ignoring the player's own.
        static Collider FirstHit(Vector3 from, Vector3 direction, Transform ignore)
        {
            Collider best = null;
            float bestDistance = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(from, direction, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(ignore) || hit.distance >= bestDistance) continue;
                best = hit.collider;
                bestDistance = hit.distance;
            }
            return best;
        }

        [UnityTest]
        public IEnumerator BeforeTheDoorOpens_ThePlayerCanNeitherLeaveNorFire()
        {
            var player = Combatant.LocalPlayer;
            yield return GiveRifle(player);
            Assert.IsTrue(SpawnProtection.InputBlocked, "the doors should still be closed");
            var start = player.transform.position;
            int mag = player.ActiveMag;
            var outward = player.transform.forward;

            // Walk out and shoot for as long as the doors are shut.
            _input.Frame.Move = new Vector2(0f, 1f);
            _input.Frame.FireHeld = true;
            while (SpawnProtection.InputBlocked)
            {
                Assert.Less(Vector3.Dot(player.transform.position - start, outward), Buildings.ElevatorBay / 2f,
                    "the player should not get past the closed door");
                Assert.AreEqual(mag, player.ActiveMag, "no round should be fired while the doors are closed");
                yield return null;
            }
            _input.Frame.FireHeld = false;
            Assert.IsFalse(ServerProtection(player).EndedEarly, "nothing tried behind the door should cost the protection");

            // Once open, the same input walks out of the station.
            yield return new WaitForSeconds(DeploymentRules.Current.DoorSlideSeconds + 2.5f);
            _input.Frame.Move = Vector2.zero;
            Assert.Greater(Flat(player.transform.position, start), Buildings.ElevatorBay, "the player should have walked out of the open station");
        }

        [UnityTest]
        public IEnumerator Protection_IgnoresDamage_UntilThePlayerFires()
        {
            var player = Combatant.LocalPlayer;
            var protection = ServerProtection(player);
            Assert.IsTrue(protection.IsProtected, "protected from the start");
            float hp = player.Hp.Value;
            player.ServerTakeDamage(30f);
            yield return null;
            Assert.AreEqual(hp, player.Hp.Value, 1e-3f, "no damage while the doors are closed");

            yield return WaitUntil(() => !SpawnProtection.InputBlocked, DeploymentRules.Current.DoorDelaySeconds + 2f, "the doors to open");
            Assert.IsTrue(protection.IsProtected, "still protected after the doors open");
            Assert.Greater(protection.SecondsLeft, DeploymentRules.Current.ProtectionSeconds - 1f);
            yield return WaitUntil(() => SpawnProtection.LocalSecondsLeft > 0f && SpawnProtection.LocalSecondsLeft <= DeploymentRules.Current.ProtectionSeconds + 0.1f,
                2f, "the HUD view of the protection countdown");
            hp = player.Hp.Value;
            player.ServerTakeDamage(30f);
            Assert.AreEqual(hp, player.Hp.Value, 1e-3f, "no damage during protection");

            // The first shot ends it.
            yield return GiveRifle(player);
            // Pressed for single-shot weapons, held for automatic ones.
            _input.Frame.FirePressed = true;
            _input.Frame.FireHeld = true;
            yield return WaitUntil(() => protection.EndedEarly, 2f, "protection to end on the first shot");
            _input.Frame.FireHeld = false;
            Assert.IsFalse(protection.IsProtected);
            yield return WaitUntil(() => SpawnProtection.LocalSecondsLeft == 0f, 2f, "the HUD view to drop the countdown");

            hp = player.Hp.Value;
            player.ServerTakeDamage(10f);
            Assert.AreEqual(hp - 10f, player.Hp.Value, 1e-3f, "damage counts again once protection is gone");

            // The armour's damage filter still runs behind the protection's.
            var gear = player.GetComponent<CombatantGear>();
            if (gear != null)
            {
                gear.ServerEquip(ArmorSlot.Vest, 1);
                player.ServerTakeDamage(new DamageInfo { Amount = 20f, Zone = HitZone.Body, Cause = DamageCause.Shot });
                Assert.Greater(gear.LastAbsorbed, 0f, "the vest should still take its share (filters chained, not replaced)");
            }
        }

        [UnityTest]
        public IEnumerator Bots_StartInStationsToo_OnePerElevator()
        {
            var bots = new List<Combatant>();
            foreach (var c in Combatant.All) if (c != null && !c.IsPlayer) bots.Add(c);
            if (bots.Count == 0) Assert.Ignore("this World scene has no bots");

            var taken = new HashSet<int>();
            int site = NearestSite(Combatant.LocalPlayer.transform.position, out _);
            taken.Add(site);
            foreach (var bot in bots)
            {
                int at = NearestSite(bot.transform.position, out float distance);
                Assert.Less(distance, BotSiteTolerance, bot.name + " should start at an elevator");
                if (ElevatorDoors.Instance.Sites.Count >= bots.Count + 1)
                    Assert.IsTrue(taken.Add(at), bot.name + " shares an elevator while others are free");
                var protection = ServerProtection(bot);
                Assert.IsTrue(protection.IsHeld, bot.name + " should be held behind its door");
                Assert.IsTrue(protection.IsProtected);
            }

            // Held bots stay put until the doors open.
            var before = new Vector3[bots.Count];
            for (int i = 0; i < bots.Count; i++) before[i] = bots[i].transform.position;
            yield return new WaitForSeconds(1f);
            if (SpawnProtection.InputBlocked)
                for (int i = 0; i < bots.Count; i++)
                    Assert.Less(Flat(bots[i].transform.position, before[i]), 0.3f, bots[i].name + " moved while its door was closed");

            yield return WaitUntil(() => !SpawnProtection.InputBlocked, DeploymentRules.Current.DoorDelaySeconds + 2f, "the doors to open");
            yield return null;
            foreach (var bot in bots) Assert.IsFalse(ServerProtection(bot).IsHeld, bot.name + " should be released with the doors");
        }
    }
}
