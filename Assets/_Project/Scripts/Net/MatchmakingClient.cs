using System;
using Game.Core;

namespace Game.Net
{
    // The one entry point the lobby UI uses for matchmaking (ADR-258): connect a LobbyNet to a dedicated server (Join), then
    // RequestStart(mode, partySize); the server's queue answers through State (matching N/target, seconds left) and finally the
    // lobby's start message (LobbyNet.MatchStarting). A host/single start does not queue: the menu starts at once with bots.
    public sealed class MatchmakingClient
    {
        readonly LobbyNet _lobby;

        public MatchmakingClient(LobbyNet lobby)
        {
            _lobby = lobby;
            _lobby.Changed += () => StateChanged?.Invoke(State);
        }

        // What the server last said about the queue (all zero before the first message).
        public MatchQueueState State => _lobby.Queue;

        // True while this client waits in the queue (the server counts people; the UI cannot tell whose ticket is whose).
        public bool Waiting { get; private set; }

        // Raised whenever the queue state or the room changed.
        public event Action<MatchQueueState> StateChanged;

        // Asks for a place: partySize 1..4 (solo mode ignores it). False when the lobby is not connected yet.
        public bool RequestStart(TeamMode mode, int partySize = 1)
        {
            Waiting = _lobby.RequestMatch(mode, partySize);
            return Waiting;
        }

        public bool Cancel()
        {
            Waiting = false;
            return _lobby.CancelMatch();
        }
    }
}
