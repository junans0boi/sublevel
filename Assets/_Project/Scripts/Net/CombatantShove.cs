using UnityEngine;
using UnityEngine.AI;

namespace Game.Net
{
    // A push given to a body on foot (a vehicle running someone over, #112), run where that body is moved: the
    // owning player's client (its CharacterController) or the server for a bot (its NavMeshAgent, flat only) or a
    // training dummy (its transform, flat only).
    // Added on first use. The upward part is spent against gravity here; the body's own gravity does the falling.
    public class CombatantShove : MonoBehaviour
    {
        const float Gravity = 9.81f;
        const float GroundFriction = 10f; // m/s² once back on the ground
        const float LeaveGroundSeconds = 0.15f;

        Vector3 _velocity;
        float _since;
        Combatant _combatant;
        CharacterController _controller;
        NavMeshAgent _agent;

        public Vector3 Velocity => enabled ? _velocity : Vector3.zero;

        public static void Apply(Combatant c, Vector3 velocity)
        {
            if (c == null) return;
            if (!c.TryGetComponent<CombatantShove>(out var shove)) shove = c.gameObject.AddComponent<CombatantShove>();
            shove._velocity = velocity;
            shove._since = 0f;
            shove.enabled = true;
        }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _controller = GetComponent<CharacterController>();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();
            if ((_combatant != null && (_combatant.Dead.Value || VehicleSeat.IsAttached(_combatant))) || _velocity.sqrMagnitude < 0.01f)
            {
                enabled = false;
                return;
            }
            _since += dt;
            var step = _velocity * dt;
            bool grounded;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.Move(new Vector3(step.x, 0f, step.z));
                grounded = true;
            }
            else if (_controller != null && _controller.enabled)
            {
                _controller.Move(step);
                grounded = _controller.isGrounded;
            }
            else
            {
                // A body nothing walks (the training dummy): slid along the ground.
                transform.position += new Vector3(step.x, 0f, step.z);
                grounded = true;
            }
            _velocity.y = Mathf.Max(0f, _velocity.y - Gravity * dt);
            if (grounded && _since >= LeaveGroundSeconds)
            {
                var flat = Vector3.MoveTowards(new Vector3(_velocity.x, 0f, _velocity.z), Vector3.zero, GroundFriction * dt);
                _velocity = new Vector3(flat.x, _velocity.y, flat.z);
            }
        }
    }
}
