using System;
using UnityEngine;

namespace Game.Net
{
    // Extension point of a vehicle (ADR-065 (5)) for the systems that come after the core: fuel, vehicle health and
    // hit zones, tyres, fire and explosions, HUD readouts. The core (Vehicle) reads the gates and factors below every
    // physics step and raises the events; it never writes the condition fields itself. Defaults leave the vehicle
    // fully working (full tank, undamaged, all tyres whole). See docs/handoff/VEHICLE_API.md.
    //
    // The fields are plain values on this side only: a system that owns one replicates it itself (SyncVars on its own
    // NetworkBehaviour on the same object) and writes the value here on every side.
    [DisallowMultipleComponent]
    public class VehicleState : MonoBehaviour
    {
        // 0..1. At 0 the engine cannot be started and a running engine stops (Vehicle turns EngineOn off on the server).
        public float Fuel01 = 1f;
        // 0..1, for readouts; the core does not act on it.
        public float Health01 = 1f;
        // Burning or wrecked: no driving (no throttle, brakes only) and nobody may get in.
        public bool Disabled;
        // Per wheel (Vehicle wheel order), multiplies the tyre grip: a flat tyre sets it below 1. Null = all 1.
        public float[] WheelGrip;
        // Per wheel, metres the corner sits lower (a flat tyre). Null = all 0.
        public float[] WheelDrop;
        // Degrees added to the front wheels' steering (a flat tyre pulls to its side; right positive).
        public float SteerPullDeg;
        // Multiplies the top speed on top of road / off road (e.g. a damaged engine).
        public float TopSpeedFactor = 1f;

        // Simulating side only (the driver's client, or the server with nobody driving): an impact harder than
        // VehicleRules.CollisionMinSpeed, with its speed along the contact normal (m/s) and the other collider.
        // A damage system reports it to the server itself (see the hand-off note).
        public event Action<float, Collider> Impact;
        // Every side: someone was put in a seat / let go of one (combatant, seat).
        public event Action<Combatant, int> Entered;
        public event Action<Combatant, int> Left;
        // Server: the riders were thrown off in a crash (motorcycle), with the speed (m/s).
        public event Action<float> Crashed;

        public float GripOf(int wheel) => WheelGrip != null && wheel >= 0 && wheel < WheelGrip.Length ? WheelGrip[wheel] : 1f;
        public float DropOf(int wheel) => WheelDrop != null && wheel >= 0 && wheel < WheelDrop.Length ? WheelDrop[wheel] : 0f;

        internal void RaiseImpact(float speed, Collider other) => Impact?.Invoke(speed, other);
        internal void RaiseEntered(Combatant c, int seat) => Entered?.Invoke(c, seat);
        internal void RaiseLeft(Combatant c, int seat) => Left?.Invoke(c, seat);
        internal void RaiseCrashed(float speed) => Crashed?.Invoke(speed);
    }
}
