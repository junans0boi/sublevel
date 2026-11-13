using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // One instance of damage on the server: how much, who did it, where it landed and why (ADR-053).
    public struct DamageInfo
    {
        public float Amount;
        public Combatant Attacker; // null when nobody did it (a fall, the zone)
        public HitZone Zone;
        public int WeaponId;       // 0 when no weapon was involved
        public DamageCause Cause;
    }

    // Anything that can shoot and be shot: players, the training dummy, later bots.
    // The server owns health and loadout; clients read the SyncVars and send requests (ADR-029).
    public partial class Combatant : NetworkBehaviour
    {
        public const float PickupRange = 3f;

        [SerializeField] bool _isPlayer;
        [SerializeField] float _maxHealth = 100f;
        [SerializeField] float _eyeHeight = 1.6f;

        public readonly SyncVar<float> Hp = new SyncVar<float>();
        public readonly SyncVar<bool> Dead = new SyncVar<bool>();
        public readonly SyncVar<bool> Reloading = new SyncVar<bool>();
        public readonly SyncVar<int> ActiveSlot = new SyncVar<int>();
        // The five weapon slots (ADR-058): 0-1 primary, 2 sidearm, 3 melee, 4 throwable. Weapon id and magazine of
        // each; use WeaponInSlot / MagInSlot to read by index. 3 and 4 stay empty for now.
        public const int SlotCount = Loadout.SlotCount;
        public readonly SyncVar<int> Slot0 = new SyncVar<int>();
        public readonly SyncVar<int> Slot1 = new SyncVar<int>();
        public readonly SyncVar<int> Slot2 = new SyncVar<int>();
        public readonly SyncVar<int> Slot3 = new SyncVar<int>();
        public readonly SyncVar<int> Slot4 = new SyncVar<int>();
        public readonly SyncVar<int> Mag0 = new SyncVar<int>();
        public readonly SyncVar<int> Mag1 = new SyncVar<int>();
        public readonly SyncVar<int> Mag2 = new SyncVar<int>();
        public readonly SyncVar<int> Mag3 = new SyncVar<int>();
        public readonly SyncVar<int> Mag4 = new SyncVar<int>();
        // Bit i set: the automatic weapon in slot i is set to single shots (ADR-058).
        public readonly SyncVar<int> SingleFireSlots = new SyncVar<int>();
        // The weapon is put away with X: the hands count as empty (ActiveWeaponId is 0) until a slot is chosen.
        public readonly SyncVar<bool> Holstered = new SyncVar<bool>();
        // Reserve rounds per calibre (ADR-054), in AmmoType order. Four plain SyncVars: the simplest robust pattern.
        public readonly SyncVar<int> ReservePistol9 = new SyncVar<int>();
        public readonly SyncVar<int> ReserveRifle556 = new SyncVar<int>();
        public readonly SyncVar<int> ReserveShotgun12 = new SyncVar<int>();
        public readonly SyncVar<int> ReserveRifle762 = new SyncVar<int>();
        public readonly SyncVar<int> Medkits = new SyncVar<int>();
        // Stance, requested by the owner (ADR-045): how the weapon is aimed and which way the body leans.
        public readonly SyncVar<int> Aim = new SyncVar<int>();   // AimMode
        public readonly SyncVar<int> Lean = new SyncVar<int>();  // -1 left, 0 upright, 1 right

        public static readonly List<Combatant> All = new List<Combatant>();
        public static Combatant LocalPlayer { get; private set; }

        // Server only: raised once when a combatant dies, with the damage that killed it (victim, last damage; the
        // amount is what was left after ServerDamageFilter). It fires after the victim's SyncVars are updated.
        // The event is static, so it outlives scenes: every subscriber must unsubscribe in its own
        // OnDestroy/OnDisable (or OnStopServer). ResetStatics clears it when a play session starts.
        public static event Action<Combatant, DamageInfo> ServerDied;

        // Server only: lets equipment change incoming damage (armour). Gets the damage as dealt and returns the
        // amount to apply; null means "as dealt". Set by a component on this combatant.
        public Func<DamageInfo, float> ServerDamageFilter { get; set; }

        // Set by the stance component on every side (server, owner, observers) so all of them agree on the eyes:
        // metres added to the upright eye height (negative when crouched), and the stance's share of the spread.
        public float EyeHeightOffset { get; set; }
        public float StanceSpreadFactor { get; set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ServerDied = null;
            All.Clear();
            LocalPlayer = null;
        }

        // Raised on every client for each projectile: (origin, end point).
        public event Action<Vector3, Vector3> ShotFired;

        // The same projectile with what it ended on, for effects and hit confirmation. Raised right after ShotFired.
        public struct ShotResult
        {
            public Vector3 Origin, End;
            public Vector3 Normal;   // of the surface that was hit; back along the shot when nothing was
            public ShotHit Hit;
            public Combatant Victim; // null unless a combatant was hit
            public HitZone Zone;     // where on the victim; Body when no combatant was hit

            public readonly bool Head => Victim != null && Zone == HitZone.Head;
        }

        public delegate void ShotResolvedHandler(in ShotResult shot);
        public event ShotResolvedHandler ShotResolved;

        // One punch (ADR-059 (1)), raised on every client: which hand, and what it landed on.
        public struct PunchResult
        {
            public bool Right;       // left and right take turns
            public ShotHit Hit;      // None (a swing at the air), Combatant or Kill
            public Combatant Victim; // null unless a combatant was hit
            public HitZone Zone;     // where on the victim; Body when nothing was hit

            public readonly bool Head => Victim != null && Zone == HitZone.Head;
        }

        public delegate void PunchedHandler(in PunchResult punch);
        public event PunchedHandler Punched;

        static readonly float[] YawBuf = new float[32];
        static readonly float[] PitchBuf = new float[32];
        static readonly RaycastHit[] LeanHits = new RaycastHit[8];
        static readonly RaycastHit[] PunchHits = new RaycastHit[8];
        // The punch event byte: the hand in bit 0, the hit kind in bits 1-2, the zone from bit 4.
        const int PunchRightBit = 1, PunchHitShift = 1, PunchHitMask = 3, PunchZoneShift = 4;

        const float LeanProbeRadius = 0.15f;
        // Kept between the leaned eye and a wall; larger than the camera's own collision radius so the
        // third-person camera cast never starts inside the wall.
        const float LeanWallClearance = 0.1f;
        // The hit kind and the hit zone travel in one byte of the shot effect: kind in the low half, zone above.
        const int ShotZoneShift = 4;
        const int ShotHitMask = (1 << ShotZoneShift) - 1;
        // Planar speed is measured over a short window: on the server a remote owner's position arrives in steps.
        const float SpeedWindowSeconds = 0.1f;
        const float TeleportDistance = 5f;
        // Same ground probe as the presentation side (CombatantMotion).
        const float GroundProbeRadius = 0.2f;
        const float GroundProbeStartHeight = 0.5f;

        Health _health;
        Loadout _loadout;
        System.Random _rng;
        readonly LeanState _lean = new LeanState();
        float _leanOffset;
        Vector3 _speedFrom;
        float _speedClock;
        // Server: punch timing and which hand goes next.
        readonly FireCadence _punchCadence = new FireCadence();
        int _punches;

        public bool IsPlayer => _isPlayer;
        // Where this combatant sees and shoots from. Leaning moves it sideways (never through a wall).
        public Vector3 EyePosition => UprightEye + transform.right * _leanOffset + Vector3.down * (GameData.Rules.Lean.EyeDropMeters * Mathf.Abs(_lean.Value));
        public AimMode AimMode => (AimMode)Aim.Value;
        // -1..1, eased: how far the lean has actually got.
        public float LeanAmount => _lean.Value;
        Vector3 UprightEye => transform.position + Vector3.up * (_eyeHeight + EyeHeightOffset);
        // Metres per second along the ground, averaged over the last moment. Measured on every side.
        public float PlanarSpeed { get; private set; }
        // No ground close under the feet right now.
        public bool Airborne => !Physics.SphereCast(transform.position + Vector3.up * GroundProbeStartHeight, GroundProbeRadius, Vector3.down, out _,
            GroundProbeStartHeight - GroundProbeRadius + GameData.Rules.Jump.GroundProbeMeters, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        // The spread cone of the active weapon is its SpreadDeg times this: aim mode, stance and own movement.
        // The server uses exactly this for a shot; clients can show it (crosshair).
        public float SpreadFactor => SpreadFactorWith(ActiveWeapon);

        float SpreadFactorWith(WeaponDef weapon)
        {
            float factor = GameData.Rules.Aim.For(AimMode).SpreadFactor * StanceSpreadFactor;
            // [VEHICLE HOOK] ADR-065 (5): seated, one fixed factor instead of the moving / airborne ones.
            if (VehicleSeat.IsSeated(this)) return weapon == null ? factor : factor * VehicleSeat.SpreadFactor;
            return weapon == null ? factor : factor * DamageRules.MovementSpread(weapon, PlanarSpeed, Airborne, GameData.Rules.Fire);
        }
        // What is in the hands: 0 when the slot in hand is empty or the weapon is put away (Holstered). Everything
        // that shows or lets the weapon be used reads this, so a put-away weapon cannot be fired, aimed or drawn.
        public int ActiveWeaponId => Holstered.Value ? 0 : WeaponInSlot(ActiveSlot.Value);
        public int ActiveMag => Holstered.Value ? 0 : MagInSlot(ActiveSlot.Value);
        public WeaponDef ActiveWeapon => GameData.Weapons.Find(ActiveWeaponId);
        // The weapon and magazine in a slot, whether or not it is in hand.
        public int WeaponInSlot(int slot)
        {
            switch (slot)
            {
                case 0: return Slot0.Value;
                case 1: return Slot1.Value;
                case 2: return Slot2.Value;
                case 3: return Slot3.Value;
                case 4: return Slot4.Value;
                default: return 0;
            }
        }
        public int MagInSlot(int slot)
        {
            switch (slot)
            {
                case 0: return Mag0.Value;
                case 1: return Mag1.Value;
                case 2: return Mag2.Value;
                case 3: return Mag3.Value;
                case 4: return Mag4.Value;
                default: return 0;
            }
        }
        // Does the weapon in this slot fire while the button is held? Single-shot weapons never do.
        public bool IsAutoFire(int slot)
        {
            var weapon = GameData.Weapons.Find(WeaponInSlot(slot));
            return weapon != null && weapon.Auto && (SingleFireSlots.Value & (1 << slot)) == 0;
        }
        public bool ActiveAutoFire => !Holstered.Value && IsAutoFire(ActiveSlot.Value);
        // The fire mode shown for a slot: "연사" or "단발"; empty for an empty slot.
        public string FireModeOf(int slot)
        {
            var weapon = GameData.Weapons.Find(WeaponInSlot(slot));
            return weapon == null ? "" : Loadout.FireModeName(IsAutoFire(slot));
        }
        // Seconds between shots of the weapon in hand in its current mode (single shots on an automatic weapon are slower).
        public double FireInterval(WeaponDef weapon) =>
            weapon.Auto && !ActiveAutoFire ? Math.Max(weapon.FireInterval, GameData.Rules.Slots.SingleModeMinInterval) : weapon.FireInterval;
        // Loadout questions for the bots.
        public bool HasFreeWeaponSlot => WeaponInSlot(0) == 0 || WeaponInSlot(1) == 0 || WeaponInSlot(2) == 0;
        public bool HasAnyWeapon => WeaponInSlot(0) != 0 || WeaponInSlot(1) != 0 || WeaponInSlot(2) != 0;
        public bool CarriesWeaponFor(AmmoType ammo)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                var weapon = GameData.Weapons.Find(WeaponInSlot(i));
                if (weapon != null && weapon.Ammo == ammo) return true;
            }
            return false;
        }
        // Another slot holding a weapon to switch to (the first), or -1.
        public int OtherWeaponSlot()
        {
            for (int i = 0; i < SlotCount; i++)
                if (i != ActiveSlot.Value && WeaponInSlot(i) != 0) return i;
            return -1;
        }

        public int ReserveFor(AmmoType type)
        {
            switch (type)
            {
                case AmmoType.Pistol9: return ReservePistol9.Value;
                case AmmoType.Rifle556: return ReserveRifle556.Value;
                case AmmoType.Shotgun12: return ReserveShotgun12.Value;
                case AmmoType.Rifle762: return ReserveRifle762.Value;
                default: return 0;
            }
        }

        // What a non-player leaves of each calibre when it dies; a training dummy carries 9999 rounds.
        public const int DroppedReserveCap = 60;

        // Server: who else may take loot kinds the loadout does not know (armour, grenades).
        ILootReceiver[] _receivers;
        CombatantOptics _optics;
        // Null on a prefab without optics.
        CombatantOptics OpticsPart => _optics != null ? _optics : (_optics = GetComponent<CombatantOptics>());
        // Server: the bag limit (null on a prefab without one: no limit).
        CombatantCarry _carry;

        public override void OnStartNetwork()
        {
            All.Add(this);
            _speedFrom = transform.position;
        }

        public override void OnStopNetwork()
        {
            All.Remove(this);
            if (LocalPlayer == this) LocalPlayer = null;
        }

        // Scene unloads destroy objects without the network callbacks.
        void OnDestroy() => OnStopNetwork();

        public override void OnStartServer()
        {
            _health = new Health(_maxHealth);
            _loadout = new Loadout(GameData.Weapons, GameData.Rules.MedkitHeal);
            // Derived from the match seed so a match can be replayed.
            int matchSeed = MatchDirector.Instance != null ? MatchDirector.Instance.Seed : 0;
            _rng = new System.Random(unchecked(matchSeed * 31 + ObjectId * 7919 + 17));
            _receivers = GetComponents<ILootReceiver>();
            _carry = GetComponent<CombatantCarry>();
            Push();
        }

        public override void OnStartClient()
        {
            if (_isPlayer && IsOwner) LocalPlayer = this;
        }

        void Update()
        {
            if (IsServerInitialized && _loadout != null)
            {
                if (_loadout.Tick(Time.timeAsDouble)) Push();
                // Nothing to aim with empty hands, and the dead neither aim nor lean.
                if (Aim.Value != 0 && (_health.IsDead || ActiveWeaponId == 0)) Aim.Value = 0;
                if (Holstered.Value && _health.IsDead) Holstered.Value = false;
                if (Lean.Value != 0 && _health.IsDead) Lean.Value = 0;
            }
            TickLean();
            TickSpeed();
        }

        void TickSpeed()
        {
            _speedClock += Time.deltaTime;
            if (_speedClock < SpeedWindowSeconds) return;
            var moved = transform.position - _speedFrom;
            // A teleport (spawn, test hook) is not movement.
            bool teleported = moved.sqrMagnitude > TeleportDistance * TeleportDistance;
            moved.y = 0f;
            PlanarSpeed = teleported ? 0f : moved.magnitude / _speedClock;
            _speedFrom = transform.position;
            _speedClock = 0f;
        }

        // Runs on every side from the replicated lean, so the eye position agrees between server and clients.
        void TickLean()
        {
            var rules = GameData.Rules.Lean;
            _lean.Tick(Lean.Value, Time.deltaTime, rules.ChangePerSecond);
            float wanted = rules.OffsetMeters * Mathf.Abs(_lean.Value);
            if (wanted < 1e-4f) { _leanOffset = 0f; return; }
            var side = transform.right * Mathf.Sign(_lean.Value);
            int hits = Physics.SphereCastNonAlloc(UprightEye, LeanProbeRadius, side, LeanHits, wanted, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                var hit = LeanHits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue;
                wanted = Mathf.Min(wanted, Mathf.Max(0f, hit.distance - LeanWallClearance));
            }
            _leanOffset = wanted * Mathf.Sign(_lean.Value);
        }

        void Push()
        {
            Hp.Value = _health.Current;
            Dead.Value = _health.IsDead;
            Reloading.Value = _loadout.IsReloading;
            ActiveSlot.Value = _loadout.ActiveSlot;
            Slot0.Value = _loadout.WeaponInSlot(0);
            Slot1.Value = _loadout.WeaponInSlot(1);
            Slot2.Value = _loadout.WeaponInSlot(2);
            Slot3.Value = _loadout.WeaponInSlot(3);
            Slot4.Value = _loadout.WeaponInSlot(4);
            Mag0.Value = _loadout.MagInSlot(0);
            Mag1.Value = _loadout.MagInSlot(1);
            Mag2.Value = _loadout.MagInSlot(2);
            Mag3.Value = _loadout.MagInSlot(3);
            Mag4.Value = _loadout.MagInSlot(4);
            int single = 0;
            for (int i = 0; i < Loadout.SlotCount; i++)
                if (_loadout.CanSwitchFireMode(i) && !_loadout.IsAutoFire(i)) single |= 1 << i;
            SingleFireSlots.Value = single;
            ReservePistol9.Value = _loadout.Reserve(AmmoType.Pistol9);
            ReserveRifle556.Value = _loadout.Reserve(AmmoType.Rifle556);
            ReserveShotgun12.Value = _loadout.Reserve(AmmoType.Shotgun12);
            ReserveRifle762.Value = _loadout.Reserve(AmmoType.Rifle762);
            Medkits.Value = _loadout.Medkits;
        }

        // ---- Server API (players reach it through the Cmd* requests below; AI calls it directly) ----

        [Server]
        public bool ServerFire(Vector3 direction)
        {
            float sqr = direction.sqrMagnitude;
            // Knocked down or reviving (ADR-072): no shooting.
            if (_health.IsDead || TeamBlocksAction || Holstered.Value || float.IsNaN(sqr) || float.IsInfinity(sqr) || sqr < 1e-6f) return false;
            // Shots always start at the server's idea of this combatant's eyes, never at a client-supplied point.
            var origin = EyePosition;
            var weapon = _loadout.ActiveWeapon;
            // A request may arrive a little ahead of the weapon's schedule (frames and network ticks never line up
            // with the fire interval); it is accepted without moving the schedule instead of leaving a gap in a burst.
            // [VEHICLE HOOK] ADR-065 (5): the driver fires only a sidearm; nobody fires while getting in, out or across.
            if (weapon == null || !VehicleSeat.CanUseWeapon(this, weapon) || !_loadout.TryFire(Time.timeAsDouble, GameData.Rules.Fire.ServerEarly(weapon.FireInterval), GameData.Rules.Slots.SingleModeMinInterval)) return false;

            var aim = Quaternion.LookRotation(direction.normalized);
            // Aim mode and stance narrow the cone; moving or being off the ground widens it.
            float spread = SpreadFactorWith(weapon);
            var zones = GameData.Rules.Fire.Zones;
            int pellets = DamageRules.ShotOffsets(weapon, _rng, YawBuf, PitchBuf, spread);
            for (int i = 0; i < pellets; i++)
            {
                var dir = aim * Quaternion.Euler(PitchBuf[i], YawBuf[i], 0f) * Vector3.forward;
                var end = origin + dir * weapon.Range;
                var normal = -dir;
                var result = ShotHit.None;
                var zone = HitZone.Body;
                NetworkObject victim = null;
                // [VEHICLE HOOK] same as Physics.Raycast on foot; from a seat it passes the shooter's own car and its occupants.
                if (VehicleSeat.Raycast(this, origin, dir, weapon.Range, out var hit))
                {
                    end = hit.point;
                    normal = hit.normal;
                    result = ShotHit.World;
                    var target = hit.collider.GetComponentInParent<Combatant>();
                    // A body that is already dead stops the shot like any other obstacle.
                    if (target != null && target != this && !target._health.IsDead)
                    {
                        // The zone comes from how high on the victim the shot landed, in the victim's current stance.
                        zone = HitZones.At(hit.point.y - target.transform.position.y, target.EyeHeightOffset, zones);
                        target.ServerTakeDamage(new DamageInfo
                        {
                            Amount = DamageRules.ForZone(weapon, hit.distance, zone),
                            Attacker = this,
                            Zone = zone,
                            WeaponId = weapon.Id,
                            Cause = DamageCause.Shot,
                        });
                        result = target._health.IsDead ? ShotHit.Kill : ShotHit.Combatant;
                        victim = target.NetworkObject;
                    }
                }
                // Window glass on the way breaks; the shot goes on through it (ADR-065 (4)).
                GlassPanes.ServerBreakAlong(origin, dir, (end - origin).magnitude, ObjectId);
                RpcShotFx(origin, end, normal, (byte)((int)result | ((int)zone << ShotZoneShift)), victim);
            }
            // Bots hear the shot where it was fired, whoever fired it.
            BotNoise.Emit(origin.x, origin.y, origin.z, BotRules.Current.GunshotHearingRadius, ObjectId);
            Push();
            return true;
        }

        // Bare hands (ADR-059 (1)): with nothing in hand the fire button punches. The punch goes where the body
        // faces, raised or lowered by the pitch of `direction` (the client chooses only the height, never the side),
        // and lands on the nearest living combatant whose chest (or head) is within reach inside the cone with
        // nothing in between. False when no punch was thrown (dead, armed, too soon); a swing at the air is true.
        [Server]
        public bool ServerPunch(Vector3 direction)
        {
            float sqr = direction.sqrMagnitude;
            if (_health == null || _health.IsDead || TeamBlocksAction || ActiveWeaponId != 0 || float.IsNaN(sqr) || float.IsInfinity(sqr)) return false;
            // [VEHICLE HOOK] ADR-065 (5): no punching from a seat.
            if (!VehicleSeat.CanPunch(this)) return false;
            var rules = MeleeRules.Current;
            if (!_punchCadence.TryFire(Time.timeAsDouble, rules.PunchInterval, GameData.Rules.Fire.ServerEarly(rules.PunchInterval))) return false;

            var eye = EyePosition;
            var axis = PunchAxis(direction);
            var zones = GameData.Rules.Fire.Zones;
            var target = FindPunchTarget(eye, axis, rules, zones);
            var result = ShotHit.None;
            var zone = HitZone.Body;
            NetworkObject victim = null;
            if (target != null)
            {
                var feet = target.transform.position;
                float flat = new Vector2(feet.x - eye.x, feet.z - eye.z).magnitude;
                float height = Melee.HitHeight(eye.y - feet.y, flat, axis.y, new Vector2(axis.x, axis.z).magnitude, zones.Height(target.EyeHeightOffset));
                zone = HitZones.At(height, target.EyeHeightOffset, zones);
                target.ServerTakeDamage(new DamageInfo
                {
                    Amount = Melee.Damage(zone, rules),
                    Attacker = this,
                    Zone = zone,
                    WeaponId = 0,
                    Cause = DamageCause.Melee,
                });
                result = target._health.IsDead ? ShotHit.Kill : ShotHit.Combatant;
                victim = target.NetworkObject;
            }
            bool right = (_punches & 1) == 1;
            _punches++;
            int info = (right ? PunchRightBit : 0) | ((int)result << PunchHitShift) | ((int)zone << PunchZoneShift);
            RpcPunch((byte)info, victim);
            return true;
        }

        // Where the body faces, tilted up or down by the pitch of the requested direction. Unit length.
        Vector3 PunchAxis(Vector3 direction)
        {
            var forward = transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            if (direction.sqrMagnitude < 1e-6f) return forward;
            var d = direction.normalized;
            return (forward * new Vector2(d.x, d.z).magnitude + Vector3.up * d.y).normalized;
        }

        // A point this high above the combatant's feet.
        Vector3 PointAt(float heightAboveFeet) => transform.position + Vector3.up * heightAboveFeet;

        Combatant FindPunchTarget(Vector3 eye, Vector3 axis, MeleeRules rules, HitZoneRules zones)
        {
            Combatant best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (c == null || c == this || c._health == null || c._health.IsDead) continue;
                float top = zones.Height(c.EyeHeightOffset);
                var chest = c.PointAt(top * rules.ChestFraction);
                float sqr = (chest - eye).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                // The chest, or the head when standing close with the fist held high.
                var head = c.PointAt(top - zones.HeadMeters * 0.5f);
                bool reach = InPunchReach(eye, chest, axis, rules) && ClearPunchLine(eye, chest, c);
                if (!reach) reach = InPunchReach(eye, head, axis, rules) && ClearPunchLine(eye, head, c);
                if (!reach) continue;
                best = c;
                bestSqr = sqr;
            }
            return best;
        }

        static bool InPunchReach(Vector3 eye, Vector3 point, Vector3 axis, MeleeRules rules)
        {
            var to = point - eye;
            return Melee.InReach(to.x, to.y, to.z, axis.x, axis.y, axis.z, rules.PunchRange, rules.PunchConeDeg);
        }

        // Nothing but the target (or this combatant's own body) between the eyes and the point.
        bool ClearPunchLine(Vector3 eye, Vector3 point, Combatant target)
        {
            var to = point - eye;
            float distance = to.magnitude;
            if (distance < 1e-3f) return true;
            int count = Physics.RaycastNonAlloc(eye, to / distance, PunchHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool clear = true;
            for (int i = 0; i < count; i++)
            {
                var hit = PunchHits[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                clear = hit.collider.GetComponentInParent<Combatant>() == target;
            }
            return clear;
        }

        // Damage with nothing known about it (tests, scripted events).
        [Server]
        public void ServerTakeDamage(float amount) => ServerTakeDamage(new DamageInfo { Amount = amount });

        // Every damage on the server ends up here. Checked by hand instead of [Server]: this overload takes its
        // argument by reference.
        public void ServerTakeDamage(in DamageInfo info)
        {
            if (!IsServerInitialized || _health == null || _health.IsDead) return;
            var dealt = info;
            // Teams (ADR-072): nothing between teammates (before armour, which would wear), knocked damage factor.
            if (!ServerTeamAdjust(ref dealt)) return;
            if (ServerDamageFilter != null) dealt.Amount = ServerDamageFilter(dealt);
            if (_health.ApplyDamage(dealt.Amount) <= 0f) return;
            // A lethal hit knocks down instead while a teammate stands (ADR-072).
            if (_health.IsDead && ServerTryKnock(dealt)) return;
            Push();
            // The dead take no further damage, so this happens once per life.
            if (_health.IsDead) ServerDied?.Invoke(this, dealt);
        }

        // Returns false when nothing on this combatant takes that kind of loot (it then stays where it was).
        public bool ServerGive(LootEntry loot) => ServerGive(loot, out _);

        // Same, and asks the bag first: `loot.Amount` is cut down to what fits and `leftover` is what stays on the
        // ground. False when none of it fits. Checked by hand instead of [Server]: this overload has an out parameter.
        public bool ServerGive(LootEntry loot, out int leftover)
        {
            leftover = 0;
            if (!IsServerInitialized || loot == null || _loadout == null) return false;
            if (_carry != null && !_carry.ServerFit(loot, out leftover)) return false;
            switch (loot.Kind)
            {
                case LootKind.Weapon:
                    // No slot takes it (a weapon this loadout has no category for): it stays on the ground.
                    if (!_loadout.CanAddWeapon(loot.WeaponId)) return false;
                    int pickupSlot = _loadout.PickupSlotFor(loot.WeaponId);
                    int replaced = _loadout.AddWeapon(loot.WeaponId, loot.Amount, out int replacedMag);
                    if (replaced != 0) OpticsPart?.ServerOnWeaponRemoved(pickupSlot);
                    // A replaced weapon goes back on the ground instead of vanishing.
                    if (replaced != 0 && MatchDirector.Instance != null)
                        MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Weapon, WeaponId = replaced, Amount = replacedMag }, transform.position + Vector3.up * 0.3f);
                    break;
                case LootKind.Ammo: _loadout.AddAmmo(loot.Ammo, loot.Amount); break;
                case LootKind.Medkit: _loadout.AddMedkits(loot.Amount); break;
                default:
                    // Kinds the loadout does not know (armour, throwables, ...) belong to another component.
                    // Every receiver is asked; the first to accept wins.
                    if (_receivers == null) _receivers = GetComponents<ILootReceiver>();
                    bool taken = false;
                    for (int i = 0; i < _receivers.Length && !taken; i++)
                        taken = _receivers[i].ServerTryTake(loot);
                    if (!taken) return false;
                    break;
            }
            Push();
            return true;
        }

        [Server]
        public void ServerSetStance(AimMode aim, int lean)
        {
            if (_health.IsDead || Knocked.Value) return;
            bool validAim = aim == AimMode.Hip || ((aim == AimMode.Shoulder || aim == AimMode.Sights) && ActiveWeaponId != 0);
            Aim.Value = validAim ? (int)aim : 0;
            Lean.Value = lean < 0 ? -1 : lean > 0 ? 1 : 0;
        }

        [Server]
        public void ServerReload()
        {
            if (!_health.IsDead && !Holstered.Value && _loadout.TryStartReload(Time.timeAsDouble)) Push();
        }

        // X: puts the weapon away or takes it out again (the same slot as before). The server refuses firing,
        // reloading and aiming while it is put away.
        [Server]
        public void ServerSetHolstered(bool holstered)
        {
            // Knocked down (ADR-072) the weapon stays put away.
            if (_health.IsDead || (Knocked.Value && !holstered) || Holstered.Value == holstered) return;
            if (holstered) _loadout.CancelReload();
            Holstered.Value = holstered;
            Push();
        }

        // B: single shots or automatic for the weapon in hand. False when it has no choice (or nothing in hand).
        [Server]
        public bool ServerToggleFireMode()
        {
            if (_health.IsDead || Holstered.Value || !_loadout.ToggleFireMode(_loadout.ActiveSlot)) return false;
            Push();
            return true;
        }

        // ---- Owner requests ----

        [ServerRpc]
        public void CmdFire(Vector3 direction) => ServerFire(direction);

        [ServerRpc]
        public void CmdPunch(Vector3 direction) => ServerPunch(direction);

        [ServerRpc]
        public void CmdReload() => ServerReload();

        [ServerRpc]
        public void CmdSetStance(int aim, int lean) => ServerSetStance((AimMode)aim, lean);

        [ServerRpc]
        public void CmdSetHolstered(bool holstered) => ServerSetHolstered(holstered);

        [ServerRpc]
        public void CmdToggleFireMode() => ServerToggleFireMode();

        // Same effect as CmdSelectSlot, for the server's own callers (bots).
        public bool ServerSelectSlot(int slot)
        {
            if (!IsServerInitialized || _loadout == null || _health.IsDead || Knocked.Value) return false;
            // Choosing a slot, even the one already in hand, takes a put-away weapon out again.
            bool drawn = Holstered.Value;
            if (drawn) Holstered.Value = false;
            if (!_loadout.SelectSlot(slot) && !drawn) return false;
            Push();
            return true;
        }

        // Same effect as CmdUseMedkit, for the server's own callers (bots).
        public bool ServerUseMedkit()
        {
            if (!IsServerInitialized || _loadout == null || Knocked.Value || !_loadout.TryUseMedkit(_health)) return false;
            Push();
            return true;
        }

        // Timed healing (CombatantHealing) gives health and uses up a medkit itself when a use finishes.
        // Returns the health actually given.
        public float ServerHeal(float amount)
        {
            if (!IsServerInitialized || _health == null || _health.IsDead || Knocked.Value) return 0f;
            float healed = _health.Heal(amount);
            if (healed > 0f) Push();
            return healed;
        }

        public bool ServerConsumeMedkit()
        {
            if (!IsServerInitialized || _loadout == null || !_loadout.RemoveMedkit()) return false;
            Push();
            return true;
        }

        // Empties weapons, ammo and medkits. Non-players keep at most DroppedReserveCap rounds per calibre in what
        // they hand back (the rest is gone), so a dummy's 9999 rounds do not litter the ground. `into` (may be null)
        // receives the contents as loot entries; the replicated values are cleared either way.
        public void ServerStripLoadout(List<LootEntry> into = null)
        {
            if (!IsServerInitialized || _loadout == null) return;
            for (int slot = 0; slot < Loadout.SlotCount; slot++)
            {
                int weaponId = _loadout.RemoveWeapon(slot, out int mag);
                if (weaponId != 0) into?.Add(new LootEntry { Kind = LootKind.Weapon, WeaponId = weaponId, Amount = mag });
            }
            for (int type = 0; type < AmmoNames.Count; type++)
            {
                int rounds = _loadout.RemoveAmmo((AmmoType)type);
                if (!_isPlayer && rounds > DroppedReserveCap) rounds = DroppedReserveCap;
                if (rounds > 0) into?.Add(new LootEntry { Kind = LootKind.Ammo, Ammo = (AmmoType)type, Amount = rounds });
            }
            int kits = 0;
            while (_loadout.RemoveMedkit()) kits++;
            if (kits > 0) into?.Add(new LootEntry { Kind = LootKind.Medkit, Amount = kits });
            Push();
        }

        [ServerRpc]
        public void CmdSelectSlot(int slot) => ServerSelectSlot(slot);

        // H: timed healing when this combatant has it (the player); otherwise the instant medkit (bots use ServerUseMedkit).
        [ServerRpc]
        public void CmdUseMedkit()
        {
            var h = GetComponent<CombatantHealing>();
            if (h != null) { h.ServerUse(); return; }
            ServerUseMedkit();
        }

        // The two primary weapons trade places; the one in hand stays in hand.
        [ServerRpc]
        public void CmdSwapWeaponSlots()
        {
            if (_health.IsDead || !_loadout.SwapSlots()) return;
            OpticsPart?.ServerOnSlotsSwapped(Loadout.PrimarySlotA, Loadout.PrimarySlotB);
            Push();
        }

        // Any two slots trade places when each takes what the other holds (a pistol only fits the sidearm slot).
        [ServerRpc]
        public void CmdSwapSlots(int a, int b)
        {
            if (_health.IsDead || !_loadout.SwapSlots(a, b)) return;
            OpticsPart?.ServerOnSlotsSwapped(a, b);
            Push();
        }

        // A weapon from the ground into a chosen slot; the weapon that was there lands on the ground.
        [ServerRpc]
        public void CmdPickupToSlot(NetworkObject lootObject, int slot)
        {
            if (_health.IsDead || TeamBlocksAction || lootObject == null || slot < 0 || slot >= Loadout.SlotCount) return;
            var item = lootObject.GetComponent<LootItem>();
            if (item == null || !item.IsSpawned || (LootKind)item.Kind.Value != LootKind.Weapon) return;
            if ((item.transform.position - transform.position).sqrMagnitude > PickupRange * PickupRange) return;
            // The slot must take this kind of weapon (a pistol only goes in the sidearm slot); also false for an unknown weapon.
            if (!_loadout.CanHold(slot, item.WeaponId.Value)) return;
            int replaced = _loadout.AddWeaponToSlot(slot, item.WeaponId.Value, item.Amount.Value, out int replacedMag);
            item.Despawn();
            if (replaced != 0) OpticsPart?.ServerOnWeaponRemoved(slot);
            if (replaced != 0 && MatchDirector.Instance != null)
                MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Weapon, WeaponId = replaced, Amount = replacedMag }, transform.position + Vector3.up * 0.3f);
            Push();
        }

        [ServerRpc]
        public void CmdPickup(NetworkObject lootObject)
        {
            if (_health.IsDead || TeamBlocksAction || lootObject == null) return;
            var item = lootObject.GetComponent<LootItem>();
            if (item == null || !item.IsSpawned) return;
            if ((item.transform.position - transform.position).sqrMagnitude > PickupRange * PickupRange) return;
            var e = item.ToEntry();
            if (ServerGive(e, out int left))
            {
                if (left > 0) item.Amount.Value = left;
                else item.Despawn();
            }
        }

        // Dropping from the inventory screen: the item leaves the loadout and lands on the ground in front.
        [ServerRpc]
        public void CmdDropWeapon(int slot)
        {
            if (_health.IsDead || MatchDirector.Instance == null) return;
            int weaponId = _loadout.RemoveWeapon(slot, out int mag);
            if (weaponId == 0) return;
            OpticsPart?.ServerOnWeaponRemoved(slot);
            ServerDropLoot(new LootEntry { Kind = LootKind.Weapon, WeaponId = weaponId, Amount = mag });
        }

        [ServerRpc]
        public void CmdDropAmmo(int ammoType)
        {
            if (_health.IsDead || MatchDirector.Instance == null) return;
            if (!AmmoNames.IsValid((AmmoType)ammoType)) return;
            int amount = _loadout.RemoveAmmo((AmmoType)ammoType);
            if (amount <= 0) return;
            ServerDropLoot(new LootEntry { Kind = LootKind.Ammo, Ammo = (AmmoType)ammoType, Amount = amount });
        }

        [ServerRpc]
        public void CmdDropMedkit()
        {
            if (_health.IsDead || MatchDirector.Instance == null) return;
            if (!_loadout.RemoveMedkit()) return;
            ServerDropLoot(new LootEntry { Kind = LootKind.Medkit, Amount = 1 });
        }

        [Server]
        void ServerDropLoot(LootEntry entry)
        {
            MatchDirector.Instance.ServerSpawnLoot(entry, transform.position + transform.forward * 1.0f + Vector3.up * 0.3f);
            Push();
        }

        // Solo has no revival: a dead player may ask the server for a fresh match.
        [ServerRpc]
        public void CmdRequestRestart()
        {
            if (_health.IsDead && MatchDirector.Instance != null) MatchDirector.Instance.Restart();
        }

        // Runs locally too, so a host sees and hears the shot in the frame it was fired instead of a network tick
        // later (ticks are 33 ms apart, which made the rhythm of automatic fire uneven).
        [ObserversRpc(RunLocally = true)]
        void RpcShotFx(Vector3 origin, Vector3 end, Vector3 normal, byte hit, NetworkObject victim)
        {
            // A server without a client has nothing to show.
            if (!IsClientStarted) return;
            ShotFired?.Invoke(origin, end);
            if (ShotResolved == null) return;
            var shot = new ShotResult
            {
                Origin = origin,
                End = end,
                Normal = normal,
                Hit = (ShotHit)(hit & ShotHitMask),
                Zone = (HitZone)(hit >> ShotZoneShift),
                Victim = victim != null ? victim.GetComponent<Combatant>() : null,
            };
            ShotResolved.Invoke(in shot);
        }

        // Every client sees and hears the punch; runs locally so a host shows it in the frame it was thrown.
        [ObserversRpc(RunLocally = true)]
        void RpcPunch(byte info, NetworkObject victim)
        {
            if (!IsClientStarted || Punched == null) return;
            var punch = new PunchResult
            {
                Right = (info & PunchRightBit) != 0,
                Hit = (ShotHit)((info >> PunchHitShift) & PunchHitMask),
                Zone = (HitZone)(info >> PunchZoneShift),
                Victim = victim != null ? victim.GetComponent<Combatant>() : null,
            };
            Punched.Invoke(in punch);
        }
    }
}
