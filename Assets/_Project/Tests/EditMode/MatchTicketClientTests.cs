using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Core;
using Game.Net;
using NUnit.Framework;

namespace Game.Tests
{
    public class MatchTicketClientTests
    {
        sealed class Fake : IBackend
        {
            public Queue<ApiResult<TicketDto>> Answers = new Queue<ApiResult<TicketDto>>();
            public int Polls; public string Cancelled; public ApiResult<TicketDto> Last;
            ApiResult<TicketDto> Next() { if (Answers.Count > 0) Last = Answers.Dequeue(); return Last; }
            public Task<ApiResult<TicketDto>> CreateTicket(string mode) => Task.FromResult(Next());
            public Task<ApiResult<TicketDto>> CurrentTicket() { Polls++; return Task.FromResult(Next()); }
            public Task<ApiResult<bool>> CancelTicket(string id) { Cancelled = id; return Task.FromResult(ApiResult<bool>.Success(true)); }
            public Task<ApiResult<bool>> Presence(string s) => Task.FromResult(ApiResult<bool>.Success(true));
            public Task<ApiResult<FriendsDto>> Friends() => Task.FromResult(ApiResult<FriendsDto>.Success(new FriendsDto()));
            public Task<ApiResult<bool>> AddFriend(string n) => Task.FromResult(ApiResult<bool>.Success(true));
            public Task<ApiResult<bool>> AcceptFriend(long i) => Task.FromResult(ApiResult<bool>.Success(true));
            public Task<ApiResult<bool>> RemoveFriend(long i) => Task.FromResult(ApiResult<bool>.Success(true));
            public PartyEnvelope PartyNow = new PartyEnvelope();
            public Task<ApiResult<PartyEnvelope>> Party() => Task.FromResult(ApiResult<PartyEnvelope>.Success(PartyNow));
            public Task<ApiResult<PartyEnvelope>> InviteToParty(long i) => Task.FromResult(ApiResult<PartyEnvelope>.Success(new PartyEnvelope()));
            public Task<ApiResult<InvitesDto>> PartyInvites() => Task.FromResult(ApiResult<InvitesDto>.Success(new InvitesDto()));
            public Task<ApiResult<bool>> AcceptInvite(long i) => Task.FromResult(ApiResult<bool>.Success(true));
            public Task<ApiResult<bool>> DeclineInvite(long i) => Task.FromResult(ApiResult<bool>.Success(true));
            public Task<ApiResult<bool>> LeaveParty() => Task.FromResult(ApiResult<bool>.Success(true));
            public Task<ApiResult<MeDto>> Me() => Task.FromResult(ApiResult<MeDto>.Success(new MeDto { nickname = "a" }));
            public Task<ApiResult<string>> ServerToken() => Task.FromResult(ApiResult<string>.Success("t"));
            public Task<ApiResult<PartyEnvelope>> SetPartyRace(PartyRaceDto r) => Task.FromResult(ApiResult<PartyEnvelope>.Success(new PartyEnvelope()));
            public Task<ApiResult<PartyEnvelope>> SetMyRace(MemberRaceDto r) => Task.FromResult(ApiResult<PartyEnvelope>.Success(new PartyEnvelope()));
            public Task<ApiResult<PartyEnvelope>> KickFromParty(long i) => Task.FromResult(ApiResult<PartyEnvelope>.Success(new PartyEnvelope()));
        }

        [SetUp] public void Fresh() => MatchTicketClient.ForgetConsumed();

        static ApiResult<TicketDto> Queued(string reason = "", int pos = 0, int fill = 10) => ApiResult<TicketDto>.Success(new TicketDto { id = "t-1", status = "queued", queue = new QueueDto { reason = reason, position = pos, fill_in_s = fill, players = 2 } });
        static ApiResult<TicketDto> Matched() => ApiResult<TicketDto>.Success(new TicketDto { id = "t-1", status = "matched", match = new MatchDto { id = "m-1", server = new ServerDto { host = "100.97.58.29", port = 7770 }, bots = 5 } });

        [Test]
        public void Queued_ThenMatched_GivesServer_AndPollsEveryTwoSeconds()
        {
            var f = new Fake(); var c = new MatchTicketClient(f);
            f.Answers.Enqueue(Queued("servers_busy", 2)); f.Answers.Enqueue(Queued()); f.Answers.Enqueue(Matched());
            c.Start("battle-royale", 0); c.Tick(0.1);
            StringAssert.Contains("2번째", c.Text);
            c.Tick(1); Assert.AreEqual(0, f.Polls, "not yet");
            c.Tick(2.1); c.Tick(2.2); Assert.AreEqual(1, f.Polls);
            c.Tick(4.2); c.Tick(4.3);
            Assert.AreEqual(MatchTicketClient.StateKind.Matched, c.State);
            Assert.AreEqual("100.97.58.29", c.Host); Assert.AreEqual(7770, c.Port); Assert.AreEqual(5, c.Bots);
        }

