using System;
using System.Threading.Tasks;

namespace Game.Net
{
    // The backend's JSON shapes (sublevel-backend docs/match-api.md). Field names are the wire names (JsonUtility).
    [Serializable] public class ServerDto { public string host; public int port; }
    [Serializable] public class MatchDto { public string id; public string status; public ServerDto server; public int bots; public int players; }
    [Serializable] public class QueueDto { public int players, tickets, oldest_wait_s, active_matches, max_matches, position, fill_in_s; public string reason; }
    [Serializable] public class TicketDto { public string id, mode, status; public int party_size; public QueueDto queue; public MatchDto match; }
    [Serializable] public class FriendDto { public long account_id; public string nickname; public string status; }
    [Serializable] public class FriendsDto { public FriendDto[] friends = new FriendDto[0], incoming = new FriendDto[0], outgoing = new FriendDto[0]; }
    [Serializable] public class PartyMemberDto { public long account_id; public string nickname; public string status; }
    [Serializable] public class PartyDto { public long id, leader_id; public int max_size; public PartyMemberDto[] members = new PartyMemberDto[0]; public FriendDto[] invited = new FriendDto[0]; }
    [Serializable] public class PartyEnvelope { public PartyDto party; public bool HasParty => party != null && party.id != 0; }
    [Serializable] public class InviteFromDto { public long account_id; public string nickname; }
    [Serializable] public class InviteDto { public long party_id; public InviteFromDto from; }
    [Serializable] public class InvitesDto { public InviteDto[] invites = new InviteDto[0]; }
    [Serializable] public class MeDto { public long id; public string nickname; }
    [Serializable] public class TokenEnvelope { public string token; }
    [Serializable] public class TicketEnvelope { public TicketDto ticket; }

    public sealed class ApiResult<T>
    {
        public bool Ok; public int Status; public string Error; public T Value;
        public static ApiResult<T> Success(T v, int status = 200) => new ApiResult<T> { Ok = true, Status = status, Value = v };
        public static ApiResult<T> Fail(int status, string error) => new ApiResult<T> { Status = status, Error = error ?? "" };
    }

    // The thin seam to the backend (#389~#392): HttpBackend is the real one, tests use a fake. Every call may finish on any thread.
    public interface IBackend
    {
        Task<ApiResult<TicketDto>> CreateTicket(string mode);
        Task<ApiResult<TicketDto>> CurrentTicket();
        Task<ApiResult<bool>> CancelTicket(string id);
        Task<ApiResult<bool>> Presence(string status);
        Task<ApiResult<MeDto>> Me();
        // Friends and party (#391, docs/social-api.md). Calls that change something return the backend's error code on failure.
        Task<ApiResult<FriendsDto>> Friends();
        Task<ApiResult<bool>> AddFriend(string nickname);
        Task<ApiResult<bool>> AcceptFriend(long accountId);
        Task<ApiResult<bool>> RemoveFriend(long accountId);
        Task<ApiResult<PartyEnvelope>> Party();
        Task<ApiResult<bool>> InviteToParty(long accountId);
        Task<ApiResult<InvitesDto>> PartyInvites();
        Task<ApiResult<bool>> AcceptInvite(long partyId);
        Task<ApiResult<bool>> DeclineInvite(long partyId);
        Task<ApiResult<bool>> LeaveParty();
        // A fresh 5-minute signed token for joining a game server (POST /api/auth/token).
        Task<ApiResult<string>> ServerToken();
    }
}
