using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // How low a combatant holds its body, and what falling does to it (ADR-053).
    // The owner asks for a stance; the server decides and replicates it. Every side then derives the same eye
    // height, spread share and capsule height from the replicated value, so the server's shots and hit zones
    // agree with what the clients show. Falls are reported by the owner (movement is the owner's, ADR-029) and
    // checked against what the server watched before they hurt.
    [RequireComponent(typeof(Combatant))]
    public class CombatantStance : NetworkBehaviour
    {
        public readonly SyncVar<int> State = new SyncVar<int>(); // Stance

        static readonly RaycastHit[] HeadHits = new RaycastHit[8];

        Combatant _combatant;
        CharacterController _controller;
        CapsuleCollider _capsule;
        float _standHeight, _radius, _feetY;
        float _appliedHeight = -1f;
        float _crouch; // 0..1, eased
        float _prone;  // 0..1, eased

        // Server: its own view of this combatant's falls, and the owner's report waiting to be checked against it.
        readonly FallTracker _watched = new FallTracker();
        float _watchedDrop;
        float _watchedAt = float.NegativeInfinity;
        float _reported;
        float _reportedAt;

        public Stance Current => (Stance)State.Value;
        // 0 = upright, 1 = fully crouched; eased, the same on every side.
        public float CrouchAmount => _crouch;
        // 0 = not lying down, 1 = fully prone; eased, the same on every side.
        public float ProneAmount => _prone;
        public float MoveFactor => Blend(1f, StanceRules.Current.Crouch.MoveFactor, StanceRules.Current.Prone.MoveFactor);
        // Footsteps are this much quieter in the current stance (1 = full volume).
        public float FootstepVolumeFactor => Blend(1f, StanceRules.Current.Crouch.FootstepVolumeFactor, StanceRules.Current.Prone.FootstepVolumeFactor);

        // A per-stance value at the current (eased) mix of standing, crouching and lying down.
        float Blend(float stand, float crouch, float prone) => stand + (crouch - stand) * _crouch + (prone - stand) * _prone;
        // Height of the collision capsule right now.
        public float CapsuleHeight => _controller != null ? _controller.height : _capsule != null ? _capsule.height : _standHeight;

        // Server: the last fall that hurt, for tests and feedback.
        public DamageInfo LastFall { get; private set; }
        public float LastFallMeters { get; private set; }
        public int FallsHurt { get; private set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _controller = GetComponent<CharacterController>();
            _capsule = GetComponent<CapsuleCollider>();
            if (_controller != null)
            {
                _standHeight = _controller.height;
                _radius = _controller.radius;
                _feetY = _controller.center.y - _controller.height * 0.5f;
            }
            else if (_capsule != null)
            {
                _standHeight = _capsule.height;
                _radius = _capsule.radius;
                _feetY = _capsule.center.y - _capsule.height * 0.5f;
            }
            else
            {
                _standHeight = StanceRules.Current.Stand.CapsuleHeight;
                _radius = 0.35f;
            }
        }

        void Update()
        {
            var rules = StanceRules.Current;
            if (IsServerInitialized)
            {
                // The dead hold no stance.
                if (State.Value != 0 && _combatant.Dead.Value) State.Value = 0;
                // Only owner-moved characters report falls; whatever moves a bot calls ServerApplyFall itself.
                if (_combatant.IsPlayer) ServerTickFall();
            }

            var current = Current;
            _crouch = Mathf.MoveTowards(_crouch, current == Stance.Crouch ? 1f : 0f, rules.ChangePerSecond * Time.deltaTime);
            _prone = Mathf.MoveTowards(_prone, current == Stance.Prone ? 1f : 0f, rules.ChangePerSecond * Time.deltaTime);
            // Going from crouching to lying (or back) the two weights cross over; the mix stays between the two stances.
            _combatant.EyeHeightOffset = -(rules.Crouch.EyeDropMeters * _crouch + rules.Prone.EyeDropMeters * _prone);
            _combatant.StanceSpreadFactor = Blend(1f, rules.Crouch.SpreadFactor, rules.Prone.SpreadFactor);
            // The capsule changes at once with the stance: head room was checked for the target height, so there is
            // no moment in which a half-risen capsule can walk under something lower. Never shorter than it is wide.
            SetCapsuleHeight(Mathf.Max(Mathf.Min(_standHeight, rules.For(current).CapsuleHeight), 2f * _radius));
        }

        void SetCapsuleHeight(float height)
        {
            if (height == _appliedHeight) return;
            _appliedHeight = height;
            // The feet stay where they are; only the top comes down.
            if (_controller != null)
            {
                var c = _controller.center;
                _controller.height = height;
                _controller.center = new Vector3(c.x, _feetY + height * 0.5f, c.z);
            }
            else if (_capsule != null)
            {
                var c = _capsule.center;
                _capsule.height = height;
                _capsule.center = new Vector3(c.x, _feetY + height * 0.5f, c.z);
            }
        }

        // Is there room above the head to stand up? Sweeps the top of the capsule up to the standing height.
        public bool CanStand() => CanReach(_standHeight);

        // The same for any taller capsule: is there room to raise the top of the capsule to `targetHeight`?
        public bool CanReach(float targetHeight)
        {
            float height = CapsuleHeight;
            float rise = Mathf.Min(targetHeight, _standHeight) - height;
            if (rise <= 1e-3f) return true;
            var top = transform.position + Vector3.up * (_feetY + height - _radius); // centre of the capsule's top end
            int hits = Physics.SphereCastNonAlloc(top, _radius, Vector3.up, HeadHits,
                rise + StanceRules.Current.HeadClearanceMeters, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                var hit = HeadHits[i];
                // Distance 0 means the sweep started touching it: a wall the capsule stands against, not a ceiling.
                if (hit.distance <= 0f || hit.collider.transform.IsChildOf(transform)) continue;
                return false;
            }
            return true;
        }

        // ---- Server API ----

        [Server]
        public void ServerSetStance(Stance stance)
        {
            if (_combatant.Dead.Value || stance == Current) return;
            // [VEHICLE HOOK] ADR-065 (5): seated stays upright (C / Z mean other things in a vehicle).
            if (VehicleSeat.IsAttached(_combatant)) return;
            var rules = StanceRules.Current;
            if (stance != Stance.Stand && stance != Stance.Crouch && !(stance == Stance.Prone && rules.ProneEnabled)) return;
            // Getting up needs head room for the taller capsule, whatever the owner believes.
            if (Stances.Rises(Current, stance, rules) && !CanReach(rules.For(stance).CapsuleHeight)) return;
            State.Value = (int)stance;
        }

        // The owner landed after dropping `meters`. Checked against the server's own view on the next update.
        [Server]
        public void ServerReportFall(float meters)
        {
            if (_combatant.Dead.Value || !(meters > FallRules.Current.SafeMeters) || float.IsInfinity(meters)) return;
            // Two reports before the first is settled: one fall, the larger claim.
            _reported = Mathf.Max(_reported, meters);
            _reportedAt = Time.time;
        }

        // Fall damage for a character the server moves itself (bots): no report to check.
        [Server]
        public void ServerApplyFall(float meters)
        {
            float damage = Locomotion.FallDamage(meters, FallRules.Current);
            if (damage <= 0f || _combatant.Dead.Value) return;
            var info = new DamageInfo { Amount = damage, Attacker = null, Zone = HitZone.Body, WeaponId = 0, Cause = DamageCause.Fall };
            LastFall = info;
            LastFallMeters = meters;
            FallsHurt++;
            _combatant.ServerTakeDamage(info);
        }

        // Watches the combatant's height as the server sees it and settles a waiting report: the owner's number
        // counts only up to the drop seen here plus a tolerance. A report that no watched landing confirms in time
        // is measured against whatever drop is still in progress (none, if the combatant never left the ground).
        void ServerTickFall()
        {
            var rules = FallRules.Current;
            float y = transform.position.y;
            float landed = _watched.Tick(!_combatant.Airborne, y);
            if (landed > 0f)
            {
                _watchedDrop = landed;
                _watchedAt = Time.time;
            }
            if (_reported <= 0f) return;

            bool waited = Time.time - _reportedAt > rules.ReportWindowSeconds;
            // Still falling as the server sees it (a remote owner is ahead of its replicated position): wait for
            // the landing here before judging.
            if (_watched.InAir && !waited) return;
            float observed;
            if (Time.time - _watchedAt <= rules.ReportWindowSeconds) observed = _watchedDrop;
            else if (waited) observed = _watched.DroppedSoFar(y);
            else return; // the server's view may still be catching up
            float accepted = Locomotion.AcceptedFall(_reported, observed, rules);
            // One watched drop pays for one report.
            _reported = 0f;
            _watchedDrop = 0f;
            _watchedAt = float.NegativeInfinity;
            ServerApplyFall(accepted);
        }

        // ---- Owner requests ----

        [ServerRpc]
        public void CmdSetStance(int stance) => ServerSetStance((Stance)stance);

        [ServerRpc]
        public void CmdReportFall(float meters) => ServerReportFall(meters);
    }
}
