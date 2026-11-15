using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // ADR-147: a door breaks under shots (rifle, shotgun), a blow and a ram, stays an open doorway, and nothing is in the way after.
    public class DoorBreakPlayTests
    {
        sealed class Walk : IPlayerInput
        {
            public bool Forward;
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1, Move = Forward ? Vector2.up : Vector2.zero };
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator ShotsBlowsAndRams_BreakTheDoor_ThenSightSoundAndBodiesPass()
        {
            var walk = new Walk();
            PlayerController.InputOverride = walk;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            var me = Combatant.LocalPlayer;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 90 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 4, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 21, Amount = 1 });
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(3000f, -0.5f, 3000f);
            floor.transform.localScale = new Vector3(80f, 1f, 60f);
            var backstop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backstop.transform.position = new Vector3(3000f, 1.5f, 3012f);
            backstop.transform.localScale = new Vector3(80f, 4f, 0.5f);
            me.StanceSpreadFactor = 0f;
            var system = DoorSystem.Instance;
            Assert.IsNotNull(system);

            Door MakeDoor(float x)
            {
                var d = system.Add(Door.Create(system.transform, system.Count, new Vector3(x, 0f, 3002f), Quaternion.LookRotation(Vector3.back), 1.3f, 2.3f));
                Physics.SyncTransforms();
                return d;
            }
            IEnumerator Stand(float x)
            {
                PlayerController.Local.Teleport(new Vector3(x, 0.1f, 3000f));
                PlayerController.Local.LookAt(new Vector3(x, 1.6f, 3010f));
                yield return new WaitForSeconds(0.8f);
            }

            // 1. Rifle (Warden, 40): hits show up as stages, and it takes 8 to 12 rounds.
            yield return Stand(3000f);
            Assert.IsTrue(me.ServerSelectSlot(0) || me.ActiveWeaponId == 3, "rifle in hand");
            yield return WaitUntil(() => me.ActiveWeaponId == 3, 2f, "rifle in hand");
            var door = MakeDoor(3000f);
            int hitsBefore = DoorFx.HitsHeard, rounds = 0, maxStage = 0;
            var stages = new System.Collections.Generic.HashSet<int>();
            while (door.State != DoorState.Broken && rounds < 20)
            {
                Assert.IsTrue(door.Blocking, "a standing door blocks");
                float hp = door.Hp;
                Assert.IsTrue(me.ServerFire(Vector3.forward), "fired");
                rounds++;
                Assert.Less(door.Hp, hp + 0.01f, "a hit costs hit points");
                stages.Add(door.Stage);
                maxStage = Math.Max(maxStage, door.Stage);
                yield return new WaitForSeconds(0.5f);
            }
            Debug.Log($"[DoorBreak] rifle rounds={rounds} stagesSeen={string.Join(",", stages)} hitSounds={DoorFx.HitsHeard - hitsBefore}");
            Assert.AreEqual(DoorState.Broken, door.State, "the door broke");
            Assert.That(rounds, Is.InRange(8, 12), "rifle rounds to break a door");
            CollectionAssert.IsSupersetOf(stages, new[] { 0, 1, 2 }, "the door passed the splintered and cracked stages");
            Assert.Greater(DoorFx.HitsHeard - hitsBefore, rounds - 2, "each hit knocks");
            Assert.AreEqual(1, DoorFx.BreaksShown, "the break was shown once");
            Assert.Greater(DoorFx.LivePieces, 0, "planks fall");
            Assert.IsFalse(door.Blocking);
            Assert.IsFalse(door.Panel.enabled, "no collider left");

            // Sight, sound and the next shot: nothing in the way.
            var eye = me.EyePosition;
            var beyond = new Vector3(3000f, 1.5f, 3008f);
            Assert.IsFalse(Physics.Linecast(eye, beyond, out var sight) && sight.collider.GetComponentInParent<Door>() != null, "sight passes");
            Assert.AreEqual(0, DoorSystem.ClosedBetween(eye, beyond), "sound passes");
            DoorSystem.ServerBotOpen(door.transform.position + Vector3.back, Vector3.forward);
            DoorSystem.ServerBotOpen(door.transform.position + Vector3.back, Vector3.forward);
            system.ServerToggle(door.Index, eye);
            Assert.AreEqual(DoorState.Broken, door.State, "F, bots and toggles do not bring it back");
            Combatant.ShotResult seen = default;
            void On(in Combatant.ShotResult s) => seen = s;
            me.ShotResolved += On;
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(me.ServerFire(Vector3.forward));
            Assert.AreEqual(12f - 0.25f, seen.End.z - 3000f, 0.5f, "the bullet flies through the doorway to the wall");
            me.ShotResolved -= On;

            // A body walks through the doorway.
            yield return Stand(3000f);
            walk.Forward = true;
            yield return WaitUntil(() => PlayerController.Local.transform.position.z > 3003.5f, 6f, "the player to walk through the broken doorway");
            walk.Forward = false;
            Assert.AreEqual(3000f, PlayerController.Local.transform.position.x, 0.6f, "straight through");

            // 2. Shotgun (Breacher): a few shells at point blank.
            yield return Stand(3020f);
            me.ServerSelectSlot(1);
            yield return WaitUntil(() => me.ActiveWeaponId == 4, 2f, "shotgun in hand");
            var door2 = MakeDoor(3020f);
            int shells = 0;
            while (door2.State != DoorState.Broken && shells < 6)
            {
                me.ServerFire(Vector3.forward);
                shells++;
                yield return new WaitForSeconds(1.2f);
            }
            Debug.Log($"[DoorBreak] shotgun shells={shells}");
            Assert.AreEqual(DoorState.Broken, door2.State, "shotgun broke it");
            Assert.LessOrEqual(shells, 4, "a shotgun needs far fewer shots than a rifle");

            // 3. A blow with a machete costs hit points.
            yield return Stand(3030f);
            me.ServerSelectSlot(3);
            yield return WaitUntil(() => me.ActiveWeaponId == 21, 2f, "machete in hand");
            var door3 = MakeDoor(3030f);
            PlayerController.Local.Teleport(new Vector3(3030f, 0.1f, 3000.9f));
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(me.ServerFire(Vector3.forward), "swung");
            Assert.AreEqual(DoorRules.Current.MaxHp - 50f, door3.Hp, 0.5f, "a machete blow (50) hits the door");

            // 4. A ram: damage from closing speed, over the rules' maximum breaks it.
            Assert.IsFalse(DoorSystem.ServerDamage(door3, Doors.RamDamage(DoorRules.Current.RamMinSpeed - 1f, DoorRules.Current)), "a nudge does nothing");
            Assert.IsTrue(DoorSystem.ServerDamage(door3, Doors.RamDamage(9f, DoorRules.Current)), "a car at 9 m/s breaks what is left");
            Assert.AreEqual(DoorState.Broken, door3.State);
            UnityEngine.Object.Destroy(floor);
            UnityEngine.Object.Destroy(backstop);
        }
    }
}
