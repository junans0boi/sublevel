using System;

namespace Game.Core
{
    // Bare-handed punching (ADR-059 (1)) and how melee weapons are swung (ADR-073). Loaded from the "Melee" block
    // of rules.json. Each melee weapon's own damage, reach, cone, rate and head factor are in weapons.json.
    [Serializable]
    public class MeleeRules
    {
        // The rules in use. Defaults until the data loader replaces them.
        public static MeleeRules Current = new MeleeRules();

        public float PunchDamage = 12f;
        public float PunchRange = 1.6f;     // metres from the eyes to the target's chest (ADR-218)
        public float PunchConeDeg = 60f;    // full opening angle of the cone around where the punch is aimed
        public float PunchInterval = 0.55f; // seconds between punches
        public float HeadMultiplier = 1.5f; // a punch that lands in the head zone
        public float ChestFraction = 0.61f; // the chest's height as a share of the target's current height
        public float BotPunchMeters = 1.5f; // an unarmed bot punches an enemy this close (feet to feet)
        // A bot with a melee weapon swings at an enemy within the weapon's reach plus this (feet to feet), and runs
        // at one that is within BotChargeMeters instead of backing off.
        public float BotReachMargin = 0.2f;
        public float BotChargeMeters = 9f;

        // The swing shown for a melee weapon (no clip: bone turns, ADR-073). It lasts SwingSeconds (or the weapon's
        // interval when that is shorter): wound up by SwingWindUpShare of that time, through the strike by
        // SwingStrikeShare, then back to the ready pose. Angles in degrees.
        public float SwingSeconds = 0.5f;
        public float SwingWindUpShare = 0.18f;
        public float SwingStrikeShare = 0.5f;
        public float ReadyArmDeg = 30f;     // the weapon arm held a little forward while the weapon is in hand
        public float SlashArmDeg = 85f;     // slash: the arm comes up to about level...
        public float SlashSweepDeg = 65f;   // ...and sweeps from this far right (wound up) to this far left
        public float SlashTorsoDeg = 25f;   // the torso twists with it
        public float ChopWindUpDeg = 165f;  // chop: the arm goes up over the head...
        public float ChopStrikeDeg = 35f;   // ...and comes down to here, in front and below level
        public float ChopTorsoDeg = 15f;    // the torso bends forward into the chop (back while winding up)
    }

    // One melee attack's numbers: bare fists or a melee weapon.
    public struct MeleeStrike
    {
        public int WeaponId;        // 0 for fists
        public float Damage, Range, ConeDeg, Interval, HeadMultiplier;
    }

    // Where a shown swing is: `Swing` goes 0 → -1 (wound up) → +1 (end of the strike) → 0; `Weight` is how much
    // the swing pose replaces the ready pose (0 at rest, 1 from the wind-up to the end of the strike).
    public struct MeleeSwingPhase
    {
        public float Swing, Weight;
    }

    // The swing pose (ADR-073), for the bone turns in third person and the weapon at the eye in first person.
    // Positive sweep and twist are toward the right; ArmRaise 0 is hanging, 90 level forward, 180 straight up.
    public struct MeleePose
    {
        public float ArmRaiseDeg, ArmSweepDeg, TorsoTwistDeg, TorsoPitchDeg;
    }

    public static class Melee
    {
        // Is a point (given relative to the eyes: dx, dy, dz) within `range` of the eyes and inside the cone of
        // full opening angle `coneDeg` around the axis (ax, ay, az, any length)? A point right at the eyes is.
        public static bool InReach(float dx, float dy, float dz, float ax, float ay, float az, float range, float coneDeg)
        {
            double sqr = (double)dx * dx + (double)dy * dy + (double)dz * dz;
            if (!(sqr <= (double)range * range)) return false;
            if (sqr < 1e-10) return true;
            double axisLength = Math.Sqrt((double)ax * ax + (double)ay * ay + (double)az * az);
            if (axisLength < 1e-6) return false;
            double cos = (dx * (double)ax + dy * (double)ay + dz * (double)az) / (Math.Sqrt(sqr) * axisLength);
            double half = Math.Max(0.0, Math.Min(180.0, coneDeg)) * 0.5 * Math.PI / 180.0;
            return cos >= Math.Cos(half) - 1e-9;
        }

        // How high above the target's feet a punch aimed along the axis lands: where the aim line passes the
        // target at `horizontalDistance`, starting from eyes `eyeAboveFeet` over the target's feet. Clamped to
        // the target's body (0 .. `top`). `axisY` and `axisHorizontal` are the axis' vertical and horizontal parts.
        public static float HitHeight(float eyeAboveFeet, float horizontalDistance, float axisY, float axisHorizontal, float top)
        {
            float height;
            if (axisHorizontal > 1e-4f) height = eyeAboveFeet + axisY / axisHorizontal * Math.Max(0f, horizontalDistance);
            else height = axisY < 0f ? 0f : top; // straight down or up
            return Math.Max(0f, Math.Min(top, height));
        }

