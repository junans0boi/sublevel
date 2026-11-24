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
    // #519 acceptance: two clients (the game's own HttpBackend + SocialClient + RaceParty) against a REAL local backend (sublevel-backend
    // with a temp DB). Ignored unless the environment names it and two fresh accounts' session tokens (never logged):
    //   SUBLEVEL_E2E_API, SUBLEVEL_E2E_GAME_A, SUBLEVEL_E2E_GAME_B
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
    }
}
