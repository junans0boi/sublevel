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
                case "party_full": return "빈 자리가 없습니다.";   // the race room's 8 less its closed slots (ADR-562); a battle royale ticket refuses 5+ itself
                case "invite_not_found": return "초대가 만료되었다.";
                case "leave_current_party_first": return "먼저 지금 파티를 나가야 한다.";
                case "already_queued": return "매칭 대기 중에는 바꿀 수 없다.";
                case "not_in_party": return "파티에 속해 있지 않다.";
                // the race room (#519)
                case "ready_locked": return "준비를 취소해야 차를 바꿀 수 있습니다.";
                case "slot_occupied": return "사람이 앉은 자리는 닫을 수 없습니다.";
                case "invalid_name": return "방 이름은 1~20자입니다.";
                case "sprint_laps": return "스프린트 코스는 1회만 달립니다.";
                case "cannot_kick_self": return "자기 자신은 내보낼 수 없습니다.";
                case "not_member": return "이미 방에 없는 사람입니다.";
            }
            return TicketRules.ErrorText(status, error);
        }

        // "lobby" / "in_match" / "offline" -> label.
        public static string StatusText(string status)
        {
            switch (status) { case "lobby": return "로비"; case "in_match": return "판 중"; default: return "오프라인"; }
        }

        // The race room's invite dialog (#520, mockup inviteHTML): a friend's presence as the row shows it ...
        public static string InviteStatus(string status) => status == "lobby" ? "온라인" : status == "in_match" ? "배틀로얄 중" : "오프라인";

        // ... and the row's button: its label and whether it sends an invite. Only an online friend not in the room and not yet invited
        // can be invited, and in a private room only by the host.
        public static (string label, bool enabled) InviteButton(string status, bool inRoom, bool sent, bool hostOnly)
        {
            if (inRoom) return ("방에 있음", false);
            if (sent) return ("초대 보냄", false);
            if (status == "in_match") return ("게임 중", false);
            if (status != "lobby") return ("오프라인", false);
            return ("초대", !hostOnly);
        }
    }
}
