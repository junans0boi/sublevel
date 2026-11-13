using System;

namespace Game.Core
{
    [Serializable]
    public class LocomotionParams
    {
        public float WalkSpeed = 4.5f;
        public float SprintSpeed = 7f;
        public float JumpSpeed = 4.8f;
        public float Gravity = -14f;
        public float GroundedStick = -2f;
    }

    // How the legs keep up when the character moves faster than its clips were animated for (ADR-048).
    [Serializable]
    public class GaitRules
    {
        public float LegPaceFollow = 0.5f; // share of the missing speed made up by playing the legs faster; the rest is foot slide
        public float MaxLegPace = 1.75f;
        // The character has no sidestep clips: the legs turn toward the way it moves and run forward or backward,
        // while the upper body keeps facing the aim (ADR-049).
        public float MaxLegTurnDeg = 60f;
        public float LegTurnDegPerSecond = 360f;
        public float LegTurnMinSpeed = 0.5f;   // slower than this the legs face forward
    }

    // Which way the legs point relative to where the character faces, and whether they run forward or backward.
    public class LegHeading
    {
        // Past sideways by this much before the legs flip between forward and backward running, so that moving
        // exactly sideways does not flicker between the two.
        const float FlipMarginDeg = 10f;

        public float TurnDeg { get; private set; }   // eased; right positive
        public bool Backward { get; private set; }   // keeps its last value while standing

        // `vx`, `vz`: velocity in the character's own frame (x right, z forward).
        public void Tick(float vx, float vz, float dt, GaitRules rules)
        {
            float target = 0f;
            if (vx * vx + vz * vz >= rules.LegTurnMinSpeed * rules.LegTurnMinSpeed)
            {
                float angle = (float)(Math.Atan2(vx, vz) * 180.0 / Math.PI); // 0 = forward, 90 = right
                float away = Math.Abs(angle);
                Backward = Backward ? away > 90f - FlipMarginDeg : away > 90f + FlipMarginDeg;
                if (Backward) angle += angle > 0f ? -180f : 180f;
                target = Math.Max(-rules.MaxLegTurnDeg, Math.Min(rules.MaxLegTurnDeg, angle));
            }
            float step = rules.LegTurnDegPerSecond * dt;
            TurnDeg = TurnDeg < target ? Math.Min(target, TurnDeg + step) : Math.Max(target, TurnDeg - step);
        }
    }

    // There is no jump clip (ADR-048): the air state holds a still pose and the jump itself is posed on top of it
    // in code — a push at take-off, knees tucked up through the air, and a knee bend that absorbs the landing.
    [Serializable]
    public class JumpRules
    {
        // Moments (0..1) of the clip the air state holds.
        public float RisePhase = 0.25f;
        public float FallPhase = 0.375f;
        public float LandPhase = 0.4f;
        public float PoseSpeed = 3f;           // vertical speed (m/s) at which the rise or fall moment is fully shown
        public float LandSeconds = 0.12f;      // the air state is held this long after touching down before moving on
        public float GroundProbeMeters = 0.35f; // airborne when there is no ground this close under the feet; more than a stair step, so walking down steps is not a fall

        // Take-off push: toes pointed, back arched, legs straight.
        public float TakeoffSeconds = 0.1f;
        public float TakeoffToeDeg = 30f;
        public float TakeoffArchDeg = 6f;
        // Tuck: thighs raised forward and shins folded back, fully at the top of the jump.
        public float TuckThighDeg = 68f;
        public float TuckShinDeg = 95f;
        public float TuckSpeed = 4f;           // vertical speed (m/s, up or down) at which the legs are stretched out again
        public float FallTuck = 0.2f;          // share of the tuck kept while rising or falling fast
        public float TuckPerSecond = 10f;      // how fast the shown tuck follows
        public float TorsoPitchDeg = 8f;       // forward, at full tuck
        public float ArmLiftDeg = 35f;         // empty hands only: arms out sideways at full tuck
        // Landing: the knees bend and the body sinks, then eases back up.
        public float LandAttackSeconds = 0.06f;
        public float LandAbsorbSeconds = 0.25f; // whole absorb, attack included
        public float LandBendDeg = 28f;        // thigh forward; the shin folds back twice this, keeping the feet under the hips
        public float LandDropMeters = 0.09f;
        public float LandPitchDeg = 8f;
    }

    // Falling hurts (ADR-053): nothing up to a safe drop, then more with every metre, lethal around the second height.
    [Serializable]
    public class FallRules
    {
        // The rules in use. Defaults until the data loader replaces them.
        public static FallRules Current = new FallRules();

        public float SafeMeters = 3f;
        public float LethalMeters = 12f;
        public float LethalDamage = 100f;
        // Movement belongs to the owner (ADR-029), so the owner reports its falls. The server believes a report
        // only up to the drop it watched itself plus this much, and waits this long for its own view to catch up.
        public float ReportToleranceMeters = 1f;
        public float ReportWindowSeconds = 1f;
    }