        // One punch on a zone: the head takes the head multiplier, everything else the plain damage.
        public static float Damage(HitZone zone, MeleeRules rules) =>
            rules.PunchDamage * (zone == HitZone.Head ? rules.HeadMultiplier : 1f);

        // Melee attacks are made with empty hands or a melee weapon, never with a gun in hand.
        public static bool CanStrike(WeaponDef inHand) => inHand == null || inHand.IsMelee;

        // The attack made with what is in hand: the weapon's numbers for a melee weapon, the punch for anything else
        // (callers refuse a gun with CanStrike first).
        public static MeleeStrike StrikeOf(WeaponDef inHand, MeleeRules rules)
        {
            if (inHand != null && inHand.IsMelee)
                return new MeleeStrike
                {
                    WeaponId = inHand.Id, Damage = inHand.Damage, Range = inHand.Range, ConeDeg = inHand.ConeDeg,
                    Interval = inHand.FireInterval, HeadMultiplier = inHand.HeadFactor,
                };
            return new MeleeStrike
            {
                WeaponId = 0, Damage = rules.PunchDamage, Range = rules.PunchRange, ConeDeg = rules.PunchConeDeg,
                Interval = rules.PunchInterval, HeadMultiplier = rules.HeadMultiplier,
            };
        }

        // One strike on a zone: the head takes the strike's head multiplier, everything else the plain damage.
        public static float Damage(HitZone zone, in MeleeStrike strike) =>
            strike.Damage * (zone == HitZone.Head ? strike.HeadMultiplier : 1f);

        // How close (feet to feet) a bot gets before it attacks with what is in hand.
        public static float BotReach(WeaponDef inHand, MeleeRules rules) =>
            inHand != null && inHand.IsMelee ? inHand.Range + rules.BotReachMargin : rules.BotPunchMeters;

        // How long one swing of this weapon is shown: SwingSeconds, or less for a weapon that swings faster.
        public static float SwingDuration(WeaponDef weapon, MeleeRules rules) =>
            weapon != null && weapon.FireInterval > 0f ? Math.Min(rules.SwingSeconds, weapon.FireInterval) : rules.SwingSeconds;

        // The swing `elapsed` seconds after it started, for a swing lasting `duration`. Rest before and after.
        public static MeleeSwingPhase SwingAt(float elapsed, float duration, MeleeRules rules)
        {
            if (!(duration > 0f) || !(elapsed > 0f) || elapsed >= duration) return default;
            float t = elapsed / duration;
            float windUp = Clamp(rules.SwingWindUpShare, 0.01f, 0.97f);
            float strike = Clamp(rules.SwingStrikeShare, windUp + 0.01f, 0.99f);
            if (t < windUp)
            {
                float w = Smooth(t / windUp);
                return new MeleeSwingPhase { Swing = -w, Weight = w };
            }
            if (t < strike) return new MeleeSwingPhase { Swing = -1f + 2f * Smooth((t - windUp) / (strike - windUp)), Weight = 1f };
            float back = 1f - Smooth((t - strike) / (1f - strike));
            return new MeleeSwingPhase { Swing = back, Weight = back };
        }

        // The pose of a swing in this phase (ready pose at rest).
        public static MeleePose PoseAt(MeleeSwingStyle style, in MeleeSwingPhase phase, MeleeRules rules)
        {
            float w = Clamp(phase.Weight, 0f, 1f), s = Clamp(phase.Swing, -1f, 1f);
            if (style == MeleeSwingStyle.Chop)
            {
                // From over the head (s = -1) down to the strike (s = +1).
                float arm = Lerp(rules.ChopStrikeDeg, rules.ChopWindUpDeg, (1f - s) * 0.5f);
                return new MeleePose { ArmRaiseDeg = Lerp(rules.ReadyArmDeg, arm, w), TorsoPitchDeg = s * rules.ChopTorsoDeg };
            }
            return new MeleePose
            {
                ArmRaiseDeg = Lerp(rules.ReadyArmDeg, rules.SlashArmDeg, w),
                ArmSweepDeg = -s * rules.SlashSweepDeg,
                TorsoTwistDeg = -s * rules.SlashTorsoDeg,
            };
        }

        static float Smooth(float x)
        {
            x = Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        static float Clamp(float x, float min, float max) => x < min ? min : x > max ? max : x;
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
