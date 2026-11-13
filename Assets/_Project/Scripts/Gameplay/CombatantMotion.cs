using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Measures how a combatant is actually moving, from its transform, so the same presentation code
    // works for the local player, remote players and server-driven characters.
    public class CombatantMotion : MonoBehaviour
    {
        // A ball rather than a ray, so standing with the middle of the body just past an edge still counts as on
        // the ground; small enough not to touch a slope before it is cast.
        const float ProbeRadius = 0.2f;
        const float ProbeStartHeight = 0.5f;
        const float TeleportDistance = 5f;

        Vector3 _lastPosition;

        public Vector3 LocalVelocity { get; private set; }
        public float VerticalSpeed { get; private set; }
        // No ground close under the feet. Unlike a speed test this holds through the top of a jump.
        public bool Airborne { get; private set; }

        void OnEnable() => _lastPosition = transform.position;

        void Update()
        {
            var delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                // Paused: nothing moved this frame.
                LocalVelocity = Vector3.zero;
                VerticalSpeed = 0f;
                return;
            }
            // A teleport (spawn, test hook) is not movement.
            if (delta.sqrMagnitude > TeleportDistance * TeleportDistance) delta = Vector3.zero;
            // On a moving elevator car (ADR-065 (1), ADR-079): standing, not falling or jumping.
            bool carried = ElevatorCar.Carries(transform.position);
            if (carried) delta.y = 0f;
            LocalVelocity = transform.InverseTransformDirection(delta / dt);
            VerticalSpeed = delta.y / dt;
            // The cast starts inside this combatant's own collider, which a cast does not report.
            Airborne = !carried && !Physics.SphereCast(transform.position + Vector3.up * ProbeStartHeight, ProbeRadius, Vector3.down, out _,
                ProbeStartHeight - ProbeRadius + GameData.Rules.Jump.GroundProbeMeters, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
