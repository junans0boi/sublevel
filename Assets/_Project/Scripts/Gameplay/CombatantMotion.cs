using UnityEngine;

namespace Game.Gameplay
{
    // Measures how a combatant is actually moving, from its transform, so the same presentation code
    // works for the local player, remote players and server-driven characters.
    public class CombatantMotion : MonoBehaviour
    {
        const float AirborneVerticalSpeed = 1.5f;
        const float TeleportDistance = 5f;

        Vector3 _lastPosition;

        public Vector3 LocalVelocity { get; private set; }
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
                return;
            }
            // A teleport (spawn, test hook) is not movement.
            if (delta.sqrMagnitude > TeleportDistance * TeleportDistance) delta = Vector3.zero;
            LocalVelocity = transform.InverseTransformDirection(delta / dt);
            Airborne = Mathf.Abs(delta.y / dt) > AirborneVerticalSpeed;
        }
    }
}
