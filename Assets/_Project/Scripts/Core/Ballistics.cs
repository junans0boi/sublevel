using System;

namespace Game.Core
{
    // Bullet flight (ADR-098, decision #126). Within InstantMeters of the shooter a shot is judged at once by a ray;
    // beyond that it is a projectile with a muzzle speed and gravity. Speeds are PUBG ratios (data: weapons.json).
    [Serializable]
    public class BallisticsRules
    {
        public float InstantMeters = 25f;
        public float Gravity = 9.81f;            // m/s^2, straight down
        public float DefaultMuzzleSpeed = 800f;  // for a weapon without MuzzleSpeed
        public float DefaultZeroMeters = 100f;   // where the sights and the bullet cross until the player zeroes (G10b)
        public float ZeroStepMeters = 100f;
        // Scope sway (ADR-103): degrees of view wander per zoom step, from SwayMinZoom up; x stance factor.
        public float SwayDegPerZoom = 0.025f;
        public float SwayMinZoom = 3f;
        public float HeldSwayFactor = 0.08f;     // share of the sway left while holding the breath
        public float WindedSwayFactor = 1.5f;    // gasping after the breath ran out
        // Hold breath (Shift in the sights): lasts BreathHoldSeconds from full, refills over BreathRecoverSeconds;
        // once empty it cannot be held again until it is back to BreathRestartFraction.
        public float BreathHoldSeconds = 5f;
        public float BreathRecoverSeconds = 4f;
        public float BreathRestartFraction = 0.3f;
        public float MaxFlightSeconds = 5f;
        public float MaxRewindSeconds = 0.30f;   // lag compensation: how far back a shot may be judged (#162, ADR-141)
    }

    public static class Ballistics
    {
        public static float MuzzleSpeed(WeaponDef weapon, BallisticsRules rules) =>
            weapon != null && weapon.MuzzleSpeed > 0f ? weapon.MuzzleSpeed : rules.DefaultMuzzleSpeed;

        // Seconds a bullet fired level needs to cover `distance` metres.
        public static float FlightSeconds(float speed, float distance) => speed > 0f ? distance / speed : 0f;

        // Metres a bullet fired level has fallen after `distance` metres.
        public static float Drop(float speed, float distance, float gravity)
        {
            float t = FlightSeconds(speed, distance);
            return 0.5f * gravity * t * t;
        }

        // Elevation (degrees) above the line of sight at which a bullet crosses that line again at `distance` metres.
        // 0 when the target is out of reach for this speed.
        public static float ElevationDeg(float speed, float distance, float gravity)
        {
            if (speed <= 0f || distance <= 0f) return 0f;
            double s = gravity * distance / ((double)speed * speed);
            if (s >= 1.0) return 45f;
            return (float)(0.5 * Math.Asin(s) * 180.0 / Math.PI);
        }

        // Height of the bullet above its line at time t, for a shot leaving at `elevationDeg` above that line.
        // Used to place the projectile; matches Drop for elevation 0.
        public static float HeightAt(float speed, float elevationDeg, float t, float gravity) =>
            speed * (float)Math.Sin(elevationDeg * Math.PI / 180.0) * t - 0.5f * gravity * t * t;
    }
}