        [Test]
        public void CreateFails_ShowsReason()
        {
            var f = new Fake(); f.Answers.Enqueue(ApiResult<TicketDto>.Fail(403, "not_party_leader"));
            var c = new MatchTicketClient(f); c.Start("battle-royale", 0); c.Tick(0.1);
            Assert.AreEqual(MatchTicketClient.StateKind.Failed, c.State);
            Assert.AreEqual(TicketRules.ErrorText(403, "not_party_leader"), c.Text);
        }

        [Test]
        public void Cancel_StopsPolling_AndTellsBackend()
        {
            var f = new Fake(); f.Answers.Enqueue(Queued()); var c = new MatchTicketClient(f);
            c.Start("battle-royale", 0); c.Tick(0.1); c.Cancel(1);
            c.Tick(10); Assert.AreEqual("t-1", f.Cancelled); Assert.AreEqual(0, f.Polls); Assert.AreEqual(MatchTicketClient.StateKind.Idle, c.State);
        }

        [Test]
        public void ExpiredTicket_Fails()
        {
            var f = new Fake(); f.Answers.Enqueue(Queued()); f.Answers.Enqueue(ApiResult<TicketDto>.Success(new TicketDto { id = "t-1", status = "expired" }));
            var c = new MatchTicketClient(f); c.Start("battle-royale", 0); c.Tick(0.1); c.Tick(3); c.Tick(3.1);
            Assert.AreEqual(MatchTicketClient.StateKind.Failed, c.State);
        }

        [Test]
        public void WireJson_ParsesLikeTheBackendSends()
        {
            var e = UnityEngine.JsonUtility.FromJson<TicketEnvelope>("{\"ticket\":{\"id\":\"t-9\",\"mode\":\"battle-royale\",\"status\":\"matched\",\"party_size\":2,\"match\":{\"id\":\"m-1\",\"status\":\"active\",\"server\":{\"host\":\"1.2.3.4\",\"port\":7770},\"bots\":5,\"players\":3}}}");
            Assert.AreEqual("1.2.3.4", e.ticket.match.server.host); Assert.AreEqual(7770, e.ticket.match.server.port);
            Assert.AreEqual("", e.ticket.queue?.reason ?? "");
        }

        // r5 playtest: the server never closed the first match at the backend, so the second start got 409 already_queued and the
        // player never got a match. The client now carries on with the ticket it already holds and joins its server.
        [Test]
        public void SecondStart_AlreadyQueued_AdoptsTheHeldTicket()
        {
            var f = new Fake(); f.Answers.Enqueue(Matched());
            var c = new MatchTicketClient(f);
            c.Start("battle-royale", 0); c.Tick(0.1);
            c.Consume();
            f.Answers.Enqueue(ApiResult<TicketDto>.Fail(409, "already_queued")); f.Answers.Enqueue(Matched());
            c.Start("battle-royale", 10); c.Tick(10.1); c.Tick(10.2);
            Assert.AreEqual(MatchTicketClient.StateKind.Matched, c.State, c.Text);
            Assert.AreEqual("100.97.58.29", c.Host);
        }

        [Test]
        public void AlreadyQueued_WithoutATicket_ShowsReason()
        {
            var f = new Fake(); f.Answers.Enqueue(ApiResult<TicketDto>.Fail(409, "already_queued")); f.Answers.Enqueue(ApiResult<TicketDto>.Fail(404, "no_ticket"));
            var c = new MatchTicketClient(f); c.Start("battle-royale", 0); c.Tick(0.1); c.Tick(0.2);
            Assert.AreEqual(MatchTicketClient.StateKind.Failed, c.State);
            Assert.AreEqual(TicketRules.ErrorText(409, "already_queued"), c.Text);
        }

        [Test]
        public void PartyMember_AdoptsLeadersTicket_JoinsOnce()
        {
            var f = new Fake(); f.Answers.Enqueue(Matched());
            var c = new MatchTicketClient(f) { Watching = true };
            c.Tick(0); c.Tick(0.1);
            Assert.AreEqual(MatchTicketClient.StateKind.Matched, c.State);
            c.Consume();
            c.Tick(5); c.Tick(5.1);
            Assert.AreEqual(MatchTicketClient.StateKind.Idle, c.State, "the same ticket is not adopted twice");
        }

