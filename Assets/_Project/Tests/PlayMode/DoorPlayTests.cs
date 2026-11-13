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
    // ADR-104: doors in the real scenes. A code-made door in the TestBox (F toggles, a shut door stops sight, shots and
    // sound, a bot opens it) and the generated map's doors against the walls they were cut for.
    public class DoorPlayTests
    {
        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
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

        [UnityTest]
        public IEnumerator ShutDoor_StopsSightShotsAndSound_FOpensIt_ABotOpensItToo()
        {
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            var me = Combatant.LocalPlayer;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == 3, 2f, "rifle in hand");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(3000f, -0.5f, 3000f);
            floor.transform.localScale = new Vector3(60f, 1f, 60f);
            PlayerController.Local.Teleport(new Vector3(3000f, 0.1f, 3000f));
            PlayerController.Local.LookAt(new Vector3(3000f, 1.6f, 3010f));
            yield return new WaitForSeconds(1.2f);

            var system = DoorSystem.Instance;
            Assert.IsNotNull(system, "the match director adds the door system");
            // A door 2 m ahead (wall normal toward the player), and a wall plate behind it to stop what gets through.
            var door = system.Add(Door.Create(system.transform, system.Count, new Vector3(3000f, 0f, 3002f), Quaternion.LookRotation(Vector3.back), 1.3f, 2.3f));
            var backstop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backstop.transform.position = new Vector3(3000f, 1.5f, 3012f);
            backstop.transform.localScale = new Vector3(6f, 4f, 0.5f);
            Physics.SyncTransforms();
            var eye = me.EyePosition;
            Assert.AreEqual(DoorState.Closed, door.State);

            // Sight and sound: a closed door is in the way.
            var beyond = new Vector3(3000f, 1.5f, 3008f);
            Assert.IsTrue(Physics.Linecast(eye, beyond, out var sight) && sight.collider == door.Panel, "closed door blocks sight");
            Assert.AreEqual(1, DoorSystem.ClosedBetween(eye, beyond), "and sound");
            // A shot stops at the door.
            Combatant.ShotResult seen = default;
            void On(in Combatant.ShotResult s) => seen = s;
            me.ShotResolved += On;
            me.StanceSpreadFactor = 0f;
            Assert.IsTrue(me.ServerFire(Vector3.forward), "fired");
            Assert.AreEqual(ShotHit.World, seen.Hit);
            Assert.AreEqual(2f - 0.03f, seen.End.z - 3000f, 0.4f, "the bullet ended at the door, not the wall behind");

            // F: the door in reach and in view opens (away from the player), and everything passes.
            Assert.IsTrue(DoorSystem.TryInteract(me, eye, Vector3.forward), "F reaches the door");
            Assert.AreEqual(DoorState.OpenAhead == door.State || DoorState.OpenBack == door.State, true);
            Assert.AreEqual(DoorState.OpenBack, door.State, "opens away from the player: the player is on its front side, so it swings to the back");
            yield return WaitUntil(() => Mathf.Abs(door.Angle) >= DoorRules.Current.OpenAngleDeg - 0.1f, 2f, "the door to swing fully open");
            Physics.SyncTransforms();
            Assert.IsFalse(Physics.Linecast(eye, beyond, out var sight2) && sight2.collider == door.Panel, "open door lets sight through");
            Assert.AreEqual(0, DoorSystem.ClosedBetween(eye, beyond));
            yield return new WaitForSeconds(0.6f);
            me.StanceSpreadFactor = 0f;
            Assert.IsTrue(me.ServerFire(Vector3.forward), "second shot");
            Assert.AreEqual(12f - 0.25f, seen.End.z - 3000f, 0.5f, "through the opening to the wall behind");

            // F again shuts it.
            Assert.IsTrue(DoorSystem.TryInteract(me, eye, Vector3.forward));
            Assert.AreEqual(DoorState.Closed, door.State);
            yield return WaitUntil(() => door.Blocking, 2f, "the door to swing shut");

            // A bot walking at the shut door opens it.
            var at = door.transform.position + Vector3.back * 1.0f;
            DoorSystem.ServerBotOpen(at, Vector3.forward);
            Assert.AreNotEqual(DoorState.Closed, door.State, "the bot opened it");
            me.ShotResolved -= On;
            UnityEngine.Object.Destroy(floor);
            UnityEngine.Object.Destroy(backstop);
        }

        [UnityTest]
        public IEnumerator MapDoors_SitInsideTheirOpenings()
        {
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 30f, "local player spawned");
            yield return null;
            var system = DoorSystem.Instance;
            Assert.IsNotNull(system);
            Assert.Greater(system.Count, 20, "the map has swing doors");
            int bad = 0;
            var hits = new Collider[16];
            for (int i = 0; i < system.Count; i++)
            {
                var door = system[i];
                // The opening is free of building geometry across its middle (door panels excluded).
                var centre = door.transform.position + Vector3.up * 1.2f;
                int n = Physics.OverlapBoxNonAlloc(centre, new Vector3(door.Width * 0.5f - 0.15f, 0.9f, 0.04f), hits, door.transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    if (hits[k].GetComponentInParent<Door>() != null || hits[k].GetComponentInParent<Combatant>() != null) continue;
                    bad++;
                    Debug.Log($"[DoorTest] door {i} at {door.transform.position} overlaps {hits[k].name} under {hits[k].transform.root.name}");
                    break;
                }
            }
            Assert.AreEqual(0, bad, "doors that do not sit in a hole of their wall");
            // And a wall stands on each side of the door's line: the opening is cut in a real wall.
            int walled = 0;
            for (int i = 0; i < system.Count; i++)
            {
                var door = system[i];
                var jamb = door.transform.position + Vector3.up * 1.2f + door.Right * (door.Width * 0.5f + 0.25f);
                if (Physics.CheckSphere(jamb, 0.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) walled++;
            }
            Assert.Greater(walled, system.Count * 0.8f, "most doors have wall beside them");
            Debug.Log($"[DoorTest] {system.Count} map doors, {walled} with a wall at the side");
        }
    }
}
