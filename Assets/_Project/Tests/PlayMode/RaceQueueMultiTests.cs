using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #513 (ADR-562, Protocol 9): a race party queues on the dedicated lobby with its course, laps, bots and each member's car and paint; the
    // lobby starts it as soon as the party is in (nobody else mixed in), and the server-only race scene seats each person in their own car,
    // fills the bots, runs the party's laps and reaches Over after the results (where DedicatedServer goes back to its lobby).
    public class RaceQueueMultiTests
    {
        const ushort Port = 17980;
        const long Party = 42;
        readonly List<LobbyNet> _rooms = new List<LobbyNet>();
        MultiHarness _net;
        void Log(string s) => Debug.Log(s);

        LobbyNet Room(string token)
        {
            var room = new GameObject("RaceQueueLobby" + _rooms.Count).AddComponent<LobbyNet>();
            if (token != null) room.SessionToken = token;
            _rooms.Add(room);
            return room;
        }

        void CloseRooms() { foreach (var r in _rooms) if (r != null) { r.Shutdown(); Object.Destroy(r.gameObject); } _rooms.Clear(); }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            CloseRooms();
            LobbyNet.QueuePartyId = 0;
            RaceEvent.Starting = null;
            if (_net != null) yield return _net.Teardown();
            _net = null;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator RaceParty_Queued_SeatedInTheirCars_BotsFill_PartysLaps_OverAfterResults()
        {
            // ---- The dedicated lobby: two racers of one party, and a third person whose requests must not mix in ----
            var server = Room(null);
            Assert.IsTrue(server.StartDedicated(Port));
            var a = Room("racer-a"); var b = Room("racer-b"); var c = Room("other");
            foreach (var r in new[] { a, b, c }) Assert.IsTrue(r.Join("127.0.0.1", Port));
            yield return NetHarness.WaitUntil(() => server.PlayerCount == 3 && a.Phase == LobbyNet.PhaseKind.InLobby && b.Phase == LobbyNet.PhaseKind.InLobby && c.Phase == LobbyNet.PhaseKind.InLobby, "three in the room", Log);

            LobbyNet.QueuePartyId = Party;
            Assert.IsTrue(a.RequestRace(2, 1, 1, 2, "GoblinV12", 3), "the first request: snake, 1 lap, 2 bots");
            Assert.IsFalse(a.RequestRace(2, 1, 4, 2, "GoblinV12", 3), "4 laps is not a choice");
            yield return NetHarness.WaitUntil(() => server.QueueActive, "the race is queued", Log);
            LobbyNet.QueuePartyId = 99;
            Assert.IsTrue(c.RequestRace(1, 1, 2, 6, "FairheavenLT80", 0), "another party's race");
            LobbyNet.QueuePartyId = 0;
            Assert.IsTrue(c.RequestMatch(TeamMode.Solo, 1), "a battle royale");
            yield return new WaitForSecondsRealtime(1f);
            Assert.IsFalse(server.HasStarted, "the party is not all in yet");
            LobbyNet.QueuePartyId = Party;
            Assert.IsTrue(b.RequestRace(2, 1, 3, 5, "Shvan92", 1), "the second member: its laps and bots are not the party's");
            yield return NetHarness.WaitUntil(() => a.HasStarted && b.HasStarted, "the race starts once the party is in", Log, 10f);

            foreach (var r in new[] { a, b })
            {
                Assert.AreEqual(1, r.StartInfo.Event, "course 1");
                Assert.AreEqual(1, r.StartInfo.Laps, "the party's laps");
                Assert.AreEqual(2, r.StartInfo.Bots, "the party's bots");
                Assert.AreEqual(2, r.StartInfo.Players);
            }
            Assert.IsFalse(c.HasStarted, "nobody else mixed in");
            var plan = server.RacePlan;
            Assert.AreEqual("snake", plan.CourseId);
            Assert.AreEqual(2, plan.People);
            Assert.AreEqual(2, plan.Racers.Count);
            CollectionAssert.AreEquivalent(new[] { "racer-a", "racer-b" }, plan.Racers.ConvertAll(e => e.Token), "each racer by session token");
            CloseRooms();
            yield return null;

            // ---- The race on a server-only match, as DedicatedServer starts it ----
            RaceEvent.Starting = plan;
            RaceStandings aSaw = default;
            _net = new MultiHarness { Log = Log };
            yield return _net.StartDedicated(RaceEvent.Courses[0].Scene);
            RemoteClient ra = null, rb = null;
            yield return _net.AddClient(x => ra = x, "racer-a", nm => nm.ClientManager.RegisterBroadcast<RaceStandings>((m, ch) => aSaw = m));
            yield return _net.AddClient(x => rb = x, "racer-b");
            var race = RaceDirector.Current;
            Combatant OnServer(RemoteClient r) => _net.Host.ServerManager.Objects.Spawned.TryGetValue(r.Player.ObjectId, out var nob) ? nob.GetComponent<Combatant>() : null;   // no client side to replicate to
            var sa = OnServer(ra);
            var sb = OnServer(rb);
            yield return NetHarness.WaitUntil(() => race.CarOf(sa) != null && race.CarOf(sb) != null, "a car each", Log, 15f);
            Assert.AreEqual("GoblinV12", race.CarOf(sa).ModelName, "a's car");
            Assert.AreEqual(3, race.CarOf(sa).PaintIndex.Value, "a's paint");
            Assert.AreEqual("Shvan92", race.CarOf(sb).ModelName, "b's car");
            Assert.AreEqual(1, race.CarOf(sb).PaintIndex.Value, "b's paint");
            yield return NetHarness.WaitUntil(() => (RacePhase)aSaw.Phase == RacePhase.Racing && aSaw.Keys != null, "GO", Log, 30f);
            Assert.AreEqual(4, race.CarCount, "two people and two bots");
            Assert.AreEqual(4, aSaw.Keys.Length);
            Assert.AreEqual(1, race.Laps);
            Assert.AreEqual(1, aSaw.Laps, "the party's laps (the course's own is " + RaceEvent.Courses[0].Laps + ")");

            // a drives (its client simulates its car) until the server says it finished.
            yield return NetHarness.WaitUntil(() => VehicleSeat.VehicleOf(ra.Player) != null, "a at the wheel on its client", Log, 10f);
            var car = VehicleSeat.VehicleOf(ra.Player);
            var course = RaceEvent.Courses[0].Course;
            float speed = course.LapLength / (course.MinLapSeconds + 3f);
            float s = course.Project(car.transform.position.x, car.transform.position.z).s - course.LapLength;
            bool ADone() { int k = aSaw.Keys != null ? System.Array.IndexOf(aSaw.Keys, sa.ObjectId) : -1; return k >= 0 && aSaw.Done[k]; }
            // One lap from just behind the line; a false start (the grid cars settling) would add one more (ADR-559).
            while (!ADone() && s < course.LapLength * 2 + 30f)
            {
                s += speed * Time.deltaTime;
                var (x, z, yaw) = course.PointAt(s);
                car.Place(new Vector3(x, 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
                yield return null;
            }
            yield return NetHarness.WaitUntil(() => (RacePhase)aSaw.Phase == RacePhase.Finishing || (RacePhase)aSaw.Phase == RacePhase.Results, "a finished after one lap", Log, 10f);
            int ka = System.Array.IndexOf(aSaw.Keys, sa.ObjectId);
            Assert.GreaterOrEqual(ka, 0);
            Assert.IsTrue(aSaw.Done[ka]);
            Assert.AreEqual(aSaw.LapsToGo[ka], aSaw.LapsDone[ka], "finished on its laps");
            Assert.LessOrEqual(aSaw.LapsToGo[ka], 2, "the party's one lap (plus one after a false start), not the course's two (three)");
            yield return NetHarness.WaitUntil(() => race.Flow.Phase == RacePhase.Over, "Over after the results (DedicatedServer ends the match there)", Log,
                RaceEvent.AfterWinnerSeconds + RaceEvent.ResultsSeconds + 10f);
        }
    }
}
