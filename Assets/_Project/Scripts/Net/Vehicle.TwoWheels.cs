using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The motorcycle's parts of Vehicle (VehicleKind.Motorcycle):
    // - On the ground the frame is held upright by a data-driven spring-damper torque about its forward axis; the lean
    //   into turns, the wheelie and the kickstand rest are shown by turning the Model (and the riders' seats under
    //   it), not by the physics body, so the wheel casts stay simple and stable.
    // - With both wheels off the ground the rider steers the body itself: Space nose up, Ctrl nose down, Q/E roll.
    // - Landing tilted further than CrashLandAngleDeg from the ground, or any impact of CrashImpactSpeed or more,
    //   throws the riders off (hurt by the speed, Vehicles.ExitDamage) and leaves the machine to fall over; it rights
    //   itself slowly afterwards like an overturned car.
    public partial class Vehicle
    {
        const float LandingMinAirSeconds = 0.2f;

        float _crashedUntil = float.NegativeInfinity;
        float _airSeconds;
        float _leanShown;
        float _wheelieShown;

        public bool Crashed => Time.time < _crashedUntil;
        // The model's lean right now (degrees, positive = right), every side.
        public float LeanShown => _leanShown;

        void SimulateTwoWheels(float dt, in DriverInput input, bool wasAirborne, Vector3 groundNormal)
        {
            var r = Rules;
            var forward = transform.forward;
            var right = transform.right;
            if (Airborne)
            {
                _airSeconds += dt;
                // Air control: only with a rider at the controls.
                if (Seat0.Value >= 0)
                {
                    Vehicles.AirControl(input.Pitch, input.Roll, r, out float aboutRight, out float aboutForward);
                    _body.AddTorque((right * aboutRight + forward * aboutForward) * Mathf.Deg2Rad, ForceMode.Acceleration);
                }
                return;
            }
            if (wasAirborne && _airSeconds >= LandingMinAirSeconds && AnyOccupant && !Crashed)
            {
                float tilt = Vector3.Angle(transform.up, groundNormal);
                if (Vehicles.BadLanding(tilt, r)) ReportCrash(_body.linearVelocity.magnitude);
            }
            _airSeconds = 0f;
            if (Crashed || _righting) return;
            // Keep the frame upright about its forward axis.
            var level = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (level.sqrMagnitude < 1e-4f) return;
            float roll = Vector3.SignedAngle(level, transform.up, forward) * Mathf.Deg2Rad;
            float rollRate = Vector3.Dot(_body.angularVelocity, forward);
            float correction = -(r.StabiliseStrength * roll + r.StabiliseDamping * rollRate);
            _body.AddTorque(forward * correction, ForceMode.Acceleration);
        }

        // Every side: the model leans into turns by speed x steering, rests on its stand when stopped with nobody on it,
        // and lifts its nose under hard acceleration. The wheelie turns about the rear wheel's contact point.
        void PlaceModel(float dt)
        {
            if (_model == null) return;
            var r = Rules;
            float lean;
            if (!AnyOccupant && Mathf.Abs(Speed) < 1f) lean = -r.KickstandRollDeg; // on its stand, leaning left
            else lean = Vehicles.LeanDeg(Speed, _steerShown / Mathf.Max(1f, r.SteerMaxDeg), r);
            _leanShown = Mathf.MoveTowards(_leanShown, lean, r.LeanDegPerSecond * dt);
            float wheelie = Seat0.Value >= 0 ? Vehicles.WheelieDeg(ShownAccel, r) : 0f;
            _wheelieShown = Mathf.MoveTowards(_wheelieShown, wheelie, r.WheelieMaxDeg * 4f * dt);
            // Lean right = a negative turn about the forward axis; nose up = a negative turn about the right axis.
            var rotation = Quaternion.AngleAxis(-_wheelieShown, Vector3.right) * Quaternion.AngleAxis(-_leanShown, Vector3.forward);
            var pivot = new Vector3(0f, 0f, -_wheelbase * 0.5f); // rear contact point
            _model.localRotation = rotation;
            _model.localPosition = pivot - rotation * pivot;
        }
    }
}
