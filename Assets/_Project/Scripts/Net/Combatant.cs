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
    public class Combatant : NetworkBehaviour
    {
        public const float PickupRange = 3f;

        [SerializeField] bool _isPlayer;
        [SerializeField] float _maxHealth = 100f;
        [SerializeField] float _eyeHeight = 1.6f;

        public readonly SyncVar<float> Hp = new SyncVar<float>();
        public readonly SyncVar<bool> Dead = new SyncVar<bool>();
        public readonly SyncVar<bool> Reloading = new SyncVar<bool>();
        public readonly SyncVar<int> ActiveSlot = new SyncVar<int>();
        public readonly SyncVar<int> Slot0 = new SyncVar<int>();
        public readonly SyncVar<int> Slot1 = new SyncVar<int>();
        public readonly SyncVar<int> Mag0 = new SyncVar<int>();
        public readonly SyncVar<int> Mag1 = new SyncVar<int>();
        public readonly SyncVar<int> ReserveLight = new SyncVar<int>();
        public readonly SyncVar<int> ReserveMedium = new SyncVar<int>();
        public readonly SyncVar<int> ReserveShell = new SyncVar<int>();
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

        static readonly float[] YawBuf = new float[32];
        static readonly float[] PitchBuf = new float[32];
        static readonly RaycastHit[] LeanHits = new RaycastHit[8];

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
            return weapon == null ? factor : factor * DamageRules.MovementSpread(weapon, PlanarSpeed, Airborne, GameData.Rules.Fire);
        }
        public int ActiveWeaponId => ActiveSlot.Value == 0 ? Slot0.Value : Slot1.Value;
        public int ActiveMag => ActiveSlot.Value == 0 ? Mag0.Value : Mag1.Value;
        public WeaponDef ActiveWeapon => GameData.Weapons.Find(ActiveWeaponId);

        public int ReserveFor(AmmoType type) =>
            type == AmmoType.Light ? ReserveLight.Value : type == AmmoType.Medium ? ReserveMedium.Value : ReserveShell.Value;

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
            Mag0.Value = _loadout.MagInSlot(0);
            Mag1.Value = _loadout.MagInSlot(1);
            ReserveLight.Value = _loadout.Reserve(AmmoType.Light);
            ReserveMedium.Value = _loadout.Reserve(AmmoType.Medium);
            ReserveShell.Value = _loadout.Reserve(AmmoType.Shell);
            Medkits.Value = _loadout.Medkits;
        }

        // ---- Server API (players reach it through the Cmd* requests below; AI calls it directly) ----

        [Server]
        public bool ServerFire(Vector3 direction)
        {
            float sqr = direction.sqrMagnitude;
            if (_health.IsDead || float.IsNaN(sqr) || float.IsInfinity(sqr) || sqr < 1e-6f) return false;
            // Shots always start at the server's idea of this combatant's eyes, never at a client-supplied point.
            var origin = EyePosition;
            var weapon = _loadout.ActiveWeapon;
            // A request may arrive a little ahead of the weapon's schedule (frames and network ticks never line up
            // with the fire interval); it is accepted without moving the schedule instead of leaving a gap in a burst.
            if (weapon == null || !_loadout.TryFire(Time.timeAsDouble, GameData.Rules.Fire.ServerEarly(weapon.FireInterval))) return false;

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
                if (Physics.Raycast(origin, dir, out var hit, weapon.Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
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
                RpcShotFx(origin, end, normal, (byte)((int)result | ((int)zone << ShotZoneShift)), victim);
            }
            Push();
            return true;
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
            if (ServerDamageFilter != null) dealt.Amount = ServerDamageFilter(info);
            if (_health.ApplyDamage(dealt.Amount) <= 0f) return;
            Push();
            // The dead take no further damage, so this happens once per life.
            if (_health.IsDead) ServerDied?.Invoke(this, dealt);
        }

        // Returns false when nothing on this combatant takes that kind of loot (it then stays where it was).
        [Server]
        public bool ServerGive(LootEntry loot)
        {
            switch (loot.Kind)
            {
                case LootKind.Weapon:
                    int replaced = _loadout.AddWeapon(loot.WeaponId, loot.Amount, out int replacedMag);
                    // A replaced weapon goes back on the ground instead of vanishing.
                    if (replaced != 0 && MatchDirector.Instance != null)
                        MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Weapon, WeaponId = replaced, Amount = replacedMag }, transform.position + Vector3.up * 0.3f);
                    break;
                case LootKind.Ammo: _loadout.AddAmmo(loot.Ammo, loot.Amount); break;
                case LootKind.Medkit: _loadout.AddMedkits(loot.Amount); break;
                default:
                    // Kinds the loadout does not know (armour, throwables, ...) belong to another component.
                    var receiver = GetComponent<ILootReceiver>();
                    if (receiver == null || !receiver.ServerTryTake(loot)) return false;
                    break;
            }
            Push();
            return true;
        }

        [Server]
        public void ServerSetStance(AimMode aim, int lean)
        {
            if (_health.IsDead) return;
            bool validAim = aim == AimMode.Hip || ((aim == AimMode.Shoulder || aim == AimMode.Sights) && ActiveWeaponId != 0);
            Aim.Value = validAim ? (int)aim : 0;
            Lean.Value = lean < 0 ? -1 : lean > 0 ? 1 : 0;
        }

        [Server]
        public void ServerReload()
        {
            if (!_health.IsDead && _loadout.TryStartReload(Time.timeAsDouble)) Push();
        }

        // ---- Owner requests ----

        [ServerRpc]
        public void CmdFire(Vector3 direction) => ServerFire(direction);

        [ServerRpc]
        public void CmdReload() => ServerReload();

        [ServerRpc]
        public void CmdSetStance(int aim, int lean) => ServerSetStance((AimMode)aim, lean);

        [ServerRpc]
        public void CmdSelectSlot(int slot)
        {
            if (!_health.IsDead && _loadout.SelectSlot(slot)) Push();
        }

        [ServerRpc]
        public void CmdUseMedkit()
        {
            if (_loadout.TryUseMedkit(_health)) Push();
        }

        [ServerRpc]
        public void CmdPickup(NetworkObject lootObject)
        {
            if (_health.IsDead || lootObject == null) return;
            var item = lootObject.GetComponent<LootItem>();
            if (item == null || !item.IsSpawned) return;
            if ((item.transform.position - transform.position).sqrMagnitude > PickupRange * PickupRange) return;
            if (ServerGive(item.ToEntry())) item.Despawn();
        }

        // Dropping from the inventory screen: the item leaves the loadout and lands on the ground in front.
        [ServerRpc]
        public void CmdDropWeapon(int slot)
        {
            if (_health.IsDead || MatchDirector.Instance == null) return;
            int weaponId = _loadout.RemoveWeapon(slot, out int mag);
            if (weaponId == 0) return;
            ServerDropLoot(new LootEntry { Kind = LootKind.Weapon, WeaponId = weaponId, Amount = mag });
        }

        [ServerRpc]
        public void CmdDropAmmo(int ammoType)
        {
            if (_health.IsDead || MatchDirector.Instance == null) return;
            if (ammoType < (int)AmmoType.Light || ammoType > (int)AmmoType.Shell) return;
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
    }
}
