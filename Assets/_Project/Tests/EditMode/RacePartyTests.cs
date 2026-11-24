using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Core;
using Game.Net;
using NUnit.Framework;

namespace Game.Tests
{
    // #519: the race room fed from the backend party (spec #505, ADR-562): party view -> RaceLobby, commands through the backend with an
    // optimistic update and a revert + toast on refusal, system lines from poll diffs, and the local path for a party of 1 / offline.
    public class RacePartyTests
    {
        // Answers party polls with PartyNow; race commands wait on a TaskCompletionSource the test settles.
        sealed class Fake : IBackend
        {
            public PartyEnvelope PartyNow = new PartyEnvelope();
            public TaskCompletionSource<ApiResult<PartyEnvelope>> Pending;
            public TaskCompletionSource<ApiResult<PartyEnvelope>> PartyPoll;   // set: the next poll waits on it
            public readonly List<string> Sent = new List<string>();
            public int Leaves;
            static Task<ApiResult<T>> Ok<T>(T v) => Task.FromResult(ApiResult<T>.Success(v));
            Task<ApiResult<PartyEnvelope>> Command(string what) { Sent.Add(what); Pending = new TaskCompletionSource<ApiResult<PartyEnvelope>>(); return Pending.Task; }
            public Task<ApiResult<PartyEnvelope>> SetPartyRace(PartyRaceDto r) => Command($"race {r.name}|{r.course}|{r.laps}|{r.@private}|{r.fill_bots}|{r.closed}");
            public Task<ApiResult<PartyEnvelope>> SetMyRace(MemberRaceDto r) => Command($"me {r.car}|{r.paint}|{r.ready}");
            public Task<ApiResult<PartyEnvelope>> KickFromParty(long id) => Command("kick " + id);
            public Task<ApiResult<PartyEnvelope>> Party()
            {
                if (PartyPoll == null) return Ok(PartyNow);
                var t = PartyPoll.Task; PartyPoll = null; return t;
            }
            public Task<ApiResult<TicketDto>> CreateTicket(string m) => Ok<TicketDto>(null);
            public Task<ApiResult<TicketDto>> CurrentTicket() => Ok<TicketDto>(null);
            public Task<ApiResult<bool>> CancelTicket(string id) => Ok(true);
            public Task<ApiResult<bool>> Presence(string s) => Ok(true);
            public Task<ApiResult<FriendsDto>> Friends() => Ok(new FriendsDto());
            public Task<ApiResult<bool>> AddFriend(string n) => Ok(true);
            public Task<ApiResult<bool>> AcceptFriend(long i) => Ok(true);
            public Task<ApiResult<bool>> RemoveFriend(long i) => Ok(true);
            public Task<ApiResult<PartyEnvelope>> InviteToParty(long i) => Command("invite " + i);
            public Task<ApiResult<InvitesDto>> PartyInvites() => Ok(new InvitesDto());
            public Task<ApiResult<bool>> AcceptInvite(long i) => Ok(true);
            public Task<ApiResult<bool>> DeclineInvite(long i) => Ok(true);
            public Task<ApiResult<bool>> LeaveParty() { Leaves++; return Ok(true); }
            public Task<ApiResult<MeDto>> Me() => Ok(new MeDto());
            public Task<ApiResult<string>> ServerToken() => Ok("t");
        }

        static PartyMemberDto Member(long id, string nick, string car = "LT80", int paint = 0, bool ready = false) =>
            new PartyMemberDto { account_id = id, nickname = nick, status = "lobby", race = new MemberRaceDto { car = car, paint = paint, ready = ready } };

        static PartyEnvelope View(long leader, params PartyMemberDto[] members) => new PartyEnvelope
        {
            party = new PartyDto
            {
                id = 12, leader_id = leader, max_size = 8, members = members,
                race = new PartyRaceDto { name = "Alice의 레이스", course = "snake", laps = 2, @private = false, fill_bots = true, closed = 0 },
            },
        };

        static PartyEnvelope Copy(PartyEnvelope e) => UnityEngine.JsonUtility.FromJson<PartyEnvelope>(UnityEngine.JsonUtility.ToJson(e));

        Fake _f;
        SocialClient _social;
        RaceLobby _room;
        RaceParty _party;
        double _now;

        // Me = Bobby (9), a guest of Alice (7), unless a test says otherwise.
        void Setup(int me = 9, PartyEnvelope view = null)
        {
            _f = new Fake { PartyNow = view ?? View(7, Member(7, "Alice", "GoblinV12", 2), Member(9, "Bobby")) };
            _social = new SocialClient(_f);
            _room = new RaceLobby(new RaceLobbyMember { Id = me, Name = me == 9 ? "Bobby" : "Alice", Car = "FairheavenLT80" });
            _party = new RaceParty(() => _social, _room, me);
            _now = 0;
        }

