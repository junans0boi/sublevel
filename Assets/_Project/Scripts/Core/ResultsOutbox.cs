using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Game.Core
{
    public struct PlayerResult
    {
        public long AccountId; public int Rank, Kills, SurvivalSeconds;
        public PlayerResult(long account, int rank, int kills, float survival)
        {
            AccountId = account; Rank = Math.Max(1, rank); Kills = Math.Max(0, kills); SurvivalSeconds = Math.Max(0, Math.Min(86400, (int)survival));
        }
    }

    public enum SendOutcome { Ok, Retry, Drop }

    // What the backend's POST /api/results expects (sublevel-backend docs/results-api.md, #390). Humans with an account only. Pure.
    public static class ResultsRules
    {
        public const int MaxPlayers = 100;
        public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

        public static string Json(string matchId, string mode, long endedAtMs, IReadOnlyList<PlayerResult> players)
        {
            var sb = new StringBuilder("{\"match_id\":\"").Append(Esc(matchId)).Append("\",\"mode\":\"").Append(Esc(mode)).Append("\",\"ended_at\":")
                .Append(endedAtMs.ToString(CultureInfo.InvariantCulture)).Append(",\"players\":[");
            for (int i = 0; i < players.Count && i < MaxPlayers; i++)
            {
                var p = players[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"account_id\":").Append(p.AccountId).Append(",\"rank\":").Append(p.Rank).Append(",\"kills\":").Append(p.Kills).Append(",\"survival_seconds\":").Append(p.SurvivalSeconds).Append('}');
            }
            return sb.Append("]}").ToString();
        }

        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        // 200 done; 400/401 will not get better by trying again; anything else (network, 5xx, 503) is tried again. status 0 = no answer.
        public static SendOutcome Outcome(int status) => status >= 200 && status < 300 ? SendOutcome.Ok : status == 400 || status == 401 ? SendOutcome.Drop : SendOutcome.Retry;

        // 1s, 2s, 4s ... up to 60s.
        public static double BackoffSeconds(int attempt) => Math.Min(60.0, Math.Pow(2, Math.Max(0, attempt - 1)));
    }

    // One file per match in a folder: written before the first try, deleted when the backend took it (or will never take it),
    // so a backend that is down or being redeployed loses nothing, even across a server restart. Flush runs from any thread.
    public sealed class ResultsOutbox
    {
        readonly string _dir;
        readonly Func<string, SendOutcome> _send;
        readonly Dictionary<string, (int attempts, double next)> _state = new Dictionary<string, (int, double)>();
        static readonly Regex Ended = new Regex("\"ended_at\":(\\d+)");

        public ResultsOutbox(string dir, Func<string, SendOutcome> send) { _dir = dir; _send = send; Directory.CreateDirectory(dir); }

        public int Pending => Directory.GetFiles(_dir, "*.json").Length;

        public void Enqueue(string matchId, string json)
        {
            string name = Regex.Replace(matchId, "[^A-Za-z0-9_-]", "_");
            string tmp = Path.Combine(_dir, name + ".tmp"), file = Path.Combine(_dir, name + ".json");
            File.WriteAllText(tmp, json);
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
        }

        // Tries every file whose backoff has passed. `now` in unix seconds.
        public void Flush(double now)
        {
            foreach (var file in Directory.GetFiles(_dir, "*.json"))
            {
                if (_state.TryGetValue(file, out var s) && now < s.next) continue;
                string json = File.ReadAllText(file);
                var m = Ended.Match(json);
                if (m.Success && now - long.Parse(m.Groups[1].Value) / 1000.0 > ResultsRules.MaxAge.TotalSeconds) { File.Delete(file); _state.Remove(file); continue; }
                var outcome = _send(json);
                if (outcome == SendOutcome.Retry)
                {
                    int n = s.attempts + 1;
                    _state[file] = (n, now + ResultsRules.BackoffSeconds(n));
                }
                else { File.Delete(file); _state.Remove(file); }
            }
        }
    }
}
