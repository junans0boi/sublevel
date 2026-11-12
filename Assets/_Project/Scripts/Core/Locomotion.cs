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

    // Jump poses are moments of the run cycle (0..1), held still: there is no jump clip (ADR-048).
    [Serializable]
    public class JumpRules
    {
        public float RisePhase = 0.25f;        // pushing off, knee up
        public float FallPhase = 0.375f;       // legs apart, reaching for the ground
        public float LandPhase = 0.4f;         // front foot coming down
        public float PoseSpeed = 3f;           // vertical speed (m/s) at which the rise or fall pose is fully shown
        public float LandSeconds = 0.12f;      // the landing pose is held this long before moving on
        public float GroundProbeMeters = 0.35f; // more than a stair step, so walking down steps is not a fall // airborne when there is no ground this close under the feet
    }

    public static class Locomotion
    {
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
