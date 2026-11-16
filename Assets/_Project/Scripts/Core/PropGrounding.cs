using System;

namespace Game.Core
{
    // ADR-201: pivot height and tilt that make a parked prop car's four corners follow the ground (plane fit over the corner heights).
    public static class PropGrounding
    {
        public struct Fit { public float Y, PitchDeg, RollDeg; }

        // yawDeg: car heading (forward = (sin, cos)). PitchDeg is nose-up positive, RollDeg is right-side-down positive.
        // In Unity apply as Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-PitchDeg, 0, -RollDeg) (Euler X+ is nose-down, Z+ lifts the right side).
        public static Fit Place(Func<float, float, float> height, float x, float z, float yawDeg, float halfLength, float halfWidth)
        {
            float yaw = yawDeg * (float)(Math.PI / 180.0), fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw);
            float H(float f, float r) => height(x + fx * f + fz * r, z + fz * f - fx * r);
            float ff = H(halfLength, 0f), fb = H(-halfLength, 0f), fr = H(0f, halfWidth), fl = H(0f, -halfWidth);
            return new Fit
            {
                Y = (ff + fb + fr + fl) * 0.25f,
                PitchDeg = (float)(Math.Atan2(ff - fb, 2f * halfLength) * 180.0 / Math.PI),
                RollDeg = (float)(Math.Atan2(fl - fr, 2f * halfWidth) * 180.0 / Math.PI),
            };
        }
    }
}
