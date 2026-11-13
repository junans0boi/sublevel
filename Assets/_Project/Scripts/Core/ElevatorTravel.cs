using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Riding an elevator to another region during the match (ADR-076, #76; ADR-017 hub: every station reaches
    // every region). Pure rules; Game.Net applies them on the server and the HUD shows them.
    public static class ElevatorTravel
    {
        // A region's elevators run while it is safe or announced (a way out); from the lockdown on they are off.
        public static bool Runs(ZoneState state) => state == ZoneState.Safe || state == ZoneState.Warning;

        // Only safe regions take arrivals: riding into an announced region makes no sense.
        public static bool Accepts(ZoneState state) => state == ZoneState.Safe;

        public static bool CanRide(int from, int to, IReadOnlyList<ZoneState> states)
        {
            if (states == null || from < 0 || to < 0 || from >= states.Count || to >= states.Count || from == to) return false;
            return Runs(states[from]) && Accepts(states[to]);
        }

        // The regions a rider in `from` may choose, in region order (empty when the elevators of `from` are off).
        public static void Destinations(int from, IReadOnlyList<ZoneState> states, List<int> result)
        {
            result.Clear();
            if (states == null) return;
            for (int r = 0; r < states.Count; r++)
                if (CanRide(from, r, states)) result.Add(r);
        }

        // Where the car comes out, decided at the arrival: the destination while it is still safe; if it was
        // announced on the way, the first other safe region (not the start); else the start if it is safe; else
        // the destination anyway (nothing safe is left, the match is about to end).
        public static int ArrivalRegion(int from, int to, IReadOnlyList<ZoneState> states)
        {
            if (states == null || to < 0 || to >= states.Count || Accepts(states[to])) return to;
            for (int r = 0; r < states.Count; r++)
                if (r != from && r != to && Accepts(states[r])) return r;
            if (from >= 0 && from < states.Count && Accepts(states[from])) return from;
            return to;
        }

        // Seconds a combatant still waits before its next ride. `arrivedAt` is NegativeInfinity before the first.
        public static float CooldownLeft(double now, double arrivedAt, DeploymentRules rules)
        {
            if (double.IsInfinity(arrivedAt)) return 0f;
            return (float)Math.Max(0.0, arrivedAt + rules.TravelCooldownSeconds - now);
        }

        // A random station of `region` (index into regionOfStation), or -1 when it has none.
        public static int ArrivalStation(int region, IReadOnlyList<int> regionOfStation, Random rng)
        {
            int count = 0;
            for (int i = 0; i < regionOfStation.Count; i++) if (regionOfStation[i] == region) count++;
            if (count == 0) return -1;
            int pick = rng.Next(count);
            for (int i = 0; i < regionOfStation.Count; i++)
                if (regionOfStation[i] == region && pick-- == 0) return i;
            return -1;
        }

        // F tapped: the choice after `current` (wrapping), or the first when `current` is not a choice any more.
        public static int NextDestination(int current, IReadOnlyList<int> choices)
        {
            if (choices.Count == 0) return -1;
            for (int i = 0; i < choices.Count; i++)
                if (choices[i] == current) return choices[(i + 1) % choices.Count];
            return choices[0];
        }

        // The selection as the choices change: kept while still a choice, else the first one (or none).
        public static int KeepSelection(int current, IReadOnlyList<int> choices)
        {
            if (choices.Count == 0) return -1;
            for (int i = 0; i < choices.Count; i++) if (choices[i] == current) return current;
            return choices[0];
        }
    }
}
