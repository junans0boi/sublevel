using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of the team features (#169, ADR-129, ADR-130) in a squad match in the World scene: pings in the world, on
    // the compass, minimap and map, the marker wheel, carrying a knocked teammate. Run on purpose:
    //   TEAM_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.TeamCaptureTests
    [Explicit, Category("Capture")]
    public class TeamCaptureTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            TeamMatch.Mode = TeamMode.Solo;
            TeamMatch.FillTeamsInOrder = false;
            BotRules.Current = GameData.Rules.Bot;
            TeamRules.Current = GameData.Rules.Team;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

        static Vector3 GroundAt(Vector3 near, float ahead, float side, Transform from)
        {
            var p = near + from.forward * ahead + from.right * side;
            return Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out var hit, 80f) ? hit.point : p;
        }

        static void Warp(Combatant c, Vector3 at)
        {
            var agent = c.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled) agent.Warp(at); else c.transform.position = at;
        }

        [UnityTest, Timeout(480000)]
        public IEnumerator CaptureTeamFeatures()
        {
            string folder = Environment.GetEnvironmentVariable("TEAM_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/teamshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            TeamMatch.Mode = TeamMode.Squad;
            TeamMatch.FillTeamsInOrder = true; // needs teammates in a one-player match (ADR-332)
            BotRules.Current = new BotRules { TargetsPlayers = false };
            TeamRules.Current = new TeamRules { ReviveSeconds = 1.5f, DownInvulnerableSeconds = 0f, BotReviveSearchMeters = 0f };
            var input = new HarnessInput();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("World");
            yield return NetHarness.WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, "player", null, 60f);
            yield return NetHarness.WaitUntil(() => Combatant.All.Count(c => c != null && c.HasTeam) >= 4 && WheelHud.Instance != null && TeamHud.Instance != null, "team and huds", null, 20f);
            yield return NetHarness.WaitUntil(() => !SpawnProtection.InputBlocked, "doors open", null, 90f);
            foreach (var c in Combatant.All) { var p = c != null ? c.GetComponent<SpawnProtection>() : null; if (p != null) p.ServerEnd(); }
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var mates = Combatant.All.Where(c => c != null && me.IsTeammateOf(c)).OrderBy(c => c.TeamSlot.Value).ToList();
            Assert.GreaterOrEqual(mates.Count, 3);
            // The view in first person (the HUD shots), looking along the way out of the station.
            if (pc.Rig.Mode != ViewMode.FirstPerson) pc.Rig.ToggleMode();
            yield return Seconds(0.5f);

            // ---- Pings: three teammates, six kinds, spread across the view ----
            var origin = pc.transform.position;
            var fwd = pc.transform;
            var spots = new[]
            {
                GroundAt(origin, 18f, -9f, fwd), GroundAt(origin, 30f, 6f, fwd), GroundAt(origin, 45f, -2f, fwd),
                GroundAt(origin, 24f, 12f, fwd), GroundAt(origin, 60f, 14f, fwd), GroundAt(origin, 38f, -16f, fwd),
            };
            pc.LookAt(GroundAt(origin, 30f, 0f, fwd) + Vector3.up * 1.2f);
            yield return Seconds(0.3f);
            for (int i = 0; i < 6; i++)
            {
                var from = mates[i % 3];
                Assert.IsTrue(from.ServerPing((PingKind)i, spots[i]), "ping " + i);
                yield return Seconds(0.45f);
            }
            yield return Seconds(0.3f);
            Debug.Log($"[TeamCapture] pings heard {me.PingsHeard.Count}, world {TeamHud.Instance.PingView.WorldShown}, compass {TeamHud.Instance.PingView.CompassShown}, map {TeamHud.Instance.PingView.MapShown}");
            for (int i = 0; i < TeamHud.Instance.PingView.Visible; i++) Debug.Log("[TeamCapture] " + TeamHud.Instance.PingView.WorldText(i));
            yield return UiShot.Save(Path.Combine(folder, "ping_1_world.png"), 1920, 1080);

            // Full map with the same pings.
            UiFocus.Toggle(UiScreen.Map);
            yield return Seconds(0.5f);
            yield return UiShot.Save(Path.Combine(folder, "ping_2_map.png"), 1920, 1080);
            UiFocus.Toggle(UiScreen.Map);
            yield return Seconds(0.3f);

            // Facing away: the pings are behind, the compass and minimap still show them.
            pc.LookAt(origin - fwd.forward * 30f + Vector3.up * 1.6f);
            yield return Seconds(0.3f);
            yield return UiShot.Save(Path.Combine(folder, "ping_3_behind.png"), 1920, 1080);
            pc.LookAt(GroundAt(origin, 30f, 0f, fwd) + Vector3.up * 1.2f);
            yield return Seconds(0.3f);

            // ---- Wheels ----
            var hud = WheelHud.Instance;
            // The real way: the ping button held, then the mouse to the part (the wheel closes when it is let go).
            input.Frame.PingHeld = true;
            yield return NetHarness.WaitUntil(() => hud.MarkerWheel.IsOpen, "the marker wheel", null, 3f);
            input.Frame.Look = new Vector2(5f, 3f); // up and to the right: the second part
            yield return null;
            input.Frame.Look = Vector2.zero;
            yield return Seconds(0.3f);
            yield return UiShot.Save(Path.Combine(folder, "wheel_1_marker.png"), 1920, 1080);
            input.Frame.PingHeld = false;
            yield return Seconds(0.5f);
            // ---- Carrying (third person first, then the carrier's own eyes) ----
            if (pc.Rig.Mode == ViewMode.FirstPerson) pc.Rig.ToggleMode();
            pc.LookAt(origin + fwd.forward * 20f + Vector3.up * 1.2f);
            yield return Seconds(0.5f);
            var mate = mates[0];
            mate.ServerTakeDamage(new DamageInfo { Amount = 500f, Zone = HitZone.Body, Cause = DamageCause.Shot });
            Warp(mate, pc.transform.position + pc.transform.right * 1.2f);
            yield return Seconds(0.5f);
            yield return UiShot.Save(Path.Combine(folder, "carry_1_prompt.png"), 1920, 1080);
            input.Frame.CarryPressed = true;
            yield return NetHarness.WaitUntil(() => me.IsCarrying, "lifted", null, 5f);
            input.Frame.Move = new Vector2(0f, 1f);
            yield return Seconds(1.2f);
            yield return UiShot.Save(Path.Combine(folder, "carry_2_walking.png"), 1920, 1080);
            input.Frame.Move = Vector2.zero;
            yield return Seconds(0.4f);
            // First person with the carried one on the back and the hint.
            pc.Rig.ToggleMode();
            yield return Seconds(0.4f);
            yield return UiShot.Save(Path.Combine(folder, "carry_3_first.png"), 1920, 1080);

            // The other way round: the player is knocked and a teammate carries it (the knocked band says so).
            me.ServerEndCarry();
            yield return Seconds(0.5f);
            me.ServerTakeDamage(new DamageInfo { Amount = 500f, Zone = HitZone.Body, Cause = DamageCause.Shot });
            Warp(mates[1], pc.transform.position + pc.transform.right * 1.0f);
            yield return Seconds(0.3f);
            Assert.IsTrue(mates[1].ServerBeginCarry(me), "a teammate lifts the player");
            yield return Seconds(1.0f);
            yield return UiShot.Save(Path.Combine(folder, "carry_4_carried_view.png"), 1920, 1080);
        }
    }
}
