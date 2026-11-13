using System;

namespace Game.Core
{
    // Swimming and diving (ADR-112, decision #127). Water is a surface height over a floor; where it is at least
    // EnterDepth deep the body swims (lying pose, at about 46% of the sprint speed, PUBG's 2.9 / 6.3). Holding the dive
    // key takes the head under; the breath lasts DiveSeconds, then drowning hurts every second until the head is up
    // again, and the breath comes back over RecoverSeconds. No shooting or throwing while swimming.
    [Serializable]
    public class SwimRules
    {
        public static SwimRules Current = new SwimRules();

        public float EnterDepth = 1.2f;
        public float ExitDepth = 1.0f;          // a little shallower than EnterDepth so the edge does not flicker
        public float SpeedOfSprint = 0.46f;     // swim speed / sprint speed
        public float DiveSeconds = 20f;
        public float RecoverSeconds = 9f;
        public float DrownDamagePerSecond = 5f;
        public float EyeAboveSurface = 0.1f;    // swimming on the surface: the eyes just clear it
        public float DiveEyeDepth = 0.9f;       // diving: the eyes this far under it
        public float HeadUnderMargin = 0.05f;
        public float VerticalSpeed = 2.5f;      // rising and sinking, m/s
        public float FloorClearance = 0.05f;
    }

    public static class Swim
    {
        public static float Depth(float surfaceY, float groundY) => Math.Max(0f, surfaceY - groundY);

        // In or out of swimming, with a little hysteresis at the edge.
        public static bool Swimming(bool was, float depth, SwimRules rules) =>
            was ? depth >= rules.ExitDepth : depth >= rules.EnterDepth;

        public static float Speed(float sprintSpeed, SwimRules rules) => sprintSpeed * rules.SpeedOfSprint;

        // Where the feet float to: the eyes `eyeAboveFeet` over them sit just above the surface, or `DiveEyeDepth` under
        // it while diving; never into the floor.
        public static float TargetFeetY(float surfaceY, float groundY, float eyeAboveFeet, bool dive, SwimRules rules)
        {
            float eye = dive ? surfaceY - rules.DiveEyeDepth : surfaceY + rules.EyeAboveSurface;
            return Math.Max(eye - eyeAboveFeet, groundY + rules.FloorClearance);
        }

        public static bool HeadUnder(float eyeY, float surfaceY, SwimRules rules) => eyeY < surfaceY - rules.HeadUnderMargin;
    }

    // The breath while swimming. Fraction 1 = full.
    public class SwimBreath
    {
        public float Fraction { get; private set; } = 1f;
        public bool Drowning => Fraction <= 0f;

        // Advances `dt` seconds; returns the drowning damage taken in it.
        public float Tick(float dt, bool headUnder, SwimRules rules)
        {
            if (!headUnder)
            {
                Fraction = Math.Min(1f, Fraction + dt / rules.RecoverSeconds);
                return 0f;
            }
            if (Fraction > 0f)
            {
                Fraction = Math.Max(0f, Fraction - dt / rules.DiveSeconds);
                return 0f;
            }
            return rules.DrownDamagePerSecond * dt;
        }

        public void Reset() => Fraction = 1f;
    }
}
