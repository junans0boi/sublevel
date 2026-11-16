using System.Collections.Generic;
using System.Globalization;

namespace Game.Core
{
    // What a dedicated server was told on its command line (#168, ADR-127). Pure: parsing only.
    //   --dedicated            run as a dedicated server (no window, no local player)
    //   --port N               lobby port, default 7770 (the match listens on N+1)
    //   --bots N               bots that fill the match; default: fill up to the usual size beside the people
    //   --region ID            the match's final region, by id or index; default random
    //   --max-players N        people in one room (1..40), default from rules.json
    //   --participants N       people + bots in the match (2..50), default rules.json Match.Participants (slice and measurement runs)
    //   --perf NAME            write server frame-time stats to Logs/perf/s8_server_NAME.csv (ADR-242)
    //   --start-seconds N      start this long after the first person joined even if not all are ready (0 = never), default 60
    public struct ServerOptions
    {
        public bool Dedicated;
        public ushort Port;
        public int Bots;            // -1 = the lobby's own default
        public string Region;       // null = random
        public int MaxPlayers;      // 0 = rules.json
        public int Participants;    // 0 = rules.json
        public float GatherX, GatherZ; // --perf-gather X,Z: where the P3 crowd meets
        public float PerfQuick;        // --perf-quick N: pollution table N times faster (PollutionRules.QuickMatch), 0 = real time
        public string Perf;         // null = off; else the scenario name for Logs/perf/s8_server_<name>.csv
        public float StartSeconds;
        public string Error;        // null when everything parsed
    }

    public static class ServerArgs
    {
        public const ushort DefaultPort = 7770;
        public const float DefaultStartSeconds = 60f;

        public static ServerOptions Parse(IReadOnlyList<string> args)
        {
            var o = new ServerOptions { Port = DefaultPort, Bots = -1, StartSeconds = DefaultStartSeconds };
            if (args == null) return o;
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i];
                if (a == "--dedicated" || a == "-dedicated") { o.Dedicated = true; continue; }
                bool known = a == "--port" || a == "--bots" || a == "--region" || a == "--max-players" || a == "--participants" || a == "--perf" || a == "--perf-gather" || a == "--perf-quick" || a == "--start-seconds";
                if (!known) continue;
                if (i + 1 >= args.Count) { o.Error = a + " needs a value"; return o; }
                string v = args[++i];
                switch (a)
                {
                    case "--port":
                        // The match takes the next port, so 65535 is out.
                        if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65534) { o.Error = "--port must be 1..65534, got '" + v + "'"; return o; }
                        o.Port = (ushort)port;
                        break;
                    case "--bots":
                        if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int bots) || bots > LobbyRules.MaxParticipants - 1) { o.Error = "--bots must be 0.." + (LobbyRules.MaxParticipants - 1) + ", got '" + v + "'"; return o; }
                        o.Bots = bots;
                        break;
                    case "--region":
                        if (string.IsNullOrWhiteSpace(v)) { o.Error = "--region is empty"; return o; }
                        o.Region = v.Trim();
                        break;
                    case "--max-players":
                        if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int max) || max < 1 || max > MatchRules.Current.MaxHumans) { o.Error = "--max-players must be 1.." + MatchRules.Current.MaxHumans + ", got '" + v + "'"; return o; }
                        o.MaxPlayers = max;
                        break;
                    case "--participants":
                        if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int parts) || parts < 2 || parts > MatchRules.MaxParticipantsLimit) { o.Error = "--participants must be 2.." + MatchRules.MaxParticipantsLimit + ", got '" + v + "'"; return o; }
                        o.Participants = parts;
                        break;
                    case "--perf":
                        if (string.IsNullOrWhiteSpace(v)) { o.Error = "--perf is empty"; return o; }
                        o.Perf = v.Trim();
                        break;
                    case "--perf-quick":
                        if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out o.PerfQuick) || o.PerfQuick < 1f) { o.Error = "--perf-quick must be 1 or more, got '" + v + "'"; return o; }
                        break;
                    case "--perf-gather":
                        var xz = v.Split(',');
                        if (xz.Length != 2 || !float.TryParse(xz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out o.GatherX) || !float.TryParse(xz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out o.GatherZ)) { o.Error = "--perf-gather must be X,Z, got '" + v + "'"; return o; }
                        break;
                    case "--start-seconds":
                        if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float secs) || secs < 0f) { o.Error = "--start-seconds must be 0 or more, got '" + v + "'"; return o; }
                        o.StartSeconds = secs;
                        break;
                }
            }
            return o;
        }
    }

    // When a dedicated server leaves a match and opens the lobby again (#168). Pure.
    public static class ServerRules
    {
        public const float ResultSeconds = 20f;       // after the match is decided, so the result screens are read
        public const float EmptySeconds = 30f;        // nobody connected (and nobody held for a reconnect)
        public const float NobodyCameSeconds = 150f;  // the lobby's people never arrived in the match

        // True when the match should be closed: decided long enough ago, everyone left, or no one ever came.
        public static bool ShouldEndMatch(float matchSeconds, bool everHadPeople, float emptyFor, float overFor)
        {
            if (overFor >= ResultSeconds) return true;
            if (everHadPeople) return emptyFor >= EmptySeconds;
            return matchSeconds >= NobodyCameSeconds;
        }

        // True when the lobby should start although not everyone is ready (the start timer, 0 = off).
        public static bool ShouldAutoStart(float startSeconds, float sinceFirstJoin, int people)
            => startSeconds > 0f && people > 0 && sinceFirstJoin >= startSeconds;
    }
}
