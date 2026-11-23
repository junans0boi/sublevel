using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Server side of reconciliation (#161, ADR-126) for a player moved by someone else's client. Movement stays the
    // owner's (ADR-029), so the owner's own body is its prediction and never waits for the server; this compares the
    // position the server holds every Interval seconds with the last one it accepted and, when the body went farther
    // than MoveReconcile allows, sends the owner back to that one. Added by Combatant.OnStartServer.
    public class MoveGuard : MonoBehaviour
    {
        public const float Interval = 0.25f;
        const float ResendSeconds = 0.75f;
        // A deliberate teleport is announced by CmdTeleported (reliable), the new position travels on its own (unreliable): under lag
        // the position can arrive first. A jump is judged only if it is still unannounced this long after it was first seen (#172).
        const float TeleportGraceSeconds = 0.45f;

        // What corrections have happened since the play session started (tests and the network HUD read them).
        public static int Corrections { get; private set; }
        public static float MaxError { get; private set; }
        public static float TotalError { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStats() { Corrections = 0; MaxError = TotalError = 0f; }

        Combatant _combatant;
        CombatantStance _stance;
        Vector3 _good;
        double _sampled;
        bool _pending;
        double _resendAt;
        double _suspectAt = double.NegativeInfinity;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _stance = GetComponent<CombatantStance>();
        }

        void OnEnable() { _good = transform.position; _sampled = Time.timeAsDouble; }

        // Test harnesses teleport remote players across the map; a real match never does (#170). Never set outside tests.
        public static bool TrustTeleports;

        double _lastTeleport = double.NegativeInfinity;

        // The owner says it moved itself on purpose to `to` (CmdTeleported, #170). Taken when the server knows why (#500, ADR-560):
        // the island's lift and jump, a place the server itself sent the body to (any ServerCorrect, a parkour sent back), an elevator
        // ride's cars, being carried. Anything else (leaving a vehicle) only when near, not too often, and with nothing solid on the
        // way: a jump through a wall or a shut door is put back like any other.
        public bool TryTeleport(Vector3 to)
        {
            double now = Time.timeAsDouble;
            float meters = (to - transform.position).magnitude;
            if (!InputGuard.Finite(to.x, to.y, to.z)) return ServerGuard.Refuse(GuardKind.Arg, _combatant, "teleport to a place that is not a number");
            if (TrustTeleports || CarriedOnIsland || Expected(to, now) || RidingTo(to)) { Accept(to); return true; }
            // On a teammate's back the body goes where the carrier walks (ADR-129), walls brushed by the carried legs included.
            if (_combatant.BeingCarried && meters <= GameData.Rules.Guard.TeleportMaxMeters) { Accept(to); return true; }
            // The owner arriving where the server itself put it (island -> elevator, any ServerCorrect) echoes CmdTeleported: no jump at all.
            if ((to - _good).sqrMagnitude < MoveReconcile.SlackMeters * MoveReconcile.SlackMeters) { Accept(to); return true; }
            if (!InputGuard.TeleportOk(meters, now - _lastTeleport, GameData.Rules.Guard))
                return ServerGuard.Refuse(GuardKind.Teleport, _combatant, $"teleport {meters:F1} m, {now - _lastTeleport:F2} s after the last");
            if (!ClearPath(transform.position, to))
                return ServerGuard.Refuse(GuardKind.Teleport, _combatant, $"teleport {meters:F1} m through something solid");
            _lastTeleport = now;
            Accept(to);
            return true;
        }

        bool CarriedOnIsland => WaitingRoom.Current != null && WaitingRoom.Current.Carries(_combatant);

        // An elevator ride: the owner moves itself from the departure car to the arrival car, km apart.
        const float StationReachMeters = 6f;
        bool RidingTo(Vector3 to) => ElevatorRides.Instance != null && ElevatorRides.Instance.ServerRideNear(_combatant, to, StationReachMeters);

        // A place the server sent the owner to (ServerCorrect, a parkour sent back): the owner's echo is taken for a while, wherever
        // the body was meanwhile (a parkour sent back crosses the wall it went over).
        const float ExpectSeconds = 3f;
        Vector3 _expected;
        double _expectedUntil = double.NegativeInfinity;

        public void Expect(Vector3 at)
        {
            _expected = at;
            _expectedUntil = Time.timeAsDouble + ExpectSeconds;
        }

        bool Expected(Vector3 to, double now) =>
            now <= _expectedUntil && (to - _expected).sqrMagnitude < MoveReconcile.SlackMeters * MoveReconcile.SlackMeters;

        static readonly RaycastHit[] PathHits = new RaycastHit[16];
        const float PathHeight = 1f;   // about the hips: a kerb or a step is no wall, a window sill is

        // Nothing solid between two feet positions but people and vehicles (leaving a car crosses its own body).
        public static bool ClearPath(Vector3 from, Vector3 to)
        {
            var a = from + Vector3.up * PathHeight;
            var d = to + Vector3.up * PathHeight - a;
            float length = d.magnitude;
            if (length < 1e-3f) return true;
            int n = Physics.RaycastNonAlloc(a, d / length, PathHits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = PathHits[i].collider;
                if (col.GetComponentInParent<Combatant>() == null && col.GetComponentInParent<Vehicle>() == null) return false;
            }
            return true;
        }

        // The server moved this body itself (waiting island -> elevator): that place is the good one now.
        public void ServerRelocate(Vector3 to) => Accept(to);

        void Accept(Vector3 to)
        {
            _stance?.ServerForgetFall();   // being moved is not a fall (#501)
            _good = to;
            _sampled = Time.timeAsDouble;
            _pending = false;
            _suspectAt = double.NegativeInfinity;
        }

        void Update()
        {
            double now = Time.timeAsDouble;
            if (now - _sampled < Interval || _combatant == null || !_combatant.IsServerInitialized) return;
            float dt = (float)(now - _sampled);
            _sampled = now;
            var p = transform.position;
            // Waiting on the island (ADR-382): the server keeps people inside it, whatever the client says.
            // On foot only the island or the race pit, never the track; a seated person goes where the race car goes (ADR-508).
            if (WaitingRoom.Current != null && WaitingRoom.Current.Open && !_combatant.Dead.Value && !VehicleSeat.IsAttached(_combatant))
            {
                float cx = p.x, cz = p.z;
                RaceTrack.ClampWalker(ref cx, ref cz);
                if (cx != p.x || cz != p.z)
                {
                    var back = new Vector3(cx, p.y, cz);
                    transform.position = back;
                    _good = back; _pending = false; _suspectAt = double.NegativeInfinity;
                    _stance.ServerCorrect(back);
                    return;
                }
            }
            // Carried by a car (its speed is the car's), by the island's lift or bungee (ADR-387: up to ~26 m/s, the server drives the
            // same curve), or dead: nothing to compare. The next place is taken as good.
            if (_combatant.Dead.Value || VehicleSeat.IsAttached(_combatant) || CarriedOnIsland) { _good = p; _pending = false; return; }
            var d = p - _good;
            if (_pending)
            {
                // Waiting for the owner to go back; once it is near there (or it never does), start over.
                if (d.sqrMagnitude < MoveReconcile.SlackMeters * MoveReconcile.SlackMeters) _pending = false;
                else if (now >= _resendAt) { _stance.ServerCorrect(_good); _resendAt = now + ResendSeconds; }
                return;
            }
            if (!MoveReconcile.Implausible(d.x, d.y, d.z, dt, GameData.Rules.Locomotion, out _))
            {
                _good = p;
                _suspectAt = double.NegativeInfinity;
                return;
            }
            // First sight of a jump: wait for its announcement (see TeleportGraceSeconds) before putting the body back.
            if (double.IsNegativeInfinity(_suspectAt)) _suspectAt = now;
            if (now - _suspectAt < TeleportGraceSeconds) return;
            _suspectAt = double.NegativeInfinity;
            float error = d.magnitude;
            Corrections++;
            ServerGuard.Refuse(GuardKind.Move, _combatant, $"moved {d.magnitude:F1} m in {dt:F2} s");
            TotalError += error;
            if (error > MaxError) MaxError = error;
            _pending = true;
            _resendAt = now + ResendSeconds;
            _stance.ServerCorrect(_good);
        }
    }
}
