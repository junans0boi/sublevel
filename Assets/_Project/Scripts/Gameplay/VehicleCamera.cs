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
        // The orbit is sized from the vehicle model's bounds (low sports car, tall pickup, long van): the pivot just
        // over the roof, so a camera pulled in by a wall stays above the body instead of inside it.
        const float PivotOverTop = 0.4f;
        const float MotorcycleMinPivot = 1.5f; // over the rider's head, not just the bike
        const float DistancePerLength = 1f;
        const float DistanceExtra = 2.2f;
        const float RoofClearance = 0.3f;
        const float CabinLightOverEye = 0.35f; // up under the roof: the own arms and shoulders right by it were blown out white
        const float CollisionRadius = 0.25f;
        const float DistanceChangePerSecond = 8f;
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        PlayerController _player;
        Combatant _combatant;
        VehicleInput _input;
        CombatantAnimation _animation;
        Bounds _local;
        float _distance = 6.5f;
        Vehicle _aimingPast;
        Vehicle _framed;
        Vector3 _pivotLocal;
        float _orbit;
        Light _cabin;

        // True while this frame's camera was placed by the car camera.
        public bool Active { get; private set; }

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _combatant = GetComponent<Combatant>();
            _input = GetComponent<VehicleInput>();
            _animation = GetComponent<CombatantAnimation>();
        }

        void OnDisable() => SetAimIgnore(null);

        void LateUpdate() => Place();

        // Also called by the capture tool, whose shots are taken before LateUpdate.
        public void Place()
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
            // Also in third person: the close views of the hands in the cabin are lit too.
            CabinLight(vehicle, !vehicle.IsMotorcycle);
            if (eyes)
            {
                // The view goes forward / sideways with the posed head (hips on the cushion, a rider's forward lean), so
                // the own shoulders are behind it, never round the camera; the height stays the seat's eye, so the
                // wheel and the hands on it are in the lower frame. View only: the shot origin stays at the seat's eye.
                var eye = _combatant.EyePosition;
                if (_animation != null && _animation.HeadEye(out var head)) eye += Vector3.ProjectOnPlane(head - eye, vehicle.transform.up);
                eye += GripsInView(vehicle, eye);
                cam.transform.SetPositionAndRotation(eye, look);
                _distance = 0f;
                return;
            }
            if (vehicle != _framed) Frame(vehicle);
            var pivot = vehicle.transform.TransformPoint(new Vector3(_pivotLocal.x, 0f, _pivotLocal.z)) + Vector3.up * _pivotLocal.y;
            var back = look * Vector3.back;
            float wanted = ClearDistance(vehicle, pivot, back, _orbit);
            // Pulled in at once when something is in the way, eased back out.
            _distance = wanted < _distance ? wanted : Mathf.MoveTowards(_distance, wanted, DistanceChangePerSecond * Time.deltaTime);
            // Pulled in close (a wall behind), the camera would end up inside the body: over the roof instead.
            var at = vehicle.transform.InverseTransformPoint(pivot + back * _distance);
            if (Mathf.Abs(at.x - _local.center.x) < _local.extents.x && Mathf.Abs(at.z - _local.center.z) < _local.extents.z && at.y < _local.max.y + RoofClearance)
                at.y = _local.max.y + RoofClearance;
            cam.transform.SetPositionAndRotation(vehicle.transform.TransformPoint(at), look);
        }

        // At a car's wheel the eye view goes down (view only) until the grips are GripsCarDeg below level (the wheel
        // top about the lower third of the frame), within MaxCarDrop. Not on a motorcycle: moving that view back puts
        // the rider's own back in front of it (tried: the bars stay under the frame, the back covers the road).
        const float GripsCarDeg = 24f, MaxCarDrop = 0.12f;

        Vector3 GripsInView(Vehicle vehicle, Vector3 eye)
        {
            var seat = VehicleSeat.SeatOf(_combatant);
            if (vehicle.IsMotorcycle || seat == null || !seat.HasGrips || !VehicleSeat.InDriverSeat(_combatant)) return Vector3.zero;
            var t = vehicle.transform;
            var to = (seat.GripLeft.position + seat.GripRight.position) * 0.5f - eye;
            float ahead = Vector3.Dot(to, t.forward), down = -Vector3.Dot(to, t.up);
            if (ahead <= 0.01f || down <= 0f) return Vector3.zero;
            return -t.up * Mathf.Clamp(down - ahead * Mathf.Tan(GripsCarDeg * Mathf.Deg2Rad), 0f, MaxCarDrop);
        }

        // The model's bounds in the vehicle's own space (renderers only: the occupants are not children).
        void Frame(Vehicle vehicle)
        {
            _framed = vehicle;
            var t = vehicle.transform;
            var local = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (var r in vehicle.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = t.InverseTransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }
            if (!any) local = new Bounds(Vector3.up * 0.7f, new Vector3(1.8f, 1.4f, 4.5f));
            _local = local;
            Orbit(local, vehicle.IsMotorcycle, out float height, out _orbit);
            _pivotLocal = new Vector3(local.center.x, height, local.center.z);
        }

        // Pivot height over the vehicle's origin and the camera distance for a model with these local bounds.
        public static void Orbit(Bounds local, bool motorcycle, out float pivotHeight, out float distance)
        {
            pivotHeight = local.max.y + PivotOverTop;
            if (motorcycle) pivotHeight = Mathf.Max(pivotHeight, MotorcycleMinPivot);
            distance = local.size.z * DistancePerLength + DistanceExtra;
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
            if (on) _cabin.transform.SetPositionAndRotation(_combatant.EyePosition + vehicle.transform.up * CabinLightOverEye, Quaternion.identity);
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
