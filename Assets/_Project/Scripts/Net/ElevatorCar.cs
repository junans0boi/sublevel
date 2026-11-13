using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The elevator car under one station (ADR-065 (1)): it waits at the bottom of the shaft with its riders and
    // comes up to the station floor over the last seconds before the doors open, then its own door sinks away
    // together with the station's. Like ElevatorDoor it is not a network object: every peer moves its own copy
    // from the one replicated opening time (ElevatorDoors.LocalSecondsToOpen through ElevatorRise), so 13 cars
    // cost nothing on the network and all peers agree to the frame.
    //
    // Riders are not parented to the car. Whoever moves a combatant asks TryRideFloor every frame and puts the
    // feet there: the owner for a player (PlayerController, owner-authoritative movement, ADR-029) and the server
    // for a bot (SpawnProtection); NetworkTransform carries both to everyone else. Parenting would fight
    // NetworkTransform and the CharacterController; a position computed from the same time function cannot lag
    // behind the car or depend on which Update runs first.
    //
    // WorldBuilder places it at the top (its floor level with the station floor) and wires the door, the lights
    // and the counterweight.
    public class ElevatorCar : MonoBehaviour
    {
        // Feet are put this far above the car floor while riding (the CharacterController's skin).
        public const float RideLift = 0.03f;
        // A combatant this close (flat) to a car's centre is inside it.
        const float RideRadius = 1.6f;
        // How far above the car floor a rider may be and still be in the car (head room, not a whole storey).
        const float RideCatchMeters = 2.5f;
        // The counterweight runs the other way, a little shorter so it never shows above the floor.
        const float CounterweightRatio = 0.9f;

        public static readonly List<ElevatorCar> All = new List<ElevatorCar>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        [SerializeField] int _station = -1;          // index into the scene's elevators (WorldInfo order)
        [SerializeField] Transform _door;            // the car's own door (a child), sinks with the station door
        [SerializeField] float _doorDrop = 2.45f;    // metres: its height and a little more, under the car floor
        [SerializeField] Light[] _lights = new Light[0];   // car lamp and shaft lamps: off once the car is up and open
        [SerializeField] Transform _counterweight;   // in the shaft, placed where it hangs while the car is at the top

        Vector3 _top;
        Vector3 _doorClosed;
        Vector3 _counterweightBase;
        Collider[] _doorColliders = new Collider[0];
        Renderer[] _doorRenderers = new Renderer[0];
        bool _doorGone;
        bool _lightsOn = true;

        public int Station => _station;
        // Station floor level (the car floor when it has arrived).
        public Vector3 Top => _top;
        public ElevatorPhase Phase { get; private set; } = ElevatorPhase.Open;
        // 0..1 of the top speed while rising.
        public float Speed01 { get; private set; }
        // Metres below the station floor right now.
        public float Offset { get; private set; }
        // 0 = the car door is shut, 1 = sunk away.
        public float DoorOpened { get; private set; }
        public bool DoorBlocking => !_doorGone;

        void Awake()
        {
            _top = transform.position;
            if (_door != null)
            {
                _doorClosed = _door.localPosition;
                _doorColliders = _door.GetComponentsInChildren<Collider>();
                _doorRenderers = _door.GetComponentsInChildren<Renderer>();
            }
            if (_counterweight != null) _counterweightBase = _counterweight.position;
            // Moved every frame: a kinematic body, so the physics scene does not rebuild a static collider.
            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDisable() => All.Remove(this);

        void Update() => Tick();

        void Tick()
        {
            var rules = DeploymentRules.Current;
            var doors = ElevatorDoors.Instance;
            // No match in this scene: the car simply stands at the top with its door shut.
            double toOpen = doors != null ? doors.LocalSecondsToOpen : 0.0;
            Phase = doors != null ? ElevatorRise.Phase(toOpen, rules) : ElevatorPhase.Open;
            Speed01 = ElevatorRise.Speed01(toOpen, rules);
            Offset = doors != null ? ElevatorRise.Offset(toOpen, rules) : 0f;
            transform.position = _top + Vector3.down * Offset;
            if (_counterweight != null) _counterweight.position = _counterweightBase + Vector3.up * (Offset * CounterweightRatio);

            DoorOpened = doors != null && Phase == ElevatorPhase.Open
                ? Mathf.Clamp01((float)(-toOpen / Mathf.Max(0.01f, rules.DoorSlideSeconds)))
                : 0f;
            if (_door != null && !_doorGone)
            {
                _door.localPosition = _doorClosed + Vector3.down * (_doorDrop * DoorOpened);
                if (DoorOpened >= 1f)
                {
                    // Sunk: out of the way for good (a restart reloads the scene).
                    _doorGone = true;
                    foreach (var c in _doorColliders) if (c != null) c.enabled = false;
                    foreach (var r in _doorRenderers) if (r != null) r.enabled = false;
                }
            }

            // The lamps only matter on the way up; daylight takes over once the doors are open.
            bool lit = !(Phase == ElevatorPhase.Open && DoorOpened >= 1f);
            if (lit != _lightsOn)
            {
                _lightsOn = lit;
                foreach (var l in _lights) if (l != null) l.enabled = lit;
            }
        }

        // Where the feet of a combatant standing at `at` go this frame, while the car is still on its way (until the
        // doors open on this peer). False when `at` is in no car's shaft or the ride is over.
        public static bool TryRideFloor(Vector3 at, out float floorY)
        {
            floorY = 0f;
            var doors = ElevatorDoors.Instance;
            if (doors == null) return false;
            double toOpen = doors.LocalSecondsToOpen;
            if (toOpen <= 0.0) return false;
            var rules = DeploymentRules.Current;
            foreach (var car in All)
            {
                if (car == null) continue;
                var top = car._top;
                float dx = at.x - top.x, dz = at.z - top.z;
                if (dx * dx + dz * dz > RideRadius * RideRadius) continue;
                if (at.y < top.y - Mathf.Max(rules.ShaftDepth, rules.CarStartDepth) - 2f) continue;
                float feet = top.y - ElevatorRise.Offset(toOpen, rules) + RideLift;
                // Above the car is not in it: someone already standing at the floor while this peer still thinks
                // the car is below (a late joiner before the door status arrived) is left alone. Below it (a
                // long frame) is caught up.
                if (at.y > feet + RideCatchMeters) continue;
                floorY = feet;
                return true;
            }
            return false;
        }
    }
}
