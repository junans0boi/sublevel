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
    // The HUD v2 layout (ADR-057): areas never overlap at the three target resolutions, the weapon strip follows
    // the weapon in hand, the kill feed sits top right, the minimap arrow turns with the player, and the HUD
    // steps aside for the inventory.
    public class HudLayoutTests
    {
        // Look is delivered once, then cleared (a held value would keep turning the player).
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.Look = Vector2.zero;
                Frame.FirePressed = Frame.Jump = false;
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

        IEnumerator LoadTestBox()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return WaitUntil(() => MatchState.Instance != null && MatchState.Instance.TotalCount == 4 && KillFeed.Instance != null, 5f, "match and kill feed");
            yield return null;
            yield return null; // one LateUpdate of the HUD with the player present
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UiFocus.Close();
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // The rect of a point-anchored area on a canvas of `width` x `height` reference units, y up from the bottom.
        static Rect Placed(RectTransform r, float width, float height)
        {
            Assert.AreEqual(r.anchorMin, r.anchorMax, r.name + " must have a point anchor");
            var size = r.sizeDelta;
            float x = r.anchorMin.x * width + r.anchoredPosition.x - r.pivot.x * size.x;
            float y = r.anchorMin.y * height + r.anchoredPosition.y - r.pivot.y * size.y;
            return new Rect(x, y, size.x, size.y);
        }

        static List<RectTransform> Areas()
        {
            var areas = new List<RectTransform>(Hud.Instance.LayoutAreas) { KillFeed.Instance.Area };
            return areas;
        }

        [UnityTest]
        public IEnumerator Layout_AreasStayOnScreenAndNeverOverlap_At720p_1080p_1440p()
        {
            yield return LoadTestBox();
            var areas = Areas();
            Assert.AreEqual(11, areas.Count, "compass strip, heading, region, kills, alive, health, weapon, status, weapon strip, minimap and the kill feed");

            // The canvas scales with screen height, so 16:9 screens of every size see the same 1920x1080 units.
            int[][] screens = { new[] { 1280, 720 }, new[] { 1920, 1080 }, new[] { 2560, 1440 } };
            foreach (var screen in screens)
            {
                float width = 1080f * screen[0] / screen[1];
                var canvasRect = new Rect(0f, 0f, width, 1080f);
                var placed = new List<Rect>();
                for (int i = 0; i < areas.Count; i++)
                {
                    var rect = Placed(areas[i], width, 1080f);
                    placed.Add(rect);
                    Assert.IsTrue(canvasRect.Contains(new Vector2(rect.xMin + 0.01f, rect.yMin + 0.01f)) && canvasRect.Contains(new Vector2(rect.xMax - 0.01f, rect.yMax - 0.01f)),
                        areas[i].name + " is off screen at " + screen[0] + "x" + screen[1]);
                }
                for (int a = 0; a < placed.Count; a++)
                    for (int b = a + 1; b < placed.Count; b++)
                        Assert.IsFalse(placed[a].Overlaps(placed[b]), areas[a].name + " overlaps " + areas[b].name + " at " + screen[0] + "x" + screen[1]);
            }
        }

        [UnityTest]
        public IEnumerator Layout_MinimapIs220Square_AboveTheWeaponStrip_AtTheBottomRight()
        {
            yield return LoadTestBox();
            var minimap = Placed(Hud.Instance.Minimap.Root, 1920f, 1080f);
            var strip = Placed(Hud.Instance.Strip.Root, 1920f, 1080f);
            Assert.AreEqual(220f, minimap.width, 0.01f);
            Assert.AreEqual(220f, minimap.height, 0.01f);
            Assert.AreEqual(1884f, minimap.xMax, 0.01f, "36 from the right edge");
            Assert.AreEqual(1884f, strip.xMax, 0.01f);
            Assert.AreEqual(36f, strip.yMin, 0.01f);
            Assert.AreEqual(strip.yMax + 10f, minimap.yMin, 0.01f, "the strip sits right under the minimap");
            Assert.AreEqual(404f, strip.width, 0.01f, "five cells of 76 with 6 between");
        }

        [UnityTest]
        public IEnumerator Layout_OnTheRealScreen_NoWorldCornersOverlap()
        {
            yield return LoadTestBox();
            if (Screen.width < Screen.height * 1.3f) Assert.Ignore("The game view is not wide enough for the landscape layout.");
            yield return null;
            var areas = Areas();
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            var corners = new Vector3[4];
            var rects = new List<Rect>();
            for (int i = 0; i < areas.Count; i++)
            {
                areas[i].GetWorldCorners(corners); // screen pixels on an overlay canvas: 0 = bottom left, 2 = top right
                var rect = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                rects.Add(rect);
                Assert.IsTrue(rect.xMin >= screen.xMin - 1f && rect.xMax <= screen.xMax + 1f && rect.yMin >= screen.yMin - 1f && rect.yMax <= screen.yMax + 1f,
                    areas[i].name + " is off screen");
            }
            for (int a = 0; a < rects.Count; a++)
                for (int b = a + 1; b < rects.Count; b++)
                    Assert.IsFalse(rects[a].Overlaps(rects[b]), areas[a].name + " overlaps " + areas[b].name);
        }

        [UnityTest]
        public IEnumerator WeaponStrip_FollowsTheWeaponInHand()
        {
            yield return LoadTestBox();
            var hud = Hud.Instance;
            var me = Combatant.LocalPlayer;
            for (int i = 0; i < 4; i++) Assert.AreEqual(0, hud.Strip.WeaponShown(i), "cell " + (i + 1) + " starts empty");
            Assert.IsFalse(hud.Strip.IsHighlighted(0));

            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return WaitUntil(() => hud.Strip.WeaponShown(0) == 3, 2f, "rifle in cell 1");
            Assert.AreEqual("weapon_3", hud.Strip.IconKeyShown(0));
            Assert.AreEqual(0, hud.Strip.ActiveShown);
            Assert.IsTrue(hud.Strip.IsHighlighted(0));

            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 12 });
            yield return WaitUntil(() => hud.Strip.WeaponShown(1) == 1, 2f, "pistol in cell 2");
            Assert.AreEqual("weapon_1", hud.Strip.IconKeyShown(1));

            Assert.IsTrue(me.ServerSelectSlot(1));
            yield return WaitUntil(() => hud.Strip.ActiveShown == 1, 2f, "highlight moved to cell 2");
            Assert.IsFalse(hud.Strip.IsHighlighted(0));
            Assert.IsTrue(hud.Strip.IsHighlighted(1));
            Assert.AreEqual(1, hud.ActiveSlotShown);
            Assert.AreEqual("단발", hud.FireModeText, "the pistol is semi-automatic");

            Assert.IsTrue(me.ServerSelectSlot(0));
            yield return WaitUntil(() => hud.Strip.ActiveShown == 0, 2f, "highlight back on cell 1");
            Assert.AreEqual("연사", hud.FireModeText, "the rifle fires automatically");
        }

        [UnityTest]
        public IEnumerator KillFeed_StacksTopRight_AndTheKillCountsOnTheCard()
        {
            yield return LoadTestBox();
            var hud = Hud.Instance;
            var feed = KillFeed.Instance;
            var state = MatchState.Instance;
            var me = Combatant.LocalPlayer;
            var dummies = new List<Combatant>();
            foreach (var c in Combatant.All)
                if (!c.IsPlayer) dummies.Add(c);
            Assert.GreaterOrEqual(dummies.Count, 2);

            for (int i = 0; i < 2; i++)
                dummies[i].ServerTakeDamage(new DamageInfo { Amount = 1000f, Attacker = me, Zone = HitZone.Body, WeaponId = 3, Cause = DamageCause.Shot });
            yield return WaitUntil(() => state.KillsPosted == 2 && feed.VisibleCount == 2, 3f, "two kills in the feed");
            yield return WaitUntil(() => hud.KillsShown == 2 && hud.AliveShown == 2, 2f, "the cards show 2 kills and 2 alive");

            var corners = new Vector3[4];
            for (int row = 0; row < 2; row++)
            {
                feed.RowRect(row).GetWorldCorners(corners);
                float cx = (corners[0].x + corners[2].x) * 0.5f, cy = (corners[0].y + corners[2].y) * 0.5f;
                Assert.Greater(cx, Screen.width * 0.6f, "row " + row + " is on the right half");
                Assert.Greater(cy, Screen.height * 0.6f, "row " + row + " is in the top part");
            }
            feed.RowRect(0).GetWorldCorners(corners);
            float newestTop = corners[1].y;
            feed.RowRect(1).GetWorldCorners(corners);
            Assert.Greater(newestTop, corners[1].y, "the newest line is on top");
            Assert.AreEqual(2, hud.KillsShown);
            Assert.AreEqual(2, hud.AliveShown);
        }

        [UnityTest]
        public IEnumerator MinimapArrow_TurnsWithThePlayerYaw()
        {
            yield return LoadTestBox();
            var hud = Hud.Instance;
            var player = PlayerController.Local;
            Assert.IsFalse(hud.Minimap.HasMapImage, "no baked map picture yet");
            Assert.IsTrue(hud.Minimap.GridShown, "a grid stands in for it");

            _input.Frame.Look = new Vector2(Mathf.DeltaAngle(player.Yaw, 90f), 0f);
            yield return WaitUntil(() => Mathf.Abs(Mathf.DeltaAngle(hud.Minimap.ArrowHeading, 90f)) < 0.5f, 2f, "arrow heading 90");
            Assert.AreEqual(90f, Mathf.Repeat(hud.Minimap.ArrowHeading, 360f), 0.5f);
            Assert.AreEqual(270f, hud.Minimap.ArrowRotationZ, 0.5f, "clockwise 90 degrees on screen is z = -90");
            Assert.AreEqual(90, hud.Compass.HeadingShown, "the compass agrees");
        }

        [UnityTest]
        public IEnumerator Hud_StepsAsideForTheInventory_AndComesBack()
        {
            yield return LoadTestBox();
            var hud = Hud.Instance;
            Assert.IsTrue(hud.Visible);
            Assert.IsTrue(KillFeed.Instance.Visible);

            UiFocus.Set(UiScreen.Inventory);
            yield return null;
            yield return null;
            Assert.IsFalse(hud.Visible, "no HUD area can overlap the inventory header");
            Assert.IsFalse(KillFeed.Instance.Visible, "the kill feed is a HUD area too");

            UiFocus.Close();
            yield return null;
            yield return null;
            Assert.IsTrue(hud.Visible);
            Assert.IsTrue(KillFeed.Instance.Visible);
        }

        [UnityTest]
        public IEnumerator PlayerStatus_ShowsStance_AndRegionCardIsHiddenWithoutAMap()
        {
            yield return LoadTestBox();
            var hud = Hud.Instance;
            Assert.AreEqual((int)Stance.Stand, hud.Status.StanceShown);
            Assert.AreEqual(0, hud.Status.ProtectionSecondsShown, "no spawn protection in the test box");
            Assert.AreEqual(0, hud.Status.ArmourTierShown(0));
            Assert.AreEqual(0, hud.Status.ArmourTierShown(1));
            Assert.IsFalse(hud.Region.Shown, "no WorldInfo here, so no region card");
        }

        [UnityTest]
        public IEnumerator WorldScene_RegionCardAndMinimapMarkersComeFromTheMap()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && WorldInfo.Current != null, 40f, "the World scene's player");
            yield return null;
            yield return null;
            var hud = Hud.Instance;
            if (hud == null) Assert.Ignore("This World scene has no HUD.");
            yield return null;

            var world = WorldInfo.Current;
            Assert.IsTrue(hud.Region.Shown);
            Assert.IsNotEmpty(hud.Region.NameShown);
            Assert.AreEqual("상태  안전", hud.Region.StateShownText);
            Assert.AreEqual(world.Elevators.Count, hud.Minimap.ElevatorMarkers);
            Assert.Greater(hud.Minimap.BorderLines, 0, "neighbouring regions get a border line");
            Assert.IsNotEmpty(hud.Minimap.RegionName);
        }
    }
}
