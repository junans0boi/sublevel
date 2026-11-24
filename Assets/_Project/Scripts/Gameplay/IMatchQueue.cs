using System;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // What the lobby panel needs of matchmaking (MatchmakingClient is the real one; tests use a fake).
    public interface IMatchQueue
    {
        MatchQueueState State { get; }
        bool Waiting { get; }
        event Action<MatchQueueState> StateChanged;
        bool RequestStart(TeamMode mode, int partySize);
        bool Cancel();
        // A line that replaces the numbers of State when set (the backend's queue explains itself in words).
        string Note => null;
    }

    public sealed class MatchQueueAdapter : IMatchQueue
    {
        readonly MatchmakingClient _mm;
        public MatchQueueAdapter(MatchmakingClient mm) { _mm = mm; }
        public MatchQueueState State => _mm.State;
        public bool Waiting => _mm.Waiting;
        public event Action<MatchQueueState> StateChanged { add { _mm.StateChanged += value; } remove { _mm.StateChanged -= value; } }
        public bool RequestStart(TeamMode mode, int partySize) => _mm.RequestStart(mode, partySize);
        public bool Cancel() => _mm.Cancel();
    }
}

namespace Game.Gameplay
{
    // 참가 through the backend (#392): a ticket for the party, polled until a game server is assigned; then the menu joins that server
    // and its own queue starts the match (MainMenu.SyncQueue). The party size comes from the backend party, not the local slots.
    public sealed class BackendMatchQueue : IMatchQueue
    {
        readonly MatchTicketClient _ticket;
        readonly SocialClient _social;
        readonly Action<string, int, int> _joinServer; // host, port, party size
        readonly Func<LobbyNet> _lobby;                 // the lobby the join connects (a race asks its queue there)
        readonly Func<long> _me;                        // my account id (my car in a race ticket)
        int _size = 1;

        public BackendMatchQueue(MatchTicketClient ticket, SocialClient social, Action<string, int, int> joinServer, Func<LobbyNet> lobby = null, Func<long> me = null)
        {
            _ticket = ticket; _social = social; _joinServer = joinServer; _lobby = lobby; _me = me ?? (() => LobbyNet.AccountId);
            _ticket.Changed += () => StateChanged?.Invoke(State);
        }

        public MatchTicketClient.StateKind TicketState => _ticket.State;

        // Matched only names the server to join; the match itself starts in that server's queue, so matching goes on (never Started here).
        public MatchQueueState State => new MatchQueueState { Open = _ticket.State == MatchTicketClient.StateKind.Queued || _ticket.State == MatchTicketClient.StateKind.Matched };
        public bool Waiting => _ticket.State == MatchTicketClient.StateKind.Queued || _ticket.State == MatchTicketClient.StateKind.Matched;
        public string Note => _ticket.State == MatchTicketClient.StateKind.Idle ? null : _ticket.Text;
        public event Action<MatchQueueState> StateChanged;

        public bool RequestStart(TeamMode mode, int partySize)
        {
            _size = Math.Max(partySize, _social != null ? _social.PartySize : 1);
            _ticket.Start("battle-royale", Time.unscaledTimeAsDouble);
            return true;
        }

        public bool Cancel() { _ticket.Cancel(Time.unscaledTimeAsDouble); return true; }

        public void Tick(double now)
        {
            _ticket.Watching = _social != null && _social.Party != null;
            _ticket.Tick(now);
            if (_ticket.Mode == RaceMode) { TickRace(now); return; }
            if (_ticket.State != MatchTicketClient.StateKind.Matched) return;
            string host = _ticket.Host; int port = _ticket.Port;
            _size = Math.Max(_size, _social != null ? _social.PartySize : 1);
            _ticket.Consume();
            _joinServer(host, port, _size);
        }

        // ---- the race room's start (#521, ADR-562 (4)) ----
        // The host's 시작 makes the party's race ticket; every member (the host's own, the others adopting it) joins the assigned server
        // quietly and asks its queue for the race with the snapshot's course/laps/bots and their own car and paint; the race starts from
        // LobbyStart (RaceStarted). Any failure on the way gives the ticket up, so everyone's poll finds it gone and is back in the room too.
        public const string RaceMode = "race";
        (int evt, int laps, int bots, string car, int paint, int people)? _race;   // my lobby request, until it is sent
        bool _joined;

        public bool RaceStarting => _joined || (_ticket.Mode == RaceMode && Waiting);
        public event Action<string> RaceFailed;

        public void StartRace(double now) => _ticket.Start(RaceMode, now);

        // The host's cancel (with a toast) or leaving the room (null: none).
        public void CancelRace(double now, string toast)
        {
            if (!RaceStarting) return;
            Abort(now);
            if (toast != null) RaceFailed?.Invoke(toast);
        }

        // LobbyStart: the race is on; the ticket is done with (not adopted again).
        public void RaceStarted() { _joined = false; _race = null; _ticket.Consume(); }

        void Abort(double now)
        {
            _ticket.Cancel(now);
            if (_joined) _lobby?.Invoke()?.Shutdown();
            _joined = false; _race = null;
        }

        void Fail(double now, string text) { Abort(now); RaceFailed?.Invoke(text); }

        void TickRace(double now)
        {
            if (_ticket.State == MatchTicketClient.StateKind.Failed) { Fail(now, _ticket.Text); return; }
            if (_ticket.State != MatchTicketClient.StateKind.Matched) return;
            if (!_joined)
            {
                _race = RaceParty.QueueRequest(_ticket.Race, _me());
                if (_race == null) { Fail(now, "레이스 정보를 읽지 못했다."); return; }
                _joined = true;
                _joinServer(_ticket.Host, _ticket.Port, _race.Value.people);
                return;
            }
            var lobby = _lobby?.Invoke();
            if (lobby == null) return;
            if (lobby.Phase == LobbyNet.PhaseKind.Failed) { Fail(now, string.IsNullOrEmpty(lobby.FailureText) ? "서버에 접속하지 못했다." : lobby.FailureText); return; }
            if (_race == null || lobby.Phase != LobbyNet.PhaseKind.InLobby || lobby.IsHost) return;
            var r = _race.Value;
            _race = null;
            LobbyNet.QueuePartyId = _social?.Party?.id ?? 0;   // the server keeps one party's race together by it
            if (!lobby.RequestRace(r.people, r.evt, r.laps, r.bots, r.car, RaceLobbyScreen.RaceTint[r.paint])) Fail(now, "레이스 요청이 거절되었다.");
        }
    }
}
