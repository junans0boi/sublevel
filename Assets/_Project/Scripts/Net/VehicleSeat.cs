using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // One seat of a vehicle: a marker at the seated occupant's eyes (forward = the way the seat faces) with the door
    // point beside it on the ground. Also the static seat hooks the rest of the game asks (ADR-065 (5)): who is
    // attached to which vehicle, what a seated combatant may do with its hands (checked on the server by Combatant
    // and ThrowableUser), and where a seated shooter's shots may go.
    //
    // Every side (server, owner, observers) keeps its own attachment table, derived by each Vehicle from its
    // replicated seat SyncVars, so the server's checks, the seated pose and the body's place agree everywhere.
    public class VehicleSeat : MonoBehaviour
    {
        [SerializeField] int _index;
        [SerializeField] Transform _door;
        // Where the hands hold on from this seat (steering wheel rim, handlebar grips), when the model has them (#89).
        // Children of the steering part, so they turn with it. The seated pose reaches for them with empty hands.
        [SerializeField] Transform _gripLeft;
        [SerializeField] Transform _gripRight;

        public int Index => _index;
        public Transform GripLeft => _gripLeft;
        public Transform GripRight => _gripRight;
        public bool HasGrips => _gripLeft != null && _gripRight != null;
        // Where an occupant steps in and out: on the ground beside the door.
        public Transform Door => _door != null ? _door : transform;
        public SeatRole Role => Vehicles.RoleOf(_index);

        // Editor builder only.
        public void Configure(int index, Transform door)
        {
            _index = index;
            _door = door;
        }

        public void ConfigureGrips(Transform left, Transform right)
        {
            _gripLeft = left;
            _gripRight = right;
        }

        // The seat a combatant is attached to in any phase (null when none): the pose reads its grips.
        public static VehicleSeat SeatOf(Combatant c)
        {
            var o = Find(c);
            return o != null && o.Vehicle != null ? o.Vehicle.Seat(o.Seat) : null;
        }

        public enum Phase { Entering, Seated, Switching, Leaving }

        // How one combatant is attached to a vehicle on this side.
        public sealed class Occupancy
        {
            public Combatant Combatant;
            public Vehicle Vehicle;
            public int Seat;
            public Phase Phase;
            public float Started;          // Time.time the phase began
            public float Seconds;          // length of the phase
            public Vector3 FromLocal;      // the body's origin when the phase began, in the vehicle's space
            public Quaternion FromRotation; // the body's rotation then, in the vehicle's space
            public Vector3 ExitWorld;      // leaving: where the feet end up
            public bool ExitFromServer;    // the exit point came from the server (not this side's own guess)
            public Collider[] Colliders;   // the combatant's own colliders, kept clear of the vehicle's
            public CharacterController Controller;
            public bool ControllerWasEnabled;

            public float Progress => Seconds <= 0f ? 1f : Mathf.Clamp01((Time.time - Started) / Seconds);
            public bool Busy => Phase == Phase.Leaving || (Phase != Phase.Seated && Progress < 1f);
        }

        static readonly Dictionary<Combatant, Occupancy> Map = new Dictionary<Combatant, Occupancy>();
        static readonly List<Combatant> Stale = new List<Combatant>();
        static readonly RaycastHit[] ShotHits = new RaycastHit[16];

        // Raised on every side when a combatant's way out of a vehicle has finished: the combatant and where its
        // feet are. The owner of a player character moves it there (movement is the owner's, ADR-029).
        public static event Action<Combatant, Vector3> Released;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Map.Clear();
            Released = null;
        }

        // ---- Questions anyone may ask ----

        // Attached in any way (getting in, seated, changing seats or getting out).
        public static bool IsAttached(Combatant c) => Find(c) != null;

        // In a seat (getting in, seated or changing seats; not on the way out).
        public static bool IsSeated(Combatant c)
        {
            var o = Find(c);
            return o != null && o.Phase != Phase.Leaving;
        }

        public static bool TryGet(Combatant c, out Vehicle vehicle, out int seat)
        {
            var o = Find(c);
            if (o == null || o.Phase == Phase.Leaving || o.Vehicle == null)
            {
                vehicle = null;
                seat = Vehicles.NoSeat;
                return false;
            }
            vehicle = o.Vehicle;
            seat = o.Seat;
            return true;
        }

        // The vehicle a combatant is attached to in any phase, or null.
        public static Vehicle VehicleOf(Combatant c) => Find(c)?.Vehicle;

        public static bool IsDriver(Combatant c) => TryGet(c, out _, out int seat) && seat == Vehicles.DriverSeat;

        // 0 = standing, 1 = fully seated: the share of the seated pose to show. The same on every side.
        public static float SeatedAmount(Combatant c)
        {
            var o = Find(c);
            if (o == null) return 0f;
            switch (o.Phase)
            {
                case Phase.Entering: return o.Progress;
                case Phase.Leaving: return 1f - o.Progress;
                default: return 1f;
            }
        }

        // The driver's seat, for the pose: hands on the wheel. Also true while getting in or out of it.
        public static bool InDriverSeat(Combatant c)
        {
            var o = Find(c);
            return o != null && o.Seat == Vehicles.DriverSeat;
        }

        // ---- Server checks (Combatant.ServerFire / ServerPunch, ThrowableUser.ServerThrow) ----

        // Firing `weapon` (null = empty hands, i.e. a punch). Not while getting in, changing seats or getting out.
        public static bool CanUseWeapon(Combatant c, WeaponDef weapon)
        {
            var o = Find(c);
            if (o == null) return true;
            if (o.Busy) return false;
            return Vehicles.CanUseWeapon(o.Seat, weapon);
        }

        public static bool CanThrow(Combatant c)
        {
            var o = Find(c);
            if (o == null) return true;
            if (o.Busy) return false;
            return Vehicles.CanUse(o.Seat, HandUse.Throwable);
        }

        public static bool CanPunch(Combatant c)
        {
            var o = Find(c);
            return o == null || Vehicles.CanUse(o.Seat, HandUse.Punch);
        }

        // A seated shooter's spread cone factor (replaces the moving / airborne factors while seated).
        public static float SpreadFactor => VehicleRules.Current.SeatedSpreadFactor;

        // Is this collider part of the vehicle or of someone attached to it?
        public static bool BelongsTo(Vehicle vehicle, Collider collider)
        {
            if (vehicle == null || collider == null) return false;
            if (collider.transform.IsChildOf(vehicle.transform)) return true;
            var c = collider.GetComponentInParent<Combatant>();
            return c != null && Map.TryGetValue(c, out var o) && o.Vehicle == vehicle;
        }

        // Sitting astride (the motorcycle) rather than in a seat: the pose differs.
        public static bool Astride(Combatant c)
        {
            var o = Find(c);
            return o != null && o.Vehicle != null && o.Vehicle.IsMotorcycle;
        }

        // The rules of the vehicle a combatant is attached to (the seated pose numbers); the car's when none.
        public static VehicleRules RulesOf(Combatant c)
        {
            var o = Find(c);
            return o != null && o.Vehicle != null ? o.Vehicle.Rules : VehicleRules.Current;
        }

        // The shot ray of Combatant.ServerFire. On foot: exactly Physics.Raycast as before. From a seat the shot
        // leaves from the occupant's eyes inside the vehicle, so it passes through the shooter's own vehicle (the body
        // does not stop a shot from inside). Fellow occupants can be hit: friendly fire inside the car is possible.
        public static bool Raycast(Combatant shooter, Vector3 origin, Vector3 direction, float range, out RaycastHit hit)
        {
            var o = Find(shooter);
            if (o == null || o.Vehicle == null)
                return Physics.Raycast(origin, direction, out hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            int count = Physics.RaycastNonAlloc(origin, direction, ShotHits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            hit = default;
            float best = float.MaxValue;
            var vehicle = o.Vehicle.transform;
            var self = shooter.transform;
            for (int i = 0; i < count; i++)
            {
                var h = ShotHits[i];
                if (h.distance >= best || h.collider.transform.IsChildOf(vehicle) || h.collider.transform.IsChildOf(self)) continue;
                best = h.distance;
                hit = h;
            }
            return best < float.MaxValue;
        }

        // ---- The table (kept by Vehicle) ----

        internal static Occupancy Find(Combatant c)
        {
            if (ReferenceEquals(c, null)) return null;
            return Map.TryGetValue(c, out var o) ? o : null;
        }

        internal static Occupancy Attach(Combatant c, Vehicle vehicle, int seat, Phase phase, float seconds)
        {
            var o = new Occupancy
            {
                Combatant = c,
                Vehicle = vehicle,
                Seat = seat,
                Colliders = c.GetComponentsInChildren<Collider>(true),
                Controller = c.GetComponent<CharacterController>(),
            };
            o.ControllerWasEnabled = o.Controller != null && o.Controller.enabled;
            Begin(o, phase, seconds);
            Map[c] = o;
            return o;
        }

        // Starts a phase from where the body is now.
        internal static void Begin(Occupancy o, Phase phase, float seconds)
        {
            o.Phase = phase;
            o.Started = Time.time;
            o.Seconds = seconds;
            var t = o.Vehicle.transform;
            var body = o.Combatant.transform;
            o.FromLocal = t.InverseTransformPoint(body.position);
            o.FromRotation = Quaternion.Inverse(t.rotation) * body.rotation;
        }

        internal static void Detach(Occupancy o, bool raise)
        {
            if (o == null || o.Combatant == null) return;
            if (Map.TryGetValue(o.Combatant, out var current) && current == o) Map.Remove(o.Combatant);
            if (raise) Released?.Invoke(o.Combatant, o.ExitWorld);
        }

        internal static void Collect(Vehicle vehicle, List<Occupancy> into)
        {
            into.Clear();
            Stale.Clear();
            foreach (var pair in Map)
            {
                // A combatant that went away with its scene or was despawned.
                if (pair.Key == null) { Stale.Add(pair.Key); continue; }
                if (pair.Value.Vehicle == vehicle) into.Add(pair.Value);
            }
            foreach (var c in Stale) Map.Remove(c);
        }
    }
}
