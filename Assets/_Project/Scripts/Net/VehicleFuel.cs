using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The tank of a vehicle (ADR-067, ADR-075). The server burns fuel while the engine runs, from the driver's
    // throttle and boost it already receives (Vehicle.ServerThrottle / ServerBoost), and replicates the share; every
    // side writes it into VehicleState.Fuel01, where the core reads it (an empty tank stops the engine on the server,
    // the vehicle then rolls on without pull). Jerrycans add to it through ServerRefill (CombatantJerrycan).
    //
    // Sits on the vehicle prefab next to Vehicle (VehicleBuilder). Without it the tank stays full.
    [RequireComponent(typeof(Vehicle))]
    public class VehicleFuel : NetworkBehaviour
    {
        public readonly SyncVar<float> Fuel = new SyncVar<float>(1f);

        Vehicle _vehicle;
        VehicleState _state;

        public Vehicle Vehicle => _vehicle;
        // The tank as this side knows it (the server's exact value, clients' replicated one).
        public float Fuel01 => IsServerInitialized ? _tank : Fuel.Value;
        public bool Empty => Fuel01 <= 0f;

        void Awake()
        {
            _vehicle = GetComponent<Vehicle>();
            _state = GetComponent<VehicleState>();
            if (_state == null) _state = gameObject.AddComponent<VehicleState>();
        }

        public override void OnStartServer()
        {
            // Seeded per match and vehicle, so a replayed match starts with the same tanks.
            int seed = MatchDirector.Instance != null ? MatchDirector.Instance.Seed : 0;
            ServerSetFuel(global::Game.Core.Fuel.SpawnShare(new System.Random(unchecked(seed * 92821 + ObjectId * 31 + 7)), FuelRules.Current));
        }

        void Update()
        {
            if (IsServerInitialized)
            {
                _tank = _vehicle.IsRaceCar ? 1f : global::Game.Core.Fuel.Burn(_tank, _vehicle.EngineOn.Value, _vehicle.ServerThrottle, _vehicle.ServerBoost,
                    _vehicle.IsMotorcycle, Time.deltaTime, FuelRules.Current);
                // SyncVar writes are not free: a step of 0.1% of a tank (or reaching empty) goes out.
                if (_tank != Fuel.Value && (_tank <= 0f || Mathf.Abs(_tank - Fuel.Value) >= 0.001f)) Fuel.Value = _tank;
                _state.Fuel01 = _tank;
            }
            else _state.Fuel01 = Fuel.Value;
        }

        // Server: the exact tank (the SyncVar follows it in 0.1% steps).
        float _tank = 1f;

        // ---- Server ----

        // One jerrycan's worth; true when the tank took anything.
        public bool ServerRefill()
        {
            if (!IsServerInitialized) return false;
            float next = global::Game.Core.Fuel.Refill(_tank, FuelRules.Current);
            if (next <= _tank) return false;
            ServerSetFuel(next);
            return true;
        }

        // Tests and captures.
        public void ServerSetFuel(float share)
        {
            if (!IsServerInitialized) return;
            _tank = Mathf.Clamp01(share);
            Fuel.Value = _tank;
            _state.Fuel01 = _tank;
        }

        // Distance from a point to the vehicle's body (its solid colliders), metres; 0 inside.
        public float DistanceToBody(Vector3 point)
        {
            float best = float.PositiveInfinity;
            var colliders = _vehicle.Colliders;
            for (int i = 0; i < colliders.Count; i++)
            {
                var c = colliders[i];
                if (c == null || !c.enabled) continue;
                var closest = c.ClosestPoint(point);
                float d = (closest - point).magnitude;
                if (d < best) best = d;
            }
            return float.IsPositiveInfinity(best) ? Vector3.Distance(point, transform.position) : best;
        }
    }
}
