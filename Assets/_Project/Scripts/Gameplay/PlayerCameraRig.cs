using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    public enum ViewMode { ThirdPerson, FirstPerson }

    // Everything besides yaw and pitch that shapes the view this frame.
    public struct ViewState
    {
        public AimMode Aim;
        public float Lean;          // -1 (left) .. 1 (right), eased
        public float LeanRollDeg;   // camera roll at full lean
        public float RecoilPitch;   // degrees, up positive
        public float RecoilYaw;
        public float ShoulderAimDistance;
    }

    // Self-contained camera placement (ADR-031): over-the-shoulder TPP with wall collision, or FPP at the eyes.
    // Aiming down sights uses the eye view in either mode; shouldering pulls the TPP camera in; leaning moves the
    // view sideways with the head and rolls it a little at the eyes (ADR-045, ADR-048).
    public class PlayerCameraRig
    {
        public float Distance = 3.2f;
        public float ShoulderOffset = 0.55f;
        public float CollisionRadius = 0.2f;
        public float DistanceChangePerSecond = 9f;

        public ViewMode Mode { get; private set; } = ViewMode.ThirdPerson;

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        // [VEHICLE HOOK] ADR-065 (5): colliders the view rays look past (set by VehicleCamera while in a car: the car
        // and its occupants, so the crosshair aims at what lies beyond them). Null on foot.
        public static System.Func<Collider, bool> AimIgnore;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => AimIgnore = null;
        float _distance = -1f;

        public void ToggleMode() => Mode = Mode == ViewMode.ThirdPerson ? ViewMode.FirstPerson : ViewMode.ThirdPerson;

        // True when the camera sits at the eyes, so the player's own body should not be drawn.
        public bool IsEyeView(AimMode aim) => Mode == ViewMode.FirstPerson || aim == AimMode.Sights;

        public void Place(Transform camera, Transform player, Vector3 eye, float yaw, float pitch, in ViewState view, float dt)
        {
            // Leaning right rolls the camera clockwise (the horizon appears to tip the other way): a negative roll.
            camera.rotation = Quaternion.Euler(-(pitch + view.RecoilPitch), yaw + view.RecoilYaw, -view.Lean * view.LeanRollDeg);
            float wanted = view.Aim == AimMode.Shoulder ? view.ShoulderAimDistance : Distance;
            _distance = _distance < 0f ? wanted : Mathf.MoveTowards(_distance, wanted, DistanceChangePerSecond * dt);
            if (IsEyeView(view.Aim))
            {
                camera.position = eye;
                return;
            }

            // The eye already carries the lean offset, so the camera follows the head sideways. It stays over the
            // right shoulder whichever way the body leans (ADR-048).
            var aimRotation = Quaternion.Euler(-(pitch + view.RecoilPitch), yaw + view.RecoilYaw, 0f);
            var offset = aimRotation * new Vector3(ShoulderOffset, 0f, -_distance);
            float length = offset.magnitude;
            var dir = offset / length;
            // Pull the camera in front of anything between the pivot and its ideal spot.
            if (NearestHit(Physics.SphereCastNonAlloc(eye, CollisionRadius, dir, Hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), player, out var hit))
                length = Mathf.Max(0.05f, hit.distance);
            camera.position = eye + dir * length;
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
                if (AimIgnore != null && AimIgnore(h.collider)) continue;
                best = h.distance;
                nearest = h;
            }
            return best < float.MaxValue;
        }
    }
}
