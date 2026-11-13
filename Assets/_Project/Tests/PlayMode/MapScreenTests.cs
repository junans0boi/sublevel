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
    // The full-screen map (key M) in the World scene: opens from UiFocus, tracks the player, places one marker by
    // click, closes with M.
    public class MapScreenTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.Map = false;
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
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && MapScreen.Instance != null, 40f, "local player and map screen in World");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            UiFocus.Close();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SidePanel_FitsOnTheScreen_At16x10And16x9()
        {
            UiFocus.Set(UiScreen.Map);
            yield return null;
            var map = MapScreen.Instance.MapArea;
            var side = (RectTransform)map.parent.Find("Side");
            Assert.IsNotNull(side);
            foreach (var size in new[] { new Vector2Int(1728, 1080), new Vector2Int(1920, 1080) })
            {
                yield return UiShot.Save($"Logs/shots/map_{size.x}x{size.y}.png", size.x, size.y, () =>
                {
                    var canvas = (RectTransform)map.parent;
                    var bounds = new Rect(canvas.rect.xMin, canvas.rect.yMin, canvas.rect.width, canvas.rect.height);
                    foreach (var rect in new[] { side, map })
                    {
                        var corners = new Vector3[4];
                        rect.GetWorldCorners(corners);
                        foreach (var corner in corners)
                        {
                            Vector2 local = canvas.InverseTransformPoint(corner);
                            Assert.IsTrue(bounds.Contains(local), $"{rect.name} corner {local} outside {bounds} at {size}");
                        }
                    }
                });
            }
        }

        [UnityTest]
        public IEnumerator OpeningTheFocus_ShowsTheMap_AndTheKeyIsAvailable()
        {
            var screen = MapScreen.Instance;
            Assert.IsTrue(UiFocus.MapAvailable);
            Assert.IsFalse(screen.IsOpen);
            UiFocus.Set(UiScreen.Map);
            yield return null;
            Assert.IsTrue(screen.IsOpen);
            UiFocus.Close();
            yield return null;
            Assert.IsFalse(screen.IsOpen);
        }

        [UnityTest]
        public IEnumerator PlayerArrow_MovesByTheMatchingPixels_WhenTheyAreTeleported100m()
        {
            var screen = MapScreen.Instance;
            var pc = PlayerController.Local;
            UiFocus.Set(UiScreen.Map);
            yield return null;
            yield return null;
            var before = screen.PlayerArrowPixels;
            var position = pc.transform.position;

            // East if there is room for it, else west.
            float step = position.x + 100f <= MapImage.WorldSize - 10f ? 100f : -100f;
            var target = position + new Vector3(step, 0f, 0f);
            var terrain = Terrain.activeTerrain;
            target.y = terrain != null ? terrain.SampleHeight(target) + terrain.transform.position.y + 0.3f : position.y;
            pc.Teleport(target);
            yield return null;
            yield return null;

            var moved = screen.PlayerArrowPixels - before;
            float expected = step / MapImage.WorldSize * screen.MapPixels;
            Assert.AreEqual(expected, moved.x, 0.5f, "the arrow moves east/west by the scaled distance");
            Assert.AreEqual(0f, moved.y, 0.5f, "and does not move north/south");
        }

        [UnityTest]
        public IEnumerator Click_PlacesOneMarker_AtTheClickedWorldPoint_AndRightClickClears()
        {
            var screen = MapScreen.Instance;
            var pc = PlayerController.Local;
            UiFocus.Set(UiScreen.Map);
            yield return null;
            yield return null;
            Assert.IsFalse(MapMarker.Has);

            var first = new Vector3(300f, 0f, 700f);
            Assert.IsTrue(screen.Click(screen.WorldToScreen(first), false));
            Assert.IsTrue(MapMarker.Has);
            Assert.AreEqual(first.x, MapMarker.Position.x, 1f);
            Assert.AreEqual(first.z, MapMarker.Position.z, 1f);
            Assert.IsTrue(screen.MarkerVisible);

            // A second click moves it.
            var second = new Vector3(800f, 0f, 200f);
            Assert.IsTrue(screen.Click(screen.WorldToScreen(second), false));
            Assert.AreEqual(second.x, MapMarker.Position.x, 1f);
            Assert.AreEqual(second.z, MapMarker.Position.z, 1f);

            yield return null;
            var p = pc.transform.position;
            int metres = Mathf.RoundToInt(MapProjection.Distance(p.x, p.z, MapMarker.Position.x, MapMarker.Position.z));
            StringAssert.Contains("마커 " + metres + " m", screen.MarkerReadout);

            // Off the picture does nothing; right click clears.
            Assert.IsFalse(screen.Click(new Vector2(-500f, -500f), false));
            Assert.IsTrue(MapMarker.Has);
            Assert.IsTrue(screen.Click(Vector2.zero, true));
            Assert.IsFalse(MapMarker.Has);
            Assert.IsFalse(screen.MarkerVisible);
        }

        [UnityTest]
        public IEnumerator TheMapKey_OpensAndClosesTheScreen()
        {
            var screen = MapScreen.Instance;
            _input.Frame.Map = true;
            yield return null;
            yield return null;
            Assert.AreEqual(UiScreen.Map, UiFocus.Current);
            Assert.IsTrue(screen.IsOpen);

            _input.Frame.Map = true;
            yield return null;
            yield return null;
            Assert.AreEqual(UiScreen.None, UiFocus.Current);
            Assert.IsFalse(screen.IsOpen);
        }
    }
}
