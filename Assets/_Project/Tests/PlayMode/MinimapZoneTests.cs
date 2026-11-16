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
            Assert.AreEqual(255, Minimap.ZoneTexel(ZoneState.Closed, true, 4, 0).a, "no gap in a closed edge");
            Assert.Less(Minimap.ZoneTexel(ZoneState.Warning, false, 0, 0).a, 60, "warning fill is faint");
            Assert.Greater(Minimap.ZoneTexel(ZoneState.Closed, false, 4, 0).a, 130);
            Assert.AreNotEqual(Minimap.ZoneColour(ZoneState.Warning), Minimap.ZoneColour(ZoneState.Closed));
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
            yield return SupplyPlayTests.WaitUntil(() => PollutionDirector.Instance != null && PollutionDirector.Instance.ServerReady, 30f, "the pollution");
            var director = PollutionDirector.Instance;
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;
            // Slow enough to photograph each stage: a confirmation every 12 s, a warning 6 s before.
            var rules = new PollutionRules { ConfirmMinutes = new[] { 0.2f, 0.4f, 0.6f, 0.8f, 1f, 1.2f }, MinConfirmGapMinutes = 0.1f, WarningLeadSeconds = 6f, FlatSpeed = 6000f, RoadSpeed = 6000f, SteepSpeed = 6000f };
            director.ServerConfigure(rules, 1);
            int first = director.Match.Timeline.Order[0], second = director.Match.Timeline.Order[1];
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
            yield return SupplyPlayTests.WaitUntil(() => director.StateOf(first) == ZoneState.Closed && director.StateOf(second) == ZoneState.Warning, 30f, "confirmation + next warning");
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(ZoneState.Warning, map.ShownState(second), "the next region shows as warning");
            yield return Stage(director, first, ZoneState.Closed, map, folder, "minimap_2_closed_and_next.png");
        }

        static IEnumerator Stage(PollutionDirector director, int region, ZoneState expect, MinimapView map, string folder, string file)
        {
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(expect, map.ShownState(region), "the minimap shows " + expect);
            if (expect != ZoneState.Safe) StringAssert.Contains(Minimap.ZoneTag(expect), map.NameLabelOf(region));
            yield return UiShot.Save(Path.Combine(folder, file), 1920, 1080);
        }
    }
}
