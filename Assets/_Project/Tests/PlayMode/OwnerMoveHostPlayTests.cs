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
    // #485 in bot practice (a hosted World match with the default bots): out of the elevator, W moves the host's own body the
    // frame it is read and running is never put back. The dedicated topology is DedicatedServerDoorPlayTests.
    public class OwnerMoveHostPlayTests
    {
        HarnessInput _input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input = new HarnessInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("World");
            yield return NetHarness.WaitUntil(() => Combatant.LocalPlayer != null && MatchState.Instance != null, "host's player in World", Debug.Log, 90f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // #485: W held in the closed elevator moves the body as soon as the doors let it (input unblocked), not a door slide later.
        [UnityTest, Timeout(300000)]
        public IEnumerator HostMatch_WHeldInTheElevator_LeavesWhenTheDoorsOpen_ThenRunsWithoutCorrections()
        {
            // A dedicated-server test before this one leaves its client's player behind for a frame or two: wait for this match's own.
            yield return NetHarness.WaitUntil(() => Combatant.LocalPlayer != null && Combatant.LocalPlayer.IsServerInitialized
                && SpawnProtection.InputBlocked && !WaitingHud.OnIsland && !ElevatorPanel.Riding, "in the closed elevator", Debug.Log, 120f);
            var me = Combatant.LocalPlayer;
            _input.Frame.Move = new Vector2(0f, 1f);
            yield return NetHarness.WaitUntil(() => !SpawnProtection.InputBlocked, "elevator doors open", Debug.Log, 60f);
            float opened = Time.realtimeSinceStartup;
            var at = me.transform.position;
            Debug.Log("[OwnerMove] doors open: " + OwnerMoveProbe.State());
            yield return NetHarness.WaitUntil(() => new Vector2(me.transform.position.x - at.x, me.transform.position.z - at.z).magnitude > 2.5f, "the body is out past the door", Debug.Log, 10f);
            float leftAfter = Time.realtimeSinceStartup - opened;
            _input.Frame.Move = Vector2.zero;
            float settle = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < settle) yield return null;
            var r = new OwnerMoveProbe();
            yield return OwnerMoveProbe.Run(_input, r, 5f);
            Debug.Log($"[OwnerMove] host bots={MatchRules.Current.BotCount} leftAfterDoorsOpen={leftAfter:F2}s {r}");
            // Walking 2.5 m (the door is ~1.4 m ahead) takes ~0.6 s at 4.7 m/s: no more than that plus a little.
            Assert.LessOrEqual(leftAfter, 0.8f, "W held: the body walks out the moment input is allowed, not after the door has sunk");
            Assert.LessOrEqual(r.SecondsToMove, 0.1f, r.ToString());
            Assert.AreEqual(0, r.Corrections, r.ToString());
            Assert.AreEqual(0, r.SnapsBack, r.ToString());
            Assert.Greater(r.Meters, 5f, r.ToString());
        }
    }
}
