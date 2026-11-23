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
    // #160: the lobby over localhost (host + guests as separate LobbyNet rooms in one process), the refusals, and the
    // way from the main menu's lobby into the elevator start of the real World match.
    public class LobbyPlayTests
    {
        const string LogDir = "Logs/net-lobby";
        static ushort _port = 17900;
        readonly System.Collections.Generic.List<LobbyNet> _rooms = new System.Collections.Generic.List<LobbyNet>();
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();
        MatchRules _rules;
        int _maxPlayers;

        void Log(string line) { Debug.Log(line); _log.AppendLine(line); }

        LobbyNet NewRoom()
        {
            var room = new GameObject("TestLobby" + _rooms.Count).AddComponent<LobbyNet>();
            _rooms.Add(room);
            return room;
        }

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            _rules = MatchRules.Current;
            _maxPlayers = _rules.MaxPlayers;
            NetSession.Reset();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            _rules.MaxPlayers = _maxPlayers;
            foreach (var r in _rooms) if (r != null) { r.Shutdown(); Object.Destroy(r.gameObject); }
            _rooms.Clear();
            PlayerController.InputOverride = null;
            LobbyNet.ServerTokenSecret = LobbyNet.AuthToken = null;
            LobbyNet.AssignedCheck = null;
            NetSession.Reset();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        IEnumerator HostWithGuest(System.Action<LobbyNet, LobbyNet> done, ushort port)
        {
            var host = NewRoom();
            Assert.IsTrue(host.StartHost(port));
            var guest = NewRoom();
            Assert.IsTrue(guest.Join("127.0.0.1", port));
            yield return NetHarness.WaitUntil(() => host.Roster.Count == 2 && guest.Phase == LobbyNet.PhaseKind.InLobby, "host and guest in the room", Log);
            done(host, guest);
        }

        [UnityTest]
        public IEnumerator Guest_JoinsReadiesUp_AndTheHostStartsForBoth()
        {
            LobbyNet host = null, guest = null;
            yield return HostWithGuest((h, g) => { host = h; guest = g; }, _port++);

            Assert.IsTrue(host.IsHost);
            Assert.AreEqual(2, guest.Roster.Count, "the guest sees both people");
            Assert.IsFalse(host.CanStart, "the guest is not ready yet");
            Assert.IsFalse(host.StartMatch());

            guest.SetReady(true);
            yield return NetHarness.WaitUntil(() => host.CanStart, "guest ready seen by the host", Log);
            host.SetBots(3);
            yield return NetHarness.WaitUntil(() => guest.Bots == 3, "bot count reaches the guest", Log);

            int starts = 0;
            host.MatchStarting += () => starts++;
            guest.MatchStarting += () => starts++;
            Assert.IsTrue(host.StartMatch());
            yield return NetHarness.WaitUntil(() => starts == 2, "both rooms told to start", Log);
            Assert.AreEqual(host.StartInfo.MatchPort, guest.StartInfo.MatchPort);
            Assert.AreEqual(2, guest.StartInfo.Players);
            Assert.AreEqual(3, guest.StartInfo.Bots);
        }

        [UnityTest]
        public IEnumerator DifferentVersion_IsRefusedWithTheReason()
        {
            ushort port = _port++;
            var host = NewRoom();
            Assert.IsTrue(host.StartHost(port));
            var guest = NewRoom();
            guest.Version = "0.0.0-other/1";
            guest.Join("127.0.0.1", port);
            yield return NetHarness.WaitUntil(() => guest.Phase == LobbyNet.PhaseKind.Failed, "guest refused", Log);
            Assert.AreEqual(LobbyRules.Reason(JoinResult.VersionMismatch), guest.FailureText);
            Assert.AreEqual(1, host.Roster.Count, "the refused guest never entered the room");
        }

        // #497: the backend answers "matched" to the client before this server's next poll of its match list sees the match.
        // The hello of an account it does not know yet waits for that poll instead of being refused at once.
        IEnumerator JoinAssignedServer(System.Func<bool> assigned, System.Action<LobbyNet> done)
        {
            const string secret = "test-secret";
            LobbyNet.ServerTokenSecret = secret;
            LobbyNet.AuthToken = GameToken.Sign(secret, 77, "late", System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            LobbyNet.AssignedCheck = account => account == 77 && assigned();
            ushort port = _port++;
            var server = NewRoom();
            Assert.IsTrue(server.StartDedicated(port));
            var guest = NewRoom();
            Assert.IsTrue(guest.Join("127.0.0.1", port));
            yield return NetHarness.WaitUntil(() => guest.Phase == LobbyNet.PhaseKind.InLobby || guest.Phase == LobbyNet.PhaseKind.Failed, "join answered", Log);
            done(guest);
        }

        [UnityTest]
        public IEnumerator AssignedJustAfterTheHello_StillGetsIn()
        {
            float assignAt = Time.realtimeSinceStartup + 1.5f;
            LobbyNet guest = null;
            yield return JoinAssignedServer(() => Time.realtimeSinceStartup >= assignAt, g => guest = g);
            Assert.AreEqual(LobbyNet.PhaseKind.InLobby, guest.Phase, guest.FailureText);
        }

        [UnityTest]
        public IEnumerator NeverAssigned_IsRefusedWithTheReason()
        {
            LobbyNet guest = null;
            yield return JoinAssignedServer(() => false, g => guest = g);
            Assert.AreEqual(LobbyRules.Reason(JoinResult.NotAssigned), guest.FailureText);
        }

        [UnityTest]
        public IEnumerator FullRoom_RefusesTheNextGuest()
        {
            MatchRules.Current.MaxPlayers = 2;
            LobbyNet host = null, guest = null;
            ushort port = _port++;
            yield return HostWithGuest((h, g) => { host = h; guest = g; }, port);
            var third = NewRoom();
            third.Join("127.0.0.1", port);
            yield return NetHarness.WaitUntil(() => third.Phase == LobbyNet.PhaseKind.Failed, "third guest refused", Log);
            Assert.AreEqual(LobbyRules.Reason(JoinResult.Full), third.FailureText);
            Assert.AreEqual(2, host.Roster.Count);
        }

        [UnityTest]
        public IEnumerator NobodyThere_FailsWithTheAddressHint()
        {
            var guest = NewRoom();
            guest.Join("127.0.0.1", _port++);
            yield return NetHarness.WaitUntil(() => guest.Phase == LobbyNet.PhaseKind.Failed, "join gives up", Log, 15f);
            Assert.AreEqual(LobbyRules.Unreachable, guest.FailureText);
        }

        // r5 playtest: one menu's room, two matches in a row against a dedicated server. After the first match the server opens a
        // new room on the same port (DedicatedServer.OpenLobby); the same client LobbyNet joins again, queues, and is started again.
        [UnityTest, Timeout(60000)]
        public IEnumerator DedicatedQueue_TwoMatchesInARow_SameClientRoom()
        {
            float window = _rules.QueueWindowSeconds;
            _rules.QueueWindowSeconds = 1f;
            try
            {
                ushort port = _port++;
                var client = NewRoom();
                for (int round = 1; round <= 2; round++)
                {
                    var server = NewRoom();
                    Assert.IsTrue(server.StartDedicated(port), "server room " + round);
                    Assert.IsTrue(client.Join("127.0.0.1", port));
                    yield return NetHarness.WaitUntil(() => client.Phase == LobbyNet.PhaseKind.InLobby && server.PlayerCount == 1, "in the room, round " + round, Log);
                    int starts = 0;
                    System.Action counted = () => starts++;
                    client.MatchStarting += counted;
                    Assert.IsTrue(client.RequestMatch(TeamMode.Solo, 1), "queued, round " + round);
                    yield return NetHarness.WaitUntil(() => server.QueueActive, "the server queued it, round " + round, Log);
                    yield return NetHarness.WaitUntil(() => starts == 1, "the match starts, round " + round, Log, 15f);
                    client.MatchStarting -= counted;
                    Assert.AreEqual(1, client.StartInfo.Players);
                    // The menu leaves the room for the match; the server closes its room and later opens a new one.
                    client.Shutdown();
                    server.Shutdown();
                    yield return null;
                }
            }
            finally { _rules.QueueWindowSeconds = window; }
        }

        [UnityTest]
        public IEnumerator GuestLeaving_FreesItsPlaceInTheRoom()
        {
            LobbyNet host = null, guest = null;
            yield return HostWithGuest((h, g) => { host = h; guest = g; }, _port++);
            guest.Shutdown();
            yield return NetHarness.WaitUntil(() => host.Roster.Count == 1, "guest removed from the room", Log);
        }

        // The whole way: main menu 방 만들기 -> a guest joins and readies -> 시작 -> the World match waits for both in
        // the elevators (the doors do not open for the first one alone) -> the guest's client connects -> the doors are scheduled.
        [UnityTest, Timeout(180000)]
        public IEnumerator MainMenuHost_StartsTheWorldMatch_WaitingForTheGuestInTheElevators()
        {
            var net = new MultiHarness { Log = Log };
            net.PrepareHostInput();
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null, "main menu", Log);
            yield return null;
            MainMenu.Instance.OpenHost();
            var hostRoom = MainMenu.Instance.Lobby.Lobby;
            yield return NetHarness.WaitUntil(() => hostRoom.Phase == LobbyNet.PhaseKind.InLobby, "menu host in its room", Log);
            Assert.AreEqual(LobbyScreen.ViewKind.Room, MainMenu.Instance.Lobby.View);

            var guest = NewRoom();
            Assert.IsTrue(guest.Join("127.0.0.1", hostRoom.Port));
            yield return NetHarness.WaitUntil(() => hostRoom.Roster.Count == 2 && guest.Phase == LobbyNet.PhaseKind.InLobby, "guest in the host's room", Log);
            guest.SetReady(true);
            hostRoom.SetBots(2);
            yield return NetHarness.WaitUntil(() => hostRoom.CanStart && guest.Bots == 2, "ready and bots synced", Log);

            Assert.IsTrue(hostRoom.StartMatch());
            yield return NetHarness.WaitUntil(() => guest.HasStarted, "guest got the start", Log);
            yield return NetHarness.WaitUntil(() => SceneManager.GetActiveScene().name == MainMenu.MatchScene && MatchDirector.Instance != null, "World loaded", Log, 90f);
            Assert.AreEqual(NetSession.RoleKind.Host, NetSession.Role);
            Assert.AreEqual(guest.StartInfo.MatchPort, MatchDirector.Instance.Port, "the match listens where the guest was told");
            yield return net.AdoptHost();

            var director = MatchDirector.Instance;
            Assert.AreEqual(2, director.ExpectedPlayers);
            Assert.AreEqual(2, director.BotCount);
            Assert.AreEqual(4, director.ElevatorAssignment.Count, "2 people + 2 bots share the deployment");
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.IsFalse(director.Elevators.ServerScheduled, "the doors wait for the second person");

            // The guest's process would do exactly this: connect to the host's match port.
            RemoteClient remote = null;
            yield return net.AddClient(c => remote = c);
            yield return NetHarness.WaitUntil(() => director.Elevators.ServerScheduled, "doors scheduled once both stand in their elevators", Log);
            // The server's list: the host's own client only sees a player within the interest radius (#167), and the
            // two elevators stand in different regions.
            int players = 0;
            foreach (var nob in net.Host.ServerManager.Objects.Spawned.Values)
                if (nob != null && nob.GetComponent<Combatant>() != null && nob.GetComponent<Combatant>().IsPlayer) players++;
            Assert.AreEqual(2, players);
            Assert.AreNotEqual(net.HostPlayer.transform.position, remote.Player.transform.position);
            yield return net.Teardown();
        }
    }
}
