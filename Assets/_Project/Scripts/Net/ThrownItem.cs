using System;
using System.Collections.Generic;
using FishNet.Object;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // A grenade in flight. The server alone moves it (free flight in fixed steps, a sphere cast per step, damped
    // bounces) and clients follow through the prefab's NetworkTransform: bounces depend on colliders and frame
    // timing, so replaying the flight on each client from a start state would drift from what the server decides.
    // When the fuse runs out the server deals the blast and tells everyone to show it.
    public class ThrownItem : NetworkBehaviour
    {
        const float StepSeconds = 1f / 60f;
        const int MaxStepsPerFrame = 8;
        // Kept between the grenade and a surface it landed on, so the next cast does not start touching it.
        const float Skin = 0.005f;
        // The object outlives its blast for a moment so the effect call reaches every client before the despawn.
        const float LingerSeconds = 0.3f;
        const float ChestHeight = 1.1f;

        [SerializeField] GameObject _visual; // hidden when it goes off

        // Raised on every client when a grenade goes off: (centre, the thrower's object or null if it is gone).
        // Static, so it outlives scenes: subscribers unsubscribe in OnDestroy. Cleared when a play session starts.
        public static event Action<Vector3, NetworkObject> ClientExploded;

        // Server only: every blast, after its damage was dealt (centre, thrower or null). For tests and score keeping.
        public static event Action<Vector3, Combatant> ServerExploded;

        static readonly RaycastHit[] Hits = new RaycastHit[16];
        static readonly List<Combatant> Victims = new List<Combatant>(16);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ClientExploded = null;
            ServerExploded = null;
        }

        ThrowableRules _rules = ThrowableRules.Current;
        Combatant _thrower;
        Vector3 _position, _velocity;
        float _fuse, _age, _clock, _flown, _despawnAt;
        bool _resting, _exploded;

        public bool Exploded => _exploded;
        // Server: lying still on a floor.
        public bool Resting => _resting;
        public Vector3 Velocity => _velocity;

        // Server, before spawning.
        public void Init(Combatant thrower, Vector3 velocity, ThrowableRules rules)
        {
            _thrower = thrower;
            _velocity = velocity;
            _rules = rules;
            _fuse = rules.FuseSeconds;
        }

        public override void OnStartServer()
        {
            _position = transform.position;
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
                if (_age >= _fuse) Explode();
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
                var chest = feet + Vector3.up * (ChestHeight * height / zones.StandingHeight);
                if (!Reaches(centre, chest)) continue;
                victim.ServerTakeDamage(new DamageInfo
                {
                    Amount = damage,
                    Attacker = attacker,
                    Zone = HitZone.Body,
                    WeaponId = 0,
                    Cause = DamageCause.Blast,
                });
            }
            Victims.Clear();

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
}
