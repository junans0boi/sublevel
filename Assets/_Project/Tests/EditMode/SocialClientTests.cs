using System.Threading.Tasks;
using Game.Core;
using Game.Net;
using NUnit.Framework;

namespace Game.Tests
{
    public class SocialClientTests
    {
        sealed class Fake : IBackend
        {
            public int PartyCalls, FriendCalls, PresenceCalls; public string Added; public ApiResult<bool> AddResult = ApiResult<bool>.Success(true);
            public PartyEnvelope PartyNow = new PartyEnvelope();
            static Task<ApiResult<T>> Ok<T>(T v) => Task.FromResult(ApiResult<T>.Success(v));
            public Task<ApiResult<TicketDto>> CreateTicket(string m) => Ok<TicketDto>(null);
            public Task<ApiResult<TicketDto>> CurrentTicket() => Ok<TicketDto>(null);
            public Task<ApiResult<bool>> CancelTicket(string id) => Ok(true);
            public Task<ApiResult<bool>> Presence(string s) { PresenceCalls++; return Ok(true); }
            public Task<ApiResult<FriendsDto>> Friends() { FriendCalls++; return Ok(new FriendsDto { friends = new[] { new FriendDto { account_id = 9, nickname = "Bobby", status = "lobby" } } }); }
            public Task<ApiResult<bool>> AddFriend(string n) { Added = n; return Task.FromResult(AddResult); }
            public Task<ApiResult<bool>> AcceptFriend(long i) => Ok(true);
            public Task<ApiResult<bool>> RemoveFriend(long i) => Ok(true);
            public Task<ApiResult<PartyEnvelope>> Party() { PartyCalls++; return Ok(PartyNow); }
            public Task<ApiResult<bool>> InviteToParty(long i) => Ok(true);
            public Task<ApiResult<InvitesDto>> PartyInvites() => Ok(new InvitesDto { invites = new[] { new InviteDto { party_id = 3, from = new InviteFromDto { nickname = "Alice" } } } });
            public Task<ApiResult<bool>> AcceptInvite(long i) => Ok(true);
            public Task<ApiResult<bool>> DeclineInvite(long i) => Ok(true);
            public Task<ApiResult<bool>> LeaveParty() => Ok(true);
            public Task<ApiResult<MeDto>> Me() => Ok(new MeDto());
            public Task<ApiResult<string>> ServerToken() => Ok("t");
            public Task<ApiResult<PartyEnvelope>> SetPartyRace(PartyRaceDto r) => Ok(new PartyEnvelope());
            public Task<ApiResult<PartyEnvelope>> SetMyRace(MemberRaceDto r) => Ok(new PartyEnvelope());
            public Task<ApiResult<PartyEnvelope>> KickFromParty(long i) => Ok(new PartyEnvelope());
        }

        [Test]
        public void Tick_PollsOnCadence_AndExposesSnapshots()
        {
            var f = new Fake(); var c = new SocialClient(f);
            c.Tick(0); c.Tick(0.1);
            Assert.AreEqual(1, f.PartyCalls); Assert.AreEqual(1, f.FriendCalls); Assert.AreEqual(1, f.PresenceCalls);
            Assert.AreEqual("Bobby", c.Friends.friends[0].nickname);
            Assert.AreEqual("Alice", c.Invites.invites[0].from.nickname);
            Assert.IsNull(c.Party); Assert.AreEqual(1, c.PartySize);
            c.Tick(1); c.Tick(1.1); Assert.AreEqual(1, f.PartyCalls, "party is polled every 2.5s");
            c.Tick(2.6); c.Tick(2.7); Assert.AreEqual(2, f.PartyCalls); Assert.AreEqual(1, f.FriendCalls);
            c.Tick(5.1); c.Tick(5.2); Assert.AreEqual(2, f.FriendCalls);
            c.Tick(20.1); Assert.AreEqual(2, f.PresenceCalls);
        }

        [Test]
        public void Party_ShowsLeaderAndSize()
        {
            var f = new Fake(); f.PartyNow = new PartyEnvelope { party = new PartyDto { id = 12, leader_id = 7, members = new[] { new PartyMemberDto { account_id = 7 }, new PartyMemberDto { account_id = 9 } } } };
            var c = new SocialClient(f); c.Tick(0); c.Tick(0.1);
            Assert.AreEqual(2, c.PartySize); Assert.IsTrue(c.IsLeader(7)); Assert.IsFalse(c.IsLeader(9));
        }

        [Test]
        public void FailedAction_ShowsBackendReason_AndRefreshesAtOnce()
        {
            var f = new Fake(); f.AddResult = ApiResult<bool>.Fail(404, "player_not_found");
            var c = new SocialClient(f); c.Tick(0); c.Tick(0.1);
            int before = f.FriendCalls;
            c.AddFriend("nobody"); c.Tick(0.2); c.Tick(0.3);
            Assert.AreEqual("nobody", f.Added);
            Assert.AreEqual(SocialRules.ErrorText(404, "player_not_found"), c.LastError);
            Assert.AreEqual(before + 1, f.FriendCalls);
        }
    }
}
