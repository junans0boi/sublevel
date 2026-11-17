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
    // ADR-276/277/278 in the TestBox as a host: what F may take (same storey, line of sight, in front), what the server
    // refuses, and one press = one target when a door and a pickup are both in reach.
    public class InteractLootPlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() { var f = Frame; Frame.Interact = false; return f; }
        }

        static readonly Vector3 Origin = new Vector3(3000f, 0f, 3000f);
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

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static GameObject Slab(string name, Vector3 centre, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = centre;
            go.transform.localScale = scale;
            return go;
        }

        IEnumerator Load()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && PickupList.Instance != null, 15f, "local player spawned");
            yield return null;
            Slab("floor", Origin + new Vector3(0, -0.5f, 0), new Vector3(60f, 1f, 60f));
            PlayerController.Local.Teleport(Origin + new Vector3(0f, 0.1f, 0f));
            PlayerController.Local.LookAt(Origin + new Vector3(0f, 1.6f, 10f));
            yield return new WaitForSeconds(0.8f);
        }

        static LootItem Spawn(Vector3 pivot, LootKind kind = LootKind.Medkit)
        {
            int before = LootItem.All.Count;
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = kind, Amount = 1 }, pivot);
            return LootItem.All.Count > before ? LootItem.All[LootItem.All.Count - 1] : null;
        }

        // The ground-floor scenario of the report: the one pickup in the room, the one upstairs, the one behind a wall and
        // the one in a far room are not offered, and the server refuses them if asked anyway.
        [UnityTest]
        public IEnumerator OnlyThePickupInSightOnThisFloorIsOffered_TheServerRefusesTheRest()
        {
            yield return Load();
            var me = Combatant.LocalPlayer;
            Slab("slab", Origin + new Vector3(0, 2.9f, 0), new Vector3(20f, 0.2f, 20f));          // the next storey, top at y 3.0
            Slab("wall", Origin + new Vector3(2.0f, 1.5f, 1.6f), new Vector3(2.4f, 3f, 0.2f));      // wall across x 0.8..3.2 at z 1.6
            Physics.SyncTransforms();
            var here = Spawn(Origin + new Vector3(0f, 0.3f, 1.0f));
            var upstairs = Spawn(Origin + new Vector3(0.3f, 3.3f, 0.8f));
            var behindWall = Spawn(Origin + new Vector3(1.5f, 0.3f, 2.4f));
            var farRoom = Spawn(Origin + new Vector3(0f, 0.3f, 8f));
            yield return new WaitForSeconds(0.5f);
            for (int i = 0; i < 3; i++) yield return null;

            Assert.AreSame(here, PlayerController.Local.NearbyLoot, "the pickup in sight is the one F takes");
            Assert.AreEqual(1, PickupList.Instance.Rows.Count, "and the only one offered");
            Assert.IsFalse(PickupList.Showing, "one pickup keeps the single prompt");

            me.CmdPickup(upstairs.NetworkObject);
            me.CmdPickup(behindWall.NetworkObject);
            me.CmdPickup(farRoom.NetworkObject);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(upstairs != null && upstairs.IsSpawned, "the server refuses a pickup from the storey above");
            Assert.IsTrue(behindWall != null && behindWall.IsSpawned, "the server refuses a pickup behind a wall");
            Assert.IsTrue(farRoom != null && farRoom.IsSpawned, "the server refuses a pickup out of reach");

            yield return UiShot.Save("Logs/captures/interact_floor_scenario.png", 1280, 720);

            _input.Frame.Interact = true;
            yield return null;
            yield return null;
            yield return WaitUntil(() => here == null || !here.IsSpawned, 2f, "the pickup in sight taken");
        }

        // A pickup in front and a door beside it: F takes the pickup and leaves the door; looking at the door, F opens it.
        [UnityTest]
        public IEnumerator FAtADoorNextToAPickup_DoesOneThing_TheOneNearerTheMiddleOfTheView()
        {
            yield return Load();
            var system = DoorSystem.Instance;
            var door = system.Add(Door.Create(system.transform, system.Count, Origin + new Vector3(0.9f, 0f, 1.3f), Quaternion.LookRotation(Vector3.back), 1.0f, 2.3f));
            Physics.SyncTransforms();
            var item = Spawn(Origin + new Vector3(0f, 0.3f, 1.4f));
            yield return new WaitForSeconds(0.4f);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreSame(item, PlayerController.Local.NearbyLoot, "looking at the pickup");

            _input.Frame.Interact = true;
            yield return null;
            yield return null;
            yield return WaitUntil(() => item == null || !item.IsSpawned, 2f, "the pickup taken");
            Assert.AreEqual(DoorState.Closed, door.State, "the door beside it did not move");

            var second = Spawn(Origin + new Vector3(-0.9f, 0.3f, 1.6f)); // off to the left, the door is straight ahead of the view
            PlayerController.Local.LookAt(Origin + new Vector3(0.9f, 1.4f, 1.3f));
            yield return new WaitForSeconds(0.5f);
            for (int i = 0; i < 3; i++) yield return null;
            _input.Frame.Interact = true;
            yield return null;
            yield return null;
            yield return WaitUntil(() => door.State != DoorState.Closed, 2f, "the door opens when it is what the view is on");
            Assert.IsTrue(second != null && second.IsSpawned, "and the pickup to the side stays");
        }
    }
}
