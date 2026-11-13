using System;

namespace Game.Core
{
    // How a bot senses, decides and shoots (M2-2). One set of values = one difficulty. Distances in metres,
    // times in seconds, angles in degrees. Bots sense honestly: only what is inside these limits and not behind
    // a wall reaches the brain.
    [Serializable]
    public class BotRules
    {
        // The rules in use. Defaults until a data loader replaces them, so everything runs without a data file.
        public static BotRules Current = new BotRules();

        // ---- Seeing ----
        public float ViewDistance = 70f;
        public float FieldOfViewDeg = 110f;       // full cone, around where the bot faces
        public float CloseSenseRadius = 3f;       // this close a combatant is noticed whatever the facing
        public float SenseInterval = 0.15f;       // how often the (ray casting) senses run
        public bool TargetsPlayers = true;        // off: bots leave human players alone (tests, practice)

        // ---- Hearing ----
        public float GunshotHearingRadius = 80f;
        public float FootstepHearingRadius = 14f; // at full volume; a crouched walker is quieter by the stance's factor
        public float FootstepMinSpeed = 2f;       // slower than this makes no sound
        public float SlowWalkHearingFactor = 0.3f; // a slow walker (Ctrl, ADR-065 (3)) is heard only this share as far
        public float GlassHearingRadius = 40f;    // a window pane shattering

        // ---- Reacting and aiming ----
        public float ReactionSeconds = 0.45f;     // from first seeing a target to the first shot
        public float ReacquireSeconds = 1.5f;     // a target lost for longer than this has to be reacted to again
        public float TurnDegPerSecond = 240f;
        public float FireConeDeg = 12f;           // the bot shoots only when it faces the target this closely
        public float AimErrorNearDeg = 1.2f;      // largest aim error at AimNearMeters or closer...
        public float AimErrorFarDeg = 2.6f;       // ...and at AimFarMeters or farther
        public float AimNearMeters = 10f;
        public float AimFarMeters = 60f;
        public float FirstShotErrorFactor = 2f;   // the error starts this much larger...
        public float AimSettleSeconds = 1.5f;     // ...and settles over this long on the same target

        // ---- Shooting ----
        public int BurstMin = 2;
        public int BurstMax = 5;
        public float BurstPauseMin = 0.3f;
        public float BurstPauseMax = 0.8f;
        public float SemiAutoInterval = 0.25f;    // fastest trigger finger on a non-automatic weapon
        public float EngageRangeFactor = 0.8f;    // closes in until inside this share of the weapon's range...
        public float PreferredMeters = 25f;       // ...and inside this distance, whichever is shorter
        public float HoldInnerFactor = 0.8f;      // once closing in, keeps going to this share of that distance
        public float ReloadBelowFraction = 0.5f;  // out of a fight, reloads a magazine emptier than this

        // ---- Surviving ----
        public float RetreatHealthFraction = 0.35f; // with a medkit and at or below this: break off and find cover
        public float HealBelowFraction = 0.6f;      // out of sight and below this: use a medkit
        public float HealSeconds = 1.5f;            // crouched this long before the medkit is used
        public float RetreatMeters = 20f;           // how far to run when no cover is found
        public float RetreatMaxSeconds = 6f;        // a retreat that has not broken line of sight by then ends...
        public float StandSeconds = 10f;            // ...and the bot fights on for this long

        // ---- Remembering and wandering ----
        public float MemorySeconds = 8f;          // goes to where a lost target was last seen for this long
        public float InvestigateSeconds = 12f;    // goes to a heard noise for this long
        public float ArriveMeters = 2.5f;
        public float RoamMinMeters = 20f;         // prefers the next point of interest at least this far away
        public float RoamRadius = 35f;            // without points of interest: wander within this
        public float RoamTimeoutSeconds = 45f;

        // ---- Looting ----
        public float LootRadius = 25f;
        public float PickupMeters = 2.2f;         // must stay below the server's pickup range
        public int AmmoWanted = 120;              // per ammo type a carried weapon uses
        public int MedkitsWanted = 3;
    }

    public enum NoiseKind : byte { Gunshot = 0, Footstep = 1, Other = 2 }

    // Why bots got stuck, counted over a match (#77): a bot that made no headway for a while gives up its target
    // and the reason is counted here. The match log and tests read it; the match director clears it per match.
    public static class BotStats
    {
        static readonly System.Collections.Generic.Dictionary<string, int> Counts = new System.Collections.Generic.Dictionary<string, int>();

        public static int Total { get; private set; }

        public static void CountStall(string reason)
        {
            if (string.IsNullOrEmpty(reason)) reason = "unknown";
            Counts.TryGetValue(reason, out int n);
            Counts[reason] = n + 1;
            Total++;
        }

        public static int StallsOf(string reason) => reason != null && Counts.TryGetValue(reason, out int n) ? n : 0;

        // A copy, reason -> count.
        public static System.Collections.Generic.Dictionary<string, int> Snapshot() => new System.Collections.Generic.Dictionary<string, int>(Counts);

        public static void Clear()
        {
            Counts.Clear();
            Total = 0;
        }
    }

    public static class BotHearing
    {
        // How far a walker's footsteps carry: the full radius, cut by the crouch (`crouchedShare` 0..1 of the way
        // down) and again by slow walking.
        public static float FootstepRadius(float crouchedShare, bool slowWalk, BotRules rules, StanceRules stances)
        {
            float share = crouchedShare < 0f ? 0f : crouchedShare > 1f ? 1f : crouchedShare;
            float radius = rules.FootstepHearingRadius * (1f + (stances.Crouch.FootstepVolumeFactor - 1f) * share);
            return slowWalk ? radius * rules.SlowWalkHearingFactor : radius;
        }
    }

    // Sounds bots can hear: "a noise at a position that carries this far". Whoever makes a noise on the server
    // calls Emit; bots read what is new since they last listened. A fixed ring, so neither side allocates and
    // nobody has to unsubscribe.
    public static class BotNoise
    {
        public struct Entry
        {
            public float X, Y, Z;
            public float Radius;
            public int SourceId;   // network object id of who made it; listeners skip their own
            public NoiseKind Kind;
        }

        public const int Capacity = 64;

        static readonly Entry[] Ring = new Entry[Capacity];

        // Number of noises emitted so far. A listener keeps the value it last saw.
        public static int Count { get; private set; }

        public static void Emit(float x, float y, float z, float radius, int sourceId, NoiseKind kind = NoiseKind.Gunshot)
        {
            if (!(radius > 0f)) return;
            Ring[Count % Capacity] = new Entry { X = x, Y = y, Z = z, Radius = radius, SourceId = sourceId, Kind = kind };
            Count++;
        }

        // The oldest noise still kept for a listener that last saw `seen`.
        public static int FirstUnread(int seen)
        {
            int oldest = Count - Capacity;
            return seen > oldest ? seen : oldest < 0 ? 0 : oldest;
        }

        // `index` in FirstUnread(seen) .. Count - 1.
        public static Entry At(int index) => Ring[index % Capacity];

        public static void Clear() => Count = 0;
    }
}
