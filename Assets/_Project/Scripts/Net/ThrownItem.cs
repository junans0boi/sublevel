using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // A grenade in flight. The server alone moves it (free flight in fixed steps, a sphere cast per step, damped
    // bounces) and clients follow through the prefab's NetworkTransform: bounces depend on colliders and frame
    // timing, so replaying the flight on each client from a start state would drift from what the server decides.
    // When the fuse runs out a fragmentation grenade deals its blast and tells everyone to show it; a smoke grenade
    // opens a cloud that hides lines of sight for a while (SmokeZones) and tells everyone to show that.
    public class ThrownItem : NetworkBehaviour
    {
        const float StepSeconds = 1f / 60f;
        const int MaxStepsPerFrame = 8;
        // Kept between the grenade and a surface it landed on, so the next cast does not start touching it.
        const float Skin = 0.005f;
        // The object outlives its blast for a moment so the effect call reaches every client before the despawn.
        const float LingerSeconds = 0.3f;
        const float ChestHeight = 1.1f;

        [SerializeField] GameObject _visual;      // the grenade; hidden when it goes off
        [SerializeField] GameObject _smokeVisual; // the smoke canister (optional); shown instead of _visual for smoke

        // The ThrowableKind; set before spawning so every client knows what it is looking at.
        public readonly SyncVar<int> Kind = new SyncVar<int>();

        // Raised on every client when a grenade goes off: (centre, the thrower's object or null if it is gone).
        // Static, so it outlives scenes: subscribers unsubscribe in OnDestroy. Cleared when a play session starts.
        public static event Action<Vector3, NetworkObject> ClientExploded;

        // Raised on every client when a smoke cloud opens: (centre, radius, seconds it lasts, thrower's object or null).
        public static event Action<Vector3, float, float, NetworkObject> ClientSmoked;

        // Server only: every blast, after its damage was dealt (centre, thrower or null). For tests and score keeping.
        public static event Action<Vector3, Combatant> ServerExploded;

        // Server only: every smoke cloud, once it exists (centre, thrower or null).
        public static event Action<Vector3, Combatant> ServerSmoked;

        // A flash grenade went off: raised on every client (centre, thrower or null), and on the server (centre, thrower).
        public static event Action<Vector3, NetworkObject> ClientFlashed;
        public static event Action<Vector3, Combatant> ServerFlashed;

        static readonly RaycastHit[] Hits = new RaycastHit[16];
        static readonly List<Combatant> Victims = new List<Combatant>(16);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ClientExploded = null;
            ServerExploded = null;
            ClientSmoked = null;
            ServerSmoked = null;
            ClientFlashed = null;
            ServerFlashed = null;
            SmokeZones.Clear();
        }

        ThrowableRules _rules = ThrowableRules.Current;
        Combatant _thrower;
        Vector3 _position, _velocity;
        float _fuse, _age, _clock, _flown, _despawnAt;
        bool _resting, _exploded;
        int _zoneId;

        public ThrowableKind ThrowKind => (ThrowableKind)Kind.Value;
        // Gone off (a blast, or a smoke cloud opened).
        public bool Exploded => _exploded;
        // Server: lying still on a floor.
        public bool Resting => _resting;
        public Vector3 Velocity => _velocity;

        // Server, before spawning.
        public void Init(Combatant thrower, Vector3 velocity, ThrowableRules rules) => Init(thrower, velocity, rules, ThrowableKind.Frag);

        public void Init(Combatant thrower, Vector3 velocity, ThrowableRules rules, ThrowableKind kind) => Init(thrower, velocity, rules, kind, -1f);

        // `fuseOverride` >= 0: the seconds left (a cooked grenade); otherwise the kind's full fuse.
        public void Init(Combatant thrower, Vector3 velocity, ThrowableRules rules, ThrowableKind kind, float fuseOverride)
        {
            _thrower = thrower;
            _velocity = velocity;
            _rules = rules;
            Kind.Value = (int)kind;
            _fuse = fuseOverride >= 0f ? fuseOverride : Throwables.FuseOf(kind, rules, SmokeRules.Current);
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public override void OnStartNetwork()
        {
            // A flash grenade is the same body in pale steel (the prefab has one grenade shape).
            if (Kind.Value == (int)ThrowableKind.Flash && _visual != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColorId, new Color(0.80f, 0.82f, 0.86f));
                foreach (var r in _visual.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(block);
            }
            // The canister of a smoke grenade looks different from a grenade (when the prefab has both).
            if (Kind.Value == (int)ThrowableKind.Smoke && _smokeVisual != null)
            {
                if (_visual != null) _visual.SetActive(false);
                _smokeVisual.SetActive(true);
            }
        }

        public override void OnStartServer()
        {
            _position = transform.position;
        }

        public override void OnStopServer()
        {
            if (_zoneId != 0) SmokeZones.Remove(_zoneId);
            _zoneId = 0;
        }

        void Update()
        {
            if (!IsServerInitialized) return;
            if (_exploded)
            {
                if (Time.time >= _despawnAt) Despawn();
                return;
            }
            _clock += Time.deltaTime;
            int steps = 0;
            while (_clock >= StepSeconds && !_exploded)
            {
                _clock -= StepSeconds;
                // A long frame does not turn into a long burst of physics; the fuse still runs in real time.
                if (steps++ < MaxStepsPerFrame) Step(StepSeconds);
                _age += StepSeconds;
                if (_age >= _fuse)
                {
                    if (Kind.Value == (int)ThrowableKind.Smoke) OpenSmoke();
                    else if (Kind.Value == (int)ThrowableKind.Flash) FlashBang();
                    else Explode();
                }
            }
            transform.position = _position;
        }

        void Step(float dt)
        {
            if (_resting) return;
            _velocity.y -= _rules.Gravity * dt;
            var move = _velocity * dt;
            float distance = move.magnitude;
            if (distance < 1e-6f) return;
            var direction = move / distance;

            if (!NearestSurface(direction, distance, out var hit))
            {
                _position += move;
                _flown += distance;
                return;
            }
            float travelled = Mathf.Max(0f, hit.distance - Skin);
            _position += direction * travelled;
            _flown += travelled;
            var n = hit.normal;
            Throwables.Bounce(ref _velocity.x, ref _velocity.y, ref _velocity.z, n.x, n.y, n.z, _rules);
            if (Throwables.ComesToRest(_velocity.x, _velocity.y, _velocity.z, n.y, _rules))
            {
                _velocity = Vector3.zero;
                _resting = true;
            }
        }

        bool NearestSurface(Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.SphereCastNonAlloc(_position, _rules.Radius, direction, Hits, distance + Skin,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool found = false;
            bool pastThrower = Throwables.TouchesThrower(_flown, _rules);
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                // Distance 0: the cast started inside this collider, which gives no usable surface.
                if (hit.distance <= 0f) continue;
                var t = hit.collider.transform;
                if (t.IsChildOf(transform)) continue;
                if (!pastThrower && _thrower != null && t.IsChildOf(_thrower.transform)) continue;
                if (found && hit.distance >= nearest.distance) continue;
                nearest = hit;
                found = true;
            }
            return found;
        }

        // The cloud: registered for line-of-sight checks right away, shown by every client, gone after its time.
        void OpenSmoke()
        {
            var smoke = SmokeRules.Current;
            _exploded = true;
            _despawnAt = Time.time + smoke.Seconds + LingerSeconds;
            var centre = _position;
            transform.position = centre;
            var attacker = _thrower != null ? _thrower : null;
            _zoneId = SmokeZones.Add(centre.x, centre.y + smoke.CentreLiftMeters, centre.z, smoke.Radius);
            ServerSmoked?.Invoke(centre, attacker);
            RpcSmoked(centre, smoke.Radius, smoke.Seconds, attacker != null ? attacker.NetworkObject : null);
        }

        // The flash: no damage; every client works out its own player's blindness (FlashOverlay).
        void FlashBang()
        {
            _exploded = true;
            _despawnAt = Time.time + LingerSeconds;
            var centre = _position;
            transform.position = centre;
            var attacker = _thrower != null ? _thrower : null;
            ServerFlashed?.Invoke(centre, attacker);
            RpcFlashed(centre, attacker != null ? attacker.NetworkObject : null);
        }

        // Runs locally too, so a host is blinded in the frame it goes off.
        [ObserversRpc(RunLocally = true)]
        void RpcFlashed(Vector3 centre, NetworkObject thrower)
        {
            _exploded = true;
            if (_visual != null) _visual.SetActive(false);
            if (!IsClientStarted) return;
            ClientFlashed?.Invoke(centre, thrower);
        }

        // Everyone within the blast radius whose chest the centre can see takes damage by distance: walls and
        // cover block it, other bodies do not. The thrower is hurt like anyone else.
        void Explode()
        {
            _exploded = true;
            _despawnAt = Time.time + LingerSeconds;
            var centre = _position;
            transform.position = centre;
            // The thrower may have left the match in the meantime.
            var attacker = _thrower != null ? _thrower : null;

            // Copied first: a death can make listeners change the list of combatants.
            Victims.Clear();
            foreach (var c in Combatant.All)
                if (c != null && !c.Dead.Value) Victims.Add(c);
            var zones = GameData.Rules.Fire.Zones;
            foreach (var victim in Victims)
            {
                if (victim == null) continue;
                var feet = victim.transform.position;
                float height = zones.Height(victim.EyeHeightOffset);
                // Distance to the nearest point of the body's upright axis: lying next to the feet is point blank.
                var nearest = new Vector3(feet.x, Mathf.Clamp(centre.y, feet.y, feet.y + height), feet.z);
                float damage = Throwables.BlastDamage(Vector3.Distance(centre, nearest), _rules);
                if (damage <= 0f) continue;
                // Lying down and a vest cut a blast (ADR-101). The vest takes its share without wearing, so the hit is
                // cut here and sent as an unprotected zone.
                bool prone = victim.TryGetComponent(out CombatantStance stance) && stance.Current == Stance.Prone;
                float vest = victim.TryGetComponent(out CombatantGear gear)
                    ? Armor.ReductionOf(ArmorSlot.Vest, gear.VestTier.Value, gear.VestDurability.Value, ArmorRules.Current) : 0f;
                damage = Throwables.AdjustBlast(damage, prone, vest, _rules);
                var chest = feet + Vector3.up * (ChestHeight * height / zones.StandingHeight);
                if (!Reaches(centre, chest)) continue;
                victim.ServerTakeDamage(new DamageInfo
                {
                    Amount = damage,
                    Attacker = attacker,
                    Zone = HitZone.Limb, // not Body: the vest was counted above and does not wear
                    WeaponId = 0,
                    Cause = DamageCause.Blast,
                });
            }
            Victims.Clear();
            // [VEHICLE HOOK] #90: vehicles near the blast.
            var blastRules = _rules;
            VehicleDamage.ServerBlastAll(centre, d => Throwables.BlastDamage(d, blastRules), VehicleDamageRules.Current.GrenadeToVehicleFactor);

            ServerExploded?.Invoke(centre, attacker);
            RpcExploded(centre, attacker != null ? attacker.NetworkObject : null);
        }

        // Nothing but combatants between the two points.
        bool Reaches(Vector3 from, Vector3 to)
        {
            var path = to - from;
            float distance = path.magnitude;
            if (distance < 1e-3f) return true;
            int count = Physics.RaycastNonAlloc(from, path / distance, Hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // A full buffer may have left a wall out: err on the side of cover.
            if (count >= Hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var collider = Hits[i].collider;
                if (collider.transform.IsChildOf(transform)) continue;
                if (collider.GetComponentInParent<Combatant>() == null) return false;
            }
            return true;
        }

        // Runs locally too, so a host sees the cloud in the frame it opens.
        [ObserversRpc(RunLocally = true)]
        void RpcSmoked(Vector3 centre, float radius, float seconds, NetworkObject thrower)
        {
            _exploded = true;
            if (_visual != null) _visual.SetActive(false);
            if (_smokeVisual != null) _smokeVisual.SetActive(false);
            // A server without a client has nothing to show.
            if (!IsClientStarted) return;
            ClientSmoked?.Invoke(centre, radius, seconds, thrower);
        }

        // Runs locally too, so a host sees the blast in the frame the damage lands.
        [ObserversRpc(RunLocally = true)]
        void RpcExploded(Vector3 centre, NetworkObject thrower)
        {
            _exploded = true;
            if (_visual != null) _visual.SetActive(false);
            // A server without a client has nothing to show.
            if (!IsClientStarted) return;
            ClientExploded?.Invoke(centre, thrower);
        }
    }

    // Line of sight through smoke clouds, for AI perception: true when a cloud hides the way from one point to
    // the other. No allocation; the clouds exist on the server (where the bots run).
    public static class SmokeSight
    {
        public static bool Blocks(Vector3 from, Vector3 to) => SmokeZones.Blocks(from.x, from.y, from.z, to.x, to.y, to.z);
    }
}
