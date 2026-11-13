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
    // Review shots of the supply crate (#144): crate and smoke close and far, the hold prompt, the map marker. Run on purpose:
    //   SUPPLY_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.SupplyCaptureTests
    [Explicit, Category("Capture")]
    public class SupplyCaptureTests
    {
        sealed class Held : IPlayerInput
        {
            public bool Hold;
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1, InteractHeld = Hold };
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            BotRules.Current = new BotRules();
            UiFocus.Close();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // The ground under `at`, looked for from `above` metres over it (the crate stands under a canopy: from high up the roof is hit).
        static Vector3 Ground(Vector3 at, float above = 120f)
        {
            return Physics.Raycast(at + Vector3.up * above, Vector3.down, out var hit, above + 100f) ? hit.point + Vector3.up * 0.1f : at;
        }

        // A spot about `distance` metres out where the top of the smoke can be seen from.
        static Vector3 FarSpot(Vector3 crate, float distance)
        {
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                var spot = Ground(crate + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * distance);
                if (!Physics.Linecast(spot + Vector3.up * 1.7f, crate + Vector3.up * 25f)) return spot;
            }
            return Ground(crate + new Vector3(-distance, 0f, 0f));
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator CaptureSupply()
        {
            string folder = Environment.GetEnvironmentVariable("SUPPLY_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/supplyshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            var input = new Held();
            ZoneDirector director = null;
            yield return SupplyPlayTests.LoadWorld(input, new SupplyRules { FirstStage = 1, EveryStages = 1, DescentSeconds = 3f, OpenSeconds = 30f, SmokeSeconds = 300f }, d => director = d);
            var supply = SupplyDirector.Instance;
            yield return SupplyPlayTests.WaitUntil(() => supply.Crates.Count == 1, 15f, "the crate");
            var crate = supply.Crates[0];
            var pc = PlayerController.Local;
            Debug.Log($"[SupplyCapture] crate in region {director.RegionNameOf(crate.Region)} at {crate.Position}");

            // On its way down, from 14 m.
            pc.Teleport(Ground(crate.Position + new Vector3(0f, 0f, -14f), 6f));
            yield return new WaitForSeconds(1.0f); // the map streams in around the player
            pc.LookAt(crate.Position + Vector3.up * 10f);
            yield return new WaitForSeconds(0.3f);
            yield return UiShot.Save(Path.Combine(folder, "supply_01_falling.png"), 1920, 1080);

            yield return SupplyPlayTests.WaitUntil(() => Time.time >= crate.LandsAt + 0.5f, 8f, "landing");
            pc.Teleport(Ground(crate.Position + new Vector3(0f, 0f, -7f), 6f));
            yield return new WaitForSeconds(0.5f);
            pc.LookAt(crate.Position + Vector3.up * 3f);
            yield return new WaitForSeconds(0.3f);
            yield return UiShot.Save(Path.Combine(folder, "supply_02_landed_close.png"), 1920, 1080);

            // Far, to see what the smoke tells you.
            pc.Teleport(FarSpot(crate.Position, 90f));
            yield return new WaitForSeconds(1.0f);
            pc.LookAt(crate.Position + Vector3.up * 22f);
            yield return new WaitForSeconds(0.3f);
            yield return UiShot.Save(Path.Combine(folder, "supply_03_smoke_far.png"), 1920, 1080);

            // In reach, F held about a third of the way.
            pc.Teleport(crate.Position + new Vector3(0f, 0.1f, -1.8f));
            yield return new WaitForSeconds(0.5f);
            pc.LookAt(crate.Position + Vector3.up * 0.5f);
            input.Hold = true;
            yield return new WaitForSeconds(10f);
            Debug.Log($"[SupplyCapture] progress {SupplyView.Instance.Progress:0.00}, prompt '{SupplyView.Instance.Prompt}'");
            yield return UiShot.Save(Path.Combine(folder, "supply_04_hold_prompt.png"), 1920, 1080);
            input.Hold = false;

            UiFocus.Set(UiScreen.Map);
            yield return new WaitForSeconds(0.3f);
            yield return UiShot.Save(Path.Combine(folder, "supply_05_map.png"), 1920, 1080);
            UiFocus.Close();

            // Opened: the items on the ground.
            yield return null;
            Assert.IsTrue(supply.ServerTryOpen(crate, Combatant.LocalPlayer));
            yield return new WaitForSeconds(0.8f);
            pc.LookAt(crate.Position + Vector3.up * 0.2f);
            yield return new WaitForSeconds(0.3f);
            yield return UiShot.Save(Path.Combine(folder, "supply_06_opened.png"), 1920, 1080);
            Debug.Log("[SupplyCapture] shots in " + folder);
        }
    }
}
