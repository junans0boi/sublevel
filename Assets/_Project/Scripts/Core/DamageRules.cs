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

        // Damage of one projectile that lands on `zone` at a distance.
        public static float ForZone(WeaponDef weapon, float distance, HitZone zone) =>
            AtDistance(weapon, distance) * HitZones.Multiplier(weapon, zone);

        // How much the shooter's own movement widens the spread cone: 1 standing still, growing with planar speed
        // up to the weapon's MoveSpreadFactor, or its AirSpreadFactor when off the ground. Never below 1.
        public static float MovementSpread(WeaponDef weapon, float planarSpeed, bool airborne, FireRules rules)
        {
            if (airborne) return Math.Max(1f, weapon.AirSpreadFactor);
            float span = rules.MoveSpreadFullSpeed - rules.MoveSpreadMinSpeed;
            if (!(planarSpeed > rules.MoveSpreadMinSpeed) || span <= 0f) return 1f;
            float t = Math.Min(1f, (planarSpeed - rules.MoveSpreadMinSpeed) / span);
            return Math.Max(1f, 1f + (weapon.MoveSpreadFactor - 1f) * t);
        }

        // Fills yaw/pitch offsets (degrees) for each projectile of one shot, uniformly inside the spread cone.
        // `spreadFactor` scales the cone (aiming and stance narrow it, moving widens it).
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
