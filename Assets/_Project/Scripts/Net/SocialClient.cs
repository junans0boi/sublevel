using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Game.Core;

namespace Game.Net
{
    // Friends, presence and party invites of the main menu (#391). Polls the backend on the cadence docs/social-api.md asks for
    // (presence 20s, party and invites 2.5s, friends 5s) and keeps the latest snapshots. Call Tick from the main thread; answers are applied there.
    public sealed class SocialClient
    {
        public const double PresenceEvery = 20, PartyEvery = 2.5, FriendsEvery = 5;

        readonly IBackend _b;
        readonly ConcurrentQueue<Action> _done = new ConcurrentQueue<Action>();
        double _nextPresence, _nextParty, _nextFriends;
        int _busy; // bit per poll in flight: 1 party, 2 invites, 4 friends

        public FriendsDto Friends { get; private set; } = new FriendsDto();
        public PartyDto Party { get; private set; }          // null = no party
        public InvitesDto Invites { get; private set; } = new InvitesDto();
        public string Status = "lobby";                       // "lobby" | "in_match", sent as presence
        public string LastError { get; private set; } = "";
        public event Action Changed;

        public SocialClient(IBackend backend) { _b = backend; }

        public bool IsLeader(long myAccountId) => Party == null || Party.leader_id == myAccountId;
        public int PartySize => Party == null ? 1 : Math.Max(1, Party.members.Length);

        public void Tick(double now)
        {
            while (_done.TryDequeue(out var a)) a();
            if (now >= _nextPresence) { _nextPresence = now + PresenceEvery; _b.Presence(Status); }
            if (now >= _nextParty)
            {
                _nextParty = now + PartyEvery;
                Poll(1, _b.Party(), r => { Party = r.Value != null && r.Value.HasParty ? r.Value.party : null; });
                Poll(2, _b.PartyInvites(), r => Invites = r.Value ?? new InvitesDto());
            }
            if (now >= _nextFriends) { _nextFriends = now + FriendsEvery; Poll(4, _b.Friends(), r => Friends = r.Value ?? new FriendsDto()); }
        }

        void Poll<T>(int bit, Task<ApiResult<T>> call, Action<ApiResult<T>> apply)
        {
            if ((_busy & bit) != 0) return;
            _busy |= bit;
            call.ContinueWith(t => _done.Enqueue(() =>
            {
                _busy &= ~bit;
                if (t.IsFaulted || !t.Result.Ok) { if (!t.IsFaulted) LastError = TicketRules.ErrorText(t.Result.Status, t.Result.Error); return; }
                apply(t.Result); Changed?.Invoke();
            }), TaskContinuationOptions.ExecuteSynchronously);
        }

        // Actions: run the call, show the backend's reason when it fails, and refresh everything at the next Tick.
        void Act(Task<ApiResult<bool>> call)
        {
            call.ContinueWith(t => _done.Enqueue(() =>
            {
                LastError = t.IsFaulted ? TicketRules.ErrorText(0, "") : t.Result.Ok ? "" : SocialRules.ErrorText(t.Result.Status, t.Result.Error);
                _nextParty = _nextFriends = 0;
                Changed?.Invoke();
            }), TaskContinuationOptions.ExecuteSynchronously);
        }

        public void AddFriend(string nickname) => Act(_b.AddFriend(nickname));
        public void AcceptFriend(long id) => Act(_b.AcceptFriend(id));
        public void RemoveFriend(long id) => Act(_b.RemoveFriend(id));
        public void Invite(long id) => Act(_b.InviteToParty(id));
        public void AcceptInvite(long partyId) => Act(_b.AcceptInvite(partyId));
        public void DeclineInvite(long partyId) => Act(_b.DeclineInvite(partyId));
        public void LeaveParty() => Act(_b.LeaveParty());
    }
}
