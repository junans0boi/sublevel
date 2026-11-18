namespace Game.Core
{
    // What the lobby says while the backend's matchmaking ticket waits (#392, sublevel-backend docs/match-api.md). Pure.
    public static class TicketRules
    {
        public const double PollSeconds = 2.0;        // the backend expires a ticket nobody polled for 45s
        public const string ServersBusy = "servers_busy";

        public static string QueueText(string reason, int position, int fillInSeconds, int players)
        {
            if (reason == ServersBusy) return "지금 서버가 가득 찼다. 순서를 기다리는 중 (" + System.Math.Max(1, position) + "번째)";
            if (fillInSeconds > 0) return "인원을 모으는 중 (" + players + "명) · " + fillInSeconds + "초 뒤 봇으로 채워 시작";
            return "인원을 모으는 중 (" + players + "명)";
        }

        // Backend error code -> sentence for the player.
        public static string ErrorText(int status, string error)
        {
            switch (error)
            {
                case "not_party_leader": return "파티장만 매칭을 시작할 수 있다.";
                case "already_queued": return "이미 대기 중인 사람이 있다.";
                case "nickname_required": return "닉네임을 먼저 정해야 한다. 사이트에서 설정하자.";
                case "not_cancellable": return "이미 끝난 매칭이다.";
            }
            if (status == 401) return "로그인이 만료되었다. 런처에서 다시 실행하자.";
            if (status == 0) return "매칭 서버에 연결하지 못했다.";
            return "매칭에 실패했다 (" + status + ").";
        }
    }
}
