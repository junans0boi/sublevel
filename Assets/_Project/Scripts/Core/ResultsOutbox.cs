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

        // 200 done; 400 refused (an unknown account or a bad row); anything else (401 key being rotated, network, 5xx, 503) is tried again. status 0 = no answer.
        public static SendOutcome Outcome(int status) => status >= 200 && status < 300 ? SendOutcome.Ok : status == 400 ? SendOutcome.Drop : SendOutcome.Retry;

        // 1s, 2s, 4s ... up to 60s.
        public static double BackoffSeconds(int attempt) => Math.Min(60.0, Math.Pow(2, Math.Max(0, attempt - 1)));
    }

    // One file per match in a folder: written before the first try, deleted when the backend took it, so a backend that is down or being
    // redeployed loses nothing, even across a server restart. What the backend refuses or what got too old moves to rejected/ (never deleted).
    // Flush runs from any thread.
    public sealed class ResultsOutbox
    {
        readonly string _dir;
        readonly Func<string, SendOutcome> _send;
        readonly Dictionary<string, (int attempts, double next)> _state = new Dictionary<string, (int, double)>();
        static readonly Regex Ended = new Regex("\"ended_at\":(\\d+)");
        static readonly Regex Row = new Regex("\\{\"account_id\":[^}]*\\}");
        const string Players = "\"players\":[";

        public ResultsOutbox(string dir, Func<string, SendOutcome> send) { _dir = dir; _send = send; Directory.CreateDirectory(dir); }

        public int Pending => Directory.GetFiles(_dir, "*.json").Length;

        // False when it could not be written (full disk): the caller goes on ending the match (#495).
        public bool Enqueue(string matchId, string json)
        {
            try { Write(Regex.Replace(matchId, "[^A-Za-z0-9_-]", "_"), json); return true; }
            catch (Exception) { return false; }
        }

        void Write(string name, string json)
        {
            string tmp = Path.Combine(_dir, name + ".tmp"), file = Path.Combine(_dir, name + ".json");
            File.WriteAllText(tmp, json);
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
        }

        void Shelve(string file)
        {
            string dir = Path.Combine(_dir, "rejected"), to = Path.Combine(dir, Path.GetFileName(file));
            Directory.CreateDirectory(dir);
            if (File.Exists(to)) File.Delete(to);
            File.Move(file, to);
            _state.Remove(file);
        }

        // The backend refuses the whole match for one unknown account (#495) and does not say which, and it takes the same match id again
        // (idempotent per account). So a refused match is split in halves and each sent again until the refused rows stand alone.
        void Refused(string file, string json)
        {
            var rows = Row.Matches(json);
            int at = json.IndexOf(Players, StringComparison.Ordinal);
            if (rows.Count < 2 || at < 0) { Shelve(file); return; }
            string head = json.Substring(0, at + Players.Length), name = Path.GetFileNameWithoutExtension(file);
            int half = rows.Count / 2;
            Write(name + "-a", head + Join(rows, 0, half) + "]}");
            Write(name + "-b", head + Join(rows, half, rows.Count) + "]}");
            File.Delete(file);
            _state.Remove(file);
        }

        static string Join(MatchCollection rows, int from, int to)
        {
            var sb = new StringBuilder();
            for (int i = from; i < to; i++) sb.Append(i > from ? "," : "").Append(rows[i].Value);
            return sb.ToString();
        }

        // Tries every file whose backoff has passed. `now` in unix seconds.
        public void Flush(double now)
        {
            foreach (var file in Directory.GetFiles(_dir, "*.json"))
            {
                if (_state.TryGetValue(file, out var s) && now < s.next) continue;
                string json = File.ReadAllText(file);
                var m = Ended.Match(json);
                if (m.Success && now - long.Parse(m.Groups[1].Value) / 1000.0 > ResultsRules.MaxAge.TotalSeconds) { Shelve(file); continue; }
                var outcome = _send(json);
                if (outcome == SendOutcome.Retry)
                {
                    int n = s.attempts + 1;
                    _state[file] = (n, now + ResultsRules.BackoffSeconds(n));
                }
                else if (outcome == SendOutcome.Drop) Refused(file, json);
                else { File.Delete(file); _state.Remove(file); }
            }
        }
    }
}
