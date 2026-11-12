using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Plays body animations for a combatant: idle, four-way run, shooting, hit reaction and death.
    // The controller only holds states; this component cross-fades between them.
    [RequireComponent(typeof(Combatant), typeof(CombatantMotion))]
    public class CombatantAnimation : MonoBehaviour
    {
        public const string Idle = "Idle", Forward = "Forward", Back = "Back", Left = "Left", Right = "Right",
            Shoot = "Shoot", Hit = "Hit", Death = "Death";

        const float Fade = 0.12f;
        const float MinMoveSpeed = 0.3f;
        const float ShootHold = 0.25f;
        const float HitHold = 0.35f;

        [SerializeField] Animator _animator;
        [SerializeField] float _fullSpeed = 7f;

        Combatant _combatant;
        CombatantMotion _motion;
        string _current;
        float _holdUntil;
        float _lastHp;

        public string CurrentState => _current;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _motion = GetComponent<CombatantMotion>();
            _combatant.ShotFired += OnShot;
        }

        void OnDestroy() => _combatant.ShotFired -= OnShot;

        void OnShot(Vector3 origin, Vector3 end)
        {
            // Only the standing shot has its own clip; while running the legs keep going.
            if (_current == Idle || _current == Shoot) PlayOnce(Shoot, ShootHold);
        }

        void Update()
        {
            if (_animator == null) return;
            float hp = _combatant.Hp.Value;
            bool dead = _combatant.Dead.Value;
            if (hp < _lastHp && !dead) PlayOnce(Hit, HitHold);
            _lastHp = hp;

            var v = _motion.LocalVelocity;
            var move = MoveAnimSelector.Pick(v.x, v.z, dead, MinMoveSpeed);
            if (move == MoveAnim.Dead) { CrossFade(Death); return; }
            // A hit reaction plays out even on the move; the standing shot only holds while standing.
            if (Time.time < _holdUntil && (move == MoveAnim.Idle || _current == Hit)) return;

            _animator.speed = move == MoveAnim.Idle ? 1f : Mathf.Clamp(new Vector2(v.x, v.z).magnitude / _fullSpeed, 0.6f, 1.2f);
            CrossFade(move == MoveAnim.Forward ? Forward : move == MoveAnim.Back ? Back
                : move == MoveAnim.Left ? Left : move == MoveAnim.Right ? Right : Idle);
        }

        void PlayOnce(string state, float hold)
        {
            _animator.speed = 1f;
            _animator.CrossFadeInFixedTime(state, 0.05f, 0, 0f);
            _current = state;
            _holdUntil = Time.time + hold;
        }

        void CrossFade(string state)
        {
            if (_current == state) return;
            if (state == Death) _animator.speed = 1f;
            _animator.CrossFadeInFixedTime(state, Fade, 0);
            _current = state;
        }
    }
}
