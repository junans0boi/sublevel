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
        // The chase distance stays in these ranges whatever the model's bounds measure (ADR-222): a car 5.5..7.5 m back, a motorcycle 4..5.5 m.
        public const float CarMinDistance = 5.5f, CarMaxDistance = 7.5f, BikeMinDistance = 4f, BikeMaxDistance = 5.5f;
        public const float SpeedExtraMeters = 2f, SpeedExtraAtKmh = 90f;   // the camera drifts back with speed, up to this much more
        public const float DrivingFov = 78f;                                // first person in a vehicle (the on-foot value is narrower)
        const float FollowIdleSeconds = 1.2f, FollowDegPerSecond = 80f, FollowMinSpeed = 3f;   // third person: behind the car again once the mouse rests
        const float RoofClearance = 0.3f;
        const float CabinLightOverEye = 0.55f; // up under the roof: the own arms and shoulders right by it were blown out white
        const float CabinLightBehindEye = 0.9f; // behind the head, so the dash and wheel get an even wash instead of a hot spot (#99)
        // A car's seat eye sits low behind the dash: the view only is raised so the road past the bonnet is in the frame, not just the wheel and the dash top.
        public const float CarEyeRaise = 0.10f;
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
        float _idleFor;
        Vehicle _seated;
        Light _cabin, _cabinLow;

        // True while this frame's camera was placed by the car camera.
        public bool Active { get; private set; }
        // Last third-person camera distance from the pivot (0 in the eye view) and the framed distance it grows from.
        public float Distance => _distance;
        public float FramedDistance => _orbit;

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
            if (vehicle == null || cam == null) { if (_cabin != null) _cabin.enabled = _cabinLow.enabled = false; return; }
            Active = true;
            // Taking the wheel looks straight down the road, level: the view pitch of getting in (down at the door) stays out of it.
            if (vehicle != _seated)
            {
                _seated = vehicle;
                _player.LookAt(_combatant.EyePosition + vehicle.transform.forward * 30f);
            }
            FollowBehind(vehicle);

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
                if (!vehicle.IsMotorcycle) eye += vehicle.transform.up * CarEyeRaise;
                cam.transform.SetPositionAndRotation(eye, look);
                cam.fieldOfView = DrivingFov;
                _distance = 0f;
                return;
            }
            if (vehicle != _framed) Frame(vehicle);
            var pivot = vehicle.transform.TransformPoint(new Vector3(_pivotLocal.x, 0f, _pivotLocal.z)) + Vector3.up * _pivotLocal.y;
            var back = look * Vector3.back;
            float wanted = ClearDistance(vehicle, pivot, back, _orbit + SpeedExtra(vehicle.SpeedKmh));
            // Pulled in at once when something is in the way, eased back out.
            _distance = wanted < _distance ? wanted : Mathf.MoveTowards(_distance, wanted, DistanceChangePerSecond * Time.deltaTime);
            // Pulled in close (a wall behind), the camera would end up inside the body: over the roof instead.
            var at = vehicle.transform.InverseTransformPoint(pivot + back * _distance);
            if (Mathf.Abs(at.x - _local.center.x) < _local.extents.x && Mathf.Abs(at.z - _local.center.z) < _local.extents.z && at.y < _local.max.y + RoofClearance)
                at.y = _local.max.y + RoofClearance;
            cam.transform.SetPositionAndRotation(vehicle.transform.TransformPoint(at), look);
        }

        public static float SpeedExtra(float kmh) => SpeedExtraMeters * Mathf.Clamp01(Mathf.Abs(kmh) / SpeedExtraAtKmh);

        // Third person: while driving and the mouse rests, the view turns back behind the car, gently (a sharp turn is followed, not snapped to).
        void FollowBehind(Vehicle vehicle)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool moved = mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0.5f;
            _idleFor = moved ? 0f : _idleFor + Time.deltaTime;
            bool eyes = (_input != null ? _input.EyeView : _player.Rig.Mode == ViewMode.FirstPerson) || _combatant.AimMode == AimMode.Sights;
            if (eyes || _idleFor < FollowIdleSeconds || vehicle.Body == null || vehicle.Body.linearVelocity.magnitude < FollowMinSpeed) return;
            var flat = Vector3.ProjectOnPlane(vehicle.Body.linearVelocity, Vector3.up);
            if (Vector3.Dot(flat, vehicle.transform.forward) < 0f) flat = -flat;   // reversing: still behind the car's own back
            if (flat.sqrMagnitude < 0.01f) return;
            float heading = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float yaw = Mathf.MoveTowardsAngle(_player.ViewYaw, heading, FollowDegPerSecond * Time.deltaTime);
            var dir = Quaternion.Euler(-_player.ViewPitch, yaw, 0f) * Vector3.forward;
            _player.LookAt(_combatant.EyePosition + dir * 30f);
        }

        // The headlamp beams (VehicleLights, runtime) are cones metres long: counted in the bounds they pushed the chase pivot 7 m ahead and 3.8 m up, so the camera sat over the car looking away from it.
        public static bool IsLightBeam(Renderer r) => r.name == "Beam" || (r.transform.parent != null && r.transform.parent.name == "Headlamps");

        // The model's bounds in the vehicle's own space from its meshes (not the renderers' world boxes: those grow with the
        // yaw the vehicle happens to have, which made the distance depend on how it was parked). Renderers only, no occupants.
        public static Bounds LocalBounds(Transform t)
        {
            Bounds local = default;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly || IsLightBeam(r)) continue;
                Bounds b;
                if (r is SkinnedMeshRenderer sk) b = sk.localBounds;
                else if (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) b = mf.sharedMesh.bounds;
                else continue;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var p = t.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }
            return any ? local : new Bounds(Vector3.up * 0.7f, new Vector3(1.8f, 1.4f, 4.5f));
        }

        // At a car's wheel the eye view goes down (view only) until the grips are GripsCarDeg below level (the wheel
        // top about the lower third of the frame), within MaxCarDrop. A motorcycle's bars sit lower and nearer: the
        // same lowering (GripsBikeDeg, MaxBikeDrop) brings them to the bottom edge looking straight ahead (#143). Never
        // moved back: that puts the rider's own back in front of the view (tried: the back covers the road).
        const float GripsCarDeg = 24f, MaxCarDrop = 0f, GripsBikeDeg = 36f, MaxBikeDrop = 0.22f;

        Vector3 GripsInView(Vehicle vehicle, Vector3 eye)
        {
            var seat = VehicleSeat.SeatOf(_combatant);
            if (seat == null || !seat.HasGrips || !VehicleSeat.InDriverSeat(_combatant)) return Vector3.zero;
            bool bike = vehicle.IsMotorcycle;
            var t = vehicle.transform;
            var to = (seat.GripLeft.position + seat.GripRight.position) * 0.5f - eye;
            float ahead = Vector3.Dot(to, t.forward), down = -Vector3.Dot(to, t.up);
            if (ahead <= 0.01f || down <= 0f) return Vector3.zero;
            return -t.up * Mathf.Clamp(down - ahead * Mathf.Tan((bike ? GripsBikeDeg : GripsCarDeg) * Mathf.Deg2Rad), 0f, bike ? MaxBikeDrop : MaxCarDrop);
        }

        // The model's bounds in the vehicle's own space (renderers only: the occupants are not children).
        void Frame(Vehicle vehicle)
        {
            _framed = vehicle;
            var local = LocalBounds(vehicle.transform);
            _local = local;
            Orbit(local, vehicle.IsMotorcycle, out float height, out _orbit);
            _pivotLocal = new Vector3(local.center.x, height, local.center.z);
        }

        // Pivot height over the vehicle's origin and the camera distance for a model with these local bounds.
        public static void Orbit(Bounds local, bool motorcycle, out float pivotHeight, out float distance)
        {
            pivotHeight = local.max.y + PivotOverTop;
            if (motorcycle) pivotHeight = Mathf.Max(pivotHeight, MotorcycleMinPivot);
            distance = Mathf.Clamp(local.size.z * DistancePerLength + DistanceExtra, motorcycle ? BikeMinDistance : CarMinDistance, motorcycle ? BikeMaxDistance : CarMaxDistance);
        }

        // The cabin is dark under the roof in first person (the sun and the sky do not reach in): one soft fill light
        // follows the seated eye, off in third person and on a motorcycle. No shadows, so it costs next to nothing.
        void CabinLight(Vehicle vehicle, bool on)
        {
            if (_cabin == null)
            {
                if (!on) return;
                _cabin = NewCabinLight("CabinLight", 7f, 10f);
                // A second, low one at hip height behind the driver (not near the wheel, which it would blow out): lights the seats' bases and the door panels that the one under the roof leaves dark (#99).
                _cabinLow = NewCabinLight("CabinLightLow", 4.5f, 7f);
            }
            _cabin.enabled = _cabinLow.enabled = on;
            if (!on) return;
            var t = vehicle.transform;
            _cabin.transform.SetPositionAndRotation(_combatant.EyePosition + t.up * CabinLightOverEye - t.forward * CabinLightBehindEye, Quaternion.identity);
            _cabinLow.transform.SetPositionAndRotation(_combatant.EyePosition - t.up * 0.45f - t.forward * 0.45f, Quaternion.identity);
        }

        static Light NewCabinLight(string name, float range, float intensity)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Point;
            l.shadows = LightShadows.None;
            l.range = range;
            l.intensity = intensity;
            l.color = new Color(1f, 0.96f, 0.9f);
            return l;
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