        [Test]
        public void BackendQueue_PartyOfTwo_JoinsAssignedServerWithPartySize()
        {
            var f = new Fake(); f.Answers.Enqueue(Queued()); f.Answers.Enqueue(Matched());
            var social = new SocialClient(f);
            string joined = null; int size = 0;
            var q = new Game.Gameplay.BackendMatchQueue(new MatchTicketClient(f), social, (h, p, n) => { joined = h + ":" + p; size = n; });
            q.RequestStart(TeamMode.Squad, 2);
            q.Tick(0); q.Tick(1e6); q.Tick(1e6 + 1); // RequestStart reads the real clock
            Assert.AreEqual("100.97.58.29:7770", joined); Assert.AreEqual(2, size);
            Assert.IsFalse(q.Waiting);
        }

        static ApiResult<TicketDto> RaceMatched() => ApiResult<TicketDto>.Success(new TicketDto { id = "t-r", mode = "race", status = "matched",
            match = new MatchDto { id = "m-r", server = new ServerDto { host = "127.0.0.1", port = 7770 }, bots = 6, players = 2 },
            race = new RaceTicketDto { course = "snake", laps = 1, fill_bots = true, bots = 6,
                racers = new[] { new RaceRacerDto { account_id = 1, car = "GoblinV12", paint = 3 }, new RaceRacerDto { account_id = 2, car = "Shvan92", paint = 1 } } } });

        [Test]
        public void WireJson_RaceSnapshot_Parses()
        {
            var e = UnityEngine.JsonUtility.FromJson<TicketEnvelope>("{\"ticket\":{\"id\":\"t-9\",\"mode\":\"race\",\"status\":\"queued\",\"party_size\":2,\"race\":{\"course\":\"pass_loop\",\"laps\":3,\"fill_bots\":true,\"bots\":4,\"racers\":[{\"account_id\":7,\"car\":\"GoblinV12\",\"paint\":2},{\"account_id\":8,\"car\":\"Shvan92\",\"paint\":5}]}}}");
            Assert.AreEqual("race", e.ticket.mode);
            Assert.AreEqual("pass_loop", e.ticket.race.course); Assert.AreEqual(3, e.ticket.race.laps); Assert.AreEqual(4, e.ticket.race.bots);
            Assert.AreEqual(2, e.ticket.race.racers.Length); Assert.AreEqual(8, e.ticket.race.racers[1].account_id); Assert.AreEqual("Shvan92", e.ticket.race.racers[1].car);
        }

        // #521: a race ticket keeps being polled after it is matched (until the race starts and it is consumed), so a member hears of the host's
        // cancel; a battle-royale ticket is consumed at once and never polled while matched.
        [Test]
        public void RaceTicket_PolledWhileMatched_CancelElsewhereFails()
        {
            var f = new Fake(); f.Answers.Enqueue(RaceMatched());
            var c = new MatchTicketClient(f);
            c.Start("race", 0); c.Tick(0.1);
            Assert.AreEqual(MatchTicketClient.StateKind.Matched, c.State);
            Assert.AreEqual("race", c.Mode); Assert.AreEqual("snake", c.Race.course);
            c.Tick(2.1); c.Tick(2.2);
            Assert.AreEqual(1, f.Polls, "still followed");
            Assert.AreEqual(MatchTicketClient.StateKind.Matched, c.State);
            f.Answers.Enqueue(ApiResult<TicketDto>.Fail(404, "no_ticket"));
            c.Tick(4.2); c.Tick(4.3);
            Assert.AreEqual(MatchTicketClient.StateKind.Failed, c.State);
            Assert.AreEqual(TicketRules.ErrorText(404, "no_ticket"), c.Text);

            var br = new Fake(); br.Answers.Enqueue(Matched());
            var d = new MatchTicketClient(br);
            d.Start("battle-royale", 0); d.Tick(0.1); d.Tick(2.1); d.Tick(4.2);
            Assert.AreEqual(0, br.Polls);
        }

