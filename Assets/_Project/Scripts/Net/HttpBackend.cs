using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Net
{
    // The real backend over HTTPS with the game token as Bearer (the launcher's short-lived scoped game token, SUBLEVEL_GAME_TOKEN; only ever sent in the Authorization header, never stored or logged). Never logs the token.
    public sealed class HttpBackend : IBackend
    {
        public const string DefaultBase = "https://sublevel.steady2vivid.kro.kr";
        readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        readonly string _base, _session;

        public HttpBackend(string baseUrl, string sessionToken) { _base = baseUrl.TrimEnd('/'); _session = sessionToken; }

        public static HttpBackend FromEnvironment()
        {
            var session = LobbyNet.ApiToken;
            if (string.IsNullOrEmpty(session)) return null;
            var url = Environment.GetEnvironmentVariable("SUBLEVEL_API_BASE");
            return new HttpBackend(string.IsNullOrEmpty(url) ? DefaultBase : url, session);
        }

        async Task<(int status, string body)> Send(HttpMethod method, string path, string json = null)
        {
            try
            {
                using (var req = new HttpRequestMessage(method, _base + path))
                {
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _session);
                    if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using (var res = await _http.SendAsync(req).ConfigureAwait(false))
                        return ((int)res.StatusCode, await res.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
            catch (Exception) { return (0, ""); }
        }

        static string ErrorOf(string body)
        {
            int i = body.IndexOf("\"error\":\"", StringComparison.Ordinal);
            if (i < 0) return "";
            i += 9;
            int j = body.IndexOf('"', i);
            return j < 0 ? "" : body.Substring(i, j - i);
        }

        static ApiResult<T> Parse<T>(int status, string body, Func<string, T> read)
        {
            if (status < 200 || status >= 300) return ApiResult<T>.Fail(status, ErrorOf(body));
            try { return ApiResult<T>.Success(read(body), status); } catch (Exception) { return ApiResult<T>.Fail(status, "bad_response"); }
        }

        public async Task<ApiResult<TicketDto>> CreateTicket(string mode)
        {
            var r = await Send(HttpMethod.Post, "/api/match/tickets", "{\"mode\":\"" + mode + "\"}").ConfigureAwait(false);
            return Parse(r.status, r.body, b => JsonUtility.FromJson<TicketEnvelope>(b).ticket);
        }

        public async Task<ApiResult<TicketDto>> CurrentTicket()
        {
            var r = await Send(HttpMethod.Get, "/api/match/tickets/current").ConfigureAwait(false);
            return Parse(r.status, r.body, b => JsonUtility.FromJson<TicketEnvelope>(b).ticket);
        }

        public async Task<ApiResult<bool>> CancelTicket(string id)
        {
            var r = await Send(HttpMethod.Delete, "/api/match/tickets/" + Uri.EscapeDataString(id)).ConfigureAwait(false);
            return Parse(r.status, r.body, _ => true);
        }

        public async Task<ApiResult<bool>> Presence(string status)
        {
            var r = await Send(HttpMethod.Put, "/api/presence", "{\"status\":\"" + status + "\"}").ConfigureAwait(false);
            return Parse(r.status, r.body, _ => true);
        }

        public async Task<ApiResult<MeDto>> Me()
        {
            var r = await Send(HttpMethod.Get, "/api/me").ConfigureAwait(false);
            return Parse(r.status, r.body, b => JsonUtility.FromJson<MeDto>(b));
        }

        public async Task<ApiResult<string>> ServerToken()
        {
            var r = await Send(HttpMethod.Post, "/api/auth/token", "{}").ConfigureAwait(false);
            return Parse(r.status, r.body, b => JsonUtility.FromJson<TokenEnvelope>(b).token);
        }

        async Task<ApiResult<bool>> Do(HttpMethod m, string path, string json = null)
        {
            var r = await Send(m, path, json).ConfigureAwait(false);
            return Parse(r.status, r.body, _ => true);
        }

        public async Task<ApiResult<FriendsDto>> Friends()
        {
            var r = await Send(HttpMethod.Get, "/api/friends").ConfigureAwait(false);
            return Parse(r.status, r.body, b => JsonUtility.FromJson<FriendsDto>(b));
        }

        public Task<ApiResult<bool>> AddFriend(string nickname) => Do(HttpMethod.Post, "/api/friends/requests", "{\"nickname\":\"" + nickname.Replace("\\", "").Replace("\"", "") + "\"}");
        public Task<ApiResult<bool>> AcceptFriend(long id) => Do(HttpMethod.Post, "/api/friends/requests/" + id + "/accept", "{}");
        public Task<ApiResult<bool>> RemoveFriend(long id) => Do(HttpMethod.Delete, "/api/friends/" + id);

        public async Task<ApiResult<PartyEnvelope>> Party()
        {
            var r = await Send(HttpMethod.Get, "/api/party").ConfigureAwait(false);
            return Parse(r.status, r.body, b => JsonUtility.FromJson<PartyEnvelope>(b));
        }

        public Task<ApiResult<PartyEnvelope>> InviteToParty(long id) => PartyCall(HttpMethod.Post, "/api/party/invites", "{\"account_id\":" + id + "}");

        public async Task<ApiResult<InvitesDto>> PartyInvites()
        {
            var r = await Send(HttpMethod.Get, "/api/party/invites").ConfigureAwait(false);
            return Parse(r.status, r.body, b => JsonUtility.FromJson<InvitesDto>(b));
        }

        public Task<ApiResult<bool>> AcceptInvite(long partyId) => Do(HttpMethod.Post, "/api/party/invites/" + partyId + "/accept", "{}");
        public Task<ApiResult<bool>> DeclineInvite(long partyId) => Do(HttpMethod.Delete, "/api/party/invites/" + partyId);
        public Task<ApiResult<bool>> LeaveParty() => Do(HttpMethod.Post, "/api/party/leave", "{}");

        async Task<ApiResult<PartyEnvelope>> PartyCall(HttpMethod m, string path, string json)
        {
            var r = await Send(m, path, json).ConfigureAwait(false);
            return Parse(r.status, r.body, b => JsonUtility.FromJson<PartyEnvelope>(b));
        }

        public Task<ApiResult<PartyEnvelope>> SetPartyRace(PartyRaceDto race) => PartyCall(HttpMethod.Put, "/api/party/race", JsonUtility.ToJson(race));
        public Task<ApiResult<PartyEnvelope>> SetMyRace(MemberRaceDto race) => PartyCall(HttpMethod.Put, "/api/party/members/me/race", JsonUtility.ToJson(race));
        public Task<ApiResult<PartyEnvelope>> KickFromParty(long id) => PartyCall(HttpMethod.Post, "/api/party/kick", "{\"account_id\":" + id + "}");
    }
}
