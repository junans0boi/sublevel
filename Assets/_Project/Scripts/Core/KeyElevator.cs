using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Where the 6-8 elevator keys of a match lie (ADR-170). Drawn from the match seed; early-closing regions get
    // fewer keys (pollution_sim_notes.md: weight x (1 - bias * (5 - k) / 5) for the region closing k-th, 1-based).
    public struct KeyPlacement
    {
        public int Region;       // where the key is: a loot point in the region, or the supply crate dropped there
        public bool InSupplyCrate;
    }

    public static class KeyPlan
    {
        // `order`: PollutionOrder.Plan result. `regionWeight`: how much loot each region has (area, loot points);
        // null = equal. Deterministic in the seed.
        public static KeyPlacement[] Plan(int seed, int[] order, IReadOnlyList<float> regionWeight, PollutionRules rules = null)
        {
            rules = rules ?? PollutionRules.Current;
            uint rng = unchecked((uint)seed * 2246822519u + 0x2545F491u);
            int count = rules.KeysMin + (int)(PollutionOrder.Next(ref rng) * (rules.KeysMax - rules.KeysMin + 1));
            int n = order.Length;
            var w = new double[n];
            for (int k = 0; k < n; k++)
            {
                int region = order[k];
                double baseW = regionWeight != null && region < regionWeight.Count ? Math.Max(0f, regionWeight[region]) : 1.0;
                // k is 0-based here: the first closing is 1-based 1 -> (5 - 1) / 5. The final region and the 5th are 1.
                double early = Math.Max(0.0, Math.Min(1.0, (n - 2 - k) / (double)Math.Max(1, n - 1)));
                w[region] = baseW * (1.0 - rules.KeyBias * early);
            }
            double total = 0;
            for (int i = 0; i < n; i++) total += w[i];
            var keys = new KeyPlacement[count];
            int crates = Math.Min(count, rules.KeysFromSupplyCrates + (PollutionOrder.Next(ref rng) < 0.5 ? 1 : 0));
            for (int i = 0; i < count; i++)
            {
                double x = PollutionOrder.Next(ref rng) * total, acc = 0;
                int pick = n - 1;
                for (int r = 0; r < n; r++) { acc += w[r]; if (x < acc) { pick = r; break; } }
                keys[i] = new KeyPlacement { Region = pick, InSupplyCrate = i < crates };
            }
            return keys;
        }
    }

    // Team cooldown after a key ride (ADR-170 5). Solo players pass a team id of their own.
    public sealed class KeyCooldowns
    {
        readonly Dictionary<int, double> _readyAt = new Dictionary<int, double>();
        public bool CanUse(int team, double now) => !_readyAt.TryGetValue(team, out var t) || now >= t;
        public float Left(int team, double now) => _readyAt.TryGetValue(team, out var t) ? (float)Math.Max(0.0, t - now) : 0f;
        public void Start(int team, double now, PollutionRules rules) => _readyAt[team] = now + rules.TeamCooldownSeconds;
    }

    public enum KeyPhase : byte { Idle = 0, DoorOpen = 1, Selecting = 2, Riding = 3, Recharging = 4 }
    public enum KeyEvent : byte { None = 0, SelectionStarted, Voided, RideEnded, Recharged }
    public enum KeyResult : byte { Ok, Busy, NoKey, TeamCooldown, NotHolder, HolderNotAboard, CarFull, NotAboard, BadDestination, WrongPhase }

    // One station's key elevator (ADR-170 / SPEC 5-2), a pure state machine on server time: insert the key -> doors
    // open 30 s, anybody boards (4 at most) -> the holder closes the doors or the time runs out -> the holder picks
    // a non-contaminated region within 15 s (else the use is void: the key comes back, the station recharges) ->
    // the ride. A station in a contaminated region is dead for RechargeSeconds after a use. No arrival protection.
    public sealed class KeyElevatorStation
    {
        public KeyPhase Phase { get; private set; }
        public int HolderId { get; private set; } = -1;
        public int HolderTeam { get; private set; }
        public int Destination { get; private set; } = -1;
        public double PhaseEnd { get; private set; }
        public bool KeyRefunded { get; private set; }   // set by a Voided event: the holder gets the key back
        public readonly List<int> Riders = new List<int>();

        bool _recharge;

        // Seconds left in the current phase (0 when idle).
        public float PhaseLeft(double now) => Phase == KeyPhase.Idle ? 0f : (float)Math.Max(0.0, PhaseEnd - now);
        public bool IsAboard(int id) => Riders.Contains(id);

        // `stationRegionState`: a contaminated region makes the station recharge after the use.
        public KeyResult Insert(double now, int holderId, int team, bool hasKey, KeyCooldowns cooldowns, PollutionState stationRegionState, PollutionRules rules)
        {
            if (Phase != KeyPhase.Idle) return KeyResult.Busy;
            if (!hasKey) return KeyResult.NoKey;
            if (cooldowns != null && !cooldowns.CanUse(team, now)) return KeyResult.TeamCooldown;
            Phase = KeyPhase.DoorOpen;
            PhaseEnd = now + rules.DoorOpenSeconds;
            HolderId = holderId; HolderTeam = team; Destination = -1; KeyRefunded = false;
            _recharge = stationRegionState == PollutionState.Contaminated;
            Riders.Clear();
            return KeyResult.Ok;
        }

        // Anybody in reach while the doors are open, the holder's enemies included.
        public KeyResult Board(int id, PollutionRules rules)
        {
            if (Phase != KeyPhase.DoorOpen) return KeyResult.WrongPhase;
            if (Riders.Contains(id)) return KeyResult.Ok;
            if (Riders.Count >= rules.CarMax) return KeyResult.CarFull;
            Riders.Add(id);
            return KeyResult.Ok;
        }

        public KeyResult Alight(int id)
        {
            if (Phase != KeyPhase.DoorOpen) return KeyResult.WrongPhase;
            return Riders.Remove(id) ? KeyResult.Ok : KeyResult.NotAboard;
        }

        // The holder shuts the doors early; the choice phase starts.
        public KeyResult CloseDoors(double now, int id, PollutionRules rules)
        {
            if (Phase != KeyPhase.DoorOpen) return KeyResult.WrongPhase;
            if (id != HolderId) return KeyResult.NotHolder;
            if (!Riders.Contains(id)) return KeyResult.HolderNotAboard;
            BeginSelecting(now, rules);
            return KeyResult.Ok;
        }

        // The holder picks the region; only a non-contaminated one other than here is accepted. The key is used up,
        // the team cooldown starts and the ride begins (the caller moves the riders after `rideSeconds`).
        public KeyResult Select(double now, int id, int region, int fromRegion, IReadOnlyList<PollutionState> states,
            float rideSeconds, KeyCooldowns cooldowns, PollutionRules rules)
        {
            if (Phase != KeyPhase.Selecting) return KeyResult.WrongPhase;
            if (id != HolderId) return KeyResult.NotHolder;
            if (!KeyElevatorTravel.CanChoose(region, fromRegion, states)) return KeyResult.BadDestination;
            Destination = region;
            Phase = KeyPhase.Riding;
            PhaseEnd = now + Math.Max(0f, rideSeconds);
            cooldowns?.Start(HolderTeam, now, rules);
            return KeyResult.Ok;
        }

        // Timeouts. Call every server tick; returns what just happened.
        public KeyEvent Tick(double now, PollutionRules rules)
        {
            if (Phase == KeyPhase.Idle || now < PhaseEnd) return KeyEvent.None;
            switch (Phase)
            {
                case KeyPhase.DoorOpen:
                    if (!Riders.Contains(HolderId)) return Void(now, rules);  // nobody can choose: void
                    BeginSelecting(now, rules);
                    return KeyEvent.SelectionStarted;
                case KeyPhase.Selecting:
                    return Void(now, rules);
                case KeyPhase.Riding:
                    return AfterRide(now, rules);
                case KeyPhase.Recharging:
                    Reset();
                    return KeyEvent.Recharged;
            }
            return KeyEvent.None;
        }

        void BeginSelecting(double now, PollutionRules rules)
        {
            Phase = KeyPhase.Selecting;
            PhaseEnd = now + rules.SelectSeconds;
        }

        // The key goes back, the doors shut, the station recharges (a contaminated one for the full time).
        KeyEvent Void(double now, PollutionRules rules)
        {
            KeyRefunded = true;
            Riders.Clear();
            Phase = KeyPhase.Recharging;
            PhaseEnd = now + (_recharge ? rules.RechargeSeconds : 0.0);
            return KeyEvent.Voided;
        }

        KeyEvent AfterRide(double now, PollutionRules rules)
        {
            if (_recharge) { Phase = KeyPhase.Recharging; PhaseEnd = now + rules.RechargeSeconds; }
            else Reset();
            return KeyEvent.RideEnded;
        }

        void Reset()
        {
            Phase = KeyPhase.Idle; HolderId = -1; HolderTeam = 0; Destination = -1; Riders.Clear(); PhaseEnd = 0;
        }
    }

    public static class KeyElevatorTravel
    {
        // Only a region that is not contaminated (safe or announced) and is not the one the rider is in.
        public static bool CanChoose(int region, int from, IReadOnlyList<PollutionState> states) =>
            states != null && region >= 0 && region < states.Count && region != from && states[region] != PollutionState.Contaminated;

        public static void Destinations(int from, IReadOnlyList<PollutionState> states, List<int> result)
        {
            result.Clear();
            if (states == null) return;
            for (int r = 0; r < states.Count; r++) if (CanChoose(r, from, states)) result.Add(r);
        }

        // Where the car comes out: the destination unless it was confirmed during the ride; then the first other
        // region that is not contaminated (not the start); else the destination anyway (nothing is left).
        public static int ArrivalRegion(int from, int to, IReadOnlyList<PollutionState> states)
        {
            if (states == null || to < 0 || to >= states.Count || states[to] != PollutionState.Contaminated) return to;
            for (int r = 0; r < states.Count; r++)
                if (r != from && r != to && states[r] != PollutionState.Contaminated) return r;
            return to;
        }

        // A bot with a key in a region that is warned or about to be reached rides out toward the final region
        // when it is open, else the first open region. -1: nowhere to go.
        public static int BotDestination(int from, IReadOnlyList<PollutionState> states, int finalRegion)
        {
            if (CanChoose(finalRegion, from, states)) return finalRegion;
            if (states == null) return -1;
            for (int r = 0; r < states.Count; r++) if (CanChoose(r, from, states)) return r;
            return -1;
        }
    }
}
