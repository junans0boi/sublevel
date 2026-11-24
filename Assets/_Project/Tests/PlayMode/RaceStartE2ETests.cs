using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #521 acceptance (spec #505, ADR-562 (4)): a race room of two on a REAL local backend (sublevel-backend, temp DB, its game server
    // = 127.0.0.1:17990) and a local dedicated lobby, two clients in this process (the game's HttpBackend, SocialClient, RaceParty,
    // BackendMatchQueue and LobbyNet, glued as MainMenu glues them). The host's Start makes the race ticket; both queues see it matched,
    // join the lobby and ask its race with the snapshot's course/laps/bots and their own car/paint; the race runs on a server-only match
    // with bots in the open slots until Over; back in the room both are unready on the backend, in the same party. Ignored unless the
    // environment names the backend and two fresh accounts' session ids (never logged): SUBLEVEL_E2E_API, SUBLEVEL_E2E_GAME_A/B.
    public class RaceStartE2ETests
    {
        const ushort Port = 17990;
        static string Env(string n) => Environment.GetEnvironmentVariable(n);
        void Log(string s) => Debug.Log("[E2E521] " + s);

        sealed class Client
        {
            public HttpBackend Api; public SocialClient Social; public RaceLobby Room; public RaceParty Party; public BackendMatchQueue Queue;
            public LobbyNet Lobby; public long Id; public string Nick; public string Toast;
            public void Tick()
            {
                double now = Time.realtimeSinceStartupAsDouble;
                Social.Tick(now); Party.Sync(); Queue.Tick(now);
                if (Lobby != null && Lobby.HasStarted && Queue.RaceStarting) Queue.RaceStarted();   // MainMenu.StartLobbyMatch
            }
        }

        readonly List<LobbyNet> _rooms = new List<LobbyNet>();
        MultiHarness _net;

        LobbyNet Room(string token)
        {
            var room = new GameObject("RaceStartLobby" + _rooms.Count).AddComponent<LobbyNet>();
            if (token != null) room.SessionToken = token;
            _rooms.Add(room);
            return room;
        }

        void CloseRooms() { foreach (var r in _rooms) if (r != null) { r.Shutdown(); UnityEngine.Object.Destroy(r.gameObject); } _rooms.Clear(); }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            CloseRooms();
            LobbyNet.QueuePartyId = 0;
            RaceEvent.Starting = null;
            MatchTicketClient.ForgetConsumed();
            if (_net != null) yield return _net.Teardown();
            _net = null;
        }

        static IEnumerator Await<T>(Task<T> t, float timeout = 10f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!t.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(t.IsCompleted, "backend call timed out");
        }

        static IEnumerator Ok<T>(Task<ApiResult<T>> t, string what) { yield return Await(t); Assert.IsTrue(t.Result.Ok, what + ": " + t.Result.Status + " " + t.Result.Error); }

        // A fresh menu's room for the client (after the race the menu is rebuilt: new SocialClient, room, queue).
        void Menu(Client c, string token)
        {
            c.Social = new SocialClient(c.Api);
            c.Room = new RaceLobby(new RaceLobbyMember { Id = (int)c.Id, Name = c.Nick, Car = "FairheavenLT80" });
            c.Party = new RaceParty(() => c.Social, c.Room, (int)c.Id);
            c.Queue = new BackendMatchQueue(new MatchTicketClient(c.Api), c.Social, (h, p, n) => { c.Lobby = Room(token); Assert.IsTrue(c.Lobby.Join(h, (ushort)p)); },
                () => c.Lobby, () => c.Id);
            c.Queue.RaceFailed += t => c.Toast = t;
        }

        IEnumerator Login(Client c, string token)
        {
            var me = c.Api.Me(); yield return Await(me);
            Assert.IsTrue(me.Result.Ok, "/api/me");
            c.Id = me.Result.Value.id; c.Nick = me.Result.Value.nickname;
            Menu(c, token);
        }

        IEnumerator Until(Client a, Client b, Func<bool> ok, string what, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!ok() && Time.realtimeSinceStartup < end) { a.Tick(); b.Tick(); yield return null; }
            Assert.IsTrue(ok(), what + (a.Toast != null ? " (host toast: " + a.Toast + ")" : "") + (b.Toast != null ? " (guest toast: " + b.Toast + ")" : ""));
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator RaceRoomOfTwo_StartsOnTheDedicatedServer_RacesInTheirCars_BackInTheRoomUnready()
        {
            string api = Env("SUBLEVEL_E2E_API"), ta = Env("SUBLEVEL_E2E_GAME_A"), tb = Env("SUBLEVEL_E2E_GAME_B");
            if (string.IsNullOrEmpty(api) || string.IsNullOrEmpty(ta) || string.IsNullOrEmpty(tb)) Assert.Ignore("no local backend configured (SUBLEVEL_E2E_*)");
            var a = new Client { Api = new HttpBackend(api, ta) };
            var b = new Client { Api = new HttpBackend(api, tb) };
            yield return Login(a, "racer-a"); yield return Login(b, "racer-b");

            // ---- the room: a friend's party of two, snake x1, b in Shvan92 paint 1 and ready ----
            var add = a.Api.AddFriend(b.Nick); yield return Await(add);   // RacePartyE2ETests may have made them friends already
            Assert.IsTrue(add.Result.Ok || add.Result.Error == "already_friends", "friend request: " + add.Result.Error);
            if (add.Result.Ok) yield return Ok(b.Api.AcceptFriend(a.Id), "friend accept");
            yield return Ok(a.Api.InviteToParty(b.Id), "invite");
            var inv = b.Api.PartyInvites(); yield return Await(inv);
            yield return Ok(b.Api.AcceptInvite(inv.Result.Value.invites[0].party_id), "accept invite");
            yield return Until(a, b, () => a.Party.Online && b.Party.Online, "both rooms on the party", 8f);
            long partyId = a.Social.Party.id;
            Assert.IsTrue(a.Party.SetCourse("snake", 1));
            yield return Until(a, b, () => b.Room.Course == "snake" && b.Room.Laps == 1, "the guest sees snake x1", 6f);
            Assert.IsTrue(b.Party.SetCar("Shvan92", 1));
            yield return Until(a, b, () => a.Room.Member((int)b.Id).Car == "Shvan92", "the host sees the guest's car", 6f);
            Assert.IsTrue(b.Party.ToggleReady());
            yield return Until(a, b, () => a.Room.Member((int)b.Id).Ready, "the host sees the guest ready", 6f);
            Assert.IsTrue(a.Room.GoFor((int)a.Id).Enabled);

            // ---- the host's 시작: the race ticket; both queues join the dedicated lobby and the race starts from LobbyStart ----
            var server = Room(null);
            Assert.IsTrue(server.StartDedicated(Port));
            float t0 = Time.realtimeSinceStartup;
            a.Queue.StartRace(Time.realtimeSinceStartupAsDouble);
            Assert.IsTrue(a.Queue.RaceStarting, "출발 준비 중 at once");
            yield return Until(a, b, () => b.Queue.RaceStarting, "the guest's room sees the start (출발 준비 중)", 6f);
            yield return Until(a, b, () => a.Lobby != null && b.Lobby != null && a.Lobby.HasStarted && b.Lobby.HasStarted, "the race starts for both", 20f);
            Log($"ticket -> LobbyStart for both in {Time.realtimeSinceStartup - t0:0.00}s");
            a.Tick(); b.Tick();   // the frame that sees LobbyStart (MainMenu does it on the lobby's event)
            Assert.IsFalse(a.Queue.RaceStarting || b.Queue.RaceStarting, "the ticket is done with");
            int snake = Array.FindIndex(RaceEvent.Courses, c => c.Id == "snake") + 1;
            foreach (var c in new[] { a, b })
            {
                Assert.AreEqual(snake, c.Lobby.StartInfo.Event, "the host's course");
                Assert.AreEqual(1, c.Lobby.StartInfo.Laps, "the host's laps");
                Assert.AreEqual(6, c.Lobby.StartInfo.Bots, "bots in the 6 open slots");
                Assert.AreEqual(2, c.Lobby.StartInfo.Players);
            }
            var plan = server.RacePlan;
            Assert.AreEqual("snake", plan.CourseId);
            var ea = plan.Racers.Find(e => e.Token == "racer-a"); var eb = plan.Racers.Find(e => e.Token == "racer-b");
            Assert.AreEqual("FairheavenLT80", ea.Car); Assert.AreEqual(RaceLobbyScreen.RaceTint[0], ea.Paint);
            Assert.AreEqual("Shvan92", eb.Car); Assert.AreEqual(RaceLobbyScreen.RaceTint[1], eb.Paint);
            a.Lobby = b.Lobby = null;
            CloseRooms();
            yield return null;

            // ---- the race on a server-only match, as DedicatedServer starts it ----
            RaceEvent.Starting = plan;
            RaceStandings aSaw = default;
            _net = new MultiHarness { Log = Log };
            yield return _net.StartDedicated(RaceEvent.Courses[snake - 1].Scene);
            RemoteClient ra = null, rb = null;
            yield return _net.AddClient(x => ra = x, "racer-a", nm => nm.ClientManager.RegisterBroadcast<RaceStandings>((m, ch) => aSaw = m));
            yield return _net.AddClient(x => rb = x, "racer-b");
            var race = RaceDirector.Current;
            Combatant OnServer(RemoteClient r) => _net.Host.ServerManager.Objects.Spawned.TryGetValue(r.Player.ObjectId, out var nob) ? nob.GetComponent<Combatant>() : null;
            var sa = OnServer(ra); var sb = OnServer(rb);
            yield return NetHarness.WaitUntil(() => race.CarOf(sa) != null && race.CarOf(sb) != null, "a car each", Log, 15f);
            Assert.AreEqual("FairheavenLT80", race.CarOf(sa).ModelName);
            Assert.AreEqual("Shvan92", race.CarOf(sb).ModelName);
            Assert.AreEqual(RaceLobbyScreen.RaceTint[1], race.CarOf(sb).PaintIndex.Value);
            yield return NetHarness.WaitUntil(() => (RacePhase)aSaw.Phase == RacePhase.Racing && aSaw.Keys != null, "GO", Log, 30f);
            Assert.AreEqual(8, race.CarCount, "two people and six bots");
            Assert.AreEqual(1, aSaw.Laps, "the host's laps");

            yield return NetHarness.WaitUntil(() => VehicleSeat.VehicleOf(ra.Player) != null, "a at the wheel", Log, 10f);
            var car = VehicleSeat.VehicleOf(ra.Player);
            var course = RaceEvent.Courses[snake - 1].Course;
            float speed = course.LapLength / (course.MinLapSeconds + 3f);
            float s = course.Project(car.transform.position.x, car.transform.position.z).s - course.LapLength;
            bool ADone() { int k = aSaw.Keys != null ? Array.IndexOf(aSaw.Keys, sa.ObjectId) : -1; return k >= 0 && aSaw.Done[k]; }
            while (!ADone() && s < course.LapLength * 2 + 30f)
            {
                s += speed * Time.deltaTime;
                var (x, z, yaw) = course.PointAt(s);
                car.Place(new Vector3(x, 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
                yield return null;
            }
            Assert.IsTrue(ADone(), "a finished");
            yield return NetHarness.WaitUntil(() => race.Flow.Phase == RacePhase.Over, "Over after the results", Log,
                RaceEvent.AfterWinnerSeconds + RaceEvent.ResultsSeconds + 10f);
            yield return _net.Teardown();
            _net = null;
            RaceEvent.Starting = null;

            // ---- back in the menu: the room again (rebuilt), everyone unready on the backend, the same party, the ticket not adopted again ----
            Menu(a, "racer-a"); Menu(b, "racer-b");
            foreach (var c in new[] { a, b }) { c.Room.BackToRoom(); c.Party.UnreadyWhenOnline(); }
            yield return Until(a, b, () => a.Party.Online && b.Party.Online, "both back in the party's room", 8f);
            Assert.AreEqual(partyId, a.Social.Party.id); Assert.AreEqual(partyId, b.Social.Party.id);
            Assert.AreEqual((int)a.Id, b.Room.HostId);
            Assert.AreEqual("snake", b.Room.Course);
            Assert.IsFalse(b.Room.Member((int)b.Id).Ready, "unready at once");
            float end = Time.realtimeSinceStartup + 6f;
            bool backendUnready = false;
            while (!backendUnready && Time.realtimeSinceStartup < end)
            {
                var p = a.Api.Party(); yield return Await(p);
                backendUnready = Array.Find(p.Result.Value.party.members, m => m.account_id == b.Id)?.race?.ready == false;
                for (float w = Time.realtimeSinceStartup + 0.5f; Time.realtimeSinceStartup < w;) { a.Tick(); b.Tick(); yield return null; }
            }
            Assert.IsTrue(backendUnready, "the guest's ready is cleared on the backend");
            yield return Until(a, b, () => !a.Room.Member((int)b.Id).Ready, "the host sees the guest unready", 6f);
            Assert.IsFalse(a.Queue.RaceStarting || b.Queue.RaceStarting, "the finished race's ticket is not adopted again");
            Assert.IsNull(a.Toast); Assert.IsNull(b.Toast);
            yield return Ok(b.Api.LeaveParty(), "cleanup b");
            yield return Ok(a.Api.LeaveParty(), "cleanup a");
        }
    }
}
