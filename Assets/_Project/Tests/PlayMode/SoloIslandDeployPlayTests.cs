using System;
using System.Collections;
using System.Linq;
using FishNet;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Solo practice through the waiting island (ADR-561), all the way into the world: after the island the player's
    // network, doors and loot work as they did when solo started in the elevator.
    public class SoloIslandDeployPlayTests
    {
        sealed class IdleInput : IPlayerInput
        {
            public Vector2 Move;
            public bool Interact;
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1, Move = Move, Interact = Interact };
        }

        float _max, _countdown, _arrive;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var r = MatchRules.Current;
            r.PracticeMaxSeconds = _max; r.PracticeCountdownSeconds = _countdown; r.PracticeBotArriveSeconds = _arrive;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator WaitUntil(Func<bool> done, float seconds, string what)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > until) Assert.Fail("timed out waiting for " + what);
                yield return null;
            }
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator Solo_AfterTheIsland_NetIsLocal_DoorsOpen_LootShows()
        {
            var input = new IdleInput();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return WaitUntil(() => MainMenu.Instance != null, 10f, "the main menu");
            yield return null;
            var r = MatchRules.Current;
            _max = r.PracticeMaxSeconds; _countdown = r.PracticeCountdownSeconds; _arrive = r.PracticeBotArriveSeconds;
            r.PracticeMaxSeconds = 12f; r.PracticeCountdownSeconds = 6f; r.PracticeBotArriveSeconds = 4f;

            MainMenu.Instance.StartGame();
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && WaitingRoom.Current != null, 60f, "the island");
            var me = Combatant.LocalPlayer;
            var net = NetStatusClient.Attach(InstanceFinder.NetworkManager);
            yield return WaitUntil(() => WaitingRoom.Current == null || !WaitingRoom.Current.Open, 40f, "the countdown ends");
            Debug.Log($"[solo-island] deploying from {me.transform.position}");

            // Out of the elevator and into the world.
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 15f)
            {
                yield return new WaitForSeconds(5f);
                Debug.Log($"[solo-island] t={Time.realtimeSinceStartup - t0:F0}s pos={me.transform.position} dead={me.Dead.Value} ping={net.PingMs} loss={net.Loss:P1} pulses={net.HasPulses} tick={net.ServerTick}/{net.TickRate} fps={1f / Time.smoothDeltaTime:F0} guard={ServerGuard.Summary()} corrections={MoveGuard.Corrections} blocked={SpawnProtection.InputBlocked} onIsland={WaitingHud.OnIsland} doorsClosed={(ElevatorDoors.Instance != null ? ElevatorDoors.Instance.DoorsClosed + "/" + ElevatorDoors.Instance.ServerDoorsClosed + " left " + ElevatorDoors.Instance.DoorSecondsLeft.ToString("F0") : "none")}");
            }

            var before = me.transform.position;
            Assert.IsTrue(ElevatorDoors.Instance.TryFacingAt(before, out float doorYaw), "the player is in an elevator bay");
            Debug.Log($"[solo-island] facing {me.transform.eulerAngles.y:F0} deg, the car faces {doorYaw:F0} deg");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(me.transform.eulerAngles.y, doorYaw)), 30f, "the player faces the car's doors, not the back wall");
            input.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(3f);
            input.Move = Vector2.zero;
            float walked = (me.transform.position - before).magnitude;
            Debug.Log($"[solo-island] walked forward {walked:F1} m from {before} to {me.transform.position}");
            Assert.IsFalse(SpawnProtection.InputBlocked, "the elevator let the player out");
            Assert.Greater(walked, 3f, "W walks the player out of the elevator");
            var p = me.transform.position;
            var loot = UnityEngine.Object.FindObjectsByType<LootItem>(FindObjectsSortMode.None);
            var near = loot.Where(l => (l.transform.position - p).sqrMagnitude < 60f * 60f).ToArray();
            int shown = near.Count(l => l.GetComponentsInChildren<Renderer>().Any(x => x.enabled));
            Debug.Log($"[solo-island] loot total={loot.Length} within60m={near.Length} rendered={shown}");

            // A building door: stand in front of the nearest shut one, look at it, press F the player's way.
            var doors = DoorSystem.Instance;
            Door door = null; float bestD = float.MaxValue;
            for (int i = 0; i < doors.Count; i++)
            {
                if (doors[i].State != DoorState.Closed) continue;
                float d = (doors[i].transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; door = doors[i]; }
            }
            Assert.IsNotNull(door, "a shut door");
            var pc = PlayerController.Local;
            pc.Teleport(door.transform.position + door.Forward * 1.3f + Vector3.up * 0.05f);
            yield return new WaitForSeconds(0.5f);
            pc.LookAt(door.transform.position + Vector3.up * 1.2f);
            yield return null;
            input.Interact = true;   // one frame, as a key press gives (two would toggle it back)
            yield return null;
            input.Interact = false;
            yield return new WaitForSeconds(0.5f);
            Debug.Log($"[solo-island] door {Mathf.Sqrt(bestD):F0} m away at {door.transform.position}: {door.State}");
            Assert.AreNotEqual(DoorState.Closed, door.State, "F opens the building door");
            input.Interact = true;   // one frame, as a key press gives (two would toggle it back)
            yield return null;
            input.Interact = false;
            yield return new WaitForSeconds(0.5f);
            Debug.Log($"[solo-island] second F: {door.State}, loot in reach {pc.NearbyLoot != null}, door first {PickupList.DoorFirst}");
            Assert.AreEqual(DoorState.Closed, door.State, "F again shuts it");

            Assert.Less(net.PingMs, NetStatusRules.BadPingMs, "solo is in-process: no real ping");
            Assert.Less(net.Loss, NetStatusRules.BadLoss, "solo is in-process: no packet loss");
            Assert.Greater(near.Length, 0, "loot around the landing");
            Assert.AreEqual(near.Length, shown, "every loot item near the player is drawn");
        }
    }
}
