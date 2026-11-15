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
        // Pose (code-driven, no clip): crawl stroke and flutter kick, rates in cycles per second.
        public float StrokeHzIdle = 0.22f;      // treading water
        public float StrokeHzFull = 0.85f;      // at the swim speed
        public float KickHzIdle = 0.8f;
        public float KickHzFull = 2.4f;
        public float KickDeg = 24f;             // thigh swing either side of straight
        public float LevelDeg = 30f;            // chest lift taken off the crawl's (ProneHeadLiftDeg) so the body lies level
        public float DivePitchDeg = 30f;        // diving: the head dips this far under the line of the body
        public float PoseBlendPerSecond = 4f;   // entering and leaving the water
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

        // 0 treading water .. 1 at the swim speed.
        public static float Effort(float speed, float sprintSpeed, SwimRules rules)
        {
            float full = Math.Max(0.1f, Speed(sprintSpeed, rules));
            return Math.Min(1f, Math.Max(0f, speed / full));
        }

        public static float StrokeHz(float effort, SwimRules rules) => rules.StrokeHzIdle + (rules.StrokeHzFull - rules.StrokeHzIdle) * effort;
        public static float KickHz(float effort, SwimRules rules) => rules.KickHzIdle + (rules.KickHzFull - rules.KickHzIdle) * effort;

        // An arm's turn from hanging back along the body (degrees about the right axis; positive lifts it up and over
        // to reach forward at 180). `phase` 0..1 of the stroke, `side` 0 or 1 (the other arm half a stroke behind).
        // Crawl: recovery over the water for the first half, the pull under it for the second.
        public static float ArmAngle(float phase, int side)
        {
            float p = phase + 0.5f * side;
            return 360f * (p - (float)Math.Floor(p));
        }

        // Treading: arms held forward of the shoulders and sculling a little.
        public static float ArmIdleAngle(float phase, int side) => 165f + 15f * (float)Math.Sin((phase + 0.5f * side) * 2.0 * Math.PI);

        // Flutter kick: a leg's thigh turn (degrees about the right axis) for a kick phase 0..1; the other leg opposite.
        public static float KickThigh(float phase, int side, SwimRules rules) =>
            rules.KickDeg * (float)Math.Sin((phase + 0.5f * side) * 2.0 * Math.PI);

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
