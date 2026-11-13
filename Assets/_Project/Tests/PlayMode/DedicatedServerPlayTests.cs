using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Game.Tests
{
    // #168: the built dedicated server (Build/server, made by `tools/unity.sh build server`) started on localhost
    // with command-line arguments, joined through the real lobby by a client in this process, left, and stopped with SIGTERM.
    // Skipped when the server build does not exist. Only 127.0.0.1 is used.
    public class DedicatedServerPlayTests
    {
        const string ServerApp = "Build/server/SublevelServer.app/Contents/MacOS";
        const ushort Port = 17950;
        const string LogDir = "Logs/net-dedicated";
        Process _server;
        string _serverLog;
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

        static int Count(string text, string needle)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        [TearDown]
        public void TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log + "\n--- server ---\n" + (_serverLog != null ? ServerText() : ""));
            if (_lobby != null) { _lobby.Shutdown(); UnityEngine.Object.Destroy(_lobby.gameObject); }
            if (_server != null && !_server.HasExited) _server.Kill();
            NetSession.Reset();
        }

        [UnityTest, Timeout(420000)]
        public IEnumerator Server_StartsWithArguments_AcceptsAGuestThroughTheLobby_ReturnsToTheLobby_AndStopsCleanly()
        {
            string dir = Path.GetFullPath(ServerApp);
            if (!Directory.Exists(dir)) Assert.Ignore("no server build at " + dir + " (tools/unity.sh build server)");
            string exe = Directory.GetFiles(dir)[0];
            Directory.CreateDirectory(LogDir);
            _serverLog = Path.GetFullPath(Path.Combine(LogDir, "server-process.log"));
            if (File.Exists(_serverLog)) File.Delete(_serverLog);
            var info = new ProcessStartInfo(exe, $"-batchmode -nographics -logFile \"{_serverLog}\" --dedicated --port {Port} --bots 2 --region C --max-players 4 --start-seconds 0")
            { UseShellExecute = false };
            _server = Process.Start(info);
            yield return NetHarness.WaitUntil(() => ServerText().Contains($"lobby open on port {Port}"), "server lobby open", Log, 120f);
            string text = ServerText();
            StringAssert.Contains("max players 4", text, "--max-players read");
            StringAssert.Contains("bots 2", text, "--bots read");
            StringAssert.Contains("region C", text, "--region read");

            // A client joins from the menu's path: the lobby of the server (no local player there).
            _lobby = new GameObject("GuestLobby").AddComponent<LobbyNet>();
            Assert.IsTrue(_lobby.Join("127.0.0.1", Port));
            yield return NetHarness.WaitUntil(() => _lobby.Phase == LobbyNet.PhaseKind.InLobby && _lobby.Roster.Count == 1, "guest in the dedicated lobby", Log, 30f);
            Assert.AreEqual(2, _lobby.Bots, "the server's --bots reaches the guest");
            Assert.AreEqual(4, _lobby.MaxPlayers, "the server's --max-players reaches the guest");
            _lobby.MatchStarting += () => _log.AppendLine("MatchStarting");
            _lobby.SetReady(true);
            yield return NetHarness.WaitUntil(() => _lobby.HasStarted, "server started the match for the ready guest", Log, 30f);
            Assert.AreEqual(1, _lobby.StartInfo.Players);
            Assert.AreEqual(2, _lobby.StartInfo.Bots);
            Assert.AreEqual(Port + 1, _lobby.StartInfo.MatchPort, "the match listens on the lobby port + 1");

            // The client goes into the match the way the menu does.
            NetSession.Begin(_lobby);
            _lobby.Shutdown();
            PlayerController_NoInput();
            SceneManager.LoadScene("World");
            yield return NetHarness.WaitUntil(() => MatchDirector.Instance != null && Combatant.LocalPlayer != null, "client's player exists on the server's match", Log, 150f);
            yield return NetHarness.WaitUntil(() => Combatant.All.Count == 3, "1 player + 2 bots seen by the client", Log, 30f);
            text = ServerText();
            StringAssert.Contains("match starting: 1 players, 2 bots, port " + (Port + 1), text);
            StringAssert.Contains("first player connected", text);
            StringAssert.Contains("final region 2 (forced)", text, "--region C is the third region");

            // Everybody leaves: the combatant is held for the reconnect time (ADR-125), then the match counts as empty and
            // after the empty period the server opens the lobby again.
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return NetHarness.WaitUntil(() => ServerText().Contains("match is empty"), "server noticed the empty match", Log, MatchRules.Current.ReconnectSeconds + 30f);
            yield return NetHarness.WaitUntil(() => Count(ServerText(), "lobby open on port") == 2, "server back in the lobby", Log, ServerRules.EmptySeconds + 40f);

            // SIGTERM stops it cleanly.
            using (var kill = Process.Start("kill", "-TERM " + _server.Id)) kill.WaitForExit();
            float end = Time.realtimeSinceStartup + 20f;
            while (!_server.HasExited && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(_server.HasExited, "the server exits on SIGTERM");
            StringAssert.Contains("[Server] shutting down", ServerText());
        }

        static void PlayerController_NoInput() => Game.Gameplay.PlayerController.InputOverride = new HarnessInput();
    }
}
