using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.AI;

namespace Game.AI
{
    // A computer-controlled combatant (M2-2). Server-side only: it senses, lets a BotBrain decide and carries the
    // decision out through the same server calls a player's requests end in. Clients just see a Combatant move.
    //
    // Honest senses: it sees other combatants only inside its view cone with a clear line from its eyes to their
    // chest or head, hears gunshots (BotNoise) and footsteps inside their radius, and notices pickups it has a
    // line of sight to. Everything else it knows is memory kept by the brain.
    //
    // It walks with a NavMeshAgent that it adds to itself on the server (so the prefab needs none and clients
    // never run one). The agent moves the transform; the combatant's capsule simply rides along.
    [RequireComponent(typeof(Combatant))]
    public class BotController : MonoBehaviour
    {
        const float ChestHeight = 1.1f;
        const float HeadHeight = 1.5f; // low enough to stay inside the crouched capsule once scaled
        const float CrouchedEyeHeight = 0.95f;
        const float AgentSnapMeters = 2f;
        const float DestinationSnapMeters = 4f;
        const float RepathSeconds = 1f;
        const float RepathMoveMeters = 1f;
        const float ProgressSeconds = 2.5f;
        const float ProgressMeters = 0.75f;
        const float LootIgnoreSeconds = 20f;
        const float CoverSeconds = 1.5f;
        const float ActionRetrySeconds = 0.5f;
        const int CoverDirections = 8;

        public static readonly List<BotController> All = new List<BotController>();

        static readonly float[] CoverRadii = { 4f, 8f, 13f };
        static readonly BotVec[] NoPoints = new BotVec[0];
        static BotVec[] _points = NoPoints;
        static MatchDirector _pointsFor, _namedFor;
        static bool _pointsGiven, _pointsBuilt;
        static int _named;

        Combatant _self;
        CombatantStance _stance;
        NavMeshAgent _agent;
        BotBrain _brain;
        BotRules _rules;
        System.Random _rng;
        bool _ready;

        BotPercept _percept;
        BotIntent _intent;
        Combatant _enemy;
        LootItem _loot;
        float _maxHp;
        float _nextSense;
        int _noiseSeen;
        int _ignoredLootId = -1;
        float _ignoreLootUntil;

        float _nextAgentTry;
        Vector3 _destination;
        bool _hasDestination;
        float _nextRepath;
        Vector3 _progressFrom;
        float _progressAt;
        Vector3 _cover;
        bool _hasCover;
        float _coverUntil;
        float _nextTrigger;
        float _nextAction;

        // "봇 N", in spawn order within a match.
        public string DisplayName { get; private set; } = "";
        public BotState State => _intent.State;
        public BotIntent Intent => _intent;
        public Combatant Target => _percept.EnemyVisible ? _enemy : null;
        public bool OnNavMesh => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            _points = NoPoints;
            _pointsFor = null;
            _namedFor = null;
            _pointsGiven = false;
            _pointsBuilt = false;
            _named = 0;
        }

        // Places bots wander between in the current match (convergence points, elevators, compounds, ...).
        // Without a call, the match's loot and dummy spawn points are used.
        public static void SetPointsOfInterest(IReadOnlyList<Vector3> points)
        {
            int count = points != null ? points.Count : 0;
            var list = new BotVec[count];
            for (int i = 0; i < count; i++) list[i] = ToVec(points[i]);
            _points = list;
            _pointsFor = MatchDirector.Instance;
            _pointsGiven = true;
            for (int i = 0; i < All.Count; i++)
                if (All[i]._brain != null) All[i]._brain.PointsOfInterest = list;
        }

