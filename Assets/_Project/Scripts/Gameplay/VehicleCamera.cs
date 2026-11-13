using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The local player's camera while in a car (ADR-065 (5)): a third-person camera orbiting the car with the mouse
    // look (the player's view yaw and pitch), or the seat's own eye view (V, or looking down the sights). Runs after
    // PlayerController has placed the on-foot camera this frame and after the animation, and before the held weapon
    // is put in front of the camera (WeaponVisual, order 100), so the first-person weapon follows this camera.
    // Also tells the aiming ray to look past the car and its occupants, so the crosshair aims at what is beyond it.
    [DefaultExecutionOrder(95)]
    public class VehicleCamera : MonoBehaviour
    {
        const float PivotHeight = 1.7f;
        const float Distance = 6.5f;
        const float MotorcycleScale = 0.75f;
        const float CollisionRadius = 0.25f;
        const float DistanceChangePerSecond = 8f;
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        PlayerController _player;
        Combatant _combatant;
        VehicleInput _input;
        float _distance = Distance;
        Vehicle _aimingPast;
        Light _cabin;

        // True while this frame's camera was placed by the car camera.
        public bool Active { get; private set; }

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _combatant = GetComponent<Combatant>();
            _input = GetComponent<VehicleInput>();
        }

        void OnDisable() => SetAimIgnore(null);

        void LateUpdate()
        {
            Active = false;
            if (_player == null || PlayerController.Local != _player || _combatant == null) return;
            var vehicle = VehicleSeat.VehicleOf(_combatant);
            SetAimIgnore(vehicle);
            var cam = _player.Camera;
            if (vehicle == null || cam == null) { if (_cabin != null) _cabin.enabled = false; return; }
            Active = true;

            float yaw = _player.ViewYaw, pitch = _player.ViewPitch;
            var look = Quaternion.Euler(-pitch, yaw, 0f);
            bool eyes = (_input != null ? _input.EyeView : _player.Rig.Mode == ViewMode.FirstPerson) || _combatant.AimMode == AimMode.Sights;
            CabinLight(vehicle, eyes && !vehicle.IsMotorcycle);
            if (eyes)
            {
                cam.transform.SetPositionAndRotation(_combatant.EyePosition, look);
                _distance = 0f;
                return;
            }
            // The motorcycle is smaller: the camera comes closer and lower.
            float scale = vehicle.IsMotorcycle ? MotorcycleScale : 1f;
            var pivot = vehicle.transform.position + Vector3.up * (PivotHeight * scale);
            var back = look * Vector3.back;
            float wanted = ClearDistance(vehicle, pivot, back, Distance * scale);
            // Pulled in at once when something is in the way, eased back out.
            _distance = wanted < _distance ? wanted : Mathf.MoveTowards(_distance, wanted, DistanceChangePerSecond * Time.deltaTime);
            cam.transform.SetPositionAndRotation(pivot + back * _distance, look);
        }

        // The cabin is dark under the roof in first person (the sun and the sky do not reach in): one soft fill light
        // follows the seated eye, off in third person and on a motorcycle. No shadows, so it costs next to nothing.
        void CabinLight(Vehicle vehicle, bool on)
        {
            if (_cabin == null)
            {
                if (!on) return;
                _cabin = new GameObject("CabinLight").AddComponent<Light>();
                _cabin.type = LightType.Point;
                _cabin.shadows = LightShadows.None;
                _cabin.range = 3.2f;
                _cabin.intensity = 1.6f;
                _cabin.color = new Color(1f, 0.96f, 0.9f);
            }
            _cabin.enabled = on;
            if (on) _cabin.transform.SetPositionAndRotation(_combatant.EyePosition + vehicle.transform.up * 0.15f, Quaternion.identity);
        }

        // How far back from the pivot the camera can go without passing into anything but the car and its occupants.
        static float ClearDistance(Vehicle vehicle, Vector3 pivot, Vector3 back, float max)
        {
            int count = Physics.SphereCastNonAlloc(pivot, CollisionRadius, back, Hits, max, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = max;
            for (int i = 0; i < count; i++)
            {
                var h = Hits[i];
                if (h.distance <= 0f || VehicleSeat.BelongsTo(vehicle, h.collider)) continue;
                best = Mathf.Min(best, h.distance);
            }
            return Mathf.Max(0.5f, best);
        }

        void SetAimIgnore(Vehicle vehicle)
        {
            if (vehicle == _aimingPast) return;
            _aimingPast = vehicle;
            if (vehicle == null) PlayerCameraRig.AimIgnore = null;
            else PlayerCameraRig.AimIgnore = c => VehicleSeat.BelongsTo(vehicle, c);
        }
    }
}
