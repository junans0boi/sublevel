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

        // ---- Bots (ADR-079) ----

        // A bot in an announced region rides out when a running station is nearer than its way out on foot.
        // `stationMeters` < 0: no free station in this region; `exitMeters` < 0: no way out on foot.
        public static bool BotShouldRide(int here, IReadOnlyList<ZoneState> states, float stationMeters, float exitMeters, float cooldownLeft)
        {
            if (states == null || here < 0 || here >= states.Count || states[here] != ZoneState.Warning) return false;
            if (cooldownLeft > 0f || stationMeters < 0f) return false;
            if (exitMeters >= 0f && stationMeters >= exitMeters) return false;
            for (int r = 0; r < states.Count; r++) if (CanRide(here, r, states)) return true;
            return false;
        }

        // Where a bot rides: the final region once it is named and reachable, else the first safe region; -1 none.
        public static int BotDestination(int here, IReadOnlyList<ZoneState> states, int finalRegion)
        {
            if (CanRide(here, finalRegion, states)) return finalRegion;
            if (states == null) return -1;
            for (int r = 0; r < states.Count; r++) if (CanRide(here, r, states)) return r;
            return -1;
        }
    }

    public enum ElevatorCycleKind : byte { Depart = 0, Arrive = 1 }

    // One station's part of a ride (ADR-079), as a pure function of the seconds since it began, so every peer moves
    // doors, car and riders the same way from one replicated start. Depart: the doors close, the car goes down
    // with its riders, comes back up empty behind the shut doors, the doors open. Arrive: the doors close, the car
    // drops out of sight, comes up with the riders, the doors open. The rider is handed from the departure car to
    // the arrival car at the bottom (SwitchAt); the whole ride lasts about DeploymentRules.TravelSeconds.
    public static class ElevatorCycle
    {
        public static float DoorSeconds(DeploymentRules r) => Math.Max(0.05f, r.DoorSlideSeconds);
        public static float MoveSeconds(DeploymentRules r) => Math.Max(0.3f, (r.TravelSeconds - 3f * DoorSeconds(r)) / 2f);
        // One station's cycle: close, move, open.
        public static float Length(DeploymentRules r) => 2f * DoorSeconds(r) + MoveSeconds(r);
        // Seconds from the start of the ride until the rider changes cars at the bottom (the arrival cycle starts).
        public static float SwitchAt(DeploymentRules r) => DoorSeconds(r) + MoveSeconds(r);
        // Start of the ride -> the arrival doors have opened.
        public static float RideSeconds(DeploymentRules r) => SwitchAt(r) + Length(r);

        public static bool Active(double t, DeploymentRules r) => t >= 0.0 && t < Length(r);
        public static bool Moving(double t, DeploymentRules r) => t >= DoorSeconds(r) && t < DoorSeconds(r) + MoveSeconds(r);

        // 0 = open, 1 = shut.
        public static float DoorClosed01(double t, DeploymentRules r)
        {
            float d = DoorSeconds(r), m = MoveSeconds(r);
            if (t <= 0.0 || t >= Length(r)) return 0f;
            if (t < d) return (float)(t / d);
            if (t < d + m) return 1f;
            return 1f - (float)((t - d - m) / d);
        }

        // Metres the car floor is below the station floor.
        public static float CarOffset(ElevatorCycleKind kind, double t, DeploymentRules r)
        {
            float d = DoorSeconds(r), m = MoveSeconds(r), depth = Math.Max(0f, r.CarStartDepth);
            if (t < d || t >= d + m) return 0f;
            float done = ElevatorRise.Travel((float)((t - d) / m), r.RiseAccelFraction);
            return kind == ElevatorCycleKind.Depart ? depth * done : depth * (1f - done);
        }

        // Metres below the station floor for the rider itself: it stays at the bottom while the empty departure car
        // goes back up and until the arrival car starts to rise. `t` counts from the start of the station's cycle
        // it is in (departure before the switch, arrival after).
        public static float RiderOffset(bool arrived, double t, DeploymentRules r)
        {
            float d = DoorSeconds(r), m = MoveSeconds(r);
            if (!arrived) return t >= d + m ? Math.Max(0f, r.CarStartDepth) : CarOffset(ElevatorCycleKind.Depart, t, r);
            return t < d ? Math.Max(0f, r.CarStartDepth) : CarOffset(ElevatorCycleKind.Arrive, t, r);
        }
    }
}
