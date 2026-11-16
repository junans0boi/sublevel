namespace Game.Core
{
    // The lobby's words about matchmaking, from the server's real MatchQueueState only (nothing is invented).
    public static class QueueText
    {
        public const string NotConnected = "서버에 연결되지 않아 매칭을 시작할 수 없습니다.";
        public const string Requested = "매칭을 요청했습니다…";
        public const string Cancelled = "매칭을 취소했습니다.";

        // "" when this client is not waiting; "매칭 중 12/50 · 남은 00:24"; "매치를 만드는 중입니다…" once the match starts.
        public static string Line(MatchQueueState s, bool waiting)
        {
            if (!waiting) return "";
            if (s.Started) return "매치를 만드는 중입니다…";
            if (!s.Open) return Requested;
            int sec = (int)System.Math.Ceiling(System.Math.Max(0f, s.SecondsLeft));
            return "매칭 중 " + s.Waiting + "/" + s.Target + " · 남은 " + (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00");
        }
    }
}