        // One frame: the client answers what came back, then the room reads the party.
        bool Frame() { _social.Tick(_now); bool c = _party.Sync(); _now += 0.01; return c; }
        void Poll() { _now += SocialClient.PartyEvery + 0.01; Frame(); Frame(); }
        void Settle(ApiResult<PartyEnvelope> r) { _f.Pending.SetResult(r); Frame(); }

        // #521: my place in the race ticket's snapshot -> the lobby request (course 1-based, laps, bots, my car as a model id, my paint, party size).
        [Test]
        public void QueueRequest_FromTheTicketSnapshot_TheRoomsCourseAndMyCar()
        {
            var race = new RaceTicketDto { course = "pass_loop", laps = 3, fill_bots = true, bots = 4,
                racers = new[] { new RaceRacerDto { account_id = 7, car = "GoblinV12", paint = 2 }, new RaceRacerDto { account_id = 9, car = "Shvan92", paint = 5 } } };
            var r = RaceParty.QueueRequest(race, 9).Value;
            Assert.AreEqual(System.Array.FindIndex(RaceEvent.Courses, c => c.Id == "pass_loop") + 1, r.evt);
            Assert.AreEqual(3, r.laps); Assert.AreEqual(4, r.bots); Assert.AreEqual(2, r.people);
            Assert.AreEqual("Shvan92", r.car); Assert.AreEqual(5, r.paint);
            Assert.AreEqual("GoblinV12", RaceParty.QueueRequest(race, 7).Value.car);
            race.racers[0].car = "LT80";
            Assert.AreEqual("FairheavenLT80", RaceParty.QueueRequest(race, 7).Value.car, "wire id -> the garage's model id");
            Assert.IsNull(RaceParty.QueueRequest(race, 8), "not a racer of this ticket");
            Assert.IsNull(RaceParty.QueueRequest(new RaceTicketDto(), 9), "no race in the ticket (JsonUtility's empty object)");
            race.racers[1].paint = 9;
            Assert.IsNull(RaceParty.QueueRequest(race, 9), "paint out of range");
        }

        // #521: back from the race the room is everyone unready; my ready on the backend is cleared once the room is online again.
        [Test]
        public void BackFromTheRace_ClearsMyReadyOnTheBackend()
        {
            Setup(9, View(7, Member(7, "Alice"), Member(9, "Bobby", ready: true)));
            _room.BackToRoom();
            _party.UnreadyWhenOnline();
            Frame(); Frame();
            Assert.IsTrue(_party.Online);
            Assert.IsFalse(_room.Member(9).Ready, "at once");
            CollectionAssert.AreEqual(new[] { "me LT80|0|False" }, _f.Sent);
            Settle(ApiResult<PartyEnvelope>.Success(View(7, Member(7, "Alice"), Member(9, "Bobby"))));
            Poll();
            Assert.AreEqual(1, _f.Sent.Count, "once");

            // the host has no ready: nothing to send
            Setup(7, View(7, Member(7, "Alice"), Member(9, "Bobby", ready: true)));
            _party.UnreadyWhenOnline();
            Frame(); Frame();
            Assert.AreEqual(0, _f.Sent.Count);
        }

        [Test]
        public void PartyOfTwo_FeedsTheRoom_WithModelCarsAndTheLeaderAsHost()
        {
            Setup();
            Frame(); Frame();
            Assert.IsTrue(_party.Online);
            Assert.AreEqual(7, _room.HostId);
            Assert.AreEqual(new[] { 7, 9 }, _room.Members.Select(m => m.Id).ToArray());
            Assert.AreEqual("GoblinV12", _room.Member(7).Car);
            Assert.AreEqual("FairheavenLT80", _room.Member(9).Car, "wire LT80 -> the garage's model id");
            Assert.AreEqual(2, _room.Member(7).Paint);
            Assert.AreEqual("Alice의 레이스", _room.Name);
            Assert.AreEqual(6, _room.BotCount);
        }

