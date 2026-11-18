using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum WaitingPhase { Waiting, Deploying }

    // The waiting room of one match as the server runs it (ADR-382, #415): people stand on the island (one standing place each,
    // the same one again when they come back), the countdown (ADR-380) runs, and when it ends everyone is sent to the elevators the
    // deployment gave them. Pure: the caller passes the clock and does the moving. The placing order is the deployment index, so
    // a party that joined together keeps its cars (the deployment decides cars by index and team, ADR-072).
    public sealed class WaitingFlow
    {
        readonly WaitingCountdown _countdown;
        readonly List<(float x, float z)> _spawns = WaitingIsland.SpawnPoints();
        readonly List<int> _order = new List<int>();                 // person ids in joining order (the deployment index of each)
        readonly Dictionary<int, int> _spot = new Dictionary<int, int>();
        readonly HashSet<int> _present = new HashSet<int>();
        int _bots;

        public WaitingPhase Phase { get; private set; } = WaitingPhase.Waiting;
        public CountdownState Last { get; private set; }
        public int People => _present.Count;
        public int Bots => _bots;
        public int Total => _present.Count + _bots;
        public IReadOnlyList<int> PlacingOrder => _order;

        public WaitingFlow(MatchRules rules, float startSecondsOverride = -1f) { _countdown = new WaitingCountdown(rules, startSecondsOverride); }

        public float MaxSeconds => _countdown.MaxSeconds;

        // A standing place for a waiting bot (key: any id not used by a person); the same place again for the same key. Not counted as a person.
        public (float x, float z)? PlaceBot(int key)
        {
            int botKey = int.MinValue + key;
            if (!_spot.TryGetValue(botKey, out int s))
            {
                var used = new HashSet<int>(_spot.Values);
                s = -1;
                for (int i = _spawns.Count - 1; i >= 0; i--) if (!used.Contains(i)) { s = i; break; } // bots take the far end, people the near one
                if (s < 0) return null;
                _spot[botKey] = s;
            }
            return _spawns[s];
        }

        public void FreeBot(int key) => _spot.Remove(int.MinValue + key);

        // Where this person stands: a free place on the island, the same one on a return. Null when the room has no free place.
        public (float x, float z)? Join(int id)
        {
            if (!_spot.TryGetValue(id, out int s))
            {
                var used = new HashSet<int>(_spot.Values);
                s = -1;
                for (int i = 0; i < _spawns.Count; i++) if (!used.Contains(i)) { s = i; break; }
                if (s < 0) return null;
                _spot[id] = s;
                _order.Add(id);
            }
            _present.Add(id);
            return _spawns[s];
        }

        // Gone for now (the place and the deployment index are kept for a return); the countdown sees one less.
        public void Leave(int id) => _present.Remove(id);

        // Waiting bots on the island (ADR-380 T5): they count towards a full room and the board.
        public void SetBots(int bots) => _bots = Math.Max(0, bots);

        // One tick. Returns true exactly once: when the people should now be sent to their elevators.
        public bool Tick(double now)
        {
            if (Phase == WaitingPhase.Deploying) return false;
            Last = _countdown.Update(now, People, _bots);
            if (!Last.Done) return false;
            Phase = WaitingPhase.Deploying;
            return true;
        }

        // The deployment index of this person (their place in the order they came), -1 if unknown.
        public int DeploymentIndex(int id) => _order.IndexOf(id);

        // Nothing hurts anyone on the island (fists, shots, blasts, falls, cars): the damage of one hit while `phase` runs (ADR-383, #416).
        public static float DamageIn(WaitingPhase phase, float amount) => phase == WaitingPhase.Waiting ? 0f : amount;

        // A stray's nearest legal place (server authority); the same point when it already stands inside.
        public static (float x, float z) Clamp(float x, float z) { WaitingIsland.Clamp(ref x, ref z); return (x, z); }
    }
}
