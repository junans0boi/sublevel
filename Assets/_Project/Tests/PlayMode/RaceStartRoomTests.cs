using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    // #521 (spec #505, ADR-562 (4)/(7)): the race room's multiplayer start on the real menu with a fake backend. The host's 시작 with a party
    // of 2 makes the race ticket and the room shows 출발 준비 중 (the host's button cancels); a refusal or a cancel elsewhere is a toast and
    // the room as it was. RACE_SHOTS set: review shots of the starting room (host and guest).
    public class RaceStartRoomTests
    {
        sealed class Backend : IBackend
        {
            public long Leader = 1;
            public bool GuestReady = true;
            public string Created; public string Cancelled;
            public ApiResult<TicketDto> CreateAnswer, CurrentAnswer = ApiResult<TicketDto>.Fail(404, "no_ticket");
            static Task<ApiResult<T>> Ok<T>(T v) => Task.FromResult(ApiResult<T>.Success(v));
            public static ApiResult<TicketDto> RaceQueued() => ApiResult<TicketDto>.Success(new TicketDto { id = "t-r", mode = "race", status = "queued", party_size = 2,
                queue = new QueueDto { reason = TicketRules.ServersBusy, position = 1 }, race = new RaceTicketDto { course = "snake", laps = 2, fill_bots = true, bots = 6 } });
            PartyMemberDto M(long id, string nick, bool ready) => new PartyMemberDto { account_id = id, nickname = nick, status = "lobby", race = new MemberRaceDto { car = "GoblinV12", paint = 2, ready = ready } };
            public Task<ApiResult<PartyEnvelope>> Party() => Ok(new PartyEnvelope { party = new PartyDto { id = 5, leader_id = Leader, max_size = 8,
                race = new PartyRaceDto { name = "민트초코의 레이스", course = "snake", laps = 2, fill_bots = true },
                members = Leader == 1 ? new[] { M(1, "플레이어", false), M(902, "민트초코", GuestReady) } : new[] { M(900, "방장님", false), M(1, "플레이어", true) } } });
            public Task<ApiResult<TicketDto>> CreateTicket(string m) { Created = m; return Task.FromResult(CreateAnswer); }
            public Task<ApiResult<TicketDto>> CurrentTicket() => Task.FromResult(CurrentAnswer);
            public Task<ApiResult<bool>> CancelTicket(string id) { Cancelled = id; return Ok(true); }
            public Task<ApiResult<PartyEnvelope>> SetPartyRace(PartyRaceDto r) => Party();
            public Task<ApiResult<PartyEnvelope>> SetMyRace(MemberRaceDto r) => Party();
            public Task<ApiResult<PartyEnvelope>> KickFromParty(long id) => Party();
            public Task<ApiResult<PartyEnvelope>> InviteToParty(long id) => Party();
            public Task<ApiResult<FriendsDto>> Friends() => Ok(new FriendsDto());
            public Task<ApiResult<bool>> Presence(string st) => Ok(true);
            public Task<ApiResult<MeDto>> Me() => Ok(new MeDto());
            public Task<ApiResult<bool>> AddFriend(string n) => Ok(true);
            public Task<ApiResult<bool>> AcceptFriend(long i) => Ok(true);
            public Task<ApiResult<bool>> RemoveFriend(long i) => Ok(true);
            public Task<ApiResult<InvitesDto>> PartyInvites() => Ok(new InvitesDto());
            public Task<ApiResult<bool>> AcceptInvite(long i) => Ok(true);
            public Task<ApiResult<bool>> DeclineInvite(long i) => Ok(true);
            public Task<ApiResult<bool>> LeaveParty() => Ok(true);
            public Task<ApiResult<string>> ServerToken() => Ok("t");
        }

        static RaceLobbyScreen Room => MainMenu.Instance.EventEntry;
        static bool Shows(string text) => Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text == text);

        static IEnumerator OpenRoom(Backend api)
        {
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            LobbyNet.Backend = api;
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null, "main menu", null, 20f);
            yield return null;
            MainMenu.Instance.ShowEventEntry();
            yield return NetHarness.WaitUntil(() => Room.Online, "the party's room", null, 10f);
            yield return null;
        }

        static IEnumerator Shot(string name)
        {
            string folder = System.Environment.GetEnvironmentVariable("RACE_SHOTS");
            if (string.IsNullOrEmpty(folder)) yield break;
            Directory.CreateDirectory(folder);
            yield return UiShot.Save(Path.Combine(folder, name), 1920, 1080);
        }

        [SetUp] public void SetUp() { PlayerPrefs.DeleteKey(RaceLobbyScreen.PrefsKey); MatchTicketClient.ForgetConsumed(); }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerPrefs.DeleteKey(UiFx.ReducedKey);
            PlayerPrefs.DeleteKey(RaceLobbyScreen.PrefsKey);
            RaceLobbyScreen.ReturnPending = false;
            LobbyNet.Backend = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Host_StartMakesTheRaceTicket_StartingRoom_CancelAndRefusalToast()
        {
            var api = new Backend { CreateAnswer = Backend.RaceQueued() };
            yield return OpenRoom(api);
            var room = Room;
            Assert.IsTrue(Shows("시작 (F5)"));
            room.Go();
            Assert.AreEqual("race", api.Created);
            yield return null; yield return null;
            Assert.IsTrue(MainMenu.Instance.RaceStarting);
            Assert.IsTrue(Shows("출발 준비 중") && Shows("누르면 취소 (F5)"));
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("30_starting_host.png");

            room.Go();   // the host's cancel
            Assert.AreEqual("t-r", api.Cancelled);
            Assert.IsFalse(MainMenu.Instance.RaceStarting);
            yield return null;
            Assert.AreEqual("출발을 취소했습니다.", room.ToastText);
            Assert.IsTrue(Shows("시작 (F5)") && !Shows("출발 준비 중"));

            // refused by the backend (a guest unreadied on the way): a toast, the room as it was, a new Start is possible
            api.CreateAnswer = ApiResult<TicketDto>.Fail(409, "not_ready");
            room.Go();
            yield return NetHarness.WaitUntil(() => room.ToastText == TicketRules.ErrorText(409, "not_ready"), "the refusal's toast", null, 5f);
            Assert.IsFalse(MainMenu.Instance.RaceStarting);
            Assert.IsTrue(Shows("시작 (F5)"));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Guest_SeesTheHostsStart_ThenItsCancel()
        {
            var api = new Backend { Leader = 900 };
            yield return OpenRoom(api);
            var room = Room;
            Assert.AreEqual(900, room.Room.HostId);
            api.CurrentAnswer = Backend.RaceQueued();   // the host started: my queue adopts the party's ticket
            yield return NetHarness.WaitUntil(() => MainMenu.Instance.RaceStarting, "the party's race ticket", null, 6f);
            yield return null;
            Assert.IsTrue(Shows("출발 준비 중") && Shows("방장이 출발시키는 중"));
            room.Go();
            Assert.IsTrue(room.Room.Member(room.Me).Ready, "a guest's button does nothing while it starts");
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("31_starting_guest.png");

            api.CurrentAnswer = ApiResult<TicketDto>.Fail(404, "no_ticket");   // the host cancelled
            yield return NetHarness.WaitUntil(() => !MainMenu.Instance.RaceStarting, "back in the room", null, 6f);
            yield return null;
            Assert.AreEqual(TicketRules.ErrorText(404, "no_ticket"), room.ToastText);
            Assert.IsFalse(Shows("출발 준비 중"));
        }
    }
}