        [Test]
        public void PartyOfOne_OrNone_KeepsTheLocalRoom()
        {
            Setup(9, View(9, Member(9, "Bobby", "Shvan92")));
            _room.SetCourse(9, "pass_loop", 3);
            Frame(); Frame();
            Assert.IsFalse(_party.Online);
            Assert.AreEqual("pass_loop", _room.Course);
            Assert.AreEqual("FairheavenLT80", _room.Member(9).Car, "a party of 1 does not touch the local room");
            Assert.IsTrue(_party.SetCar("Shvan92", 4));
            Assert.AreEqual(0, _f.Sent.Count, "local commands never reach the backend");

            Setup(9, new PartyEnvelope());
            Frame(); Frame();
            Assert.IsFalse(_party.Online);
            Assert.IsTrue(_party.ToggleReady() == false, "solo: I am the host, nothing to ready");
            Assert.AreEqual(0, _f.Sent.Count);
        }

        [Test]
        public void OldBackend_WithoutRaceState_StaysLocal()
        {
            var v = View(7, Member(7, "Alice"), Member(9, "Bobby"));
            v.party.race = new PartyRaceDto();   // JsonUtility gives an empty object for a missing "race"
            Setup(9, v);
            Frame(); Frame();
            Assert.IsFalse(_party.Online);
        }

        [Test]
        public void GuestReady_IsOptimistic_AndSendsMyRace()
        {
            Setup(); Frame(); Frame();
            Assert.IsTrue(_party.ToggleReady());
            Assert.IsTrue(_room.Member(9).Ready, "shown at once");
            Assert.AreEqual("me LT80|0|True", _f.Sent.Single());
            var after = View(7, Member(7, "Alice", "GoblinV12", 2), Member(9, "Bobby", ready: true));
            Settle(ApiResult<PartyEnvelope>.Success(after));
            Assert.IsTrue(_room.Member(9).Ready);
            Assert.IsNull(_party.TakeToast());
        }

        [Test]
        public void Refusal_RevertsTheRoomAndItsLines_WithAToast()
        {
            Setup(); Frame(); Frame();
            int lines = _room.Lines.Count;
            Assert.IsTrue(_party.SetCar("Shvan92", 3));
            Assert.AreEqual("Shvan92", _room.Member(9).Car);
            Assert.AreEqual("me Shvan92|3|False", _f.Sent.Single());
            Settle(ApiResult<PartyEnvelope>.Fail(409, "ready_locked"));
            Assert.AreEqual("FairheavenLT80", _room.Member(9).Car, "reverted");
            Assert.AreEqual(lines, _room.Lines.Count, "the car line of the refused change is taken back");
            Assert.AreEqual(SocialRules.ErrorText(409, "ready_locked"), _party.TakeToast());
            Assert.IsNull(_party.TakeToast(), "one toast per refusal");
        }

        [Test]
        public void HostCommands_SendTheWholeRaceState_KickSendsTheAccount()
        {
            Setup(7); Frame(); Frame();
            Assert.IsTrue(_party.SetCourse("pass_up", 3));
            Assert.AreEqual(1, _room.Laps, "a sprint is one run");
            Assert.AreEqual("race Alice의 레이스|pass_up|1|False|True|0", _f.Sent.Last());
            Settle(ApiResult<PartyEnvelope>.Success(Copy(_f.PartyNow)));   // the answer's view (unchanged here) replaces the room
            Assert.IsTrue(_party.SetSettings("밤 레이스", true, false));
            Assert.AreEqual("race 밤 레이스|snake|2|True|False|0", _f.Sent.Last());
            Settle(ApiResult<PartyEnvelope>.Success(Copy(_f.PartyNow)));
            Assert.IsTrue(_party.CloseSlot(7));
            Assert.AreEqual("race Alice의 레이스|snake|2|False|True|128", _f.Sent.Last());
            Settle(ApiResult<PartyEnvelope>.Success(Copy(_f.PartyNow)));
            Assert.IsTrue(_party.Kick(9));
            Assert.AreEqual("kick 9", _f.Sent.Last());
            Assert.IsNull(_room.Member(9));
            Assert.IsFalse(_party.Kick(7), "not myself");
            Assert.IsFalse(_party.SetCourse("island", 1), "the room's own rules still refuse first");
        }

        [Test]
        public void GuestHostCommands_AreRefusedLocally_NothingSent()
        {
            Setup(); Frame(); Frame();
            Assert.IsFalse(_party.SetCourse("pass_loop", 3));
            Assert.IsFalse(_party.Kick(7));
            Assert.IsFalse(_party.CloseSlot(5));
            Assert.AreEqual(0, _f.Sent.Count);
        }

        [Test]
        public void Polls_WhileACommandIsInFlight_DoNotUndoTheOptimisticChange()
        {
            Setup(); Frame(); Frame();
            var poll = _f.PartyPoll = new TaskCompletionSource<ApiResult<PartyEnvelope>>();   // a poll goes out ...
            _now += SocialClient.PartyEvery + 0.01; Frame();
            Assert.IsTrue(_party.ToggleReady());                                   // ... then I ready ...
            poll.SetResult(ApiResult<PartyEnvelope>.Success(Copy(_f.PartyNow)));   // ... and the poll answers with the old view
            Frame();
            Assert.IsTrue(_room.Member(9).Ready, "the stale poll is dropped");
        }

