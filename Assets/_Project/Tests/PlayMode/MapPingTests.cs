using System;
using System.Collections;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // The map marker on the HUD (ADR-065 (6)) in the test box: the compass diamond sits at the marker's bearing
    // relative to the view with its distance, the minimap draws it with a line, a middle-click ping marks the ground
    // under the crosshair, and walking up to it clears it.
    public class MapPingTests
    {
        // Look and ping are delivered once, then cleared.
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.Look = Vector2.zero;
                Frame.Ping = false;
                return f;
            }
        }

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
            MapMarker.Clear();
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && Hud.Instance != null, 15f, "local player and HUD");
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UiFocus.Close();
            PlayerController.InputOverride = null;
            MapMarker.Clear();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // Turns the player to a compass heading, level.
        static IEnumerator Face(float heading)
        {
            var player = PlayerController.Local;
            float rad = heading * Mathf.Deg2Rad;
            player.LookAt(Combatant.LocalPlayer.EyePosition + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * 50f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(player.Yaw, heading), 0.01f, "facing " + heading);
            yield return null;
            yield return null; // a HUD LateUpdate with the new heading
        }

        [UnityTest]
        public IEnumerator Compass_ShowsTheMarker_AtItsBearing_WithItsDistance_AndArrivalClearsIt()
        {
            var hud = Hud.Instance;
            var pc = PlayerController.Local;
            var compass = hud.Compass;
            Assert.IsFalse(compass.MarkerShown, "no marker yet");

            yield return Face(0f);
            var start = pc.transform.position;
            MapMarker.Set(start + new Vector3(100f, 0f, 0f));
            yield return null;
            yield return null;

            // Facing north, a marker 100 m east is 90 degrees to the right: past the visible arc, held at the right
            // end with the arrow.
            Assert.IsTrue(compass.MarkerShown);
            Assert.AreEqual(90f, compass.MarkerRelativeDeg, 0.5f, "the 90 degree position");
            Assert.Greater(compass.MarkerX, 0f, "right of centre");
            Assert.AreEqual(CompassStrip.MarkerLimit, compass.MarkerX, 0.01f, "held at the right end");
            Assert.IsTrue(compass.MarkerClamped, "with the arrow");
            Assert.AreEqual("100m", compass.MarkerDistanceText);

            // The minimap (150 m each way) draws it 100 m east of the player, with a line from the player to it.
            Assert.IsTrue(hud.Minimap.MarkerShown);
            Assert.AreEqual(100f * Minimap.PixelsPerMeter, hud.Minimap.MarkerViewPosition.x, 1f);
            Assert.AreEqual(0f, hud.Minimap.MarkerViewPosition.y, 1f);
            Assert.AreEqual(100f * Minimap.PixelsPerMeter, hud.Minimap.MarkerLineLength, 1f);

            // Turning to face east centres it.
            yield return Face(90f);
            Assert.AreEqual(0f, compass.MarkerRelativeDeg, 0.5f);
            Assert.AreEqual(0f, compass.MarkerX, 2f, "centred");
            Assert.IsFalse(compass.MarkerClamped);
            Assert.AreEqual("100m", compass.MarkerDistanceText);

            // Facing it, the in-world icon is on screen near the middle, with the same distance.
            var world = hud.WorldMarker;
            Assert.IsTrue(world.Shown, "in front of the camera");
            Assert.AreEqual(0f, world.CanvasPosition.x, 40f, "straight ahead (the shoulder camera sits a little right)");
            Assert.AreEqual("100m", world.DistanceText);

            // Turned away from it, the in-world icon hides and the compass holds it at the left end.
            yield return Face(180f);
            Assert.IsFalse(world.Shown, "behind the camera");
            Assert.IsTrue(compass.MarkerClamped);
            Assert.AreEqual(-CompassStrip.MarkerLimit, compass.MarkerX, 0.01f, "east is on the left when facing south");
            yield return Face(90f);

            // Walking up to it (within 10 m) clears it everywhere.
            var marker = MapMarker.Position;
            pc.Teleport(new Vector3(marker.x - 3f, start.y, marker.z));
            yield return null;
            yield return null;
            Assert.IsFalse(MapMarker.Has, "reached");
            Assert.IsFalse(compass.MarkerShown);
            Assert.IsFalse(hud.WorldMarker.Shown);
            yield return null;
            Assert.IsFalse(hud.Minimap.MarkerShown);
        }

        [UnityTest]
        public IEnumerator MiddleClick_PingsTheGroundUnderTheCrosshair()
        {
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            // Look down at the floor a few metres ahead.
            var ahead = pc.transform.position + pc.transform.forward * 6f;
            pc.LookAt(ahead);
            yield return null;
            yield return null;

            _input.Frame.Ping = true;
            yield return WaitUntil(() => MapMarker.Has, 2f, "a ping marker");
            float metres = MapMarker.DistanceFrom(pc.transform.position);
            Assert.Less(metres, 40f, "on the floor in front, not at the 600 m reach");
            Assert.AreEqual(MapMarker.LocalOwner, MapMarker.OwnerId);
            Assert.IsFalse(me.Dead.Value);
        }
    }
}
