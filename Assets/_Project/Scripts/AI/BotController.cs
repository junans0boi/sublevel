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
    public partial class BotController : MonoBehaviour
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
        // Long walks (#77): a far target is reached by stepping along NavMesh.CalculatePath corners, one waypoint
        // WaypointAheadMeters ahead at a time, planned again every RouteRepathSeconds (a path across the island may
        // come back partial; the bot then walks to its end and plans again from there).
        const float RouteLongMeters = 30f;
        const float WaypointAheadMeters = 40f;
        const float RouteRepathSeconds = 3f;
        const float RouteTargetMoveMeters = 8f;
        const float WaypointReachedMeters = 3f;
        const float FarSnapMeters = 15f;
        // No headway (moved less than StallMeters) for StallSeconds while not shooting, healing or reloading: the bot
        // drops what it was after and picks something else; the reason is counted in BotStats.
        const float StallSeconds = 6f;
        const float StallMeters = 2f;
        const float IgnoreSeconds = 10f;
        const float EscapeOnwardMeters = 6f;
        const int KeysWanted = 1;

        // What the lockdown asks of a bot right now (#77), in priority order.
        public enum ZoneGoal { None, Escape, SafeSubZone, ToFinalRegion, Hunt, Elevator }

        public static readonly List<BotController> All = new List<BotController>();

        static readonly float[] CoverRadii = { 4f, 8f, 13f };
        static readonly BotVec[] NoPoints = new BotVec[0];
        static BotVec[] _points = NoPoints;
        static MatchDirector _pointsFor, _namedFor;
        static bool _pointsGiven, _pointsBuilt;
        static int _named;
        int _botIndex;
        BotDifficulty _level = BotDifficulty.Normal;

        Combatant _self;
        CombatantStance _stance;
        NavMeshAgent _agent;
        KeyHolder _keys; // null on a prefab without keys
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

        NavMeshPath _route;
        readonly Vector3[] _corners = new Vector3[64];
        bool _routing;
        float _routeRetryAt;
        ZoneGoal _goal;
        int _goalPass = -1;
        bool _escapeOnward;
        int _skipPass = -1;
        float _skipPassUntil;
        int _ignoredEnemyId = -1;
        float _ignoreEnemyUntil;
        Vector3 _stallFrom;
        float _stallAt;
        float _lastAttackAt = -100f;
        float _hearFactor = 1f;
        Vector3 _enemyVelocity, _enemyLastAt;
        int _enemyVelocityId = -1;
        float _enemyVelocityTime;

        // "봇 N", in spawn order within a match.
        public string DisplayName { get; private set; } = "";
        public BotState State => _intent.State;
        public BotIntent Intent => _intent;
        public Combatant Target => _percept.EnemyVisible ? _enemy : null;
        public bool OnNavMesh => _agent != null && _agent.enabled && _agent.isOnNavMesh;
        // What the lockdown made this bot do on its last update (None when it followed its own brain).
        public ZoneGoal Goal => _goal;

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

        // The supply crate (#144, ADR-113): bots roam toward it too. Its spot counts SupplyRules.BotWeight times
        // among their places to go, until somebody opens it. shortcut: no race to it, no check for rivals nearby.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void HookSupply()
        {
            SupplyDirector.Dropped -= OnSupplyDropped;
            SupplyDirector.Dropped += OnSupplyDropped;
            SupplyDirector.Opened -= OnSupplyOpened;
            SupplyDirector.Opened += OnSupplyOpened;
        }

        static void OnSupplyDropped(SupplyCrate crate)
        {
            SyncMatch();
            var at = ToVec(crate.Position);
            int weight = Mathf.Max(1, SupplyRules.Current.BotWeight);
            var list = new BotVec[_points.Length + weight];
            _points.CopyTo(list, 0);
            for (int i = 0; i < weight; i++) list[_points.Length + i] = at;
            SetPoints(list);
        }

        static void OnSupplyOpened(SupplyCrate crate)
        {
            var at = ToVec(crate.Position);
            var list = new List<BotVec>(_points.Length);
            foreach (var p in _points) if (p.X != at.X || p.Z != at.Z) list.Add(p);
            SetPoints(list.ToArray());
        }

        static void SetPoints(BotVec[] list)
        {
            _points = list;
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

        void OnEnable()
        {
            All.Add(this);
            // Held behind a shut elevator door (disabled) is not being stuck.
            _stallFrom = transform.position;
            _stallAt = Time.time;
            _progressFrom = transform.position;
            _progressAt = Time.time;
        }

        void OnDisable()
        {
            All.Remove(this);
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
        }

        float _nextDoor;

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
            // Knocked down (ADR-072): crawl to a teammate, nothing else.
            if (_self.Knocked.Value)
            {
                TickKnocked(Time.time);
                return;
            }

            float now = Time.time;
            if (now >= _nextSense)
            {
                _nextSense = now + _rules.SenseInterval;
                Sense();
            }
            // Bots open a closed door in their way (G11, ADR-104).
            if (now >= _nextDoor)
            {
                _nextDoor = now + 0.25f;
                DoorSystem.ServerBotOpen(transform.position, OnNavMesh && _agent.velocity.sqrMagnitude > 0.04f ? _agent.velocity : transform.forward);
            }
            ApplyBlindness(now);
            FillSelf();
            _intent = _brain.Think(_percept);
            // Teams (ADR-072): revive a knocked teammate, keep near the leader.
            ApplyTeam(ref _intent, now);
            // Items (ADR-116): heal, boost, throw; blind and burning bodies react.
            ApplyItems(ref _intent, now);
            // One-shot senses are the brain's to remember from here on.
            _percept.Heard = false;
            _percept.Stuck = false;
            Act(now);
            WatchStall(now);
        }

        void Init()
        {
            _ready = true;
            _hearFactor = 1f;
            // The difficulty scales a copy of the data (Normal is the data itself).
            int matchSeed = MatchDirector.Instance != null ? MatchDirector.Instance.Seed : 0;
            // Each bot has its own level, the setting being the centre of a mix (ADR-245).
            _botIndex = _named;
            _level = BotMix.For(BotLevels.Level, matchSeed, _botIndex);
            _rules = BotLevels.For(BotRules.Current, _level);
            if (!ReferenceEquals(_rules, BotRules.Current)) _hearFactor = _rules.GunshotHearingRadius / Mathf.Max(1f, BotRules.Current.GunshotHearingRadius);
            _keys = GetComponent<KeyHolder>();
            SyncMatch();
            int seed = unchecked(matchSeed * 31 + _self.ObjectId * 104729 + 5);
            _rng = new System.Random(seed);
            InitItems();
            _brain = new BotBrain(_rules, unchecked(seed * 7 + 1)) { PointsOfInterest = _points, RoamFilter = RoamAllowed };
            _route = new NavMeshPath();
            _noiseSeen = BotNoise.Count;
            _maxHp = Mathf.Max(1f, _self.Hp.Value);
            _named++;
            DisplayName = BotNames.For(matchSeed, _botIndex);
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
                if (c == null || c == _self || c.Dead.Value || _self.IsTeammateOf(c)) continue;
                if (c.IsPlayer && !rules.TargetsPlayers) continue;
                // One it could neither reach nor hit for a while (a stall) is left alone for a moment.
                if (c.ObjectId == _ignoredEnemyId && Time.time < _ignoreEnemyUntil) continue;
                var chest = ChestOf(c);
                var to = chest - eye;
                float sqr = to.sqrMagnitude;
                bool visible = false;
                if (sqr <= viewSqr)
                {
                    float score = c == _enemy ? sqr * 0.5f : sqr;
                    // One already knocked down is finished off only when nobody on its feet is closer (ADR-072).
                    if (c.Knocked.Value) score *= KnockedTargetFactor;
                    bool inCone = sqr <= rules.CloseSenseRadius * rules.CloseSenseRadius || Vector3.Angle(forward, to) <= halfCone;
                    if (inCone && score < seenScore && (Sees(eye, chest, c) || Sees(eye, HeadOf(c), c)))
                    {
                        seen = c;
                        seenScore = score;
                        visible = true;
                    }
                }
                if (visible) continue;

                // Footsteps: someone moving fast enough, close enough. Crouching and slow walking make them quieter.
                if (c.PlanarSpeed < rules.FootstepMinSpeed) continue;
                var crouch = StanceRules.Current.Crouch;
                float crouched = crouch.EyeDropMeters > 0f ? Mathf.Clamp01(-c.EyeHeightOffset / crouch.EyeDropMeters) : 0f;
                var gait = c.GetComponent<CombatantStance>();
                float radius = BotHearing.FootstepRadius(crouched, gait != null && gait.SlowWalk.Value, rules, StanceRules.Current);
                // A sprint carries further, and what the floor is made of matters (ADR-117, the same ratios the ears of a player use).
                if (sqr <= radius * radius * 2.7f) // 1.25 sprint x 1.3 metal, squared: only then can the floor matter
                {
                    var loco = GameData.Rules.Locomotion;
                    bool sprint = SoundRules.IsSprint(c.PlanarSpeed, loco.WalkSpeed, loco.SprintSpeed, gait != null ? gait.MoveFactor : 1f);
                    var feet = c.transform.position;
                    var surface = BotHearing.SurfaceAt != null ? BotHearing.SurfaceAt(feet.x, feet.y, feet.z) : FootSurface.Concrete;
                    radius *= (sprint ? SoundRules.SprintStepFactor : 1f) * SoundRules.SurfaceLoudness(surface);
                }
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
                float heardRadius = noise.Radius * _hearFactor;
                if (sqr > heardRadius * heardRadius || sqr >= heardSqr) continue;
                // A closed door on the way muffles it: the radius shrinks for each one (G11, ADR-104).
                int shut = DoorSystem.ClosedBetween(at, eye);
                if (shut > 0 && Mathf.Sqrt(sqr) > heardRadius * Mathf.Pow(DoorRules.Current.ClosedSoundFactor, shut)) continue;
                heard = true;
                heardAt = at;
                heardSqr = sqr;
            }
            _noiseSeen = count;
            if (heard)
            {
                // Ears are not a map: the place is off by a share of the distance, so a far shot is only a direction.
                float off = Mathf.Min(Mathf.Sqrt(heardSqr) * rules.HearingErrorFraction, rules.HearingErrorMaxMeters);
                if (off > 0f)
                {
                    double a = _rng.NextDouble() * System.Math.PI * 2;
                    float r = Mathf.Sqrt((float)_rng.NextDouble()) * off;
                    heardAt += new Vector3(Mathf.Cos((float)a) * r, 0f, Mathf.Sin((float)a) * r);
                }
                _percept.Heard = true;
                _percept.HeardPosition = ToVec(heardAt);
            }

            SenseLoot(eye);
        }

        // A clear line from the eyes to that point on that combatant.
        bool Sees(Vector3 eye, Vector3 point, Combatant target)
        {
            // A smoke cloud hides whoever is in or behind it.
            if (SmokeSight.Blocks(eye, point)) return false;
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
            var h = _healing;
            var t = _throwables;
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
                // A melee weapon only fills an empty melee slot (ADR-073); guns want a free gun slot.
                bool slotFree = isWeapon && GameData.Weapons.Find(item.WeaponId.Value)?.IsMelee == true
                    ? _self.WeaponInSlot(Loadout.MeleeSlot) == 0 : emptySlot;
                // A key (ADR-058 (7)) gets it through a locked pass; one is enough.
                bool wanted = kind == LootKind.Key
                    ? _keys != null && _keys.Keys.Value < KeysWanted
                    : BotBrain.WantsLoot(kind, slotFree, usesAmmo, reserve, medkits, rules) || WantsGear(item, kind)
                      || BotItems.WantsLoot(kind, h != null ? h.Bandages.Value : 99, h != null ? h.FirstAids.Value : 99,
                          h != null ? h.Drinks.Value : 99, h != null ? h.Pills.Value : 99, h != null ? h.Syringes.Value : 99,
                          t != null ? t.Grenades.Value : 99, t != null ? t.Smokes.Value : 99, t != null ? t.Flashes.Value : 99, t != null ? t.Molotovs.Value : 99, rules);
                if (!wanted) continue;
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

        // A helmet or vest better than the one worn (the server takes only a better piece).
        bool WantsGear(LootItem item, LootKind kind)
        {
            if (!Armor.IsArmor(kind)) return false;
            var gear = _gear;
            if (gear == null) return false;
            var slot = Armor.SlotOf(kind);
            return item.Tier.Value > gear.TierOf(slot);
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

            // A melee weapon is not a gun to the brain: it fights with it like with fists (ADR-073, see Punch).
            var weapon = _self.ActiveWeapon;
            if (weapon != null && weapon.IsMelee) weapon = null;
            _percept.HasWeapon = weapon != null;
            _percept.WeaponId = weapon != null ? weapon.Id : 0;
            _percept.Mag = weapon != null ? _self.ActiveMag : 0;
            _percept.MagSize = weapon != null ? weapon.MagSize : 0;
            _percept.Reserve = weapon != null ? _self.ReserveFor(weapon.Ammo) : 0;
            _percept.WeaponRange = weapon != null ? weapon.Range : 0f;
            _percept.Reloading = _self.Reloading.Value;
            _percept.Medkits = HealCount;

            int otherSlot = _self.OtherWeaponSlot();
            var other = GameData.Weapons.Find(otherSlot >= 0 ? _self.WeaponInSlot(otherSlot) : 0);
            // A gun out of rounds is put away for a melee weapon in another slot (it never runs dry); never the reverse.
            _percept.OtherSlotUsable = other != null && (other.IsMelee ? weapon != null : _self.MagInSlot(otherSlot) > 0 || _self.ReserveFor(other.Ammo) > 0);

            // Between two looks the target is followed without a new ray, as long as it is still there.
            if (_percept.EnemyVisible)
            {
                if (_enemy == null || _enemy.Dead.Value)
                {
                    _percept.EnemyVisible = false;
                    _enemy = null;
                }
                else
                {
                    var chest = ChestOf(_enemy);
                    TrackEnemy(chest);
                    _percept.EnemyPosition = ToVec(chest);
                }
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
            var aim = intent.Aim && !Melee.CanStrike(weapon) ? AimMode.Shoulder : AimMode.Hip;
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
                    BotServerActions.Heal(_self);
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
            if (intent.Fire && weapon != null && !weapon.IsMelee) Fire(intent, weapon, now);
            // Empty hands or a melee weapon with the enemy in sight (the brain backs off then, still facing it):
            // punch or swing when it is that close.
            else if (Melee.CanStrike(weapon) && _percept.EnemyVisible) Punch();
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
            var at = transform.position;
            // The lockdown first (#77): out of a region that is closing, out of a closing sub-zone of the final
            // region, into the final region once it is named, and at the very end toward the others. Urgent goals
            // take over the legs whatever the brain wants (it still aims and shoots); the others only when the brain
            // is just wandering or looking into something.
            _goal = CurrentZoneGoal(at, out var goalTarget, out bool urgent, out _goalPass);
            if (_goal != ZoneGoal.Escape) _escapeOnward = false;
            var zones = PollutionDirector.Instance;
            // The very end with nothing to shoot with: running away would never end the match. Fists out, go at it.
            if (_percept.EnemyVisible && _enemy != null && !CanFight && zones != null && zones.OnlyFinalRegionLeft)
            {
                if (!Melee.CanStrike(_self.ActiveWeapon)) _self.ServerSetHolstered(true); // a melee weapon stays in hand
                _goal = ZoneGoal.Hunt;
                goalTarget = _enemy.transform.position;
                urgent = true;
            }
            // A melee weapon in hand and no gun to use (ADR-073): run at an enemy that close instead of backing off.
            var inHand = _self.ActiveWeapon;
            if (_percept.EnemyVisible && _enemy != null && !CanFight && inHand != null && inHand.IsMelee
                && Flat(_enemy.transform.position - at) <= MeleeRules.Current.BotChargeMeters)
            {
                _goal = ZoneGoal.Hunt;
                goalTarget = _enemy.transform.position;
                urgent = true;
            }
            bool zoneMove = _goal != ZoneGoal.None && (urgent || intent.State == BotState.Roam || intent.State == BotState.Investigate);
            if (!zoneMove) _goal = ZoneGoal.None;

            if (!intent.Move && !zoneMove)
            {
                if (!_agent.isStopped)
                {
                    _agent.isStopped = true;
                    _agent.velocity = Vector3.zero;
                }
                _hasDestination = false;
                _routing = false;
                _progressFrom = transform.position;
                _progressAt = now;
                return;
            }

            var target = zoneMove ? goalTarget : ToVector(intent.MoveTarget);
            if (intent.SeekCover && !zoneMove)
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
            float speed = (intent.Sprint || zoneMove) && !intent.Crouch ? locomotion.SprintSpeed : locomotion.WalkSpeed;
            if (_stance != null) speed *= _stance.MoveFactor;
            _agent.speed = speed * Combatant.StaggerMoveFactorOf(_self); // stopping power (ADR-182)

            bool far = Flat(target - at) > RouteLongMeters;
            float moveLimit = far ? RouteTargetMoveMeters : RepathMoveMeters;
            // Stepping along a long route: the waypoint is (nearly) reached, plan the next one now.
            bool waypointDone = _routing && !_agent.pathPending && _agent.remainingDistance <= WaypointReachedMeters;
            // No route last time: a long search is not run again every frame.
            if (far && !_hasDestination && now < _routeRetryAt) return;
            if (!_hasDestination || now >= _nextRepath || waypointDone || (target - _destination).sqrMagnitude > moveLimit * moveLimit)
            {
                _nextRepath = now + (far ? RouteRepathSeconds : RepathSeconds);
                if (!_hasDestination)
                {
                    _progressFrom = transform.position;
                    _progressAt = now;
                }
                _hasDestination = true;
                _destination = target;
                _routing = false;
                if (!SnapToNavMesh(target, far, out var onMesh))
                {
                    GiveUp(intent, now);
                    return;
                }
                var next = onMesh;
                if (far)
                {
                    if (!NextWaypoint(onMesh, out next))
                    {
                        _routeRetryAt = now + RouteRepathSeconds;
                        GiveUp(intent, now);
                        return;
                    }
                    _routing = true;
                }
                if (!_agent.SetDestination(next))
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
                bool moved = (transform.position - _progressFrom).sqrMagnitude >= ProgressMeters * ProgressMeters;
                if (!arrived && !moved) GiveUp(intent, now);
                // At the end of a waypoint (or of a partial route), or a lockdown goal: plan on from here.
                else if (arrived && (_routing || zoneMove)) _hasDestination = false;
                // Arrived as far as the navmesh goes, yet the brain still wants to get closer (a pickup out of
                // reach, a point of interest off the navmesh): let it choose something else.
                else if (arrived && !intent.PickUp && !intent.SeekCover && intent.State != BotState.Engage) GiveUp(intent, now);
                _progressFrom = transform.position;
                _progressAt = now;
            }
        }

        // A weapon with rounds in hand or in another slot (what the brain calls being able to fight).
        bool CanFight => (_percept.HasWeapon && (_percept.Mag > 0 || _percept.Reserve > 0)) || _percept.OtherSlotUsable;

        // The lockdown's wish for a bot standing at `at` (see ZoneGoal), with where to go and whether it overrides
        // everything the brain wants. `pass` is the pass of an escape, else -1.
        ZoneGoal CurrentZoneGoal(Vector3 at, out Vector3 target, out bool urgent, out int pass)
        {
            target = default;
            urgent = false;
            pass = -1;
            var zones = PollutionDirector.Instance;
            if (zones == null || zones.RegionCount == 0) return ZoneGoal.None;

            int skip = Time.time < _skipPassUntil ? _skipPass : -1;
            // A warning on the region it stands in: leave after a reaction delay by difficulty, before the front comes (ADR-245).
            if (TryWarnedLeaveTarget(zones, at, out target, out urgent))
            {
                _warnedLeaves++;
                _goalPass = -1;
                return ZoneGoal.Escape;
            }
            if (zones.TryEscapeTarget(at, skip, out target, out pass, out int toRegion))
            {
                urgent = true;
                // A running station nearer than the target (ADR-079): ride out instead; standing in it starts the ride.
                // (Until the key elevators replace free rides, ADR-170 / WP 7.)
                var rides = ElevatorRides.Instance;
                if (rides != null && rides.ServerBotRide(_self, Flat(target - at), out var station))
                {
                    target = station;
                    pass = -1;
                    return ZoneGoal.Elevator;
                }
                // The border need not run exactly through the pass: once at the point beyond it while still counted
                // in the closing region, keep on toward the middle of the region beyond.
                if (Flat(target - at) < EscapeOnwardMeters) _escapeOnward = true;
                if (_escapeOnward) target = zones.RegionCentre(toRegion);
                return ZoneGoal.Escape;
            }
            int final = zones.FinalRegion;
            if (final < 0) return ZoneGoal.None;
            if (zones.RegionIndexAt(at) != final)
            {
                target = zones.RegionCentre(final);
                return ZoneGoal.ToFinalRegion;
            }
            // Only the final region is left: nobody may hide until the end, the bots go looking for each other.
            if (zones.OnlyFinalRegionLeft && TryNearestEnemy(at, out target)) return ZoneGoal.Hunt;
            return ZoneGoal.None;
        }

        // Counts the legs this bot started because of a warning, for the statistics (BotStats).
        public static int WarnedLeaveStarts;
        int _warnedLeaves;

        bool TryWarnedLeaveTarget(PollutionDirector zones, Vector3 at, out Vector3 target, out bool urgent)
        {
            target = default;
            urgent = false;
            int own = zones.RegionIndexAt(at);
            if (own < 0 || zones.StateOf(own) != ZoneState.Warning) return false;
            float left = zones.SecondsLeft(own);
            if (left < 0f) return false;
            float since = Mathf.Max(0f, zones.Rules.WarningLeadSeconds - left);
            if (!BotPollutionPlan.ShouldLeave(PollutionState.Warning, since, left, zones.IsDoomed(at), _level, unchecked(_botIndex * 7919 + _self.ObjectId))) return false;
            // The middle of the nearest region that is neither warned nor lost.
            float best = float.MaxValue;
            bool found = false;
            for (int r = 0; r < zones.RegionCount; r++)
            {
                if (r == own || zones.StateOf(r) != ZoneState.Safe) continue;
                var c = zones.RegionCentre(r);
                if (zones.IsDoomed(c)) continue;
                float d = Flat(c - at);
                if (d < best) { best = d; target = c; found = true; }
            }
            if (!found) return false;
            urgent = left <= 40f; // before that the brain may still fight; in the last stretch walking out wins
            if (_warnedLeaves == 0) WarnedLeaveStarts++;
            return true;
        }

        // The nearest living combatant this bot may fight (endgame knowledge, like a shrinking final circle). One
        // standing where the lockdown is closing in is only chosen when nobody else is left: it will come out or die.
        bool TryNearestEnemy(Vector3 at, out Vector3 position)
        {
            position = default;
            var zones = PollutionDirector.Instance;
            float best = float.MaxValue, bestDoomed = float.MaxValue;
            Vector3 doomedAt = default;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == _self || c.Dead.Value || !c.IsServerInitialized || _self.IsTeammateOf(c)) continue;
                if (c.IsPlayer && !_rules.TargetsPlayers) continue;
                var p = c.transform.position;
                float d = (p - at).sqrMagnitude;
                if (zones != null && zones.IsDoomed(p))
                {
                    if (d < bestDoomed) { bestDoomed = d; doomedAt = p; }
                    continue;
                }
                if (d >= best) continue;
                best = d;
                position = p;
            }
            if (best < float.MaxValue) return true;
            position = doomedAt;
            return bestDoomed < float.MaxValue;
        }

        // Points of interest worth wandering to: not where the lockdown is about to be, and inside the final region
        // once it is known.
        bool RoamAllowed(BotVec point)
        {
            var zones = PollutionDirector.Instance;
            if (zones == null || zones.RegionCount == 0) return true;
            var p = ToVector(point);
            if (zones.IsDoomed(p)) return false;
            int final = zones.FinalRegion;
            return final < 0 || zones.RegionIndexAt(p) == final;
        }

        // The navmesh point for a target; far targets (region centres, points past a pass) may lie farther off it.
        static bool SnapToNavMesh(Vector3 target, bool far, out Vector3 onMesh)
        {
            onMesh = default;
            if (NavMesh.SamplePosition(target, out var hit, DestinationSnapMeters, NavMesh.AllAreas))
            {
                onMesh = hit.position;
                return true;
            }
            if (!far || !NavMesh.SamplePosition(target, out hit, FarSnapMeters, NavMesh.AllAreas)) return false;
            onMesh = hit.position;
            return true;
        }

        // The next stop on the way to a far goal: WaypointAheadMeters along the planned path (or its end). False
        // when there is no path at all, or a partial one that ends where the bot already stands.
        bool NextWaypoint(Vector3 goal, out Vector3 waypoint)
        {
            waypoint = goal;
            var from = transform.position;
            if (_route == null) _route = new NavMeshPath();
            if (!NavMesh.CalculatePath(from, goal, NavMesh.AllAreas, _route) || _route.status == NavMeshPathStatus.PathInvalid) return false;
            int count = _route.GetCornersNonAlloc(_corners);
            if (count == 0) return false;
            waypoint = _corners[count - 1];
            float walked = 0f;
            for (int i = 1; i < count; i++)
            {
                var a = _corners[i - 1];
                var b = _corners[i];
                float segment = Vector3.Distance(a, b);
                if (walked + segment >= WaypointAheadMeters)
                {
                    waypoint = segment > 1e-3f ? Vector3.Lerp(a, b, (WaypointAheadMeters - walked) / segment) : b;
                    break;
                }
                walked += segment;
            }
            return !(_route.status == NavMeshPathStatus.PathPartial && Flat(waypoint - from) < 1.5f);
        }

        // ---- Stall detector (#77) ----

        void WatchStall(float now)
        {
            // No navmesh (a test box): the bot holds its ground on purpose.
            if (!OnNavMesh) return;
            var position = transform.position;
            bool busy = _intent.State == BotState.Heal || _self.Reloading.Value || now - _lastAttackAt < StallSeconds;
            if (busy || (position - _stallFrom).sqrMagnitude > StallMeters * StallMeters)
            {
                _stallFrom = position;
                _stallAt = now;
                return;
            }
            if (now - _stallAt < StallSeconds) return;

            string reason = StallReason();
            BotStats.CountStall(reason);
            Debug.Log($"[Bot] {DisplayName} made no headway for {StallSeconds:0} s ({reason}) at {position}; picking a new target");
            // Whatever it was after is dropped: the enemy it could neither reach nor hit, the pass it could not get
            // to, the pickup or the place it was walking to.
            if (_enemy != null && (_percept.EnemyVisible || _intent.State == BotState.Engage))
            {
                _ignoredEnemyId = _enemy.ObjectId;
                _ignoreEnemyUntil = now + IgnoreSeconds;
                _enemy = null;
                _percept.EnemyVisible = false;
            }
            if (_goal == ZoneGoal.Escape && _goalPass >= 0)
            {
                _skipPass = _goalPass;
                _skipPassUntil = now + IgnoreSeconds;
                _escapeOnward = false;
            }
            GiveUp(_intent, now);
            _stallFrom = position;
            _stallAt = now;
        }

        string StallReason()
        {
            switch (_goal)
            {
                case ZoneGoal.Escape:
                    return "escape";
                case ZoneGoal.SafeSubZone: return "safe-subzone";
                case ZoneGoal.ToFinalRegion: return "to-final-region";
                case ZoneGoal.Hunt: return "hunt";
                case ZoneGoal.Elevator: return "elevator";
            }
            switch (_intent.State)
            {
                case BotState.Engage: return _percept.EnemyVisible ? "engage-unreachable" : "engage";
                case BotState.Loot: return "loot";
                case BotState.Investigate: return "investigate";
                case BotState.Retreat: return "retreat";
                case BotState.Heal: return "heal";
                default: return _intent.Move ? "roam" : "idle";
            }
        }

        void GiveUp(in BotIntent intent, float now)
        {
            _percept.Stuck = true;
            _hasDestination = false;
            _routing = false;
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
            // A bullet that is not judged at once takes time to arrive: lead a target that moves (ADR-117).
            var ballistics = GameData.Rules.Ballistics;
            if (_rules.AimLead > 0f && distance > ballistics.InstantMeters)
            {
                var lead = _enemyVelocity;
                lead.y = 0f;
                to += lead * (Ballistics.FlightSeconds(Ballistics.MuzzleSpeed(weapon, ballistics), distance) * _rules.AimLead);
                distance = to.magnitude;
            }
            // The body has to have turned to the target first.
            var flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 1e-4f && Vector3.Angle(transform.forward, flat) > _rules.FireConeDeg) return;

            float error = BotAim.ErrorDeg(_rules, distance, intent.SecondsOnTarget);
            // Uniform inside the error cone.
            double angle = _rng.NextDouble() * System.Math.PI * 2;
            float radius = Mathf.Sqrt((float)_rng.NextDouble()) * error;
            var off = Quaternion.Euler(Mathf.Sin((float)angle) * radius, Mathf.Cos((float)angle) * radius, 0f);
            if (!_self.ServerFire(Quaternion.LookRotation(to / distance) * off * Vector3.forward, distance)) return;
            _lastAttackAt = now;
            if (!weapon.Auto) _nextTrigger = now + Mathf.Max(weapon.FireInterval, _rules.SemiAutoInterval);
            BotNoise.Emit(eye.x, eye.y, eye.z, _self.ShotHearingRadius(_rules.GunshotHearingRadius), _self.ObjectId, NoiseKind.Gunshot);
        }

        // An enemy right in front gets punched (ADR-059), through the same server method as a player's punch;
        // the server keeps the rate and checks reach and the line.
        // A melee weapon in hand swings the same way with its own reach and cone (ADR-073).
        void Punch()
        {
            var inHand = _self.ActiveWeapon;
            if (_enemy == null || !Melee.CanStrike(inHand)) return;
            var melee = MeleeRules.Current;
            var strike = Melee.StrikeOf(inHand, melee);
            float reach = Melee.BotReach(inHand, melee);
            var flat = _enemy.transform.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > reach * reach) return;
            if (flat.sqrMagnitude > 1e-4f && Vector3.Angle(transform.forward, flat) > strike.ConeDeg * 0.5f) return;
            if (_self.ServerPunch(ChestOf(_enemy) - _self.EyePosition)) _lastAttackAt = Time.time;
        }

        // The target's velocity from where it was a moment ago (smoothed; zero for a new target).
        void TrackEnemy(Vector3 chest)
        {
            float now = Time.time;
            if (_enemy.ObjectId != _enemyVelocityId || now - _enemyVelocityTime > 1f)
            {
                _enemyVelocityId = _enemy.ObjectId;
                _enemyVelocity = Vector3.zero;
            }
            else if (now > _enemyVelocityTime + 1e-4f)
                _enemyVelocity = Vector3.Lerp(_enemyVelocity, (chest - _enemyLastAt) / (now - _enemyVelocityTime), 0.25f);
            _enemyLastAt = chest;
            _enemyVelocityTime = now;
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

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    }
}