        [Test]
        public void PollDiffs_BecomeSystemLines()
        {
            Setup(); Frame(); Frame();
            _room.Lines.Clear();
            _f.PartyNow = View(7, Member(7, "Alice", "GoblinV12", 2), Member(9, "Bobby"), Member(11, "Carol", "SW84"));
            Poll();
            Assert.Contains("Carol 님이 들어왔습니다.", _room.Lines);
            var v = View(7, Member(7, "Alice", "Lifted85", 2), Member(9, "Bobby"), Member(11, "Carol", "SW84"));
            v.party.race.course = "pass_loop"; v.party.race.laps = 3;
            _f.PartyNow = v;
            Poll();
            Assert.IsTrue(_room.Lines.Any(l => l.StartsWith("Alice 님이 ")), "car pick");
            Assert.IsTrue(_room.Lines.Any(l => l.StartsWith("코스가 ")), "course change");
            _f.PartyNow = View(7, Member(7, "Alice", "Lifted85", 2), Member(9, "Bobby"));
            Poll();
            Assert.Contains("Carol 님이 나갔습니다.", _room.Lines);
        }

        [Test]
        public void HostLeaves_GuestBecomesHost()
        {
            Setup(); Frame(); Frame();
            _f.PartyNow = View(9, Member(9, "Bobby"), Member(11, "Carol"));
            Poll();
            Assert.IsTrue(_party.Online);
            Assert.AreEqual(9, _room.HostId);
            Assert.Contains("Bobby 님이 방장이 되었습니다.", _room.Lines);
            // the last guest leaves too: a party of 1 is the local room again, me as host
            _f.PartyNow = View(9, Member(9, "Bobby"));
            Poll();
            Assert.IsFalse(_party.Online);
            Assert.AreEqual(9, _room.HostId);
            Assert.AreEqual(1, _room.Members.Count);
            Assert.Contains("Carol 님이 나갔습니다.", _room.Lines);
        }

        [Test]
        public void Kicked_FallsBackToTheLocalRoom_WithAToast()
        {
            Setup(); Frame(); Frame();
            _f.PartyNow = new PartyEnvelope();
            Poll();
            Assert.IsFalse(_party.Online);
            Assert.AreEqual(9, _room.HostId);
            Assert.AreEqual(new[] { 9 }, _room.Members.Select(m => m.Id).ToArray());
            Assert.AreEqual("방에서 내보내졌습니다.", _room.Lines.Last());
            Assert.AreEqual("방에서 내보내졌습니다.", _party.TakeToast());
        }

        [Test]
        public void Leave_LeavesTheParty_WithoutCallingItAKick()
        {
            Setup(); Frame(); Frame();
            _party.Leave();
            Assert.AreEqual(1, _f.Leaves);
            Assert.IsFalse(_party.Online);
            _f.PartyNow = new PartyEnvelope();
            Poll();
            Assert.IsNull(_party.TakeToast());
            Assert.IsFalse(_room.Lines.Contains("방에서 내보내졌습니다."));
        }

        // ---- #520 the invite dialog ----

        [Test]
        public void InviteButton_FollowsTheMockup()
        {
            Assert.AreEqual(("초대", true), SocialRules.InviteButton("lobby", false, false, false));
            Assert.AreEqual(("게임 중", false), SocialRules.InviteButton("in_match", false, false, false));
            Assert.AreEqual(("오프라인", false), SocialRules.InviteButton("offline", false, false, false));
            Assert.AreEqual(("오프라인", false), SocialRules.InviteButton(null, false, false, false));
            Assert.AreEqual(("방에 있음", false), SocialRules.InviteButton("lobby", true, false, false));
            Assert.AreEqual(("초대 보냄", false), SocialRules.InviteButton("lobby", false, true, false));
            Assert.AreEqual(("초대", false), SocialRules.InviteButton("lobby", false, false, true), "private room, guest: disabled");
            Assert.AreEqual("온라인", SocialRules.InviteStatus("lobby"));
            Assert.AreEqual("배틀로얄 중", SocialRules.InviteStatus("in_match"));
            Assert.AreEqual("오프라인", SocialRules.InviteStatus("offline"));
        }

