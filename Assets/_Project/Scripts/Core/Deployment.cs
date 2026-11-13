using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Timing of the elevator start (ADR-055 (2)(3)): the door opens a moment after the match starts, and whoever
    // came up in an elevator is protected for a while after that (until their first shot or throw).
    [Serializable]
    public class DeploymentRules
    {
        // Set from data when there is any; tests may replace it.
        public static DeploymentRules Current = new DeploymentRules();

        public float DoorDelaySeconds = 3f;   // match start -> the doors start to open; no moving or firing before
        public float ProtectionSeconds = 6f;  // after the doors open; the first shot or throw ends it early
        public float DoorSlideSeconds = 1.2f; // how long the door takes to sink into the floor
        public float DoorThickness = 0.12f;   // metres; the door fills the station's front opening
        public float DoorWallOverlap = 0.1f;  // metres the door reaches into the side walls on each side

        // The car comes up from underground (ADR-065 (1)). It rises during the last RiseSeconds before the doors
        // open (never longer than DoorDelaySeconds, so it never jumps), stops ArriveSettleSeconds before they open
        // and bounces once while it settles. Riders stand in it and cannot move.
        public float RiseSeconds = 3f;          // the same as DoorDelaySeconds by default: the whole wait is the ride
        public float CarStartDepth = 12f;       // metres the car floor starts below the station floor
        public float ShaftDepth = 13f;          // metres from the station floor to the shaft's pit floor
        public float ArriveSettleSeconds = 0.4f;
        public float RiseAccelFraction = 0.25f; // share of the travel spent speeding up (and again slowing down)
        public float BounceMeters = 0.03f;      // the overshoot when the car stops
        public float ShakeMeters = 0.006f;      // vertical judder at full speed

        // Riding to another region during the match (ADR-076, #76).
        public float TravelSeconds = 8f;          // request accepted -> standing in the arrival station
        public float TravelCooldownSeconds = 60f; // per combatant, from its arrival
        public float TravelHoldSeconds = 0.8f;    // F held this long in a station starts the ride (a tap changes the destination)
        public float StationRadius = 1.6f;        // flat metres from a station's spawn point that count as inside it
    }

    public enum ElevatorPhase { Waiting, Rising, Settling, Open }

    // Where the elevator car is, as a pure function of the time left until the doors open (ADR-065 (1)), so every
    // peer moves its cars and riders the same way from the one replicated opening time. `secondsToOpen` is
    // PositiveInfinity while the opening is not scheduled yet (the car waits at the bottom).
    public static class ElevatorRise
    {
        // Seconds of actual travel (the settle comes after it).
        public static float TravelSeconds(DeploymentRules r) =>
            Math.Max(0.05f, Math.Min(r.RiseSeconds, r.DoorDelaySeconds) - SettleSeconds(r));

        public static float SettleSeconds(DeploymentRules r) =>
            Math.Max(0f, Math.Min(r.ArriveSettleSeconds, Math.Min(r.RiseSeconds, r.DoorDelaySeconds) * 0.5f));

        public static ElevatorPhase Phase(double secondsToOpen, DeploymentRules r)
        {
            if (double.IsNaN(secondsToOpen) || secondsToOpen <= 0.0) return ElevatorPhase.Open;
            float settle = SettleSeconds(r);
            if (secondsToOpen <= settle) return ElevatorPhase.Settling;
            return secondsToOpen < settle + TravelSeconds(r) ? ElevatorPhase.Rising : ElevatorPhase.Waiting;
        }

        // Metres the car floor is below the station floor: CarStartDepth while waiting, 0 once the doors open,
        // briefly negative (a little above) in the arrival bounce.
        public static float Offset(double secondsToOpen, DeploymentRules r)
        {
            float depth = Math.Max(0f, r.CarStartDepth);
            float settle = SettleSeconds(r), travel = TravelSeconds(r);
            switch (Phase(secondsToOpen, r))
            {
                case ElevatorPhase.Waiting: return depth;
                case ElevatorPhase.Rising:
                {
                    float u = 1f - (float)((secondsToOpen - settle) / travel);
                    float envelope = (float)Math.Sin(Math.PI * Clamp01(u));
                    float judder = (float)(Math.Sin(secondsToOpen * 47.0) + 0.6 * Math.Sin(secondsToOpen * 83.0 + 1.3)) / 1.6f;
                    return depth * (1f - Travel(u, r.RiseAccelFraction)) + r.ShakeMeters * envelope * judder;
                }
                case ElevatorPhase.Settling:
                {
                    float k = settle <= 0f ? 1f : 1f - (float)(secondsToOpen / settle); // 0 at the stop, 1 at the opening
                    return -r.BounceMeters * (float)Math.Sin(2.0 * Math.PI * k) * (1f - k);
                }
                default: return 0f;
            }
        }

        // 0..1 of the top speed (for the hum); 0 outside the travel.
        public static float Speed01(double secondsToOpen, DeploymentRules r)
        {
            if (Phase(secondsToOpen, r) != ElevatorPhase.Rising) return 0f;
            float u = 1f - (float)((secondsToOpen - SettleSeconds(r)) / TravelSeconds(r));
            float a = AccelFraction(r.RiseAccelFraction);
            return u < a ? u / a : u > 1f - a ? (1f - u) / a : 1f;
        }

        // Share of the way done at `u` (0..1 of the travel time): constant acceleration for the first `accel` share,
        // full speed, then the mirror image to a stop.
        public static float Travel(float u, float accel)
        {
            u = Clamp01(u);
            float a = AccelFraction(accel);
            float top = 1f / (1f - a);
            if (u < a) return top * u * u / (2f * a);
            if (u <= 1f - a) return top * (a * 0.5f + (u - a));
            float rest = 1f - u;
            return 1f - top * rest * rest / (2f * a);
        }

        static float AccelFraction(float a) => a < 0.01f ? 0.01f : a > 0.5f ? 0.5f : a;
        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    // How many take part in a match (#77, M4 gate: 16-24 combatants). Data: rules.json "Match".
    // Only maps with elevators use BotCount; maps without them (TestBox, ZoneD) keep one bot per scene spawn point.
    [Serializable]
    public class MatchRules
    {
        public const int MaxBotsLimit = 23;   // 24 participants with the player

        // Set from data when there is any; tests may replace it.
        public static MatchRules Current = new MatchRules();

        public int BotCount = 15;             // + the player = 16
        public int MaxBots = MaxBotsLimit;
        // People in one room (#160, ADR-124); bots fill the rest of the match.
        public int MaxPlayers = 16;

        // BotCount within 0..MaxBots (and never above MaxBotsLimit).
        public int ClampedBotCount => Math.Max(0, Math.Min(BotCount, Math.Min(Math.Max(0, MaxBots), MaxBotsLimit)));
    }

    // One elevator as the assignment sees it: the region it stands in (any id; equal ids = same region).
    public struct ElevatorSlot
    {
        public int Region;

        public ElevatorSlot(int region) { Region = region; }
    }

    // Which elevator each participant (players and bots alike) starts in (ADR-055 (1)(4)). Pure and deterministic:
    // the same participants, elevators and seed always give the same answer.
    //  - The regions take turns in an order shuffled by the seed, so a single player lands in a random region.
    //  - No region gets more than RegionCap(participants, regions) = ceil(n / regions) + 1.
    //  - One participant per elevator while any elevator (in a region under its cap) is free; after that elevators
    //    are shared, least occupied first. The cap wins over "one per elevator" if the two ever disagree.
    public static class Deployment
    {
        public static int RegionCap(int participants, int regions) =>
            regions <= 0 ? Math.Max(0, participants) : (Math.Max(0, participants) + regions - 1) / regions + 1;

        // The elevator index (into `elevators`) for each participant; -1 for everyone when there are no elevators.
        public static int[] Assign(int participants, IReadOnlyList<ElevatorSlot> elevators, int seed)
        {
            var result = new int[Math.Max(0, participants)];
            if (result.Length == 0) return result;
            if (elevators == null || elevators.Count == 0)
            {
                for (int i = 0; i < result.Length; i++) result[i] = -1;
                return result;
            }

            var rng = new Mix(seed);

            // Regions by id (sorted first so the input order of the ids does not matter), then shuffled.
            var ids = new List<int>();
            for (int e = 0; e < elevators.Count; e++)
                if (!ids.Contains(elevators[e].Region)) ids.Add(elevators[e].Region);
            ids.Sort();
            int regionCount = ids.Count;
            var members = new List<int>[regionCount];
            for (int r = 0; r < regionCount; r++) members[r] = new List<int>();
            for (int e = 0; e < elevators.Count; e++) members[ids.IndexOf(elevators[e].Region)].Add(e);
            var order = new int[regionCount];
            for (int r = 0; r < regionCount; r++) order[r] = r;
            Shuffle(order, rng);
            // Within a region the elevators are taken in a shuffled order too.
            for (int r = 0; r < regionCount; r++)
            {
                var list = members[r].ToArray();
                Shuffle(list, rng);
                members[r] = new List<int>(list);
            }

            int cap = RegionCap(result.Length, regionCount);
            var regionLoad = new int[regionCount];
            var elevatorLoad = new int[elevators.Count];
            int turn = 0;
            for (int p = 0; p < result.Length; p++)
            {
                // Pass 0: a free elevator in a region under its cap. Pass 1: the least occupied elevator in a region
                // under its cap. Pass 2 (cannot happen while cap * regions >= participants): the least occupied anywhere.
                int chosenRegion = -1, chosenElevator = -1;
                for (int pass = 0; pass < 3 && chosenRegion < 0; pass++)
                {
                    for (int k = 0; k < regionCount; k++)
                    {
                        int r = order[(turn + k) % regionCount];
                        if (pass < 2 && regionLoad[r] >= cap) continue;
                        int e = LeastOccupied(members[r], elevatorLoad);
                        if (pass == 0 && elevatorLoad[e] > 0) continue;
                        if (pass == 2 && chosenRegion >= 0 && elevatorLoad[e] >= elevatorLoad[chosenElevator]) continue;
                        chosenRegion = r;
                        chosenElevator = e;
                        turn = (turn + k + 1) % regionCount;
                        if (pass < 2) break;
                    }
                }
                regionLoad[chosenRegion]++;
                elevatorLoad[chosenElevator]++;
                result[p] = chosenElevator;
            }
            return result;
        }

        static int LeastOccupied(List<int> elevators, int[] load)
        {
            int best = elevators[0];
            for (int i = 1; i < elevators.Count; i++)
                if (load[elevators[i]] < load[best]) best = elevators[i];
            return best;
        }

        static void Shuffle(int[] items, Mix rng)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = items[i];
                items[i] = items[j];
                items[j] = t;
            }
        }

        // A small seeded generator of our own (SplitMix64), so the result never depends on a runtime's System.Random.
        sealed class Mix
        {
            ulong _state;

            public Mix(int seed) { _state = unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL); }

            public int Next(int bound)
            {
                unchecked
                {
                    ulong z = _state += 0x9E3779B97F4A7C15UL;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    z ^= z >> 31;
                    return (int)(z % (ulong)bound);
                }
            }
        }
    }
}
