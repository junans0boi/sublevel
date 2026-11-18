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
        int _size = 1;

        public BackendMatchQueue(MatchTicketClient ticket, SocialClient social, Action<string, int, int> joinServer)
        {
            _ticket = ticket; _social = social; _joinServer = joinServer;
            _ticket.Changed += () => StateChanged?.Invoke(State);
        }

        public MatchQueueState State => new MatchQueueState { Open = _ticket.State == MatchTicketClient.StateKind.Queued, Started = _ticket.State == MatchTicketClient.StateKind.Matched };
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
            if (_ticket.State != MatchTicketClient.StateKind.Matched) return;
            string host = _ticket.Host; int port = _ticket.Port;
            _size = Math.Max(_size, _social != null ? _social.PartySize : 1);
            _ticket.Consume();
            _joinServer(host, port, _size);
        }
    }
}
