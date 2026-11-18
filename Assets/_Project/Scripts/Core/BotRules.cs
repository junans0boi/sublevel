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

        // A copy with the same values (the difficulty scales a copy, never the data file's own).
        public BotRules Clone() => (BotRules)MemberwiseClone();

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
        public float InvestigateChance = 0.7f;    // share of bots that go to noises at all (each bot's own fixed roll, ADR-312; levels: 0.4 / 0.7 / 1.0)
        public float ArriveMeters = 2.5f;
        public float RoamMinMeters = 20f;         // prefers the next point of interest at least this far away
        public float RoamRadius = 35f;            // without points of interest: wander within this
        public float RoamTimeoutSeconds = 45f;

        // ---- Looting ----
        public float LootRadius = 25f;
        public float PickupMeters = 2.2f;         // must stay below the server's pickup range
        public int AmmoWanted = 120;              // per ammo type a carried weapon uses
        public int MedkitsWanted = 3;
        public int HealItemsWanted = 4;           // bandages + first-aid kits it keeps picking up (ADR-116)
        public int BoostWanted = 2;               // per boost item type
        public int ThrowablesWanted = 2;          // per throwable type

        // ---- Farming (ADR-294): right after the drop a bot walks the loot points of a nearby building until it is kitted out ----
        public float FarmSeconds = 210f;          // longest it farms (Normal; the levels scale it: 240 / 210 / 180)
        public bool FarmHint = true;              // out of time and unarmed: it is told where the nearest weapon lies
        public int FarmMags = 1;                  // ready needs a gun with this many magazines of rounds (mag + reserve)
        public int FarmArmorPieces = 0;           // ...and this many armour pieces worn (0..2)
        public int FarmHealItems = 1;             // ...and this many healing items (medkits, bandages, first-aid kits)
        public int FarmBuildingCap = 2;           // at most this many other bots choose the same building (soft)
        public float FarmArriveMeters = 2.5f;     // a loot point counts as visited this close (and within 2.5 m in height)

        // ---- Cover and peeking (ADR-311, #375) ----
        public bool PeekCycle = true;             // fire from the open for a while, hide to reload and heal, come back out on the other side
        public float PeekExposeMin = 2f;          // seconds in the open before it hides again...
        public float PeekExposeMax = 3f;
        public float PeekHideMin = 1.5f;          // ...and at least this long behind cover (reloading, healing)...
        public float PeekHideMax = 3f;
        public float PeekMaxSeconds = 2.5f;       // coming out and still no line to the enemy for this long: hides again
        public bool PeekAlternate = true;         // comes out on the other side each time (off: always the same side)
        public float CoverSearchSeconds = 1.5f;   // how often the hiding place is looked for again
        public int CoverSearchesPerFrame = 2;     // bots that may look for cover in one frame (rays are spread over frames)
        // Where each kind of gun is fought from: sniper and marksman rifle far, rifle mid, SMG and shotgun close, pistol behind cover.
        public float PreferSniperMeters = 70f;
        public float PreferDmrMeters = 45f;
        public float PreferSmgMeters = 12f;
        public float PreferShotgunMeters = 7f;
        public float PreferPistolMeters = 20f;

        // ---- The first minutes (ADR-315): bots farm and leave each other be ----
        public float EarlyPeaceSeconds = 300f;    // after the drop a bot ignores enemies farther than EarlyPeaceMeters, unless it was hurt lately
        public float EarlyPeaceMeters = 2f;
        public float EarlyPeaceHurtSeconds = 8f;  // hurt this lately: the peace is off for this bot

        // ---- Without a weapon (ADR-310, #374) ----
        public float UnarmedFistMeters = 1.35f;   // an unarmed bot fights an armed enemy this close (ADR-218 punch range)
        public float UnarmedFistChaseMeters = 12f; // an unarmed bot fights an unarmed enemy only this near; farther away it leaves it be and goes on (ADR-314)
        public float FleeSeconds = 8f;            // a flight counts as lost when the enemy still sees it after this long
        public float LootDashMeters = 10f;        // fleeing, a weapon this close on the ground is picked up first
        public float CorpseLootMeters = 40f;      // goes to where somebody fell if it is this near...
        public float CorpseMemorySeconds = 60f;   // ...and that was this recently...
        public float CorpseQuietSeconds = 6f;     // ...and no shots have been heard near it for this long
        public float CorpseGiveUpSeconds = 25f;   // a trip to a place that long without arriving is dropped

        // ---- Fighting like a player (ADR-117) ----
        public float StrafeMeters = 3f;           // sidesteps this far while shooting (0 = stands still)
        public float StrafeMinSeconds = 0.8f;     // then changes side after a time in this range
        public float StrafeMaxSeconds = 1.8f;
        public float StrafeMinTarget = 8f;        // closer than this it just stands and shoots
        public float ReloadCoverMinMeters = 10f;  // reloads out of the enemy's sight when it is at least this far
        public float AimLead = 1f;                // share of a moving target's travel during the bullet's flight it leads (0 = none)
        public float HearingErrorFraction = 0.08f; // a heard noise is placed this share of its distance off
        public float HearingErrorMaxMeters = 6f;

        // ---- Items (ADR-116) ----
        public float ThrowIntervalSeconds = 6f;   // between two throws of one bot
        public float FragMinMeters = 10f;         // a fragmentation grenade is thrown at a target between these
        public float FragMaxMeters = 28f;
        public float FlashMinMeters = 7f;         // a flash close enough to blind, far enough not to catch the thrower
        public float FlashMaxMeters = 20f;
        public float MolotovMinMeters = 8f;       // a fire bottle at someone standing still
        public float MolotovMaxMeters = 25f;
        public float StillSpeed = 1f;             // slower than this counts as standing still
        public float BoostBelowFraction = 0.85f;  // out of sight: a boost item below this health...
        public float BoostThreatSeconds = 15f;    // ...or this soon after seeing an enemy
        public float FireFleeMeters = 9f;         // out of a fire: this far from its centre
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
        // What the ground at a point is made of (x, y, z); set by the audio side, which knows the world. Null = concrete.
        public static System.Func<float, float, float, FootSurface> SurfaceAt;

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
