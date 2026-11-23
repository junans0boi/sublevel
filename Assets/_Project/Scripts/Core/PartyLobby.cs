using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum Perspective : byte { Third = 0, First = 1 }

    public enum LobbyAction : byte { Start, CancelMatch, Ready, Unready, LeaveParty }

    // What the one big button of the lobby control panel shows and does (adopted.html: ready/start/cancel in one button).
    public struct StatusButton
    {
        public LobbyAction Action;
        public string Label;
        public bool Enabled;
        public string Reason; // why Enabled is false; "" otherwise
    }

    // Lobby party state for the adopted lobby (ADR-245): mode and perspective are the leader's, members lock to them and
    // press ready, the leader starts when everyone is ready, the leader leaving hands the party to the next member and
    // keeps the matching going. Pure; the network side (LobbyNet) feeds it and shows what it says.
    public sealed class PartyLobby
    {
        public const int SquadSize = 4;

        readonly List<LobbyMember> _members = new List<LobbyMember>();

        public IReadOnlyList<LobbyMember> Members => _members;
        public int LeaderId { get; private set; } = -1;
        public TeamMode Mode { get; private set; } = TeamMode.Solo;
        public Perspective View { get; private set; } = Perspective.Third;
        public bool Matching { get; private set; }

        public int Count => _members.Count;
        public bool IsLeader(int id) => id == LeaderId;

        public PartyLobby(int selfId, string selfName, PlayerAppearance appearance = default) { _members.Add(new LobbyMember { Id = selfId, Name = selfName, Appearance = appearance.Normalized() }); LeaderId = selfId; }

        // A member's look changed (their sync message arrived, or the local player edited theirs).
        public void SetAppearance(int id, PlayerAppearance appearance)
        {
            int i = Index(id);
            if (i < 0) return;
            var m = _members[i]; m.Appearance = appearance.Normalized(); _members[i] = m;
        }

        // Someone joins: the party becomes a squad (mode locks for members). Refused when full or matching.
        public bool Join(int id, string name, PlayerAppearance appearance = default)
        {
            if (Matching || _members.Count >= SquadSize || Index(id) >= 0) return false;
            _members.Add(new LobbyMember { Id = id, Name = string.IsNullOrWhiteSpace(name) ? "플레이어" : name.Trim(), Appearance = appearance.Normalized() });
            Mode = TeamMode.Squad;
            return true;
        }

        // Leaving while matching keeps the match going without that person; the leader handing over: the next member leads.
        public void Leave(int id)
        {
            int i = Index(id);
            if (i < 0) return;
            _members.RemoveAt(i);
            if (_members.Count == 0) { LeaderId = -1; Matching = false; return; }
            if (id == LeaderId) LeaderId = _members[0].Id;
            if (_members.Count == 1 && !Matching) Mode = TeamMode.Solo;
        }

        public void SetReady(int id, bool ready)
        {
            int i = Index(id);
            if (i < 0 || id == LeaderId || Matching) return;
            var m = _members[i]; m.Ready = ready; _members[i] = m;
        }

        // Leader only, not while matching; the party must fit one team of that mode (solo needs an empty party).
        public bool SetMode(int by, TeamMode mode)
        {
            if (by != LeaderId || Matching || _members.Count > TeamRules.Current.SizeOf(mode)) return false;
            Mode = mode;
            return true;
        }

        public bool SetView(int by, Perspective view)
        {
            if (by != LeaderId || Matching) return false;
            View = view;
            return true;
        }

        public bool CanChange(int id) => id == LeaderId && !Matching;

        public IEnumerable<string> NotReady()
        {
            foreach (var m in _members) if (m.Id != LeaderId && !m.Ready) yield return m.Name;
        }

        // "서아 님이 준비 중입니다…" / "서아 님 외 1명이 준비 중입니다…"; "" when everyone is ready.
        public string WaitingLine()
        {
            string first = null; int n = 0;
            foreach (var name in NotReady()) { if (first == null) first = name; n++; }
            if (n == 0) return "";
            return n == 1 ? first + " 님이 준비 중입니다…" : first + " 님 외 " + (n - 1) + "명이 준비 중입니다…";
        }

        public bool Start(int by)
        {
            if (by != LeaderId || Matching || WaitingLine().Length > 0 || TooBig().Length > 0) return false;
            Matching = true;
            return true;
        }

        public bool CancelMatch(int by)
        {
            if (by != LeaderId || !Matching) return false;
            Matching = false;
            return true;
        }

        // Matching has ended in a match (loading begins): the party stays together, ready marks clear.
        public void MatchFound()
        {
            Matching = false;
            for (int i = 0; i < _members.Count; i++) { var m = _members[i]; m.Ready = false; _members[i] = m; }
        }

        // `matchSeconds` is the time spent matching, for the leader's cancel label.
        public StatusButton ButtonFor(int id, int matchSeconds = 0)
        {
            int i = Index(id);
            if (i < 0) return new StatusButton { Action = LobbyAction.LeaveParty, Label = "", Enabled = false, Reason = "파티에 속해 있지 않습니다." };
            if (id != LeaderId)
            {
                if (Matching) return new StatusButton { Action = LobbyAction.LeaveParty, Label = "파티 나가기", Enabled = true, Reason = "" };
                bool ready = _members[i].Ready;
                return new StatusButton { Action = ready ? LobbyAction.Unready : LobbyAction.Ready, Label = ready ? "✓ 준비 완료 · 취소" : "준비", Enabled = true, Reason = "" };
            }
            if (Matching)
                return new StatusButton { Action = LobbyAction.CancelMatch, Label = "매칭 취소 · " + Clock(matchSeconds), Enabled = true, Reason = "" };
            string wait = TooBig();
            if (wait.Length == 0) wait = WaitingLine();
            return new StatusButton { Action = LobbyAction.Start, Label = _members.Count == 1 ? "혼자 시작" : "파티 시작", Enabled = wait.Length == 0, Reason = wait };
        }

        // #499: a party bigger than one team of its mode would push the next team's places; "" when it fits.
        string TooBig()
        {
            int team = TeamRules.Current.SizeOf(Mode);
            return _members.Count > team ? "한 팀은 최대 " + team + "명입니다." : "";
        }

        public static string Clock(int seconds)
        {
            seconds = Math.Max(0, seconds);
            return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        int Index(int id)
        {
            for (int i = 0; i < _members.Count; i++) if (_members[i].Id == id) return i;
            return -1;
        }
    }

    // The assembly area before deployment (ADR-245, proposed numbers): the countdown starts once enough people are in,
    // or when the longest wait ends (bots fill the rest).
    public static class AssemblyRules
    {
        public const int Capacity = 50;
        public const int MinToStart = 30;
        public const int CountdownSeconds = 30;
        public const int MaxWaitSeconds = 120;

        // Seconds left before deployment, or -1 while still waiting. `inArea` people, `waited` seconds since the first
        // arrival, `countdownStarted` seconds since the minimum was reached (-1 if not reached yet).
        public static int Remaining(int inArea, float waited, float countdownStarted)
        {
            if (countdownStarted >= 0f) return Math.Max(0, (int)Math.Ceiling(CountdownSeconds - countdownStarted));
            if (inArea >= Capacity) return 0;
            if (waited >= MaxWaitSeconds) return 0;
            return -1;
        }

        public static bool ShouldStartCountdown(int inArea) => inArea >= MinToStart;

        // Bots that fill the match to Capacity.
        public static int Bots(int humans) => Math.Max(0, Capacity - humans);
    }
}
