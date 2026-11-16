using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // The facility supply crate (#144, ADR-113) in the World scene with a shortened lockdown: stage start -> a crate
    // comes down in a safe region under a signal smoke, it cannot be opened while falling, F held next to it opens
    // it into pickups, and the map shows it. SupplyCaptureTests is the Explicit review-shots version.
    public class SupplyPlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() => Frame;
        }

        internal static PollutionRules Fast() => PollutionDirectorTests.Fast();

        internal static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        ScriptedInput _input;
        SupplyRules _before;
        PollutionDirector _director;

        // The World scene with the shortened lockdown, the player unhurtable and out of the first region to close.
        internal static IEnumerator LoadWorld(IPlayerInput input, SupplyRules supply, Action<PollutionDirector> got)
        {
            BotRules.Current = new BotRules { TargetsPlayers = false };
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "the local player in World");
            yield return WaitUntil(() => PollutionDirector.Instance != null && PollutionDirector.Instance.ServerReady, 30f, "the pollution to start");
            var director = PollutionDirector.Instance;
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;
            SupplyRules.Current = supply;
            int player = director.RegionIndexAt(Combatant.LocalPlayer.transform.position);
            var rules = Fast();
            bool found = false;
            for (int seed = 1; seed < 80 && !found; seed++)
            {
                Assert.IsTrue(director.ServerConfigure(rules, seed));
                found = director.Match.Timeline.Order[0] != player;
            }
            Assert.IsTrue(found, "a seed whose first region is not the player's");
            got(director);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _before = SupplyRules.Current;
            _input = new ScriptedInput();
            yield return LoadWorld(_input, new SupplyRules { FirstStage = 1, EveryStages = 1, DescentSeconds = 4f, OpenSeconds = 0.6f, SmokeSeconds = 60f }, d => _director = d);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SupplyRules.Current = _before;
            BotRules.Current = new BotRules();
            PlayerController.InputOverride = null;
            UiFocus.Close();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator StageStart_DropsACrateWithSmoke_WhichOpensIntoItems_AndShowsOnTheMap()
        {
            var supply = SupplyDirector.Instance;
            Assert.IsNotNull(supply, "the supply director installs itself next to the zone director");
            yield return WaitUntil(() => supply.Crates.Count == 1, 15f, "the stage-1 crate");
            var crate = supply.Crates[0];
            Assert.AreEqual(1, supply.DropCount);
            Assert.AreEqual(ZoneState.Safe, _director.StateOf(crate.Region), "it comes down in a region that is still safe");
            yield return WaitUntil(() => SupplyView.Instance != null && SupplyView.Instance.SmokePuffsOn > 0, 5f, "the signal smoke");
            Assert.AreEqual(1, SupplyView.Instance.CrateCount);

            var me = Combatant.LocalPlayer;
            PlayerController.Local.Teleport(crate.Position + new Vector3(1.2f, 0.1f, 0f));
            _input.Frame.InteractHeld = true;
            yield return new WaitForSeconds(1.2f);
            Assert.Less(Time.time, crate.LandsAt, "still coming down");
            Assert.AreEqual(1, supply.Crates.Count, "it cannot be opened while it is falling");

            yield return WaitUntil(() => Time.time >= crate.LandsAt, 6f, "the crate to land");
            yield return WaitUntil(() => SupplyView.Instance.Progress > 0.05f, 3f, "the hold to start");
            yield return null;
            yield return WaitUntil(() => supply.Crates.Count == 0, 5f, "the hold to open the crate");
            _input.Frame.InteractHeld = false;

            yield return null;
            var items = LootItem.All.Where(i => i != null && (i.transform.position - crate.Position).sqrMagnitude < 4f * 4f).ToList();
            Assert.GreaterOrEqual(items.Count, SupplyRules.Current.ItemCount, "its loot lies around it");
            Assert.IsTrue(items.Any(i => i.Kind.Value == (int)LootKind.Weapon), "a gun");
            Assert.IsTrue(items.Any(i => Armor.IsArmor((LootKind)i.Kind.Value) || i.Kind.Value == (int)LootKind.Backpack), "armour or a backpack");
        }

        [UnityTest]
        public IEnumerator TheCrate_IsOnTheMap_UntilItIsOpened()
        {
            var supply = SupplyDirector.Instance;
            yield return WaitUntil(() => supply.Crates.Count == 1, 15f, "the stage-1 crate");
            yield return WaitUntil(() => MapScreen.Instance != null, 5f, "the map screen");
            UiFocus.Set(UiScreen.Map);
            yield return null;
            yield return null;
            Assert.AreEqual(1, MapScreen.Instance.SupplyMarkersShown);
            Assert.Greater(MapScreen.Instance.FirstSupplyPixels.x, 0f);
            Assert.Less(MapScreen.Instance.FirstSupplyPixels.x, MapScreen.MapPx);
            // Opened by someone in reach once it landed: the marker goes.
            yield return WaitUntil(() => Time.time >= supply.Crates[0].LandsAt, 6f, "the crate to land");
            PlayerController.Local.Teleport(supply.Crates[0].Position + new Vector3(1f, 0.1f, 0f));
            yield return null;
            Assert.IsTrue(supply.ServerTryOpen(supply.Crates[0], Combatant.LocalPlayer));
            yield return null;
            Assert.AreEqual(0, MapScreen.Instance.SupplyMarkersShown);
        }
    }
}
