using System;
using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // One body part of the ragdoll, baked by Editor/RagdollRigBuilder in the bind (T) pose. Sizes are in the bone's own space.
    [Serializable]
    public struct RagdollPart
    {
        public const byte Capsule = 0, Box = 1, Sphere = 2;
        public Transform Bone;
        public int Parent;          // index of the part it hangs from; -1 for the hips
        public byte Shape;
        public Vector3 Center;
        public Vector3 Size;        // capsule: (radius, height, axis 0/1/2); box: size; sphere: (radius, -, -)
        public float Mass;
        public Vector3 Axis, SwingAxis; // joint axes in the bone's space
        public float LowTwist, HighTwist, Swing1, Swing2;
    }

    // A bone between the hips and the parts, with its bind pose: the joints are made in this pose so their limits
    // mean the same on every model (the limits are measured from the pose a joint is created in).
    [Serializable]
    public struct RagdollRestBone
    {
        public Transform Bone;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    // ADR-395 (#196): when a combatant this side saw alive dies, the death clip plays for a moment and then the body
    // turns into a ragdoll pushed along the killing hit. Client-only visual: the bodies and colliders exist only here,
    // on the Ignore Raycast layer (no shot, sight line or melee test meets them) and never touch a combatant's own
    // collider. Everything the server judges (loot ring, death crate, chip) stays at the combatant's position.
    // At most RagdollRules.MaxActive bodies simulate at once, the nearest to the camera; each freezes into its pose once
    // still, after a few seconds or when it drifts too far, and then has no physics cost at all.
    public class CombatantRagdoll : MonoBehaviour
    {
        public const int Layer = 2; // built-in Ignore Raycast
        enum State : byte { None, Simulating, Frozen }

        [SerializeField] RagdollPart[] _parts;
        [SerializeField] RagdollRestBone[] _rest;
        [SerializeField] Animator _animator;

        static readonly List<CombatantRagdoll> Simulated = new List<CombatantRagdoll>();
        static readonly List<float> Distances = new List<float>();

        Combatant _combatant;
        CombatantMotion _motion;
        State _state;
        bool _started, _seenDead, _sawDeath;
        float _deathAt;
        Vector3 _deathPosition;
        RagdollSettle _settle;
        Rigidbody[] _bodies;
        CharacterJoint[] _joints;
        GameObject[] _colliders;
        Vector3[] _posePosition;
        Quaternion[] _poseRotation;
        Vector3 _blastCentre;
        float _blastAt = float.NegativeInfinity;

        public static int SimulatingCount => Simulated.Count;
        public bool HasRig => _parts != null && _parts.Length > 0 && _animator != null;
        public int PartCount => _parts != null ? _parts.Length : 0;
        public bool Active => _state != State.None;
        public bool Simulating => _state == State.Simulating;
        public bool Frozen => _state == State.Frozen;
        // The push given when the ragdoll started (tests, captures).
        public Vector3 LastImpulse { get; private set; }
        public Transform Hips => HasRig ? _parts[0].Bone : null;
        // Test and capture hook: ignore the camera distance and the delay check for this body.
        public bool ForceAdmit { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Simulated.Clear();

#if UNITY_EDITOR
        // Editor/RagdollRigBuilder only.
        public void Bake(RagdollPart[] parts, RagdollRestBone[] rest, Animator animator)
        {
            _parts = parts; _rest = rest; _animator = animator;
        }
        public RagdollPart Part(int i) => _parts[i];
#endif

        void Awake()
        {
            _combatant = GetComponentInParent<Combatant>();
            _motion = GetComponentInParent<CombatantMotion>();
            // A preview model (lobby, look dev) has no combatant: nothing to do.
            if (_combatant == null) enabled = false;
        }

        void OnEnable() => ThrownItem.ClientExploded += OnExploded;

        void OnDisable()
        {
            ThrownItem.ClientExploded -= OnExploded;
            Simulated.Remove(this);
        }

        void OnExploded(Vector3 centre, FishNet.Object.NetworkObject thrower)
        {
            _blastCentre = centre;
            _blastAt = Time.time;
        }

        void Update()
        {
            bool dead = _combatant.Dead.Value;
            if (!_started) { _started = true; _seenDead = dead; }
            if (dead && !_seenDead) { _sawDeath = true; _deathAt = Time.time; }
            if (!dead && _seenDead) { Restore(); _sawDeath = false; }
            _seenDead = dead;
            if (dead && _sawDeath && _state == State.None) TryStart();
            if (_state == State.Simulating) Settle(Time.deltaTime);
        }

        void TryStart()
        {
            var rules = RagdollRules.Current;
            float since = Time.time - _deathAt;
            if (since < rules.DelaySeconds) return;
            _sawDeath = false; // one try per death; a body that cannot ragdoll keeps the clip
            bool swimming = WaterVolume.DepthAt(transform.position, out _, out _) > 0.6f;
            if (!RagdollSwitch.MayStart(true, swimming, VehicleSeat.IsAttached(_combatant), HasRig, since, rules)) return;
            var cam = PlayerController.Local != null ? PlayerController.Local.Camera : Camera.main;
            if (cam == null && !ForceAdmit) return; // a server with no view
            Vector3 eye = cam != null ? cam.transform.position : transform.position;
            Distances.Clear();
            foreach (var s in Simulated) Distances.Add(Vector3.Distance(eye, s.transform.position));
            int slot = RagdollBudget.Admit(Distances, ForceAdmit ? 0f : Vector3.Distance(eye, transform.position), rules);
            if (slot == RagdollBudget.Reject) return;
            if (slot >= 0) Simulated[slot].Freeze();
            Begin(rules);
        }

        void Begin(RagdollRules rules)
        {
            int n = _parts.Length;
            _bodies = new Rigidbody[n];
            _joints = new CharacterJoint[n];
            _colliders = new GameObject[n];
            _posePosition ??= new Vector3[_rest.Length];
            _poseRotation ??= new Quaternion[_rest.Length];
            _animator.enabled = false;

            // Joints are made in the bind pose (their limits count from it), then the bones go back to the pose of death.
            for (int i = 0; i < _rest.Length; i++)
            {
                var b = _rest[i].Bone;
                _posePosition[i] = b.localPosition; _poseRotation[i] = b.localRotation;
                b.localPosition = _rest[i].Position; b.localRotation = _rest[i].Rotation;
            }
            for (int i = 0; i < n; i++)
            {
                var p = _parts[i];
                var rb = p.Bone.gameObject.AddComponent<Rigidbody>();
                rb.mass = p.Mass;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.8f;
                rb.maxDepenetrationVelocity = 1.5f; // dying against a wall must not shoot the body out of it
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = i <= 1 ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;
                rb.solverIterations = 8;
                _bodies[i] = rb;
                _colliders[i] = MakeCollider(p);
            }
            for (int i = 0; i < n; i++)
            {
                var p = _parts[i];
                if (p.Parent < 0) continue;
                var j = p.Bone.gameObject.AddComponent<CharacterJoint>();
                j.axis = p.Axis;
                j.swingAxis = p.SwingAxis;
                j.lowTwistLimit = new SoftJointLimit { limit = p.LowTwist };
                j.highTwistLimit = new SoftJointLimit { limit = p.HighTwist };
                j.swing1Limit = new SoftJointLimit { limit = p.Swing1 };
                j.swing2Limit = new SoftJointLimit { limit = p.Swing2 };
                j.enableProjection = true;
                j.enablePreprocessing = false;
                j.connectedBody = _bodies[p.Parent];
                _joints[i] = j;
            }
            for (int i = 0; i < _rest.Length; i++)
            {
                _rest[i].Bone.localPosition = _posePosition[i];
                _rest[i].Bone.localRotation = _poseRotation[i];
            }

            // Never pushes or blocks a living body (or its own standing collider).
            foreach (var c in Combatant.All)
            {
                if (c == null) continue;
                foreach (var other in c.GetComponents<Collider>())
                    for (int i = 0; i < n; i++) Physics.IgnoreCollision(_colliders[i].GetComponent<Collider>(), other);
            }

            // Running into death carries on; then the push of the killing hit (or blast).
            var carry = _motion != null ? Vector3.ClampMagnitude(transform.TransformDirection(_motion.LocalVelocity), 8f) : Vector3.zero;
            foreach (var rb in _bodies) rb.linearVelocity = carry;
            Push(rules);

            _deathPosition = transform.position;
            _settle = default;
            _state = State.Simulating;
            Simulated.Add(this);
        }

        GameObject MakeCollider(in RagdollPart p)
        {
            var go = new GameObject("RagdollCollider") { layer = Layer };
            go.transform.SetParent(p.Bone, false);
            switch (p.Shape)
            {
                case RagdollPart.Box:
                    var box = go.AddComponent<BoxCollider>();
                    box.center = p.Center; box.size = p.Size;
                    break;
                case RagdollPart.Sphere:
                    var sphere = go.AddComponent<SphereCollider>();
                    sphere.center = p.Center; sphere.radius = p.Size.x;
                    break;
                default:
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.center = p.Center; capsule.radius = p.Size.x; capsule.height = p.Size.y; capsule.direction = (int)p.Size.z;
                    break;
            }
            return go;
        }

        void Push(RagdollRules rules)
        {
            var hips = _bodies[0];
            var chest = _bodies.Length > 1 ? _bodies[1] : hips;
            var facing = transform.forward;
            bool hit = Time.time - _combatant.LastHitTime <= rules.HitMemorySeconds + rules.DelaySeconds && _combatant.LastHitCount > 0;
            var dir = hit ? _combatant.LastHitDirection : Vector3.zero;
            RagdollImpulse.Hit(dir.x, dir.y, dir.z, hit ? _combatant.LastHitCount : 0, facing.x, facing.z, rules, out float x, out float y, out float z);
            var impulse = new Vector3(x, y, z);
            // A grenade that went off close by in the same moment outweighs a bullet.
            if (Time.time - _blastAt <= rules.HitMemorySeconds + rules.DelaySeconds)
            {
                var away = hips.position - _blastCentre;
                float blast = RagdollImpulse.Blast(away.magnitude, rules);
                if (blast > impulse.magnitude) impulse = (away.normalized + Vector3.up * rules.Lift) * blast;
            }
            LastImpulse = impulse;
            // Part of it where the bullet landed (the hit part swings), the rest on the trunk (the body is knocked over).
            var at = hit ? _combatant.LastHitPoint : chest.position;
            var nearest = Nearest(at);
            nearest.AddForceAtPosition(impulse * 0.4f, at, ForceMode.Impulse);
            hips.AddForce(impulse * 0.3f, ForceMode.Impulse);
            chest.AddForce(impulse * 0.3f, ForceMode.Impulse);
        }

        Rigidbody Nearest(Vector3 point)
        {
            Rigidbody best = _bodies[0];
            float bestSq = float.MaxValue;
            foreach (var rb in _bodies)
            {
                float d = (rb.worldCenterOfMass - point).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = rb; }
            }
            return best;
        }

        void Settle(float dt)
        {
            float fastest = 0f;
            foreach (var rb in _bodies) fastest = Mathf.Max(fastest, rb.linearVelocity.magnitude);
            var hips = _parts[0].Bone.position;
            var flat = new Vector2(hips.x - _deathPosition.x, hips.z - _deathPosition.z).magnitude;
            float drift = Mathf.Max(flat, _deathPosition.y - hips.y);
            if (_settle.Tick(fastest, drift, dt, RagdollRules.Current)) Freeze();
        }

        // Keeps the pose it has now and drops every physics part.
        public void Freeze()
        {
            if (_state != State.Simulating) return;
            TearDown();
            _state = State.Frozen;
        }

        // Alive again (revived): the animator takes the bones back.
        void Restore()
        {
            if (_state == State.None) return;
            TearDown();
            _state = State.None;
            _animator.enabled = true;
        }

        void TearDown()
        {
            Simulated.Remove(this);
            if (_bodies == null) return;
            // Joints first: a body cannot go while a joint still holds it.
            foreach (var j in _joints) if (j != null) DestroyImmediate(j);
            foreach (var rb in _bodies) if (rb != null) Destroy(rb);
            foreach (var c in _colliders) if (c != null) Destroy(c);
            _bodies = null; _joints = null; _colliders = null;
        }
    }
}
