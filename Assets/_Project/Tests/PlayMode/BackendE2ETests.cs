using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #389 two-client end to end against a REAL backend (docs/E2E_MULTI_CHECKLIST.md): the game's own HttpBackend, MatchTicketClient,
    // LobbyNet (dedicated server + clients), BackendMatchPoller and ResultsReporter, in one process. Ignored unless the environment names
    // a running backend and three test accounts' launcher tokens (sg_). Tokens and keys are read from the environment only, never logged.
    //   SUBLEVEL_E2E_API, SUBLEVEL_E2E_GAME_A/B/C, SUBLEVEL_TOKEN_SECRET, BACKEND_BASE_URL, INTERNAL_API_KEY, SUBLEVEL_E2E_PORT (= backend GAME_SERVER_ADDR port)
    public class BackendE2ETests
    {
        const string LogDir = "Logs/net-e2e";
        readonly List<LobbyNet> _rooms = new List<LobbyNet>();
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

        void Log(string line) { Debug.Log("[E2E] " + line); _log.AppendLine(line); }
        static string Env(string n) => Environment.GetEnvironmentVariable(n);

        LobbyNet NewRoom()
        {
            var room = new GameObject("E2ELobby" + _rooms.Count).AddComponent<LobbyNet>();
            _rooms.Add(room);
            return room;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            foreach (var r in _rooms) if (r != null) { r.Shutdown(); UnityEngine.Object.Destroy(r.gameObject); }
            _rooms.Clear();
            LobbyNet.ApiToken = LobbyNet.AuthToken = LobbyNet.AuthNickname = LobbyNet.ServerTokenSecret = null;
            LobbyNet.Backend = null;
            LobbyNet.AssignedCheck = null;
            LobbyNet.AccountsBySession.Clear();
            MatchTicketClient.ForgetConsumed();
            yield return null;
        }

        static IEnumerator Await<T>(Task<T> t, float timeout = 10f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!t.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(t.IsCompleted, "backend call timed out");
        }

        IEnumerator Join(LobbyNet room, HttpBackend backend, string apiToken, string host, int port)
        {
            // LobbyNet's login is static (one player per game process): set this player's before its hello goes out.
            LobbyNet.ApiToken = apiToken; LobbyNet.Backend = backend; LobbyNet.AuthToken = null; LobbyNet.AuthNickname = null;
            if (backend != null)
            {
                var me = backend.Me(); yield return Await(me);
                Assert.IsTrue(me.Result.Ok, "/api/me with the launcher token");
                LobbyNet.AuthNickname = me.Result.Value.nickname;
            }
            Assert.IsTrue(room.Join(host, (ushort)port));
            yield return NetHarness.WaitUntil(() => room.Phase == LobbyNet.PhaseKind.InLobby || room.Phase == LobbyNet.PhaseKind.Failed, "join answered", Log);
        }

        [UnityTest]
        public IEnumerator TwoClients_LoginFriendsPartyTicketAssignJoinResults()
        {
            string api = Env("SUBLEVEL_E2E_API"), ta = Env("SUBLEVEL_E2E_GAME_A"), tb = Env("SUBLEVEL_E2E_GAME_B"), tc = Env("SUBLEVEL_E2E_GAME_C");
            if (string.IsNullOrEmpty(api) || string.IsNullOrEmpty(ta) || string.IsNullOrEmpty(tb) || string.IsNullOrEmpty(tc) || string.IsNullOrEmpty(Env("SUBLEVEL_TOKEN_SECRET")))
                Assert.Ignore("no local backend configured (SUBLEVEL_E2E_*)");
            int port = int.Parse(Env("SUBLEVEL_E2E_PORT") ?? "17989");
            var a = new HttpBackend(api, ta); var b = new HttpBackend(api, tb); var c = new HttpBackend(api, tc);

            // 1. login: the launcher token reads /api/me
            var meA = a.Me(); var meB = b.Me(); var meC = c.Me();
            yield return Await(meA); yield return Await(meB); yield return Await(meC);
            Assert.IsTrue(meA.Result.Ok && meB.Result.Ok && meC.Result.Ok, "all three /api/me");
            long idA = meA.Result.Value.id, idB = meB.Result.Value.id;
            string nickA = meA.Result.Value.nickname, nickB = meB.Result.Value.nickname;
            Log($"1 login ok: A={nickA}({idA}) B={nickB}({idB}) C={meC.Result.Value.nickname}");

            // 2. friends + party through the game's client
            var bad = a.AddFriend("NoSuchPlayer" + UnityEngine.Random.Range(1000, 9999)); yield return Await(bad);
            Assert.AreEqual("그 닉네임의 플레이어가 없다.", SocialRules.ErrorText(bad.Result.Status, bad.Result.Error));
            Log("2.5 unknown nickname -> " + SocialRules.ErrorText(bad.Result.Status, bad.Result.Error));
            var add = a.AddFriend(nickB); yield return Await(add); Assert.IsTrue(add.Result.Ok, "A adds B: " + add.Result.Error);
            var fb = b.Friends(); yield return Await(fb);
            Assert.IsTrue(Array.Exists(fb.Result.Value.incoming, f => f.account_id == idA), "B sees A's request");
            var acc = b.AcceptFriend(idA); yield return Await(acc); Assert.IsTrue(acc.Result.Ok);
            var pb = b.Presence("lobby"); yield return Await(pb);
            var fa = a.Friends(); yield return Await(fa);
            Assert.IsTrue(Array.Exists(fa.Result.Value.friends, f => f.account_id == idB && f.status == "lobby"), "A sees B in the lobby");
            Log("2.1-2.2 friend request/accept, B shows 'lobby' for A");
            var inv = a.InviteToParty(idB); yield return Await(inv); Assert.IsTrue(inv.Result.Ok, "invite: " + inv.Result.Error);
            var invs = b.PartyInvites(); yield return Await(invs);
            var invite = Array.Find(invs.Result.Value.invites, i => i.from.account_id == idA); Assert.IsNotNull(invite, "B sees the invite");
            var join = b.AcceptInvite(invite.party_id); yield return Await(join); Assert.IsTrue(join.Result.Ok);
            var pa = a.Party(); yield return Await(pa);
            Assert.AreEqual(2, pa.Result.Value.party.members.Length); Assert.AreEqual(idA, pa.Result.Value.party.leader_id);
            var leave = b.LeaveParty(); yield return Await(leave); Assert.IsTrue(leave.Result.Ok);
            pa = a.Party(); yield return Await(pa);
            Assert.IsTrue(!pa.Result.Value.HasParty || pa.Result.Value.party.members.Length == 1, "B's leave reaches A");
            inv = a.InviteToParty(idB); yield return Await(inv); invs = b.PartyInvites(); yield return Await(invs);
            invite = Array.Find(invs.Result.Value.invites, i => i.from.account_id == idA);
            join = b.AcceptInvite(invite.party_id); yield return Await(join); Assert.IsTrue(join.Result.Ok, "rejoin");
            Log($"2.3-2.4 party 2/4 (leader A), B left and rejoined (party {invite.party_id})");

            // the dedicated server's side: secret from the env (LoginEnv), poller + results like DedicatedServer.Start
            LobbyNet.ServerTokenSecret = Env("SUBLEVEL_TOKEN_SECRET");
            ResultsReporter.Start();
            BackendMatchPoller.Start();
            Assert.IsTrue(BackendMatchPoller.Enabled && ResultsReporter.Enabled, "server sees BACKEND_BASE_URL + INTERNAL_API_KEY");
            var server = NewRoom();
            Assert.IsTrue(server.StartDedicated((ushort)port));

            // 3.6 cancel: C queues alone and cancels
            var tcC = new MatchTicketClient(c);
            tcC.Start("battle-royale", Time.realtimeSinceStartupAsDouble);
            yield return NetHarness.WaitUntil(() => { tcC.Tick(Time.realtimeSinceStartupAsDouble); return tcC.State != MatchTicketClient.StateKind.Queued || tcC.Text.Contains("봇"); }, "C queued", Log);
            tcC.Cancel(Time.realtimeSinceStartupAsDouble);
            yield return new WaitForSecondsRealtime(0.5f);
            var cur = c.CurrentTicket(); yield return Await(cur);
            Assert.AreEqual(404, cur.Result.Status, "cancelled ticket is gone");
            Log("3.6 C's ticket cancelled -> GET /api/match/tickets/current 404");

            // 3.1-3.3 A (leader) queues, B watches and adopts the party ticket, both get the server
            var tA = new MatchTicketClient(a); var tB = new MatchTicketClient(b) { Watching = true };
            tA.Start("battle-royale", Time.realtimeSinceStartupAsDouble);
            string textA = "", textB = "";
            yield return NetHarness.WaitUntil(() =>
            {
                double now = Time.realtimeSinceStartupAsDouble; tA.Tick(now); tB.Tick(now); BackendMatchPoller.Tick(now);
                if (tA.State == MatchTicketClient.StateKind.Queued && tA.Text.Contains("봇")) textA = tA.Text;
                if (tB.State == MatchTicketClient.StateKind.Queued && tB.Text.Contains("봇")) textB = tB.Text;
                return tA.State == MatchTicketClient.StateKind.Matched && tB.State == MatchTicketClient.StateKind.Matched || tA.State == MatchTicketClient.StateKind.Failed;
            }, "both tickets matched", Log, 40f);
            Assert.AreEqual(MatchTicketClient.StateKind.Matched, tA.State, tA.Text);
            Log($"3.1 A waiting text: '{textA}'  3.2 B waiting text: '{textB}'");
            Assert.IsNotEmpty(textA); Assert.IsNotEmpty(textB);
            Assert.AreEqual(port, tA.Port); Assert.AreEqual(tA.Host, tB.Host);
            yield return NetHarness.WaitUntil(() => { BackendMatchPoller.Tick(Time.realtimeSinceStartupAsDouble); return BackendMatchPoller.Active.Count > 0; }, "server polled the active match", Log, 10f);
            string matchId = BackendMatchPoller.Active[0].Id;
            Log($"3.3 matched -> {tA.Host}:{tA.Port}, backend match {matchId}, bots {tA.Bots}");

            // 1.3 a client without a login is refused
            var anon = NewRoom();
            yield return Join(anon, null, null, tA.Host, tA.Port);
            Assert.AreEqual(LobbyRules.Reason(JoinResult.LoginRequired), anon.FailureText);
            Log("1.3 no login -> " + anon.FailureText);

            // 3.3-3.4 A and B join with their launcher tokens (traded for a signed join token), names are the backend's
            var roomA = NewRoom(); yield return Join(roomA, a, ta, tA.Host, tA.Port);
            Assert.AreEqual(LobbyNet.PhaseKind.InLobby, roomA.Phase, roomA.FailureText);
            var roomB = NewRoom(); yield return Join(roomB, b, tb, tB.Host, tB.Port);
            Assert.AreEqual(LobbyNet.PhaseKind.InLobby, roomB.Phase, roomB.FailureText);
            yield return NetHarness.WaitUntil(() => roomB.Roster.Count == 2, "both in the room", Log);
            var names = new List<string>(); foreach (var e in roomB.Roster.Members) names.Add(e.Name);
            CollectionAssert.AreEquivalent(new[] { nickA, nickB }, names);
            Log("3.3-3.4 room: " + string.Join(", ", names));

            // 3.7 C (not in the match) is refused
            var roomC = NewRoom(); yield return Join(roomC, c, tc, tA.Host, tA.Port);
            Assert.AreEqual(LobbyRules.Reason(JoinResult.NotAssigned), roomC.FailureText);
            Log("3.7 unassigned C -> " + roomC.FailureText);

            // the server picks the backend match from who is present (DedicatedServer.OnMatchStarting)
            Assert.AreEqual(matchId, BackendMatchPoller.Pick(new List<long>(LobbyNet.AccountsBySession.Values)));
            ResultsReporter.CurrentMatchId = matchId;

            // 4.2-4.4, 4.6 the decided match's rows (humans only) go to the backend
            var field = typeof(MatchState).GetField("ServerResults", BindingFlags.Static | BindingFlags.NonPublic);
            var raise = (Action<IReadOnlyList<PlayerResult>>)field.GetValue(null);
            Assert.IsNotNull(raise, "ResultsReporter listens to MatchState.ServerResults");
            raise(new List<PlayerResult> { new PlayerResult(idA, 1, 3, 612f), new PlayerResult(idB, 2, 1, 598f) });
            using (var http = new HttpClient())
            {
                bool done = false; string bodyA = "", bodyB = "", active = "";
                float end = Time.realtimeSinceStartup + 20f;
                while (!done && Time.realtimeSinceStartup < end)
                {
                    yield return new WaitForSecondsRealtime(1f);
                    ResultsReporter.Pump();
                    var ga = Get(http, api + "/api/me/results", ta); var gb = Get(http, api + "/api/me/results", tb);
                    var gm = GetKey(http, api + "/api/match/matches?status=active");
                    yield return Await(ga); yield return Await(gb); yield return Await(gm);
                    bodyA = ga.Result; bodyB = gb.Result; active = gm.Result;
                    done = bodyA.Contains(matchId) && bodyB.Contains(matchId) && !active.Contains(matchId);
                }
                Log("4.3 A results: " + bodyA);
                Log("4.3 B results: " + bodyB);
                Log("4.4 active matches after results: " + active);
                Assert.IsTrue(done, "results reached the backend and the match is finished");
            }
        }

        static async Task<string> Get(HttpClient http, string url, string token)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                using (var res = await http.SendAsync(req).ConfigureAwait(false)) return await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        static async Task<string> GetKey(HttpClient http, string url)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req.Headers.Add("x-api-key", Env("INTERNAL_API_KEY"));
                using (var res = await http.SendAsync(req).ConfigureAwait(false)) return await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }
    }
}
