using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Dedicated server (#392): asks the backend every few seconds which matches it should run (GET /api/match/matches?status=active),
    // lets only those accounts into the room, and tells the backend when a match ended without results (POST .../finish).
    // Needs BACKEND_BASE_URL + INTERNAL_API_KEY like ResultsReporter; without them the room stays open to every login.
    public static class BackendMatchPoller
    {
        const double Every = 3.0;
        static HttpClient _http;
        static string _base, _key;
        static double _next;
        static int _busy;
        static readonly ConcurrentQueue<string> _bodies = new ConcurrentQueue<string>();
        static readonly List<AssignedMatch> _active = new List<AssignedMatch>();

        public static bool Enabled => _http != null;
        public static IReadOnlyList<AssignedMatch> Active => _active;

        public static void Start()
        {
            _base = Environment.GetEnvironmentVariable("BACKEND_BASE_URL");
            _key = Environment.GetEnvironmentVariable("INTERNAL_API_KEY");
            if (string.IsNullOrEmpty(_base) || string.IsNullOrEmpty(_key)) return;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            LobbyNet.AssignedCheck = account => BackendMatchRules.Allows(_active, account);
            Debug.Log("[Server] backend matches: only assigned accounts may join");
        }

        // Main thread, every frame.
        public static void Tick(double now)
        {
            if (_http == null) return;
            while (_bodies.TryDequeue(out var body)) Apply(body);
            if (now < _next || System.Threading.Interlocked.Exchange(ref _busy, 1) == 1) return;
            _next = now + Every;
            Task.Run(async () =>
            {
                try { _bodies.Enqueue(await Send(HttpMethod.Get, "/api/match/matches?status=active")); }
                catch (Exception) { /* backend down: the last list stays */ }
                finally { System.Threading.Interlocked.Exchange(ref _busy, 0); }
            });
        }

        static void Apply(string body)
        {
            if (string.IsNullOrEmpty(body)) return;
            var dto = JsonUtility.FromJson<ServerMatchesDto>(body);
            _active.Clear();
            foreach (var m in dto.matches)
            {
                var ids = new long[m.accounts.Length];
                for (int i = 0; i < ids.Length; i++) ids[i] = m.accounts[i].account_id;
                _active.Add(new AssignedMatch { Id = m.id, Accounts = ids, Bots = m.bots });
            }
        }

        static async Task<string> Send(HttpMethod method, string path)
        {
            using (var req = new HttpRequestMessage(method, _base.TrimEnd('/') + path))
            {
                req.Headers.Add("x-api-key", _key);
                if (method == HttpMethod.Post) req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                using (var res = await _http.SendAsync(req).ConfigureAwait(false))
                    return res.IsSuccessStatusCode ? await res.Content.ReadAsStringAsync().ConfigureAwait(false) : "";
            }
        }

        // The match that started now: the one most of the people in the room were assigned to.
        public static string Pick(ICollection<long> present) => BackendMatchRules.Pick(_active, present)?.Id;

        // A match that ended undecided frees its slot at the backend (best effort; the backend also times it out).
        public static void Finish(string matchId)
        {
            if (_http == null || string.IsNullOrEmpty(matchId)) return;
            Task.Run(async () => { try { await Send(HttpMethod.Post, "/api/match/matches/" + Uri.EscapeDataString(matchId) + "/finish"); } catch (Exception) { } });
        }
    }
}