        // #521: the host's Start makes the race ticket; matched -> join the server with the party's size; the room shows 출발 준비 중 until
        // the race starts (RaceStarted). A refusal (not_ready) or a cancel at the backend fails it with a toast and nothing held.
        [Test]
        public void BackendQueue_Race_JoinsOnce_StartingUntilStarted()
        {
            var f = new Fake(); f.Answers.Enqueue(RaceMatched());
            string joined = null; int size = 0, joins = 0;
            var q = new Game.Gameplay.BackendMatchQueue(new MatchTicketClient(f), new SocialClient(f), (h, p, n) => { joined = h + ":" + p; size = n; joins++; }, null, () => 2);
            Assert.IsFalse(q.RaceStarting);
            q.StartRace(0);
            Assert.IsTrue(q.RaceStarting, "출발 준비 중 at once");
            q.Tick(0.1); q.Tick(0.2);
            Assert.AreEqual("127.0.0.1:7770", joined); Assert.AreEqual(2, size); Assert.AreEqual(1, joins);
            Assert.IsTrue(q.RaceStarting);
            q.Tick(2.1); q.Tick(2.2);
            Assert.AreEqual(1, joins, "joined once");
            q.RaceStarted();
            Assert.IsFalse(q.RaceStarting);
            Assert.AreEqual(MatchTicketClient.StateKind.Idle, q.TicketState);
        }

        [Test]
        public void BackendQueue_Race_RefusedOrCancelled_FailsWithAToast()
        {
            var f = new Fake(); f.Answers.Enqueue(ApiResult<TicketDto>.Fail(409, "not_ready"));
            string failed = null;
            var q = new Game.Gameplay.BackendMatchQueue(new MatchTicketClient(f), new SocialClient(f), (h, p, n) => Assert.Fail("no join"), null, () => 2);
            q.RaceFailed += t => failed = t;
            q.StartRace(0); q.Tick(0.1);
            Assert.AreEqual(TicketRules.ErrorText(409, "not_ready"), failed);
            Assert.IsFalse(q.RaceStarting);
            Assert.AreEqual(MatchTicketClient.StateKind.Idle, q.TicketState, "nothing held for the next Start");

            // matched, then the host cancelled at the backend: the member's poll finds no ticket
            f.Answers.Enqueue(RaceMatched()); failed = null;
            int joins = 0;
            q = new Game.Gameplay.BackendMatchQueue(new MatchTicketClient(f), new SocialClient(f), (h, p, n) => joins++, null, () => 2);
            q.RaceFailed += t => failed = t;
            q.StartRace(0); q.Tick(0.1);
            Assert.AreEqual(1, joins);
            f.Answers.Enqueue(ApiResult<TicketDto>.Fail(404, "no_ticket"));
            q.Tick(2.1); q.Tick(2.2);
            Assert.AreEqual(TicketRules.ErrorText(404, "no_ticket"), failed);
            Assert.IsFalse(q.RaceStarting);
        }

        [Test]
        public void BackendQueue_Race_HostCancel_TellsTheBackend_NotAMember()
        {
            var f = new Fake(); f.Answers.Enqueue(RaceMatched());
            var q = new Game.Gameplay.BackendMatchQueue(new MatchTicketClient(f), new SocialClient(f), (h, p, n) => { }, null, () => 1);
            string failed = null; q.RaceFailed += t => failed = t;
            q.StartRace(0); q.Tick(0.1);
            q.CancelRace(1, "출발을 취소했습니다.");
            Assert.AreEqual("t-r", f.Cancelled);
            Assert.AreEqual("출발을 취소했습니다.", failed);
            Assert.IsFalse(q.RaceStarting);

            f.Answers.Enqueue(RaceMatched());
            q = new Game.Gameplay.BackendMatchQueue(new MatchTicketClient(f), new SocialClient(f), (h, p, n) => { }, null, () => 5);
            failed = null; q.RaceFailed += t => failed = t;
            q.StartRace(0); q.Tick(0.1);
            Assert.IsNotNull(failed, "I am not one of the ticket's racers");
            Assert.IsFalse(q.RaceStarting);
        }

        [Test]
        public void BackendQueue_PartyMember_AdoptsTheRaceTicket()
        {
            var f = new Fake(); f.Answers.Enqueue(RaceMatched());
            f.PartyNow = new PartyEnvelope { party = new PartyDto { id = 12, leader_id = 1, members = new[] { new PartyMemberDto { account_id = 1 }, new PartyMemberDto { account_id = 2 } } } };
            var social = new SocialClient(f);
            social.Tick(0); social.Tick(0.01);
            Assert.IsNotNull(social.Party);
            int joins = 0;
            var q = new Game.Gameplay.BackendMatchQueue(new MatchTicketClient(f), social, (h, p, n) => joins++, null, () => 2);
            q.Tick(0); q.Tick(0.1);
            Assert.AreEqual(1, joins);
            Assert.IsTrue(q.RaceStarting);
        }
    }
}
