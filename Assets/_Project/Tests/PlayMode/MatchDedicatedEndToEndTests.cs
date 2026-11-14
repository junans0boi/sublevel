using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FishNet.Transporting;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Game.Tests
{
    // #172: the same kind of match against the BUILT dedicated server (a separate process; tools/unity.sh build server):
    // this process is one real remote client that goes through the lobby, is deployed by the server, loots a gun and its
    // ammo, shoots a bot at the server's real latency path (100 ms / 2 % simulated on this side), looks around for a
    // vehicle, and leaves; the server then returns to its lobby. A single human cannot end a dedicated match (nobody can
    // kill the bots from a client), so the result screen is only reached if the bots kill the player within the time box.
    // Skipped without Build/server. Shots: MATCH_SHOTS (default Logs/matchshots).
    [Category("Long"), Explicit("needs Build/server; run on purpose: test playmode Game.Tests.MatchDedicatedEndToEndTests")]
    public class MatchDedicatedEndToEndTests
    {
        const string ServerApp = "Build/server/SublevelServer.app/Contents/MacOS";
        const ushort Port = 17960;
        const string LogDir = "Logs/net-match";
        Process _server;
        string _serverLog;
        LobbyNet _lobby;
        long _bytes;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();
        float _t0;

        void Log(string line)
        {
            string s = $"[{Time.realtimeSinceStartup - _t0,7:F1}s] {line}";
            Debug.Log("[MatchDedicated] " + s);
            _log.AppendLine(s);
        }

        string ServerText()
        {
            try
            {
                using (var f = new FileStream(_serverLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(f)) return r.ReadToEnd();
            }
            catch (IOException) { return ""; }
        }

        [TearDown]
        public void TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "dedicated-" + TestContext.CurrentContext.Test.Name + ".log"), _log + "\n--- server ---\n" + (_serverLog != null ? ServerText() : ""));
            if (_lobby != null) { _lobby.Shutdown(); UnityEngine.Object.Destroy(_lobby.gameObject); }
            if (_server != null && !_server.HasExited) _server.Kill();
            PlayerController.InputOverride = null;
            NetSession.Reset();
        }

        static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
        }

        IEnumerator Until(Func<bool> c, string what, float timeout = 20f) => NetHarness.WaitUntil(c, what, Log, timeout);

        IEnumerator Shot(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            string folder = Environment.GetEnvironmentVariable("MATCH_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/matchshots";
            string path = Path.Combine(Path.GetFullPath(folder), name + ".png");
            yield return UiShot.Save(path, 1600, 900);
            Log("SHOT " + path);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator DedicatedServer_RemoteClient_LobbyDeployLootShootDriveLeave()
        {
            _t0 = Time.realtimeSinceStartup;
            string dir = Path.GetFullPath(ServerApp);
            if (!Directory.Exists(dir)) Assert.Ignore("no server build at " + dir + " (tools/unity.sh build server)");
            string exe = Directory.GetFiles(dir)[0];
            Directory.CreateDirectory(LogDir);
            _serverLog = Path.GetFullPath(Path.Combine(LogDir, "server-process-match.log"));
            if (File.Exists(_serverLog)) File.Delete(_serverLog);
            _server = Process.Start(new ProcessStartInfo(exe, $"-batchmode -nographics -logFile \"{_serverLog}\" --dedicated --port {Port} --bots 8 --region C --max-players 4 --start-seconds 0") { UseShellExecute = false });
            yield return Until(() => ServerText().Contains($"lobby open on port {Port}"), "server lobby open", 120f);

            _lobby = new GameObject("DedicatedGuest").AddComponent<LobbyNet>();
            Assert.IsTrue(_lobby.Join("127.0.0.1", Port));
            yield return Until(() => _lobby.Phase == LobbyNet.PhaseKind.InLobby, "guest in the dedicated lobby", 30f);
            Log($"lobby: bots {_lobby.Bots}, max players {_lobby.MaxPlayers}");
            _lobby.SetReady(true);
            yield return Until(() => _lobby.HasStarted, "server started the match", 30f);
            NetSession.Begin(_lobby);
            _lobby.Shutdown();
            var input = new HarnessInput();
            PlayerController.InputOverride = input;
            MoveGuard.TrustTeleports = false; // a real client: no teleports at all
            SceneManager.LoadScene("World");
            yield return Until(() => MatchDirector.Instance != null && Combatant.LocalPlayer != null && PlayerController.Local != null, "client's player on the server's match", 150f);
            var nm = FishNet.InstanceFinder.NetworkManager;
            nm.TransportManager.Transport.OnClientReceivedData += (ClientReceivedDataArgs a) => _bytes += a.Data.Count;
            var sim = nm.TransportManager.LatencySimulator;
            sim.SetLatency(MultiHarness.BadRoundTripMs / 2);
            sim.SetPacketLoss(MultiHarness.BadLoss);
            sim.SetEnabled(true);
            float connectedAt = Time.realtimeSinceStartup;
            var me = Combatant.LocalPlayer;
            // Interest management (#167): the others stand in far elevators, so only the near ones are sent; the match count (HUD) has everyone.
            yield return Until(() => MatchState.Instance != null && MatchState.Instance.TotalCount == 9, "match counts the player + 8 bots", 30f);
            Log($"combatants sent to this client: {Combatant.All.Count} of {MatchState.Instance.TotalCount} (interest management)");
            yield return Until(() => !SpawnProtection.InputBlocked, "elevator doors opened (synchronized deployment)", 90f);
            Log("doors open for the dedicated client");
            yield return Seconds(1.5f);
            yield return Shot("d01_deployed");

            // Walk to the nearest gun (real input), pick it up with F, then its ammo.
            LootItem best = null;
            float bd = float.MaxValue;
            foreach (var l in LootItem.All)
            {
                if (l == null || l.Kind.Value != (int)LootKind.Weapon) continue;
                var def = GameData.Weapons.Find(l.WeaponId.Value);
                if (def == null || def.IsMelee) continue;
                float d = Vector3.Distance(l.transform.position, me.transform.position);
                if (d < bd) { bd = d; best = l; }
            }
            Assert.IsNotNull(best, "a gun lies on the client's map");
            Log($"nearest gun {bd:F0} m away; walking there");
            var gunAt = best.transform.position;
            yield return WalkTo(input, me, () => gunAt, 1.2f, 70f);
            Log($"arrived within {Vector3.Distance(me.transform.position, gunAt):F1} m of the gun");
            int wid = best.WeaponId.Value;
            for (int i = 0; i < 3 && me.WeaponInSlot(0) != wid && me.WeaponInSlot(1) != wid && me.WeaponInSlot(2) != wid; i++)
            { input.Frame.Interact = true; yield return Seconds(0.6f); }
            Log($"gun in a slot: {me.WeaponInSlot(0) == wid || me.WeaponInSlot(1) == wid || me.WeaponInSlot(2) == wid}, magazine in hand {me.ActiveMag}");
            // The ammo item beside it (unloaded guns, ADR-136).
            foreach (var l in new List<LootItem>(LootItem.All))
                if (l != null && l.IsSpawned && l.Kind.Value == (int)LootKind.Ammo && Vector3.Distance(l.transform.position, me.transform.position) < 2f)
                { input.Frame.Interact = true; yield return Seconds(0.6f); break; }
            yield return Shot("d02_looting");

            // Wait for the player to be shot at / see bots; fire at the nearest bot in view if any within 40 m.
            // Walk toward the nearest bot this client was sent (the others are far away), then shoot it in view.
            Combatant hunted = null;
            foreach (var c in Combatant.All) if (c != null && !c.IsPlayer && !c.Dead.Value && (hunted == null || Vector3.Distance(c.transform.position, me.transform.position) < Vector3.Distance(hunted.transform.position, me.transform.position))) hunted = c;
            Log("hunting: " + (hunted != null ? $"{hunted.name} {Vector3.Distance(hunted.transform.position, me.transform.position):F0} m away" : "no bot sent to this client"));
            if (hunted != null) yield return WalkTo(input, me, () => hunted != null ? hunted.transform.position : me.transform.position, 18f, 40f);
            float end = Time.time + 40f;
            int sawBot = 0; float nextTap = 0f;
            while (Time.time < end && !me.Dead.Value)
            {
                Combatant target = null; float td = 40f;
                foreach (var c in Combatant.All)
                {
                    if (c == null || c.IsPlayer || c.Dead.Value) continue;
                    float d = Vector3.Distance(c.transform.position, me.transform.position);
                    if (d < td && !Physics.Linecast(me.EyePosition, c.transform.position + Vector3.up * 1.3f, out var hit, ~0, QueryTriggerInteraction.Ignore)) { td = d; target = c; }
                }
                if (target != null)
                {
                    sawBot++;
                    PlayerController.Local.LookAt(target.transform.position + Vector3.up * 1.3f);
                    input.Frame.FireHeld = false;
                    if (Time.time >= nextTap) { input.Frame.FirePressed = input.Frame.FireHeld = true; nextTap = Time.time + 0.2f; }
                    if (me.ActiveMag <= 0 && !me.Reloading.Value) input.Frame.Reload = true;
                }
                else input.Frame.FireHeld = false;
                yield return null;
            }
            input.Frame.FireHeld = false;
            Log($"firefight window over: frames with a bot in view {sawBot}, own hp {me.Hp.Value:F0}, dead {me.Dead.Value}");
            yield return Shot("d03_firefight_or_idle");
            if (me.Dead.Value)
            {
                yield return Until(() => ResultScreen.Instance != null && ResultScreen.Instance.Current != ResultScreen.Phase.None, "death cam/panel", 10f);
                yield return Seconds(ResultScreen.DeathCamSeconds + 0.5f);
                Log($"result panel: '{ResultScreen.Instance.TitleText}' {ResultScreen.Instance.PlacementText} by {ResultScreen.Instance.KilledByText}");
                yield return Shot("d04_result");
            }
            double secs = Time.realtimeSinceStartup - connectedAt;
            Log($"client numbers: received {_bytes / 1024.0:F0} KiB in {secs:F0} s = {_bytes / secs / 1024.0:F1} KiB/s, server refusals of this client are in the server log, corrections {MoveGuard.Corrections} (client side)");

            // Leave: the server notices the empty match and reopens the lobby.
            MainMenu.LeaveMatch();
            yield return Until(() => ServerText().Contains("match is empty"), "server noticed the empty match", MatchRules.Current.ReconnectSeconds + 40f);
            yield return Until(() => Count(ServerText(), "lobby open on port") >= 2, "server back in the lobby", ServerRules.EmptySeconds + 40f);
            string text = ServerText();
            foreach (var line in text.Split('\n'))
                if (line.Contains("[ServerGuard]")) Log("server: " + line.Trim());
            Log("DEDICATED MATCH COMPLETE (client part)");
        }

        // Real input only: follows the navmesh path to `target` (the client has the map's navmesh) with look + forward + sprint.
        IEnumerator WalkTo(HarnessInput input, Combatant me, Func<Vector3> target, float stop, float seconds)
        {
            float end = Time.time + seconds;
            var path = new UnityEngine.AI.NavMeshPath();
            float nextPath = 0f;
            int corner = 1;
            while (Time.time < end && !me.Dead.Value)
            {
                var goal = target();
                if (Vector3.Distance(me.transform.position, goal) <= stop) break;
                if (Time.time >= nextPath)
                {
                    nextPath = Time.time + 1.5f;
                    corner = 1;
                    UnityEngine.AI.NavMesh.SamplePosition(me.transform.position, out var from, 3f, UnityEngine.AI.NavMesh.AllAreas);
                    UnityEngine.AI.NavMesh.SamplePosition(goal, out var to, 3f, UnityEngine.AI.NavMesh.AllAreas);
                    UnityEngine.AI.NavMesh.CalculatePath(from.position, to.position, UnityEngine.AI.NavMesh.AllAreas, path);
                }
                var c = path.corners;
                Vector3 aim = goal;
                if (c != null && c.Length > 1)
                {
                    while (corner < c.Length - 1 && Vector2.Distance(new Vector2(me.transform.position.x, me.transform.position.z), new Vector2(c[corner].x, c[corner].z)) < 1.2f) corner++;
                    aim = c[Mathf.Min(corner, c.Length - 1)];
                }
                var flat = aim - me.transform.position; flat.y = 0f;
                PlayerController.Local.LookAt(me.transform.position + flat + Vector3.up * 1.6f);
                input.Frame.Move = new Vector2(0, 1);
                input.Frame.Sprint = true;
                yield return null;
            }
            input.Frame.Move = Vector2.zero; input.Frame.Sprint = false;
        }

        static int Count(string text, string needle)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}
