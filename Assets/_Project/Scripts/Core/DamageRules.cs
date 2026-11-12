using System;

namespace Game.Core
{
    public static class DamageRules
    {
        // Damage of one projectile at a distance: full up to FalloffStart, then linear down to MinDamageFactor at FalloffEnd.
        public static float AtDistance(WeaponDef weapon, float distance)
        {
            if (distance > weapon.Range) return 0f;
            if (weapon.FalloffEnd <= weapon.FalloffStart || distance <= weapon.FalloffStart) return weapon.Damage;
            float t = Math.Min(1f, (distance - weapon.FalloffStart) / (weapon.FalloffEnd - weapon.FalloffStart));
            return weapon.Damage * (1f + (weapon.MinDamageFactor - 1f) * t);
        }

        // Fills yaw/pitch offsets (degrees) for each projectile of one shot, uniformly inside the spread cone.
        // `spreadFactor` narrows the cone (aiming).
        public static int ShotOffsets(WeaponDef weapon, Random rng, float[] yawDeg, float[] pitchDeg, float spreadFactor = 1f)
        {
            int n = Math.Min(Math.Max(1, weapon.Pellets), Math.Min(yawDeg.Length, pitchDeg.Length));
            for (int i = 0; i < n; i++)
            {
                double angle = rng.NextDouble() * Math.PI * 2;
                double radius = Math.Sqrt(rng.NextDouble()) * weapon.SpreadDeg * spreadFactor * 0.5;
                yawDeg[i] = (float)(Math.Cos(angle) * radius);
                pitchDeg[i] = (float)(Math.Sin(angle) * radius);
            }
            return n;
        }
    }
}
