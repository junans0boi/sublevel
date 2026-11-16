using System;
using Game.Core;
using Game.Net;

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