        // Once per match (the director is new after every scene load).
        static void SyncMatch()
        {
            var director = MatchDirector.Instance;
            if (!ReferenceEquals(director, _namedFor))
            {
                _namedFor = director;
                _named = 0;
            }
            if (ReferenceEquals(director, _pointsFor) && (_pointsGiven || _pointsBuilt)) return;
            _pointsFor = director;
            _pointsGiven = false;
            _pointsBuilt = true;
            _points = NoPoints;
            if (director == null) return;
            var loot = director.LootSpawns;
            var dummies = director.DummySpawns;
            int count = 0;
            if (loot != null)
                for (int i = 0; i < loot.Length; i++) if (loot[i] != null) count++;
            if (dummies != null)
                for (int i = 0; i < dummies.Length; i++) if (dummies[i] != null) count++;
            if (count == 0) return;
            var list = new BotVec[count];
            int n = 0;
            if (loot != null)
                for (int i = 0; i < loot.Length; i++) if (loot[i] != null) list[n++] = ToVec(loot[i].position);
            if (dummies != null)
                for (int i = 0; i < dummies.Length; i++) if (dummies[i] != null) list[n++] = ToVec(dummies[i].position);
            _points = list;
        }

        void Awake()
        {
            _self = GetComponent<Combatant>();
            _stance = GetComponent<CombatantStance>();
        }

        void OnEnable() => All.Add(this);

        void OnDisable()
        {
            All.Remove(this);
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
        }

        void Update()
        {
            if (!_self.IsServerInitialized) return;
            if (!_ready) Init();
            if (_self.Dead.Value)
            {
                if (_agent != null && _agent.enabled) _agent.enabled = false;
                return;
            }
            EnsureAgent();

            float now = Time.time;
            if (now >= _nextSense)
            {
                _nextSense = now + _rules.SenseInterval;
                Sense();
            }
            FillSelf();
            _intent = _brain.Think(_percept);
            // One-shot senses are the brain's to remember from here on.
            _percept.Heard = false;
            _percept.Stuck = false;
            Act(now);
        }

        void Init()
        {
            _ready = true;
            _rules = BotRules.Current;
            SyncMatch();
            int matchSeed = MatchDirector.Instance != null ? MatchDirector.Instance.Seed : 0;
            int seed = unchecked(matchSeed * 31 + _self.ObjectId * 104729 + 5);
            _rng = new System.Random(seed);
            _brain = new BotBrain(_rules, unchecked(seed * 7 + 1)) { PointsOfInterest = _points };
            _noiseSeen = BotNoise.Count;
            _maxHp = Mathf.Max(1f, _self.Hp.Value);
            _named++;
            DisplayName = "봇 " + _named;
            BotServerActions.SetDisplayName(_self, DisplayName);
        }

        // ---- Senses ----

        void Sense()
        {
            var rules = _rules;
            var eye = _self.EyePosition;
            var forward = transform.forward;
            float halfCone = rules.FieldOfViewDeg * 0.5f;
            float viewSqr = rules.ViewDistance * rules.ViewDistance;

            // Sight: the nearest combatant it can actually see. The one it is already on is kept a little longer,
            // so two targets at similar distances do not make it flip between them.
            Combatant seen = null;
            float seenScore = float.MaxValue;
            bool stepHeard = false;
            Vector3 stepAt = default;
            float stepSqr = float.MaxValue;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == _self || c.Dead.Value) continue;
                if (c.IsPlayer && !rules.TargetsPlayers) continue;
                var chest = ChestOf(c);
                var to = chest - eye;
                float sqr = to.sqrMagnitude;
                bool visible = false;
                if (sqr <= viewSqr)
                {
                    float score = c == _enemy ? sqr * 0.5f : sqr;
                    bool inCone = sqr <= rules.CloseSenseRadius * rules.CloseSenseRadius || Vector3.Angle(forward, to) <= halfCone;
                    if (inCone && score < seenScore && (Sees(eye, chest, c) || Sees(eye, HeadOf(c), c)))
                    {
                        seen = c;
                        seenScore = score;
                        visible = true;
                    }
                }
                if (visible) continue;