    // Looking around without turning the character (free look).
    [Serializable]
    public class FreeLookRules
    {
        public static FreeLookRules Current = new FreeLookRules();

        public float MaxYawDeg = 120f;            // either side of where the character faces
        public float ReturnDegPerSecond = 540f;   // how fast the view swings back when the key is released
    }

    // Measures a drop: from the highest point reached off the ground down to where the ground was met again.
    public class FallTracker
    {
        bool _inAir;
        float _peak;

        public bool InAir => _inAir;

        // Call every frame with the current height. Returns the metres dropped on the frame of landing, else 0.
        public float Tick(bool grounded, float height)
        {
            if (!grounded)
            {
                if (!_inAir || height > _peak) _peak = height;
                _inAir = true;
                return 0f;
            }
            if (!_inAir) return 0f;
            _inAir = false;
            return Math.Max(0f, _peak - height);
        }

        // How far below its highest point a fall still in progress is.
        public float DroppedSoFar(float height) => _inAir ? Math.Max(0f, _peak - height) : 0f;

        // A teleport is not a fall.
        public void Reset() => _inAir = false;
    }

    public static class Locomotion
    {
        // Damage for dropping `meters`: none up to the safe height, then rising evenly, lethal at the lethal height
        // (and more beyond it).
        public static float FallDamage(float meters, FallRules rules)
        {
            if (!(meters > rules.SafeMeters)) return 0f; // also false for NaN
            if (rules.LethalMeters <= rules.SafeMeters) return rules.LethalDamage;
            return (meters - rules.SafeMeters) / (rules.LethalMeters - rules.SafeMeters) * rules.LethalDamage;
        }

        // The drop the server accepts from an owner's report: never more than what it watched plus the tolerance.
        public static float AcceptedFall(float reportedMeters, float observedMeters, FallRules rules)
        {
            if (!(reportedMeters > 0f) || float.IsInfinity(reportedMeters)) return 0f;
            return Math.Min(reportedMeters, Math.Max(0f, observedMeters) + rules.ReportToleranceMeters);
        }

        // Playback speed for the legs when moving at `speed` with a clip animated for `clipSpeed`.
        public static float LegPace(float speed, float clipSpeed, GaitRules rules)
        {
            if (clipSpeed <= 0f || speed <= clipSpeed) return 1f;
            return Math.Min(rules.MaxLegPace, 1f + (speed / clipSpeed - 1f) * rules.LegPaceFollow);
        }

        // Which moment of the run cycle to show in the air: the rise pose going up, the fall pose coming down.
        public static float AirPhase(float verticalSpeed, JumpRules rules)
        {
            float t = rules.PoseSpeed <= 0f ? (verticalSpeed > 0f ? 1f : 0f) : Math.Min(1f, Math.Max(0f, (verticalSpeed + rules.PoseSpeed) / (2f * rules.PoseSpeed)));
            return rules.FallPhase + (rules.RisePhase - rules.FallPhase) * t;
        }

        // How far the knees are tucked up (0..1) for a vertical speed: all the way at the top of a jump, least
        // while rising or falling fast.
        public static float JumpTuck(float verticalSpeed, JumpRules rules)
        {
            if (rules.TuckSpeed <= 0f) return 1f;
            float fast = Math.Min(1f, Math.Abs(verticalSpeed) / rules.TuckSpeed);
            return 1f + (rules.FallTuck - 1f) * fast;
        }

        // A short reaction `t` seconds after its start: rises evenly to 1 over `attack`, then eases back to 0 by `total`.
        public static float Pulse(float t, float attack, float total)
        {
            if (t <= 0f || t >= total) return 0f;
            if (t < attack) return t / attack;
            float back = (total - t) / (total - attack);
            return back * back * (3f - 2f * back);
        }

        // World-space planar velocity from stick input (x = right, y = forward) relative to a yaw heading.
        public static void PlanarVelocity(float moveX, float moveY, float yawDeg, bool sprint, LocomotionParams p, out float vx, out float vz, float speedFactor = 1f)
        {
            double len = Math.Sqrt(moveX * moveX + moveY * moveY);
            if (len < 1e-4) { vx = 0; vz = 0; return; }
            if (len > 1) { moveX = (float)(moveX / len); moveY = (float)(moveY / len); }
            double yaw = yawDeg * Math.PI / 180.0;
            double sin = Math.Sin(yaw), cos = Math.Cos(yaw);
            // Sprint only applies when moving forward.
            float speed = (sprint && moveY > 0.1f ? p.SprintSpeed : p.WalkSpeed) * speedFactor;
            vx = (float)(moveX * cos + moveY * sin) * speed;
            vz = (float)(moveY * cos - moveX * sin) * speed;
        }

        public static float VerticalVelocity(float current, bool grounded, bool jump, float dt, LocomotionParams p)
        {
            if (grounded)
                return jump ? p.JumpSpeed : p.GroundedStick;
            return current + p.Gravity * dt;
        }
    }
}
