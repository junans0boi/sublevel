using System.Collections;
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
    // Review shots of the HUD v2 sides (squad list, zone panel, minimap, weapon stack) in the World scene: solo and a
    // squad of four (one knocked, one dead), out of the gas and in it, at 1920x1080 and 16:10. Judged by eye against
    // docs/research/ui_mockups/hud_v2.html. Run on purpose:
    //   tools/unity.sh test playmode Game.Tests.HudSidesCaptureTests   -> Logs/hud_v2/*.png
    [Explicit, Category("Capture")]
    public class HudSidesCaptureTests
    {
        const string Folder = "Logs/hud_v2";

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            TeamMatch.Mode = TeamMode.Solo;
            TeamMatch.FillTeamsInOrder = false;
            BotRules.Current = GameData.Rules.Bot;
            TeamRules.Current = GameData.Rules.Team;
            PollutionDirector.RulesOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

        [UnityTest, Timeout(480000)]
        public IEnumerator CaptureSquad() => Capture(true);

        [UnityTest, Timeout(480000)]
        public IEnumerator CaptureSolo() => Capture(false);

        IEnumerator Capture(bool squad)
        {
            System.IO.Directory.CreateDirectory(Folder);
            string mode = squad ? "squad" : "solo";
            TeamMatch.Mode = squad ? TeamMode.Squad : TeamMode.Solo;
            TeamMatch.FillTeamsInOrder = squad; // teammates in a one-player match (ADR-332)
            BotRules.Current = new BotRules { TargetsPlayers = false };
            TeamRules.Current = new TeamRules { ReviveSeconds = 1.5f, DownInvulnerableSeconds = 0f, BotReviveSearchMeters = 0f };
            PlayerController.InputOverride = new HarnessInput();
            SceneManager.LoadScene("World");
            yield return NetHarness.WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, "player", null, 60f);
            yield return NetHarness.WaitUntil(() => PollutionDirector.Instance != null && PollutionDirector.Instance.ServerReady && Hud.Instance != null, "pollution and hud", null, 30f);
            if (squad) yield return NetHarness.WaitUntil(() => TeamHud.Instance != null && Combatant.All.Count(c => c != null && c.HasTeam && c.IsTeammateOf(Combatant.LocalPlayer)) >= 3, "teammates", null, 20f);
            yield return NetHarness.WaitUntil(() => !SpawnProtection.InputBlocked, "doors open", null, 90f);
            foreach (var c in Combatant.All) { var p = c != null ? c.GetComponent<SpawnProtection>() : null; if (p != null) p.ServerEnd(); }

            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            if (pc.Rig.Mode != ViewMode.FirstPerson) pc.Rig.ToggleMode();
            me.ServerDamageFilter = _ => 0f; // the look, not the damage
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 12 });
            me.ServerGive(new LootEntry { Kind = LootKind.Grenade, Amount = 2 });
            yield return Seconds(0.3f);
            me.ServerSelectSlot(0);

            if (squad)
            {
                var mates = Combatant.All.Where(c => c != null && c != me && me.IsTeammateOf(c)).OrderBy(c => c.TeamSlot.Value).ToList();
                Assert.GreaterOrEqual(mates.Count, 3);
                foreach (var m in mates) { var a = m.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (a != null && a.enabled) a.isStopped = true; }
                mates[0].ServerTakeDamage(new DamageInfo { Amount = 62f, Zone = HitZone.Body, Cause = DamageCause.Shot });
                mates[1].ServerTakeDamage(new DamageInfo { Amount = 500f, Zone = HitZone.Body, Cause = DamageCause.Shot }); // knocked
                mates[2].ServerTakeDamage(new DamageInfo { Amount = 500f, Zone = HitZone.Body, Cause = DamageCause.Shot });
                yield return null;
                mates[2].ServerTakeDamage(new DamageInfo { Amount = 500f, Zone = HitZone.Body, Cause = DamageCause.Shot }); // dead
                yield return NetHarness.WaitUntil(() => mates[1].Knocked.Value && mates[2].Dead.Value, "one knocked, one dead", null, 5f);
            }

            // The pollution on a short clock: the first region seals at 3 s, the second is warned from the start (75 s lead).
            var director = PollutionDirector.Instance;
            var rules = PollutionDirectorTests.Timed(3f, 2f);
            rules.ConfirmMinutes[1] = 40f / 60f;
            rules.WarningLeadSeconds = 75f;
            Assert.IsTrue(director.ServerConfigure(rules, 1));
            int first = director.Match.Timeline.Order[0], second = director.Match.Timeline.Order[1];
            var sites = ElevatorDoors.Instance.Sites;

            // Out of the gas: a station in a region that is neither sealed nor next.
            int outside = -1;
            for (int i = 0; i < sites.Count && outside < 0; i++) if (sites[i].Region != first && sites[i].Region != second) outside = i;
            Assert.GreaterOrEqual(outside, 0);
            pc.Teleport(sites[outside].Spawn + Vector3.up * 0.05f);
            if (squad) Bring(me);
            yield return Seconds(4f); // the first region sealed: "1 / 6", the second counting down
            var zone = Hud.Instance.Zone;
            Assert.IsTrue(zone.Shown && !zone.Hot, "zone panel out of the gas");
            Debug.Log($"[HudSides] {mode} out: '{zone.TitleText}' {zone.ClockText} bar {zone.Bar:0.00} '{zone.CountText}'");
            Snap($"{Folder}/{mode}_out_1920x1080.png");
            Snap($"{Folder}/{mode}_out_1728x1080.png", 1728, 1080);

            // In the gas: a station of the sealed region.
            int inside = -1;
            for (int i = 0; i < sites.Count && inside < 0; i++) if (sites[i].Region == first) inside = i;
            Assert.GreaterOrEqual(inside, 0);
            pc.Teleport(sites[inside].Spawn + Vector3.up * 0.05f);
            if (squad) Bring(me);
            yield return PollutionDirectorTests.WaitUntil(() => PollutionHudView.Model.InGas, 15f, "the gas at the player");
            yield return Seconds(1f);
            Assert.IsTrue(zone.Hot, "the outline in the gas");
            Debug.Log($"[HudSides] {mode} in: '{zone.TitleText}' {zone.ClockText} '{zone.CountText}' '{zone.GasText}'");
            Snap($"{Folder}/{mode}_gas_1920x1080.png");
            Snap($"{Folder}/{mode}_gas_1728x1080.png", 1728, 1080);
        }

        // Teammates follow the player to the new spot (the minimap dots stay in view).
        static void Bring(Combatant me)
        {
            int n = 0;
            foreach (var c in Combatant.All)
            {
                if (c == null || c == me || !me.IsTeammateOf(c)) continue;
                var at = me.transform.position + new Vector3(3f + 2f * n++, 0f, 4f);
                var agent = c.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null && agent.enabled) agent.Warp(at); else c.transform.position = at;
            }
        }

        // Renders the player's camera with every overlay canvas on it into a w x h PNG (batch mode has no screen), as in
        // VehicleHandlingTests.Snap.
        static void Snap(string path, int w = 1920, int h = 1080)
        {
            var cam = PlayerController.Local.Camera;
            var rt = new RenderTexture(w, h, 24);
            var before = cam.targetTexture;
            cam.targetTexture = rt;
            var moved = new System.Collections.Generic.List<Canvas>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || !c.enabled || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + 0.05f;
                Rescale(c);
                moved.Add(c);
            }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = before;
            foreach (var c in moved) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; Rescale(c); }
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        static void Rescale(Canvas c)
        {
            var scaler = c.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null) return;
            scaler.enabled = false; scaler.enabled = true;
        }
    }
}