                // Footsteps: someone moving fast enough, close enough. Crouching makes them quieter.
                if (c.PlanarSpeed < rules.FootstepMinSpeed) continue;
                var crouch = StanceRules.Current.Crouch;
                float crouched = crouch.EyeDropMeters > 0f ? Mathf.Clamp01(-c.EyeHeightOffset / crouch.EyeDropMeters) : 0f;
                float radius = rules.FootstepHearingRadius * Mathf.Lerp(1f, crouch.FootstepVolumeFactor, crouched);
                if (sqr <= radius * radius && sqr < stepSqr)
                {
                    stepHeard = true;
                    stepAt = c.transform.position;
                    stepSqr = sqr;
                }
            }
            _enemy = seen;
            _percept.EnemyVisible = seen != null;
            if (seen != null)
            {
                _percept.EnemyId = seen.ObjectId;
                _percept.EnemyPosition = ToVec(ChestOf(seen));
            }

            // Hearing: gunshots and whatever else was emitted since the last time. The nearest one counts.
            bool heard = stepHeard;
            Vector3 heardAt = stepAt;
            float heardSqr = stepSqr;
            int self = _self.ObjectId;
            int count = BotNoise.Count;
            for (int i = BotNoise.FirstUnread(_noiseSeen); i < count; i++)
            {
                var noise = BotNoise.At(i);
                if (noise.SourceId == self) continue;
                var at = new Vector3(noise.X, noise.Y, noise.Z);
                float sqr = (at - eye).sqrMagnitude;
                if (sqr > noise.Radius * noise.Radius || sqr >= heardSqr) continue;
                heard = true;
                heardAt = at;
                heardSqr = sqr;
            }
            _noiseSeen = count;
            if (heard)
            {
                _percept.Heard = true;
                _percept.HeardPosition = ToVec(heardAt);
            }

            SenseLoot(eye);
        }

        // A clear line from the eyes to that point on that combatant.
        bool Sees(Vector3 eye, Vector3 point, Combatant target)
        {
            var to = point - eye;
            float distance = to.magnitude;
            if (distance < 1e-3f) return true;
            if (!Physics.Raycast(eye, to / distance, out var hit, distance + 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            return hit.collider.GetComponentInParent<Combatant>() == target;
        }

        void SenseLoot(Vector3 eye)
        {
            var rules = _rules;
            _loot = null;
            _percept.LootVisible = false;
            if (rules.LootRadius <= 0f) return;
            bool emptySlot = _self.HasFreeWeaponSlot;
            bool unarmed = !_self.HasAnyWeapon;
            int medkits = _self.Medkits.Value;
            float now = Time.time;
            float bestSqr = rules.LootRadius * rules.LootRadius;
            bool bestIsWeapon = false;
            var feet = transform.position;
            var items = LootItem.All;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsSpawned) continue;
                if (item.ObjectId == _ignoredLootId && now < _ignoreLootUntil) continue;
                var kind = (LootKind)item.Kind.Value;
                bool isWeapon = kind == LootKind.Weapon;
                // Empty-handed, any weapon beats everything else in reach.
                if (unarmed && bestIsWeapon && !isWeapon) continue;
                bool usesAmmo = false;
                int reserve = 0;
                if (kind == LootKind.Ammo)
                {
                    var ammo = (AmmoType)item.Ammo.Value;
                    usesAmmo = _self.CarriesWeaponFor(ammo);
                    reserve = _self.ReserveFor(ammo);
                }
                if (!BotBrain.WantsLoot(kind, emptySlot, usesAmmo, reserve, medkits, rules)) continue;
                var at = item.transform.position;
                float sqr = (at - feet).sqrMagnitude;
                bool better = unarmed && isWeapon && !bestIsWeapon ? sqr <= rules.LootRadius * rules.LootRadius : sqr < bestSqr;
                if (!better) continue;
                // Pickups lie on the floor; low clutter may hide the thing itself but not the space just above it.
                if (Blocked(eye, at + Vector3.up * 0.1f) && Blocked(eye, at + Vector3.up * 0.6f)) continue;
                _loot = item;
                bestSqr = sqr;
                bestIsWeapon = isWeapon;
            }
            if (_loot == null) return;
            _percept.LootVisible = true;
            _percept.LootId = _loot.ObjectId;
            _percept.LootPosition = ToVec(_loot.transform.position);
        }

        // Something solid between the two points (other combatants do not count as walls).
        static bool Blocked(Vector3 from, Vector3 to)
        {
            if (!Physics.Linecast(from, to, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            return hit.collider.GetComponentInParent<Combatant>() == null;
        }

        void FillSelf()
        {
            _percept.Time = Time.timeAsDouble;
            _percept.Position = ToVec(transform.position);
            _maxHp = Mathf.Max(_maxHp, _self.Hp.Value);
            _percept.HealthFraction = _self.Hp.Value / _maxHp;

            var weapon = _self.ActiveWeapon;
            _percept.HasWeapon = weapon != null;
            _percept.WeaponId = weapon != null ? weapon.Id : 0;
            _percept.Mag = weapon != null ? _self.ActiveMag : 0;
            _percept.MagSize = weapon != null ? weapon.MagSize : 0;
            _percept.Reserve = weapon != null ? _self.ReserveFor(weapon.Ammo) : 0;
            _percept.WeaponRange = weapon != null ? weapon.Range : 0f;
            _percept.Reloading = _self.Reloading.Value;
            _percept.Medkits = _self.Medkits.Value;

            int otherSlot = _self.OtherWeaponSlot();
            var other = GameData.Weapons.Find(otherSlot >= 0 ? _self.WeaponInSlot(otherSlot) : 0);
            _percept.OtherSlotUsable = other != null && (_self.MagInSlot(otherSlot) > 0 || _self.ReserveFor(other.Ammo) > 0);

            // Between two looks the target is followed without a new ray, as long as it is still there.
            if (_percept.EnemyVisible)
            {
                if (_enemy == null || _enemy.Dead.Value)
                {
                    _percept.EnemyVisible = false;
                    _enemy = null;
                }
                else _percept.EnemyPosition = ToVec(ChestOf(_enemy));
            }
            if (_percept.LootVisible && (_loot == null || !_loot.IsSpawned))
            {
                _percept.LootVisible = false;
                _loot = null;
            }
        }

        // ---- Acting ----

        void Act(float now)
        {
            var intent = _intent;
            var weapon = _self.ActiveWeapon;

            if (_stance != null)
            {
                var wanted = intent.Crouch ? Stance.Crouch : Stance.Stand;
                if (_stance.Current != wanted) _stance.ServerSetStance(wanted);
            }
            var aim = intent.Aim && weapon != null ? AimMode.Shoulder : AimMode.Hip;
            if (_self.AimMode != aim || _self.Lean.Value != 0) _self.ServerSetStance(aim, 0);

            if (intent.Reload && !_self.Reloading.Value) _self.ServerReload();
            if (now >= _nextAction)
            {
                if (intent.SwitchWeapon)
                {
                    _nextAction = now + ActionRetrySeconds;
                    int otherSlot = _self.OtherWeaponSlot();
                    if (otherSlot >= 0) BotServerActions.SelectSlot(_self, otherSlot);
                }
                else if (intent.Heal)
                {
                    _nextAction = now + ActionRetrySeconds;
                    BotServerActions.UseMedkit(_self);
                }
                else if (intent.PickUp && _loot != null && _loot.ObjectId == intent.LootId)
                {
                    _nextAction = now + ActionRetrySeconds;
                    if (BotServerActions.Pickup(_self, _loot))
                    {
                        _loot = null;
                        _percept.LootVisible = false;
                        _nextSense = 0f; // look again right away: there may be more
                    }
                }
            }

            Move(intent, now);
            Turn(intent);
            if (intent.Fire && weapon != null) Fire(intent, weapon, now);
        }

        void EnsureAgent()
        {
            if (_agent != null)
            {
                if (!_agent.enabled) _agent.enabled = true;
                return;
            }
            if (Time.time < _nextAgentTry) return;
            _nextAgentTry = Time.time + 1f;
            // No navmesh here (a test box): the bot holds its ground and still fights.
            if (!NavMesh.SamplePosition(transform.position, out var hit, AgentSnapMeters, NavMesh.AllAreas)) return;
            transform.position = hit.position;
            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null) _agent = gameObject.AddComponent<NavMeshAgent>();
            var capsule = GetComponent<CapsuleCollider>();
            _agent.radius = capsule != null ? capsule.radius : 0.35f;
            _agent.height = capsule != null ? capsule.height : 1.8f;
            _agent.baseOffset = 0f;
            _agent.updateRotation = false; // the bot turns itself: it faces what it looks at, not where it walks
            _agent.updateUpAxis = true;
            _agent.acceleration = 30f;
            _agent.angularSpeed = 0f;
            _agent.stoppingDistance = 0.4f;
            _agent.autoBraking = true;
            _agent.autoRepath = true;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            _agent.speed = GameData.Rules.Locomotion.WalkSpeed;
            _progressFrom = transform.position;
            _progressAt = Time.time;
        }

        void Move(in BotIntent intent, float now)
        {
            if (!OnNavMesh) return;
            if (!intent.Move)
            {
                if (!_agent.isStopped)
                {
                    _agent.isStopped = true;
                    _agent.velocity = Vector3.zero;
                }
                _hasDestination = false;
                _progressFrom = transform.position;
                _progressAt = now;
                return;
            }

            var target = ToVector(intent.MoveTarget);
            if (intent.SeekCover)
            {
                var threat = ToVector(intent.Threat);
                if (now >= _coverUntil)
                {
                    _coverUntil = now + CoverSeconds;
                    _hasCover = FindCover(threat, out _cover);
                }
                if (_hasCover) target = _cover;
            }

            var locomotion = GameData.Rules.Locomotion;
            float speed = intent.Sprint && !intent.Crouch ? locomotion.SprintSpeed : locomotion.WalkSpeed;
            if (_stance != null) speed *= _stance.MoveFactor;
            _agent.speed = speed;

            if (!_hasDestination || now >= _nextRepath || (target - _destination).sqrMagnitude > RepathMoveMeters * RepathMoveMeters)
            {
                _nextRepath = now + RepathSeconds;
                if (!_hasDestination)
                {
                    _progressFrom = transform.position;
                    _progressAt = now;
                }
                _hasDestination = true;
                _destination = target;
                if (!NavMesh.SamplePosition(target, out var hit, DestinationSnapMeters, NavMesh.AllAreas) || !_agent.SetDestination(hit.position))
                {
                    GiveUp(intent, now);
                    return;
                }
            }
            if (_agent.isStopped) _agent.isStopped = false;

            // A path that stops short of the target, or no headway for a while: this target is not reachable.
            if (!_agent.pathPending && _agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                GiveUp(intent, now);
                return;
            }
            if (now - _progressAt >= ProgressSeconds)
            {
                bool arrived = !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.5f;
                if (!arrived && (transform.position - _progressFrom).sqrMagnitude < ProgressMeters * ProgressMeters) GiveUp(intent, now);
                // Arrived as far as the navmesh goes, yet the brain still wants to get closer (a pickup out of
                // reach, a point of interest off the navmesh): let it choose something else.
                else if (arrived && !intent.PickUp && !intent.SeekCover && intent.State != BotState.Engage) GiveUp(intent, now);
                _progressFrom = transform.position;
                _progressAt = now;
            }
        }

        void GiveUp(in BotIntent intent, float now)
        {
            _percept.Stuck = true;
            _hasDestination = false;
            _hasCover = false;
            _coverUntil = 0f;
            _progressFrom = transform.position;
            _progressAt = now;
            if (intent.State == BotState.Loot && intent.LootId >= 0)
            {
                _ignoredLootId = intent.LootId;
                _ignoreLootUntil = now + LootIgnoreSeconds;
                _loot = null;
                _percept.LootVisible = false;
            }
        }

        // The nearest spot on the navmesh around the bot from which a crouched body cannot be seen by the threat.
        bool FindCover(Vector3 threat, out Vector3 cover)
        {
            cover = default;
            var from = transform.position;
            var threatEye = threat; // the brain's threat position is already at chest height
            for (int r = 0; r < CoverRadii.Length; r++)
            {
                float bestSqr = float.MaxValue;
                bool found = false;
                for (int d = 0; d < CoverDirections; d++)
                {
                    float angle = d * (Mathf.PI * 2f / CoverDirections);
                    var candidate = from + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * CoverRadii[r];
                    if (!NavMesh.SamplePosition(candidate, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                    if (!Physics.Linecast(hit.position + Vector3.up * CrouchedEyeHeight, threatEye, out var block, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    if (block.collider.GetComponentInParent<Combatant>() != null) continue;
                    // Not one that means running at the threat.
                    float sqr = (hit.position - threat).sqrMagnitude;
                    if ((from - threat).sqrMagnitude > sqr + CoverRadii[r] * CoverRadii[r]) continue;
                    float walk = (hit.position - from).sqrMagnitude;
                    if (walk >= bestSqr) continue;
                    bestSqr = walk;
                    cover = hit.position;
                    found = true;
                }
                if (found) return true;
            }
            return false;
        }

        void Turn(in BotIntent intent)
        {
            Vector3 facing;
            if (intent.Look) facing = ToVector(intent.LookTarget) - transform.position;
            else if (OnNavMesh && _agent.velocity.sqrMagnitude > 0.25f) facing = _agent.velocity;
            else return;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), _rules.TurnDegPerSecond * Time.deltaTime);
        }

        void Fire(in BotIntent intent, WeaponDef weapon, float now)
        {
            if (_enemy == null || now < _nextTrigger) return;
            var eye = _self.EyePosition;
            var to = ToVector(_percept.EnemyPosition) - eye;
            float distance = to.magnitude;
            if (distance < 1e-3f) return;
            // The body has to have turned to the target first.
            var flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 1e-4f && Vector3.Angle(transform.forward, flat) > _rules.FireConeDeg) return;

            float error = BotAim.ErrorDeg(_rules, distance, intent.SecondsOnTarget);
            // Uniform inside the error cone.
            double angle = _rng.NextDouble() * System.Math.PI * 2;
            float radius = Mathf.Sqrt((float)_rng.NextDouble()) * error;
            var off = Quaternion.Euler(Mathf.Sin((float)angle) * radius, Mathf.Cos((float)angle) * radius, 0f);
            if (!_self.ServerFire(Quaternion.LookRotation(to / distance) * off * Vector3.forward)) return;
            if (!weapon.Auto) _nextTrigger = now + Mathf.Max(weapon.FireInterval, _rules.SemiAutoInterval);
            BotNoise.Emit(eye.x, eye.y, eye.z, _rules.GunshotHearingRadius, _self.ObjectId, NoiseKind.Gunshot);
        }

        // ---- Helpers ----

        // The chest and head sit lower when the stance is lower (crouching), so a bot neither sees nor aims over
        // a target that ducked behind cover.
        static Vector3 ChestOf(Combatant target) => target.transform.position + Vector3.up * (ChestHeight * HeightScale(target));

        static Vector3 HeadOf(Combatant target) => target.transform.position + Vector3.up * (HeadHeight * HeightScale(target));

        static float HeightScale(Combatant target)
        {
            var zones = GameData.Rules.Fire.Zones;
            return zones.Height(target.EyeHeightOffset) / zones.StandingHeight;
        }

        static BotVec ToVec(Vector3 v) => new BotVec(v.x, v.y, v.z);

        static Vector3 ToVector(BotVec v) => new Vector3(v.X, v.Y, v.Z);
    }
}
