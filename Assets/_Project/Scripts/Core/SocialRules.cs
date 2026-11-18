namespace Game.Core
{
    // Backend error code -> sentence for the friends and party UI (docs/social-api.md). Pure.
    public static class SocialRules
    {
        public const int MaxPartySize = 4;

        public static string ErrorText(int status, string error)
        {
            switch (error)
            {
                case "player_not_found": return "그 닉네임의 플레이어가 없다.";
                case "cannot_add_self": return "자기 자신은 추가할 수 없다.";
                case "already_friends": return "이미 친구다.";
                case "already_requested": return "이미 요청을 보냈다.";
                case "request_not_found": return "요청이 없거나 취소되었다.";
                case "not_friends": return "친구만 파티에 초대할 수 있다.";
                case "not_party_leader": return "파티장만 할 수 있다.";
                case "already_member": return "이미 파티에 있다.";
                case "party_full": return "파티가 가득 찼다 (최대 " + MaxPartySize + "명).";
                case "invite_not_found": return "초대가 만료되었다.";
                case "leave_current_party_first": return "먼저 지금 파티를 나가야 한다.";
                case "already_queued": return "매칭 대기 중에는 바꿀 수 없다.";
                case "not_in_party": return "파티에 속해 있지 않다.";
            }
            return TicketRules.ErrorText(status, error);
        }

        // "lobby" / "in_match" / "offline" -> label.
        public static string StatusText(string status)
        {
            switch (status) { case "lobby": return "로비"; case "in_match": return "판 중"; default: return "오프라인"; }
        }
    }
}
