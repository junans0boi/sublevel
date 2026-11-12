using UnityEngine;

namespace Game.Gameplay
{
    public enum ViewMode { ThirdPerson, FirstPerson }

    // Self-contained camera placement (ADR-031): over-the-shoulder TPP with wall collision, or FPP at the eyes.
    public class PlayerCameraRig
    {
        public float Distance = 3.2f;
        public float ShoulderOffset = 0.55f;
        public float PivotHeight = 1.6f;
        public float CollisionRadius = 0.2f;

        public ViewMode Mode { get; private set; } = ViewMode.ThirdPerson;
        public int ShoulderSide { get; private set; } = 1; // 1 = right, -1 = left

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        public void ToggleMode() => Mode = Mode == ViewMode.ThirdPerson ? ViewMode.FirstPerson : ViewMode.ThirdPerson;
        public void SwapShoulder() => ShoulderSide = -ShoulderSide;

        public void Place(Transform camera, Transform player, Vector3 eye, float yaw, float pitch)
        {
            var rotation = Quaternion.Euler(-pitch, yaw, 0f);
            camera.rotation = rotation;
            if (Mode == ViewMode.FirstPerson)
            {
                camera.position = eye;
                return;
            }

            var pivot = player.position + Vector3.up * PivotHeight;
            var offset = rotation * new Vector3(ShoulderOffset * ShoulderSide, 0f, -Distance);
            float length = offset.magnitude;
            var dir = offset / length;
            // Pull the camera in front of anything between the pivot and its ideal spot.
            if (NearestHit(Physics.SphereCastNonAlloc(pivot, CollisionRadius, dir, Hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), player, out var hit))
                length = Mathf.Max(0.05f, hit.distance);
            camera.position = pivot + dir * length;
        }

        // Where the centre of the screen points, ignoring the player's own colliders.
        public Vector3 AimPoint(Transform camera, Transform player, float maxDistance)
        {
            int count = Physics.RaycastNonAlloc(camera.position, camera.forward, Hits, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return NearestHit(count, player, out var hit) ? hit.point : camera.position + camera.forward * maxDistance;
        }

        static bool NearestHit(int count, Transform ignoreRoot, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = Hits[i];
                // distance 0 means the cast started inside the collider.
                if (h.distance <= 0f || h.collider.transform.IsChildOf(ignoreRoot) || h.distance >= best) continue;
                best = h.distance;
                nearest = h;
            }
            return best < float.MaxValue;
        }
    }
}
