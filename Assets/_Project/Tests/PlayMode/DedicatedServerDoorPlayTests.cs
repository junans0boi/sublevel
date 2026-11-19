using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
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
    // r5 playtest: "doors do not open" on the live Linux ARM64 dedicated server, while the in-process tests passed. This runs
    // the real server build as its own process the way the live one runs (-batchmode -nographics --dedicated): the Linux
    // ARM64 Server-subtarget build in a Docker container when it and docker exist (tools/unity.sh build server-linux-arm64),
    // else the Mac build (tools/unity.sh build server). A client in this process goes through lobby, island and elevator,
    // walks up to a map door and presses F on it; the server must open it. Skipped when no build exists. Local only: 127.0.0.1,
    // or under colima (whose UDP port forwarding does not reach localhost) the VM's private address with host networking.
    public class DedicatedServerDoorPlayTests
    {
        const string LinuxServer = "Build/server-linux-arm64/SublevelServer";
        const string MacServer = "Build/server/SublevelServer.app/Contents/MacOS";
        const ushort Port = 17960;
        const string LogDir = "Logs/net-dedicated";
        const string Container = "sublevel-door-test";
        Process _server;
        string _serverLog, _docker;
        LobbyNet _lobby;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

        void Log(string line) { Debug.Log(line); _log.AppendLine(line); }

        string ServerText()
        {
            try
            {
                using (var f = new FileStream(_serverLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(f)) return r.ReadToEnd();
            }
            catch (IOException) { return ""; }
        }

        static string Docker()
        {
            foreach (var p in new[] { "/opt/homebrew/bin/docker", "/usr/local/bin/docker" })
            {
                if (!File.Exists(p)) continue;
                using (var info = Process.Start(new ProcessStartInfo(p, "info") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    info.WaitForExit(20000);
                    if (info.HasExited && info.ExitCode == 0) return p;
                }
            }
            return null;
        }

        // colima's VM address ("colima start --network-address"), or null (Docker Desktop: published ports on 127.0.0.1).
        static string ColimaAddress()
        {
            foreach (var p in new[] { "/opt/homebrew/bin/colima", "/usr/local/bin/colima" })
            {
                if (!File.Exists(p)) continue;
                using (var ls = Process.Start(new ProcessStartInfo(p, "ls -j") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    string json = ls.StandardOutput.ReadToEnd();
                    ls.WaitForExit(20000);
                    var m = Regex.Match(json, "\"address\":\"([0-9.]+)\"");
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            return null;
        }

        void RemoveContainer()
        {
            if (_docker == null) return;
            using (var rm = Process.Start(new ProcessStartInfo(_docker, "rm -f " + Container) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })) rm.WaitForExit(20000);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log + "\n--- server ---\n" + (_serverLog != null ? ServerText() : ""));
            if (_lobby != null) { _lobby.Shutdown(); UnityEngine.Object.Destroy(_lobby.gameObject); }
            if (_server != null && !_server.HasExited) _server.Kill();
            RemoveContainer();
            PlayerController.InputOverride = null;
            NetSession.Reset();
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator RemoteClient_OpensAMapDoor_OnTheRealDedicatedServer()
        {
            string args = $"-batchmode -nographics --dedicated --port {Port} --bots 2 --region C --max-players 4 --start-seconds 5";
            Directory.CreateDirectory(LogDir);
            string logDir = Path.GetFullPath(LogDir);
            _serverLog = Path.Combine(logDir, "door-server-process.log");
            if (File.Exists(_serverLog)) File.Delete(_serverLog);
            string linux = Path.GetFullPath(LinuxServer), mac = Path.GetFullPath(MacServer), address = "127.0.0.1";
            if (File.Exists(linux) && (_docker = Docker()) != null)
            {
                RemoveContainer();
                // The live host: Ubuntu 24.04 ARM64. The lobby port and the match port (lobby + 1) are published for UDP.
                string vm = ColimaAddress();
                string net = vm != null ? "--network host" : $"-p {Port}:{Port}/udp -p {Port + 1}:{Port + 1}/udp";
                if (vm != null) address = vm;
                string run = $"run --rm --name {Container} {net} " +
                             $"-v \"{Path.GetDirectoryName(linux)}\":/srv:ro -v \"{logDir}\":/logs ubuntu:24.04 /srv/SublevelServer -logFile /logs/door-server-process.log {args}";
                Log("server: linux arm64 in docker");
                _server = Process.Start(new ProcessStartInfo(_docker, run) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            }
            else if (Directory.Exists(mac))
            {
                Log("server: mac build");
                _server = Process.Start(new ProcessStartInfo(Directory.GetFiles(mac)[0], $"-logFile \"{_serverLog}\" {args}") { UseShellExecute = false });
            }
            else Assert.Ignore("no server build (tools/unity.sh build server-linux-arm64 or build server)");

            yield return NetHarness.WaitUntil(() => ServerText().Contains($"lobby open on port {Port}"), "server lobby open", Log, 120f);
            _lobby = new GameObject("GuestLobby").AddComponent<LobbyNet>();
            Assert.IsTrue(_lobby.Join(address, Port));
            yield return NetHarness.WaitUntil(() => _lobby.Phase == LobbyNet.PhaseKind.InLobby, "guest in the dedicated lobby", Log, 30f);
            _lobby.SetReady(true);
            yield return NetHarness.WaitUntil(() => _lobby.HasStarted, "server started the match", Log, 30f);
            NetSession.Begin(_lobby);
            _lobby.Shutdown();
            var input = new HarnessInput();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("World");
            yield return NetHarness.WaitUntil(() => MatchDirector.Instance != null && Combatant.LocalPlayer != null, "client's player in the server's match", Log, 150f);
            // A real connection's delay and loss (only this side simulates: the whole round trip on the client's packets).
            var sim = FishNet.InstanceFinder.NetworkManager.TransportManager.LatencySimulator;
            sim.SetLatency(MultiHarness.BadRoundTripMs);
            sim.SetPacketLoss(MultiHarness.BadLoss);
            sim.SetEnabled(true);

            // Both peers built the map's doors, and the same number of them.
            yield return NetHarness.WaitUntil(() => Regex.IsMatch(ServerText(), @"\[Doors\] \d+ doors built"), "server logged its doors", Log, 30f);
            int serverDoors = int.Parse(Regex.Match(ServerText(), @"\[Doors\] (\d+) doors built").Groups[1].Value);
            var doors = DoorSystem.Instance;
            Assert.IsNotNull(doors, "the client has a door system");
            Log($"doors: server {serverDoors}, client {doors.Count}");
            Assert.Greater(serverDoors, 0, "the dedicated server built the map's doors");
            Assert.AreEqual(serverDoors, doors.Count, "server and client built the same doors");

            // Island -> elevator: the countdown sends the person down; then the body is ours to move.
            yield return NetHarness.WaitUntil(() => ServerText().Contains("countdown over"), "island countdown over", Log, 90f);
            var me = Combatant.LocalPlayer;
            var controller = me.GetComponent<PlayerController>();
            yield return NetHarness.WaitUntil(() => Mathf.Abs(me.transform.position.x - WaitingIsland.CentreX) + Mathf.Abs(me.transform.position.z - WaitingIsland.CentreZ) > 300f, "the body left the island", Log, 30f);
            yield return Wait(2f);

            // The entrance door nearest the body; walk up to it in announced hops a real match would allow (MoveGuard).
            var from = me.transform.position;
            int best = -1;
            float bestSqr = float.MaxValue;
            foreach (var (_, position) in doors.ExteriorDoors)
            {
                float sqr = (position - from).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = doors.Resolve(-1, position); }
            }
            Assert.GreaterOrEqual(best, 0, "an entrance door");
            var door = doors[best];
            var at = door.transform.position;
            var stand = at + door.Forward * 1.2f + Vector3.up * 0.1f;
            Log($"door {best} at {at}, body at {from}, {Mathf.Sqrt(bestSqr):F0} m");
            var step = GameData.Rules.Guard.TeleportMaxMeters * 0.8f;
            while ((me.transform.position - stand).magnitude > 0.5f)
            {
                var p = me.transform.position;
                controller.Teleport(Vector3.MoveTowards(p, stand, step));
                yield return Wait(GameData.Rules.Guard.TeleportMinIntervalSeconds + 0.15f);
            }
            controller.Teleport(stand);
            yield return Wait(1.5f);
            StringAssert.DoesNotContain("refused Teleport", ServerText(), "the server took the walk to the door");

            Assert.AreEqual(DoorState.Closed, door.State);
            // F the way a person presses it: through PlayerController (door vs pickup, reach, the request to the server).
            controller.LookAt(at + Vector3.up * 1.4f);
            yield return Wait(0.3f);
            Assert.AreNotEqual(float.MaxValue, DoorSystem.Bearing(me.EyePosition, controller.Camera != null ? controller.Camera.transform.forward : me.transform.forward), "the door is in reach and in view");
            input.Frame.Interact = true;
            yield return NetHarness.WaitUntil(() => door.State != DoorState.Closed, "the server opened the door for the remote client", Log, 5f);
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
