using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum JoinResult : byte { Ok = 0, VersionMismatch = 1, Full = 2, MatchStarted = 3 }

    // Lobby rules (#160, ADR-124): who may join, how many bots fill the match, when it can start. Pure.
    public static class LobbyRules
    {
        public const int MaxParticipants = MatchRules.MaxBotsLimit + 1; // 24: the cap of a match, humans and bots together

        // A forced start needs someone in the room, except a measurement run (--perf, ADR-242), which plays bots only.
        public static bool MayForceStart(int people, bool allowEmpty) => people > 0 || allowEmpty;

        // Ok unless the versions differ, the match already began, or the room holds `maxPlayers` people.
        public static JoinResult CheckJoin(string serverVersion, string clientVersion, int players, int maxPlayers, bool matchStarted)
        {
            if (!string.Equals(serverVersion, clientVersion, StringComparison.Ordinal)) return JoinResult.VersionMismatch;
            if (matchStarted) return JoinResult.MatchStarted;
            if (players >= maxPlayers) return JoinResult.Full;
            return JoinResult.Ok;
        }

        public static string Reason(JoinResult result)
        {
            switch (result)
            {
                case JoinResult.VersionMismatch: return "버전이 달라 참가할 수 없다. 호스트와 같은 버전을 쓰자.";
                case JoinResult.Full: return "방이 가득 찼다.";
                case JoinResult.MatchStarted: return "이미 매치가 시작되었다.";
                default: return "";
            }
        }

        // Shown when no answer came at all: wrong address, wrong port, nobody hosting, a firewall.
        public const string Unreachable = "응답이 없다. 주소와 포트, 호스트가 방을 열었는지 확인하자.";
        public const string Dropped = "호스트와의 연결이 끊어졌다.";

        // Bots that fill the match to `target` participants (the single-player size, MatchRules.BotCount + 1).
        public static int DefaultBots(int humans, int target) => ClampBots(target - Math.Max(1, humans), humans);

        // 0 .. what still fits under MaxParticipants.
        public static int ClampBots(int bots, int humans) => Math.Max(0, Math.Min(bots, MaxParticipants - Math.Max(1, humans)));

        // "host:port" or "host" (default port); false when the port is not 1..65535 or the host is empty.
        public static bool TryParseAddress(string text, ushort defaultPort, out string host, out ushort port)
        {
            host = "";
            port = defaultPort;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            int colon = text.LastIndexOf(':');
            if (colon < 0) { host = text; return true; }
            host = text.Substring(0, colon).Trim();
            if (host.Length == 0) return false;
            return ushort.TryParse(text.Substring(colon + 1).Trim(), out port) && port != 0;
        }
    }

    public struct LobbyMember
    {
        public int Id;
        public string Name;
        public bool Ready;
    }

    // The people in a room. The host counts as ready; the match can start when everyone is.
    public sealed class LobbyRoster
    {
        readonly List<LobbyMember> _members = new List<LobbyMember>();

        public IReadOnlyList<LobbyMember> Members => _members;
        public int Count => _members.Count;
        public int HostId { get; set; } = -1;

        public void Clear() => _members.Clear();

        public bool Contains(int id) => IndexOf(id) >= 0;

        public void Add(int id, string name, bool ready = false)
        {
            if (Contains(id)) return;
            _members.Add(new LobbyMember { Id = id, Name = string.IsNullOrWhiteSpace(name) ? "플레이어 " + (id + 1) : name.Trim(), Ready = ready });
        }

        public void Remove(int id)
        {
            int i = IndexOf(id);
            if (i >= 0) _members.RemoveAt(i);
        }

        public void SetReady(int id, bool ready)
        {
            int i = IndexOf(id);
            if (i < 0) return;
            var m = _members[i];
            m.Ready = ready;
            _members[i] = m;
        }

        public bool IsReady(int id)
        {
            int i = IndexOf(id);
            return i >= 0 && (id == HostId || _members[i].Ready);
        }

        public bool AllReady
        {
            get
            {
                if (_members.Count == 0) return false;
                foreach (var m in _members) if (m.Id != HostId && !m.Ready) return false;
                return true;
            }
        }

        int IndexOf(int id)
        {
            for (int i = 0; i < _members.Count; i++) if (_members[i].Id == id) return i;
            return -1;
        }
    }
}
