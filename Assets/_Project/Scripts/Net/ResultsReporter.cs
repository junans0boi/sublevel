using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Dedicated server: sends who placed where to the backend when a match is decided (#390). BACKEND_BASE_URL and INTERNAL_API_KEY come
    // from the server's environment only (never logged). Results wait in a folder until the backend took them; sending never blocks the match.
    public static class ResultsReporter
    {
        static ResultsOutbox _outbox;
        static HttpClient _http;
        static string _baseUrl, _key;
        static int _flushing;

        public static bool Enabled => _outbox != null;
        // The backend's id of the running match (BackendMatchPoller.Pick), so the backend closes its slot when the results arrive; null = make one.
        public static string CurrentMatchId;
        public static bool Reported;

        public static void Start()
        {
            _baseUrl = Environment.GetEnvironmentVariable("BACKEND_BASE_URL");
            _key = Environment.GetEnvironmentVariable("INTERNAL_API_KEY");
            if (string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_key)) { Debug.Log("[Server] results: BACKEND_BASE_URL/INTERNAL_API_KEY not set, results are not sent"); return; }
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _outbox = new ResultsOutbox(Path.Combine(Application.persistentDataPath, "results-outbox"), Send);
            MatchState.ServerResults += OnResults;
            Debug.Log("[Server] results: sending to the backend, " + _outbox.Pending + " waiting from earlier");
            Pump();
        }

        static void OnResults(IReadOnlyList<PlayerResult> rows)
        {
            if (_outbox == null) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string id = CurrentMatchId ?? "m-" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH-mm-ss'Z'") + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
            Reported = true;
            _outbox.Enqueue(id, ResultsRules.Json(id, "battle-royale", now, rows));
            Debug.Log($"[Server] results: {rows.Count} people queued for the backend ({id})");
            Pump();
        }

        // Tries what is waiting, on a worker thread; called again whenever something new arrives and every few seconds by Tick.
        public static void Pump()
        {
            if (_outbox == null || Interlocked.Exchange(ref _flushing, 1) == 1) return;
            Task.Run(() =>
            {
                try { _outbox.Flush(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
                catch (Exception e) { Debug.LogWarning("[Server] results: " + e.GetType().Name); }
                finally { Interlocked.Exchange(ref _flushing, 0); }
            });
        }

        static SendOutcome Send(string json)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl.TrimEnd('/') + "/api/results") { Content = new StringContent(json, Encoding.UTF8, "application/json") })
                {
                    req.Headers.Add("x-api-key", _key);
                    int status = (int)_http.SendAsync(req).GetAwaiter().GetResult().StatusCode;
                    var outcome = ResultsRules.Outcome(status);
                    Debug.Log("[Server] results: backend answered " + status + (outcome == SendOutcome.Retry ? ", will retry" : ""));
                    return outcome;
                }
            }
            catch (Exception) { Debug.Log("[Server] results: backend not reachable, will retry"); return SendOutcome.Retry; }
        }
    }
}
