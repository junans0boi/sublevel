using System;

namespace Game.Core
{
    // The boost gauge (ADR-099, PUBG_GAP_V2 G8): drinks, pills and syringes fill it, it drains by itself, and while
    // it is up it heals a little and, from the third step on, speeds the body up. Four equal steps.
    [Serializable]
    public class BoostRules
    {
        public static BoostRules Current = new BoostRules();

        public float MaxGauge = 100f;
        public float DrainPerSecond = 0.8f;                                    // 100 -> 0 in 125 s ("a couple of minutes")
        public float[] HealPerSecond = { 0.25f, 0.5f, 0.75f, 1f };             // by step 1..4
        public float[] SpeedBonus = { 0f, 0f, 0.025f, 0.05f };                 // by step 1..4, added to the speed factor
    }

    public static class Boost
    {
        public const int Steps = 4;

        // 0 when empty, else 1..4 (a started step counts).
        public static int Step(float gauge, BoostRules r)
        {
            if (gauge <= 0f || r.MaxGauge <= 0f) return 0;
            int step = (int)Math.Ceiling(gauge / (r.MaxGauge / Steps));
            return step < 1 ? 1 : step > Steps ? Steps : step;
        }

        public static float HealRate(float gauge, BoostRules r)
        {
            int s = Step(gauge, r);
            return s == 0 ? 0f : r.HealPerSecond[s - 1];
        }

        // Multiplies walking and running speed.
        public static float SpeedFactor(float gauge, BoostRules r)
        {
            int s = Step(gauge, r);
            return s == 0 ? 1f : 1f + r.SpeedBonus[s - 1];
        }

        // Runs the gauge for dt seconds: returns the health it gave in that time and lowers the gauge. The drain
        // is stepwise-exact across a step boundary only to the frame, which is fine for a game tick.
        public static float Tick(ref float gauge, float dt, BoostRules r)
        {
            if (gauge <= 0f || dt <= 0f) return 0f;
            float heal = HealRate(gauge, r) * dt;
            gauge = Math.Max(0f, gauge - r.DrainPerSecond * dt);
            return heal;
        }

        public static float Add(float gauge, float amount, BoostRules r) => Math.Min(r.MaxGauge, Math.Max(0f, gauge) + Math.Max(0f, amount));
    }
}
