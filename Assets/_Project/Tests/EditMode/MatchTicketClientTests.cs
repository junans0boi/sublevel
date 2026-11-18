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
            public Task<ApiResult<PartyEnvelope>> Party() => Task.FromResult(ApiResult<PartyEnvelope>.Success(new PartyEnvelope()));
            public Task<ApiResult<bool>> InviteToParty(long i) => Task.FromResult(ApiResult<bool>.Success(true));
            public Task<ApiResult<InvitesDto>> PartyInvites() => Task.FromResult(ApiResult<InvitesDto>.Success(new InvitesDto()));
            public Task<ApiResult<bool>> AcceptInvite(long i) => Task.FromResult(ApiResult<bool>.Success(true));
            public Task<ApiResult<bool>> DeclineInvite(long i) => Task.FromResult(ApiResult<bool>.Success(true));
            public Task<ApiResult<bool>> LeaveParty() => Task.FromResult(ApiResult<bool>.Success(true));
            public Task<ApiResult<MeDto>> Me() => Task.FromResult(ApiResult<MeDto>.Success(new MeDto { nickname = "a" }));
            public Task<ApiResult<string>> ServerToken() => Task.FromResult(ApiResult<string>.Success("t"));
        }

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
    }
}
