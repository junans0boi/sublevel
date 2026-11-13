using System;

namespace Game.Core
{
    // A point in the world for the pure bot code (this assembly has no engine types).
    public struct BotVec
    {
        public float X, Y, Z;

        public BotVec(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static float Distance(BotVec a, BotVec b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // Along the ground.
        public static float PlanarDistance(BotVec a, BotVec b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    public enum BotState { Roam = 0, Loot = 1, Investigate = 2, Engage = 3, Retreat = 4, Heal = 5 }

    // What a bot knows this moment: its own condition and what its senses delivered.
    public struct BotPercept
    {
        public double Time;
        public BotVec Position;
        public float HealthFraction;   // 0..1

        public bool HasWeapon;         // a weapon is in hand
        public int WeaponId;
        public int Mag, MagSize;
        public int Reserve;            // rounds in reserve for the weapon in hand
        public bool Reloading;
        public bool OtherSlotUsable;   // the other slot holds a weapon with rounds in it or in reserve
        public float WeaponRange;
        public int Medkits;

        public bool EnemyVisible;      // seen right now, with a clear line of sight
        public int EnemyId;
        public BotVec EnemyPosition;

        public bool LootVisible;       // the nearest pickup it has a use for
        public int LootId;
        public BotVec LootPosition;

        public bool Heard;             // a new noise since the last call
        public BotVec HeardPosition;

        public bool Stuck;             // the last move target cannot be reached
    }

    // What the bot wants to do this moment. The controller turns it into movement and server calls.
    public struct BotIntent
    {
        public BotState State;

        public bool Move;
        public BotVec MoveTarget;
        public bool Sprint;
        public bool SeekCover;         // go where Threat cannot see; MoveTarget is the fallback (straight away from it)
        public BotVec Threat;

        public bool Look;
        public BotVec LookTarget;
        public bool Crouch;
        public bool Aim;               // weapon raised

        public bool Fire;
        public float SecondsOnTarget;  // how long the target has been tracked (aim settles with it)
        public bool Reload;
        public bool SwitchWeapon;
        public bool PickUp;            // LootId is within reach: take it
        public int LootId;
        public bool Heal;              // use a medkit now
    }

    public static class BotAim
    {
        // Largest angle (degrees) a shot may be off: grows with distance, shrinks while tracking the same target.
        public static float ErrorDeg(BotRules rules, float distance, float secondsOnTarget)
        {
            float span = rules.AimFarMeters - rules.AimNearMeters;
            float t = span <= 0f ? 1f : Math.Max(0f, Math.Min(1f, (distance - rules.AimNearMeters) / span));
            float error = rules.AimErrorNearDeg + (rules.AimErrorFarDeg - rules.AimErrorNearDeg) * t;
            float settled = rules.AimSettleSeconds <= 0f ? 1f : Math.Max(0f, Math.Min(1f, secondsOnTarget / rules.AimSettleSeconds));
            return error * (rules.FirstShotErrorFactor + (1f - rules.FirstShotErrorFactor) * settled);
        }
    }

    // Decides what a bot does (M2-2). Pure and deterministic: the same seed and the same percepts give the same
    // intents. Priorities, highest first: survive (retreat, heal) > fight what it sees > arm itself > look into
    // what it saw or heard > pick up useful things > wander between points of interest.
    public class BotBrain
    {
        readonly BotRules _rules;
        readonly Random _rng;

        // Target tracking.
        bool _hasLastSeen;
        int _enemyId;
        BotVec _lastSeen;
        double _lastSeenTime;
        double _spottedAt;

        bool _hasNoise;
        BotVec _noise;
        double _noiseUntil;

        // Bursts.
        int _burstLeft;
        double _pauseUntil;
        int _lastMag = -1;
        int _lastWeaponId;

        bool _closing;
        double _healReadyAt = -1;
        double _retreatSince = -1;
        double _standUntil;

        bool _hasRoam;
        BotVec _roam;
        double _roamUntil;

        // Places worth walking to (shared, never modified here). Null or empty: wander at random.
        public BotVec[] PointsOfInterest { get; set; }
        // Points of interest it may wander to (null: all). The controller leaves out places the lockdown is about to
        // take (#77); when none is allowed, every point is.
        public Func<BotVec, bool> RoamFilter { get; set; }

        public BotState State { get; private set; }

        public BotBrain(BotRules rules, int seed)
        {
            _rules = rules ?? new BotRules();
            _rng = new Random(seed);
        }

        // Does a bot in this condition have a use for a pickup?
        public static bool WantsLoot(LootKind kind, bool hasEmptySlot, bool carriesWeaponForAmmo, int reserve, int medkits, BotRules rules)
        {
            switch (kind)
            {
                case LootKind.Weapon: return hasEmptySlot;
                case LootKind.Ammo: return carriesWeaponForAmmo && reserve < rules.AmmoWanted;
                case LootKind.Medkit: return medkits < rules.MedkitsWanted;
                default: return false;
            }
        }

        public BotIntent Think(in BotPercept p)
        {
            var intent = Decide(p);
            State = intent.State;
            return intent;
        }

        BotIntent Decide(in BotPercept p)
        {
            var rules = _rules;
            double now = p.Time;
            var intent = new BotIntent { LootId = -1 };

            // Shots that left the magazine since the last call count against the burst.
            if (p.HasWeapon && p.WeaponId == _lastWeaponId && _lastMag >= 0 && p.Mag < _lastMag && _burstLeft > 0)
            {
                _burstLeft -= _lastMag - p.Mag;
                if (_burstLeft <= 0)
                {
                    _burstLeft = 0;
                    _pauseUntil = now + Between(rules.BurstPauseMin, rules.BurstPauseMax);
                }
            }
            _lastMag = p.HasWeapon ? p.Mag : -1;
            _lastWeaponId = p.HasWeapon ? p.WeaponId : 0;

            if (p.EnemyVisible)
            {
                // A new target, or one that was out of sight for a while, has to be reacted to first.
                if (!_hasLastSeen || p.EnemyId != _enemyId || now - _lastSeenTime > rules.ReacquireSeconds) _spottedAt = now;
                _hasLastSeen = true;
                _enemyId = p.EnemyId;
                _lastSeen = p.EnemyPosition;
                _lastSeenTime = now;
            }
            else if (p.Heard)
            {
                _hasNoise = true;
                _noise = p.HeardPosition;
                _noiseUntil = now + rules.InvestigateSeconds;
            }

            bool armed = p.HasWeapon && (p.Mag > 0 || p.Reserve > 0);
            bool canFight = armed || p.OtherSlotUsable;
            bool low = p.HealthFraction <= rules.RetreatHealthFraction;

            if (p.EnemyVisible)
            {
                _healReadyAt = -1;
                intent.Look = true;
                intent.LookTarget = p.EnemyPosition;
                intent.Threat = p.EnemyPosition;
                float distance = BotVec.Distance(p.Position, p.EnemyPosition);

                bool wantsOut = !canFight || (low && p.Medkits > 0 && now >= _standUntil);
                if (wantsOut && canFight)
                {
                    // A retreat that never breaks line of sight is worse than fighting back.
                    if (_retreatSince < 0) _retreatSince = now;
                    else if (now - _retreatSince > rules.RetreatMaxSeconds)
                    {
                        _standUntil = now + rules.StandSeconds;
                        _retreatSince = -1;
                        wantsOut = false;
                    }
                }
                if (wantsOut)
                {
                    intent.State = BotState.Retreat;
                    intent.Move = true;
                    intent.Sprint = true;
                    intent.SeekCover = true;
                    intent.MoveTarget = Away(p.Position, p.EnemyPosition, rules.RetreatMeters);
                    return intent;
                }
                _retreatSince = -1;

                intent.State = BotState.Engage;
                intent.Aim = true;
                intent.SecondsOnTarget = (float)(now - _spottedAt);
                if (!armed)
                {
                    intent.SwitchWeapon = true;
                    return intent;
                }
                if (p.Reloading)
                {
                    intent.Crouch = true;
                    return intent;
                }
                if (p.Mag <= 0)
                {
                    // `armed` with an empty magazine means there are rounds in reserve.
                    intent.Reload = true;
                    intent.Crouch = true;
                    return intent;
                }

                float hold = Math.Min(p.WeaponRange * rules.EngageRangeFactor, rules.PreferredMeters);
                // Closes in until well inside, so it does not start and stop at the edge.
                _closing = _closing ? distance > hold * rules.HoldInnerFactor : distance > hold;
                if (_closing)
                {
                    intent.Move = true;
                    intent.MoveTarget = p.EnemyPosition;
                    intent.Sprint = distance > p.WeaponRange;
                }
                bool reacted = now - _spottedAt >= rules.ReactionSeconds;
                if (reacted && distance <= p.WeaponRange && now >= _pauseUntil)
                {
                    if (_burstLeft <= 0) _burstLeft = _rng.Next(Math.Max(1, rules.BurstMin), Math.Max(1, Math.Max(rules.BurstMin, rules.BurstMax)) + 1);
                    intent.Fire = true;
                }
                return intent;
            }

            // ---- Nobody in sight ----
            _retreatSince = -1;
            bool recentThreat = _hasLastSeen && now - _lastSeenTime < rules.MemorySeconds;
            if (_hasLastSeen && !recentThreat) _hasLastSeen = false;
            if (_hasNoise && now >= _noiseUntil) _hasNoise = false;

            if (p.HasWeapon && !p.Reloading)
            {
                if (p.Reserve > 0 && (p.Mag <= 0 || p.Mag < p.MagSize * rules.ReloadBelowFraction)) intent.Reload = true;
                else if (p.Mag <= 0 && p.Reserve <= 0 && p.OtherSlotUsable) intent.SwitchWeapon = true;
            }
            else if (!p.HasWeapon && p.OtherSlotUsable) intent.SwitchWeapon = true;

            if (p.Medkits > 0 && p.HealthFraction < rules.HealBelowFraction)
            {
                intent.State = BotState.Heal;
                intent.Crouch = true;
                if (recentThreat)
                {
                    intent.Look = true;
                    intent.LookTarget = _lastSeen;
                }
                if (_healReadyAt < 0) _healReadyAt = now + rules.HealSeconds;
                if (now >= _healReadyAt)
                {
                    intent.Heal = true;
                    _healReadyAt = -1;
                }
                return intent;
            }
            _healReadyAt = -1;

            // Keeps low while reloading with a threat around.
            if (p.Reloading && (recentThreat || _hasNoise)) intent.Crouch = true;

            // Without a weapon, one on the ground comes before anything else.
            bool armFirst = !canFight && p.LootVisible;
            if (canFight && !low && !armFirst)
            {
                if (recentThreat)
                {
                    if (BotVec.PlanarDistance(p.Position, _lastSeen) <= rules.ArriveMeters || p.Stuck) _hasLastSeen = false;
                    else return Investigate(intent, _lastSeen);
                }
                if (_hasNoise)
                {
                    if (BotVec.PlanarDistance(p.Position, _noise) <= rules.ArriveMeters || p.Stuck) _hasNoise = false;
                    else return Investigate(intent, _noise);
                }
            }

            if (p.LootVisible)
            {
                intent.State = BotState.Loot;
                intent.Move = true;
                intent.MoveTarget = p.LootPosition;
                intent.Look = true;
                intent.LookTarget = p.LootPosition;
                intent.LootId = p.LootId;
                intent.PickUp = BotVec.Distance(p.Position, p.LootPosition) <= rules.PickupMeters;
                return intent;
            }

            intent.State = BotState.Roam;
            bool arrived = _hasRoam && BotVec.PlanarDistance(p.Position, _roam) <= rules.ArriveMeters;
            if (!_hasRoam || arrived || p.Stuck || now >= _roamUntil) PickRoam(p.Position, now);
            intent.Move = true;
            intent.MoveTarget = _roam;
            return intent;
        }

        static BotIntent Investigate(BotIntent intent, BotVec where)
        {
            intent.State = BotState.Investigate;
            intent.Move = true;
            intent.MoveTarget = where;
            intent.Look = true;
            intent.LookTarget = where;
            intent.Aim = true;
            return intent;
        }

        void PickRoam(BotVec from, double now)
        {
            var rules = _rules;
            var points = PointsOfInterest;
            _hasRoam = true;
            _roamUntil = now + rules.RoamTimeoutSeconds;
            if (points != null && points.Length > 0)
            {
                // From a random start, the first one that is a real walk away; otherwise the farthest there is.
                int start = _rng.Next(points.Length);
                int pick = start;
                float farthest = -1f;
                var filter = RoamFilter;
                if (filter != null)
                {
                    bool any = false;
                    for (int i = 0; i < points.Length && !any; i++) any = filter(points[i]);
                    if (!any) filter = null;
                }
                for (int i = 0; i < points.Length; i++)
                {
                    int index = (start + i) % points.Length;
                    if (filter != null && !filter(points[index])) continue;
                    float distance = BotVec.PlanarDistance(from, points[index]);
                    if (distance >= rules.RoamMinMeters)
                    {
                        pick = index;
                        break;
                    }
                    if (distance > farthest)
                    {
                        farthest = distance;
                        pick = index;
                    }
                }
                _roam = points[pick];
                return;
            }
            double angle = _rng.NextDouble() * Math.PI * 2;
            double radius = rules.RoamRadius * (0.5 + 0.5 * _rng.NextDouble());
            _roam = new BotVec(from.X + (float)(Math.Cos(angle) * radius), from.Y, from.Z + (float)(Math.Sin(angle) * radius));
        }

        // A point `meters` from `from`, straight away from `threat` along the ground.
        static BotVec Away(BotVec from, BotVec threat, float meters)
        {
            float dx = from.X - threat.X, dz = from.Z - threat.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 1e-3f) { dx = 1f; dz = 0f; length = 1f; }
            return new BotVec(from.X + dx / length * meters, from.Y, from.Z + dz / length * meters);
        }

        float Between(float min, float max) => max <= min ? min : min + (float)_rng.NextDouble() * (max - min);
    }
}