        [Test]
        public void FirstInvite_MakesTheParty_AndSendsMyLocalRoomWithIt()
        {
            Setup(9, new PartyEnvelope());
            Frame(); Frame();
            _room.SetCourse(9, "pass_loop", 3);
            _room.CloseSlot(9, 6);
            Assert.IsNull(_party.Invite(11, "Carol"));
            Assert.AreEqual("invite 11", _f.Sent.Single());
            Assert.IsTrue(_party.Invited(11), "초대 보냄 while the call is out");
            var made = View(9, Member(9, "Bobby"));
            made.party.race.name = "Bobby의 레이스";
            made.party.invited = new[] { new FriendDto { account_id = 11, nickname = "Carol" } };
            Settle(ApiResult<PartyEnvelope>.Success(made));
            Assert.IsFalse(_party.Online, "a party of 1 is still my local room");
            Assert.IsTrue(_party.Invited(11), "the party's invited list");
            Assert.AreEqual("pass_loop", _room.Course);
            Assert.Contains("Carol 님에게 초대를 보냈습니다.", _room.Lines);
            Assert.AreEqual(new[] { "invite 11", "race Bobby의 레이스|pass_loop|3|False|True|64", "me LT80|0|False" }, _f.Sent.ToArray());
        }

        [Test]
        public void WaitingOnInvites_LocalChangesGoToTheParty()
        {
            var v = View(9, Member(9, "Bobby"));
            v.party.invited = new[] { new FriendDto { account_id = 11, nickname = "Carol" } };
            Setup(9, v);
            Frame(); Frame();
            Assert.IsFalse(_party.Online);
            Assert.IsTrue(_party.SetCourse("pass_loop", 2));
            Assert.AreEqual("race Bobby의 레이스|pass_loop|2|False|True|0", _f.Sent.Single(), "the friend who accepts joins the room as I set it");
        }

        [Test]
        public void Accepted_TheFriendSitsInAFreeSlotUnready()
        {
            var v = View(9, Member(9, "Bobby"));
            v.party.invited = new[] { new FriendDto { account_id = 11, nickname = "Carol" } };
            Setup(9, v);
            _room.CloseSlot(9, 1);
            Frame(); Frame();
            var joined = View(9, Member(9, "Bobby"), Member(11, "Carol"));
            joined.party.race.closed = 2;
            _f.PartyNow = joined;
            Poll();
            Assert.IsTrue(_party.Online);
            Assert.IsFalse(_party.Invited(11));
            Assert.AreEqual(11, _room.SeatAt(2).Id, "slot 1 is closed: the next free one");
            Assert.IsFalse(_room.Member(11).Ready);
        }

        [Test]
        public void Invite_FullRoom_PrivateGuest_NoLogin_AreRefusedWithoutACall()
        {
            Setup(); Frame(); Frame();
            for (int i = 2; i < RaceLobby.Slots; i++) Assert.IsTrue(_room.Join(new RaceLobbyMember { Id = 100 + i, Name = "P" + i }));
            Assert.AreEqual("빈 자리가 없습니다.", _party.Invite(11, "Carol"));
            Setup(); Frame(); Frame();
            var s = _room.Snapshot(); s.Private = true; _room.Load(s);
            Assert.AreEqual("비공개 방은 방장만 초대할 수 있습니다.", _party.Invite(11, "Carol"));
            Assert.AreEqual(0, _f.Sent.Count);
            var solo = new RaceParty(() => null, _room, 9);
            Assert.IsNotNull(solo.Invite(11, "Carol"), "no login, no friends to invite");
        }

        [Test]
        public void InviteRefused_Toasts_AndIsNotMarkedSent()
        {
            Setup(); Frame(); Frame();
            int lines = _room.Lines.Count;
            Assert.IsNull(_party.Invite(11, "Carol"));
            Settle(ApiResult<PartyEnvelope>.Fail(409, "party_full"));
            Assert.AreEqual("빈 자리가 없습니다.", _party.TakeToast());
            Assert.IsFalse(_party.Invited(11));
            Assert.AreEqual(lines, _room.Lines.Count);
            Assert.AreEqual(1, _f.Sent.Count, "a guest's invite sends nothing else");
        }

        [Test]
        public void CarIds_RoundTripTheWire()
        {
            foreach (var w in new[] { "GoblinV12", "LT80", "SW84", "Lifted85", "Shvan92" })
                Assert.AreEqual(w, RaceParty.CarToWire(RaceParty.CarFromWire(w)));
            Assert.AreEqual("FairheavenSW84", RaceParty.CarFromWire("SW84"));
            Assert.AreEqual("LT80", RaceParty.CarToWire("nope"), "unknown -> the default car");
        }
    }
}
