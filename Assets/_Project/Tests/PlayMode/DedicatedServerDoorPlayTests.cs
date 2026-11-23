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

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log + "\n--- server ---\n" + (_serverLog != null ? ServerText() : ""));
            if (_lobby != null) { _lobby.Shutdown(); UnityEngine.Object.Destroy(_lobby.gameObject); }
            if (_server != null && !_server.HasExited) _server.Kill();
            RemoveContainer();
            PlayerController.InputOverride = null;
            NetSession.Reset();
            // Leave nothing of this client's World behind (its connection, local player, MatchDirector): the next test's harness
            // takes the first local player and network manager it finds (MultiHarness.AdoptHost), as MultiHarness.Teardown does.
            var nm = FishNet.InstanceFinder.NetworkManager;
            if (nm != null && nm.ClientManager.Started) nm.ClientManager.StopConnection();
            float end = Time.realtimeSinceStartup + 10f;
            while (nm != null && nm.ClientManager.Started && Time.realtimeSinceStartup < end) yield return null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // Starts the server build (Linux ARM64 in docker on 2 CPUs like the live host, else the Mac build), joins its lobby, starts the
        // match and loads the client's World with `input` driving the local player under a real connection's delay and loss.
        IEnumerator StartServerAndJoin(HarnessInput input, string logName)
        {
            // DEDICATED_BOTS: the server's bot count (default 2); 79 is the live 80-person load (#485).
            string bots = Environment.GetEnvironmentVariable("DEDICATED_BOTS") ?? "2";
            string args = $"-batchmode -nographics --dedicated --port {Port} --bots {bots} --region C --max-players 4 --start-seconds {Environment.GetEnvironmentVariable("DEDICATED_START_SECONDS") ?? "5"}";
            Directory.CreateDirectory(LogDir);
            string logDir = Path.GetFullPath(LogDir);
            _serverLog = Path.Combine(logDir, logName);
            if (File.Exists(_serverLog)) File.Delete(_serverLog);
            string linux = Path.GetFullPath(LinuxServer), mac = Path.GetFullPath(MacServer), address = "127.0.0.1";
            if (File.Exists(linux) && (_docker = Docker()) != null)
            {
                RemoveContainer();
                // The live host: Ubuntu 24.04 ARM64, 2 cores. The lobby port and the match port (lobby + 1) are published for UDP.
                string vm = ColimaAddress();
                string net = vm != null ? "--network host" : $"-p {Port}:{Port}/udp -p {Port + 1}:{Port + 1}/udp";
                if (vm != null) address = vm;
                string cpus = Environment.GetEnvironmentVariable("DEDICATED_CPUS") ?? "2"; // fewer = a slower host than the M4 (#485)
                string run = $"run --rm --name {Container} --cpus {cpus} {net} " +
                             $"-v \"{Path.GetDirectoryName(linux)}\":/srv:ro -v \"{logDir}\":/logs ubuntu:24.04 /srv/SublevelServer -logFile /logs/{logName} {args}";
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
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("World");
            yield return NetHarness.WaitUntil(() => MatchDirector.Instance != null && Combatant.LocalPlayer != null, "client's player in the server's match", Log, 150f);
            // A real connection's delay and loss (only this side simulates: the whole round trip on the client's packets).
            var sim = FishNet.InstanceFinder.NetworkManager.TransportManager.LatencySimulator;
            sim.SetLatency(MultiHarness.BadRoundTripMs);
            sim.SetPacketLoss(MultiHarness.BadLoss);
            sim.SetEnabled(true);
        }

        // #485: on the island and again out of the elevator, W moves the owner's body the frame it is read, and normal running is
        // never put back by the server (MoveGuard).
        [UnityTest, Timeout(600000)]
        public IEnumerator RemoteClient_MovesAtOnce_AndRunsWithoutCorrections_OnTheRealDedicatedServer()
        {
            var input = new HarnessInput();
            yield return StartServerAndJoin(input, "move-server-process.log");
            var me = Combatant.LocalPlayer;
            yield return NetHarness.WaitUntil(() => WaitingHud.OnIsland, "on the waiting island", Log, 30f);
            yield return Wait(1f); // past the arrival's loading frames
            var island = new OwnerMoveProbe();
            // The island countdown is --start-seconds (5 here): 1 + 1.5 s are done before it sends the body down. DEDICATED_ISLAND_SECONDS for a longer stay.
            yield return OwnerMoveProbe.Run(input, island, float.TryParse(Environment.GetEnvironmentVariable("DEDICATED_ISLAND_SECONDS"), out float stay) ? stay : 1.5f);
            Log("island: " + island);

            yield return NetHarness.WaitUntil(() => ServerText().Contains("countdown over"), "island countdown over", Log, 120f);
            yield return NetHarness.WaitUntil(() => Mathf.Abs(me.transform.position.x - WaitingIsland.CentreX) + Mathf.Abs(me.transform.position.z - WaitingIsland.CentreZ) > 300f, "the body left the island", Log, 30f);
            // In the closed car (the island's last broadcast no longer counts, #485): face the way out and hold W through the opening.
            yield return NetHarness.WaitUntil(() => SpawnProtection.InputBlocked && !WaitingHud.OnIsland, "in the closed elevator", Log, 30f);
            var site = ElevatorDoors.Instance.Sites[0];
            foreach (var x in ElevatorDoors.Instance.Sites)
                if ((x.Spawn - me.transform.position).sqrMagnitude < (site.Spawn - me.transform.position).sqrMagnitude) site = x;
            me.GetComponent<PlayerController>().LookAt(me.EyePosition + Quaternion.Euler(0f, site.YawDeg, 0f) * Vector3.forward * 20f);
            input.Frame.Move = new Vector2(0f, 1f);
            yield return NetHarness.WaitUntil(() => !SpawnProtection.InputBlocked && !ElevatorPanel.Riding, "elevator doors open", Log, 90f);
            float opened = Time.realtimeSinceStartup;
            var at = me.transform.position;
            yield return NetHarness.WaitUntil(() => new Vector2(me.transform.position.x - at.x, me.transform.position.z - at.z).magnitude > 2.5f, "out past the door", Log, 10f);
            float leftAfter = Time.realtimeSinceStartup - opened;
            input.Frame.Move = Vector2.zero;
            Log($"left the elevator {leftAfter:F2} s after the doors opened");
            yield return Wait(1f);
            int refusedBefore = Regex.Matches(ServerText(), "refused Move").Count;
            var field = new OwnerMoveProbe();
            yield return OwnerMoveProbe.Run(input, field, float.TryParse(Environment.GetEnvironmentVariable("DEDICATED_RUN_SECONDS"), out float runFor) ? runFor : 5f);
            yield return Wait(1.5f); // a correction the server sent at the end is still on its way
            int refused = Regex.Matches(ServerText(), "refused Move").Count - refusedBefore;
            Log($"field: {field} serverRefusedMove={refused}");

            foreach (var (name, r) in new[] { ("island", island), ("field", field) })
            {
                Assert.LessOrEqual(r.SecondsToMove, 0.1f, $"{name}: W moves the owner's body at once, the server is not asked ({r})");
                Assert.AreEqual(0, r.Corrections, $"{name}: no server correction while running normally ({r})");
                Assert.AreEqual(0, r.SnapsBack, $"{name}: the body is never put back ({r})");
            }
            Assert.AreEqual(0, refused, "the server refused none of the run (MoveGuard)");
            // Walking 2.5 m (the door is ~1.4 m ahead) takes ~0.6 s: the door is down by then, it does not hold the body (#485).
            Assert.LessOrEqual(leftAfter, 0.8f, "W held: the body walks out the moment input is allowed");
            Assert.Greater(field.Meters, 5f, "the body ran out of the elevator");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator RemoteClient_OpensAMapDoor_OnTheRealDedicatedServer()
        {
            var input = new HarnessInput();
            yield return StartServerAndJoin(input, "door-server-process.log");

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
            // Out of the elevator on foot once its doors open (a real player cannot teleport out, #500).
            yield return NetHarness.WaitUntil(() => SpawnProtection.InputBlocked && !WaitingHud.OnIsland, "in the closed elevator", Log, 30f);
            var site = ElevatorDoors.Instance.Sites[0];
            foreach (var x in ElevatorDoors.Instance.Sites)
                if ((x.Spawn - me.transform.position).sqrMagnitude < (site.Spawn - me.transform.position).sqrMagnitude) site = x;
            yield return NetHarness.WaitUntil(() => !SpawnProtection.InputBlocked && !ElevatorPanel.Riding, "elevator doors open", Log, 90f);
            var outside = site.Spawn + Quaternion.Euler(0f, site.YawDeg, 0f) * Vector3.forward * 4f;
            yield return WalkTo(input, controller, me, outside, 10f);

            // The entrance door nearest the body; walk up to it along the zone's navmesh, the way a person runs there.
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
            Assert.IsTrue(UnityEngine.AI.NavMesh.SamplePosition(from, out var start, 5f, UnityEngine.AI.NavMesh.AllAreas), "the body stands on the zone's navmesh");
            Assert.IsTrue(UnityEngine.AI.NavMesh.SamplePosition(stand, out var goal, 3f, UnityEngine.AI.NavMesh.AllAreas), "the door's front is on the navmesh");
            var path = new UnityEngine.AI.NavMeshPath();
            Assert.IsTrue(UnityEngine.AI.NavMesh.CalculatePath(start.position, goal.position, UnityEngine.AI.NavMesh.AllAreas, path) && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete, "a way to the door");
            foreach (var corner in path.corners) yield return WalkTo(input, controller, me, corner, 20f);
            yield return WalkTo(input, controller, me, stand, 5f, 0.6f);
            yield return Wait(1.5f);
            StringAssert.DoesNotContain("refused Teleport", ServerText(), "no teleport on the way");
            StringAssert.DoesNotContain("refused Move", ServerText(), "the server took the walk to the door");

            Assert.AreEqual(DoorState.Closed, door.State);
            // F the way a person presses it: through PlayerController (door vs pickup, reach, the request to the server).
            controller.LookAt(at + Vector3.up * 1.4f);
            yield return Wait(0.3f);
            Assert.AreNotEqual(float.MaxValue, DoorSystem.Bearing(me.EyePosition, controller.Camera != null ? controller.Camera.transform.forward : me.transform.forward), "the door is in reach and in view");
            input.Frame.Interact = true;
            yield return NetHarness.WaitUntil(() => door.State != DoorState.Closed, "the server opened the door for the remote client", Log, 5f);
        }

        // Runs the local player to `target` with the input a person gives (look that way, W and sprint), then stops.
        IEnumerator WalkTo(HarnessInput input, PlayerController controller, Combatant me, Vector3 target, float timeout, float reach = 1f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            input.Frame.Sprint = true;
            while (true)
            {
                var d = target - me.transform.position;
                float flat = new Vector2(d.x, d.z).magnitude;
                if (flat <= reach) break;
                if (Time.realtimeSinceStartup > end) { input.Frame.Move = Vector2.zero; Assert.Fail($"walking to {target}: stuck at {me.transform.position}, {flat:F1} m short"); }
                controller.LookAt(new Vector3(target.x, me.EyePosition.y, target.z));
                input.Frame.Move = new Vector2(0f, 1f);
                input.Frame.Sprint = flat > 3f;
                yield return null;
            }
            input.Frame.Move = Vector2.zero;
            input.Frame.Sprint = false;
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
