using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #178: the minimap paints regions by their state (warning = next to close, lockdown/bombardment = closing now,
    // closed) with a tag under the name. Also saves HUD shots of the stages to MINIMAP_SHOTS (default Logs/minimapshots).
    public class MinimapZoneTests
    {
        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            BotRules.Current = new BotRules();
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [Test]
        public void ZoneTexel_SeparatesWarningFromClosing_AndSafeIsClear()
        {
            Assert.AreEqual(0, Minimap.ZoneTexel(ZoneState.Safe, true, 3, 3).a);
            // Edge of the next region (warning): dashed. Edge of one closing now: solid.
            Assert.AreEqual(0, Minimap.ZoneTexel(ZoneState.Warning, true, 4, 0).a, "dash gap");
            Assert.Greater(Minimap.ZoneTexel(ZoneState.Warning, true, 0, 0).a, 200);
            Assert.AreEqual(255, Minimap.ZoneTexel(ZoneState.Lockdown, true, 4, 0).a, "no gap in a lockdown edge");
            Assert.Less(Minimap.ZoneTexel(ZoneState.Warning, false, 0, 0).a, 60, "warning fill is faint");
            Assert.Greater(Minimap.ZoneTexel(ZoneState.Lockdown, false, 0, 0).a, 100);
            Assert.Greater(Minimap.ZoneTexel(ZoneState.Closed, false, 4, 0).a, 130);
            Assert.AreNotEqual(Minimap.ZoneColour(ZoneState.Lockdown), Minimap.ZoneColour(ZoneState.Bombardment));
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator MinimapShowsEachStage_AndNextRegionApart()
        {
            string folder = Environment.GetEnvironmentVariable("MINIMAP_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/minimapshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);

            BotRules.Current = new BotRules { TargetsPlayers = false };
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("World");
            yield return SupplyPlayTests.WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "the local player in World");
            var director = UnityEngine.Object.FindFirstObjectByType<ZoneDirector>();
            if (director == null) director = new GameObject("ZoneDirector").AddComponent<ZoneDirector>();
            yield return SupplyPlayTests.WaitUntil(() => director.ServerReady, 15f, "the zone director");
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;
            var rules = new LockdownRules
            {
                FirstWarningSeconds = 3f, WarningSeconds = 10f, LockdownSeconds = 10f, BombardmentSeconds = 10f,
                MinSecondsBetweenClosings = 12f, RadiationDps = 0f, RadiationGraceSeconds = 100f, ClosedDps = 0f,
                BlastDamage = 0f, KeysPerRegion = 0, SubZoneStartDelaySeconds = 1000f,
            };
            director.ServerConfigure(rules, 1);
            int first = director.ServerPlan.Order[0], second = director.ServerPlan.Order[1];
            var a = director.RegionCentre(first);
            var b = director.RegionCentre(second);
            var mid = (a + b) * 0.5f;
            var pc = PlayerController.Local;
            pc.Teleport(Physics.Raycast(mid + Vector3.up * 200f, Vector3.down, out var hit, 400f) ? hit.point + Vector3.up * 0.5f : mid + Vector3.up * 2f);
            yield return new WaitForSeconds(1f);
            var hud = UnityEngine.Object.FindFirstObjectByType<Hud>();
            Assert.IsNotNull(hud);
            var map = hud.Minimap;

            yield return Stage(director, first, ZoneState.Safe, map, folder, "minimap_0_safe.png");
            yield return SupplyPlayTests.WaitUntil(() => director.StateOf(first) == ZoneState.Warning, 20f, "warning");
            yield return Stage(director, first, ZoneState.Warning, map, folder, "minimap_1_warning_next.png");
            yield return SupplyPlayTests.WaitUntil(() => director.StateOf(first) == ZoneState.Lockdown && director.StateOf(second) == ZoneState.Warning, 30f, "lockdown + next warning");
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(ZoneState.Warning, map.ShownState(second), "the next region shows as warning");
            yield return Stage(director, first, ZoneState.Lockdown, map, folder, "minimap_2_lockdown_and_next.png");
            yield return SupplyPlayTests.WaitUntil(() => director.StateOf(first) == ZoneState.Bombardment, 30f, "bombardment");
            yield return Stage(director, first, ZoneState.Bombardment, map, folder, "minimap_3_bombardment.png");
            yield return SupplyPlayTests.WaitUntil(() => director.StateOf(first) == ZoneState.Closed, 30f, "closed");
            yield return Stage(director, first, ZoneState.Closed, map, folder, "minimap_4_closed.png");
        }

        static IEnumerator Stage(ZoneDirector director, int region, ZoneState expect, MinimapView map, string folder, string file)
        {
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(expect, map.ShownState(region), "the minimap shows " + expect);
            if (expect != ZoneState.Safe) StringAssert.Contains(Minimap.ZoneTag(expect), map.NameLabelOf(region));
            yield return UiShot.Save(Path.Combine(folder, file), 1920, 1080);
        }
    }
}
