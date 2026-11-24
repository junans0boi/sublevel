using System;
using System.Collections;
using System.Threading.Tasks;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #519 (and #520 invite) acceptance: two clients (the game's own HttpBackend + SocialClient + RaceParty) against a REAL local backend (sublevel-backend
    // with a temp DB). Ignored unless the environment names it and two fresh accounts' session tokens (never logged):
    //   SUBLEVEL_E2E_API, SUBLEVEL_E2E_GAME_A, SUBLEVEL_E2E_GAME_B (#520: two more, SUBLEVEL_E2E_GAME_C, SUBLEVEL_E2E_GAME_D)
    public class RacePartyE2ETests
    {
        static string Env(string n) => Environment.GetEnvironmentVariable(n);

        sealed class Client
        {
            public HttpBackend Api; public SocialClient Social; public RaceLobby Room; public RaceParty Party; public long Id; public string Nick;
            public void Tick() { Social.Tick(Time.realtimeSinceStartupAsDouble); Party.Sync(); }
        }

        static IEnumerator Await<T>(Task<T> t, float timeout = 10f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!t.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(t.IsCompleted, "backend call timed out");
        }

        static IEnumerator Login(Client c)
        {
            var me = c.Api.Me(); yield return Await(me);
            Assert.IsTrue(me.Result.Ok, "/api/me");
            c.Id = me.Result.Value.id; c.Nick = me.Result.Value.nickname;
            c.Social = new SocialClient(c.Api);
            c.Room = new RaceLobby(new RaceLobbyMember { Id = (int)c.Id, Name = c.Nick, Car = "FairheavenLT80" });
            c.Party = new RaceParty(() => c.Social, c.Room, (int)c.Id);
        }

        // Ticks both clients until `ok` or the time runs out; one poll is 2.5 s.
        static IEnumerator Until(Client a, Client b, Func<bool> ok, string what, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!ok() && Time.realtimeSinceStartup < end) { a.Tick(); b.Tick(); yield return null; }
            Assert.IsTrue(ok(), what);
        }

        static IEnumerator Ok<T>(Task<ApiResult<T>> t, string what) { yield return Await(t); Assert.IsTrue(t.Result.Ok, what + ": " + t.Result.Status + " " + t.Result.Error); }

        IEnumerator JoinParty(Client host, Client guest)
        {
            yield return Ok(host.Api.InviteToParty(guest.Id), "invite");
            var inv = guest.Api.PartyInvites(); yield return Await(inv);
            yield return Ok(guest.Api.AcceptInvite(inv.Result.Value.invites[0].party_id), "accept invite");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TwoClients_ReadyCourseKickAndLeaderLeave()
        {
            string api = Env("SUBLEVEL_E2E_API"), ta = Env("SUBLEVEL_E2E_GAME_A"), tb = Env("SUBLEVEL_E2E_GAME_B");
            if (string.IsNullOrEmpty(api) || string.IsNullOrEmpty(ta) || string.IsNullOrEmpty(tb)) Assert.Ignore("no local backend configured (SUBLEVEL_E2E_*)");
            var a = new Client { Api = new HttpBackend(api, ta) };
            var b = new Client { Api = new HttpBackend(api, tb) };
            yield return Login(a); yield return Login(b);
            yield return Ok(a.Api.AddFriend(b.Nick), "friend request");
            yield return Ok(b.Api.AcceptFriend(a.Id), "friend accept");
            yield return JoinParty(a, b);

            yield return Until(a, b, () => a.Party.Online && b.Party.Online, "both rooms on the party", 8f);
            Assert.AreEqual((int)a.Id, a.Room.HostId); Assert.AreEqual((int)a.Id, b.Room.HostId);

            // guest readies -> the host sees 준비 완료 within one poll (+ the request itself)
            Assert.IsTrue(b.Party.ToggleReady());
            float t0 = Time.realtimeSinceStartup;
            yield return Until(a, b, () => a.Room.Member((int)b.Id).Ready, "host sees the guest ready", 4f);
            Debug.Log($"[E2E519] ready seen by the host after {Time.realtimeSinceStartup - t0:0.00}s");

            Assert.IsFalse(b.Party.SetCar("Shvan92", 1), "garage locked while ready");

            // host changes course -> the guest is unready
            Assert.IsTrue(a.Party.SetCourse("pass_loop", 3));
            yield return Until(a, b, () => b.Room.Course == "pass_loop" && b.Room.Laps == 3 && !b.Room.Member((int)b.Id).Ready, "guest unready after the course change", 6f);
            StringAssert.StartsWith("코스가 ", b.Room.Lines[b.Room.Lines.Count - 1]);

            // kick removes the guest: their poll finds no party
            Assert.IsTrue(a.Party.Kick((int)b.Id));
            yield return Until(a, b, () => !b.Party.Online && a.Room.Member((int)b.Id) == null, "kick removes the guest", 6f);
            Assert.AreEqual("방에서 내보내졌습니다.", b.Party.TakeToast());
            Assert.AreEqual((int)b.Id, b.Room.HostId, "the kicked guest is back in a local room of their own");

            // back in; the host leaves -> the guest becomes host
            yield return JoinParty(a, b);
            yield return Until(a, b, () => a.Party.Online && b.Party.Online, "rejoined", 8f);
            a.Party.Leave();
            yield return Until(a, b, () => b.Room.HostId == (int)b.Id && !b.Party.Online, "guest becomes host", 6f);
            var p = b.Api.Party(); yield return Await(p);
            Assert.AreEqual(b.Id, p.Result.Value.party.leader_id, "the backend handed the party over");
            Assert.IsNull(b.Party.TakeToast(), "a leader leaving is not a kick");
            yield return Ok(b.Api.LeaveParty(), "cleanup");
        }
        // #520: the host invites from the room (no party yet): offline until the friend's presence arrives, then 초대; the backend makes the
        // party with the host's local room on it; the friend accepts and sits in the next free slot, unready, in the room as the host set it.
        [UnityTest, Timeout(120000)]
        public IEnumerator Invite_FromTheLocalRoom_FriendAcceptsIntoAFreeSlotUnready()
        {
            string api = Env("SUBLEVEL_E2E_API"), tc = Env("SUBLEVEL_E2E_GAME_C"), td = Env("SUBLEVEL_E2E_GAME_D");
            if (string.IsNullOrEmpty(api) || string.IsNullOrEmpty(tc) || string.IsNullOrEmpty(td)) Assert.Ignore("no local backend configured (SUBLEVEL_E2E_*)");
            var a = new Client { Api = new HttpBackend(api, tc) };
            var b = new Client { Api = new HttpBackend(api, td) };
            yield return Login(a); yield return Login(b);
            yield return Ok(a.Api.AddFriend(b.Nick), "friend request");
            yield return Ok(b.Api.AcceptFriend(a.Id), "friend accept");

            // only the host ticks: the friend has sent no presence yet -> offline, no invite
            float end = Time.realtimeSinceStartup + 4f;
            FriendDto F() => Array.Find(a.Social.Friends.friends, f => f.account_id == b.Id);
            while (F() == null && Time.realtimeSinceStartup < end) { a.Tick(); yield return null; }
            Assert.AreEqual(("오프라인", false), SocialRules.InviteButton(F().status, false, false, false));
            yield return Until(a, b, () => F().status == "lobby", "the friend's presence reaches the host", 8f);
            Assert.AreEqual(("초대", true), SocialRules.InviteButton(F().status, a.Room.Member((int)b.Id) != null, a.Party.Invited(b.Id), false));

            // the host's local room: pass_loop x3, slot 1 closed
            Assert.IsTrue(a.Party.SetCourse("pass_loop", 3));
            Assert.IsTrue(a.Party.CloseSlot(1));
            Assert.IsNull(a.Party.Invite(b.Id, b.Nick));
            yield return Until(a, b, () => b.Social.Invites.invites.Length > 0 && a.Party.Invited(b.Id), "the invite reaches the friend", 6f);
            Assert.IsFalse(a.Party.Online, "a party of 1: still the host's local room");
            var party = a.Api.Party(); yield return Await(party);
            Assert.AreEqual("pass_loop", party.Result.Value.party.race.course, "the local room went up with the new party");
            Assert.AreEqual(2, party.Result.Value.party.race.closed);

            b.Social.AcceptInvite(b.Social.Invites.invites[0].party_id);
            yield return Until(a, b, () => a.Party.Online && b.Party.Online, "both rooms on the party", 8f);
            Assert.IsFalse(a.Party.Invited(b.Id));
            Assert.AreEqual((int)b.Id, a.Room.SeatAt(2)?.Id, "slot 1 closed: the next free slot");
            Assert.IsFalse(a.Room.Member((int)b.Id).Ready);
            Assert.AreEqual("pass_loop", b.Room.Course); Assert.AreEqual(3, b.Room.Laps); Assert.IsTrue(b.Room.IsClosed(1));
            Assert.AreEqual(("방에 있음", false), SocialRules.InviteButton(F().status, a.Room.Member((int)b.Id) != null, a.Party.Invited(b.Id), false));

            // full: close every other slot -> no invite goes out; the backend refuses an invite of a member
            for (int i = 3; i < RaceLobby.Slots; i++) Assert.IsTrue(a.Party.CloseSlot(i));
            yield return Until(a, b, () => b.Room.Capacity == 2, "the guest sees the closed slots", 6f);
            Assert.AreEqual("빈 자리가 없습니다.", a.Party.Invite(b.Id, b.Nick));
            var again = a.Api.InviteToParty(b.Id); yield return Await(again);
            Assert.AreEqual(409, again.Result.Status); Assert.AreEqual("already_member", again.Result.Error);
            yield return Ok(b.Api.LeaveParty(), "cleanup b");
            yield return Ok(a.Api.LeaveParty(), "cleanup a");
        }
    }
}
