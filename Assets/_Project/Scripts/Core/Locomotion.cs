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

    public static class Locomotion
    {
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
