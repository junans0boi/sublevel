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

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _stance = GetComponent<CombatantStance>();
        }

        void OnEnable() { _good = transform.position; _sampled = Time.timeAsDouble; }

        // The owner moved itself on purpose to `to` (CmdTeleported).
        public void Accept(Vector3 to)
        {
            _good = to;
            _sampled = Time.timeAsDouble;
            _pending = false;
        }

        void Update()
        {
            double now = Time.timeAsDouble;
            if (now - _sampled < Interval || _combatant == null || !_combatant.IsServerInitialized) return;
            float dt = (float)(now - _sampled);
            _sampled = now;
            var p = transform.position;
            // Carried by a car (its speed is the car's) or dead: nothing to compare. The next place is taken as good.
            if (_combatant.Dead.Value || VehicleSeat.IsAttached(_combatant)) { _good = p; _pending = false; return; }
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
                return;
            }
            float error = d.magnitude;
            Corrections++;
            TotalError += error;
            if (error > MaxError) MaxError = error;
            _pending = true;
            _resendAt = now + ResendSeconds;
            _stance.ServerCorrect(_good);
        }
    }
}
