using UnityEngine;

namespace Game.Net
{
    // The moving parts of a vehicle model that are not wheels (#89), every side, after Vehicle has placed the wheels:
    // - the steering part (a car's steering wheel, a motorcycle's handlebar and fork) turns about its own Z axis by the
    //   front wheels' angle times a ratio (negative for a steering wheel: its Z points from the driver to the dash);
    // - a motorcycle's swingarm turns about its pivot so its end stays on the rear wheel through the suspension travel.
    // The seats' grips are children of the steering part, so the driver's hands turn with it.
    [DefaultExecutionOrder(-99)] // after Vehicle (-100) in LateUpdate, before the riders' animation reads the grips
    public class VehicleRig : MonoBehaviour
    {
        [SerializeField] Vehicle _vehicle;
        [SerializeField] Transform _steering;
        [SerializeField] float _steeringRatio = 1f;
        [SerializeField] Transform _swingarm;
        [SerializeField] int _swingWheel = 1;
        // The rear axle as built, in the swingarm's parent space.
        [SerializeField] Vector3 _swingEnd;

        Quaternion _steeringRest = Quaternion.identity, _swingRest = Quaternion.identity;

        public Transform Steering => _steering;
        public float SteeringRatio => _steeringRatio;

        void Awake()
        {
            if (_vehicle == null) _vehicle = GetComponent<Vehicle>();
            if (_steering != null) _steeringRest = _steering.localRotation;
            if (_swingarm != null) _swingRest = _swingarm.localRotation;
        }

        void LateUpdate()
        {
            if (_vehicle == null) return;
            if (_steering != null)
                _steering.localRotation = _steeringRest * Quaternion.AngleAxis(_vehicle.SteerShown * _steeringRatio, Vector3.forward);
            var wheel = _vehicle.WheelPivot(_swingWheel);
            if (_swingarm != null && wheel != null && _swingarm.parent != null)
            {
                var pivot = _swingarm.localPosition;
                var rest = _swingEnd - pivot;
                var now = _swingarm.parent.InverseTransformPoint(wheel.position) - pivot;
                float angle = Vector3.SignedAngle(new Vector3(0f, rest.y, rest.z), new Vector3(0f, now.y, now.z), Vector3.right);
                _swingarm.localRotation = Quaternion.AngleAxis(angle, Vector3.right) * _swingRest;
            }
        }

        // Editor builder only.
        public void Configure(Vehicle vehicle, Transform steering, float steeringRatio, Transform swingarm, int swingWheel, Vector3 swingEnd)
        {
            _vehicle = vehicle;
            _steering = steering;
            _steeringRatio = steeringRatio;
            _swingarm = swingarm;
            _swingWheel = swingWheel;
            _swingEnd = swingEnd;
        }
    }
}
