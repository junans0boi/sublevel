using System;

namespace Game.Core
{
    // Zeroing, scope sway and holding the breath (ADR-103, G10b).
    public static class Zeroing
    {
        // Furthest zero a scope offers, by its magnification; the 1x sights offer only the base 100 m.
        public static float MaxMeters(float zoom) => zoom < 2f ? 100f : zoom < 4f ? 400f : zoom < 6f ? 500f : zoom < 8f ? 600f : 800f;

        public static int Count(OpticDef def, BallisticsRules rules)
        {
            if (def == null) return 1;
            float max = MaxMeters(def.Kind == OpticKind.Scope ? def.Zoom : 1f);
            return 1 + (int)Math.Round((max - rules.DefaultZeroMeters) / rules.ZeroStepMeters);
        }

        // The distance list of an optic: DefaultZeroMeters, then +ZeroStepMeters up to MaxMeters.
        public static float Distance(OpticDef def, BallisticsRules rules, int index)
        {
            int count = Count(def, rules);
            return rules.DefaultZeroMeters + Math.Max(0, Math.Min(count - 1, index)) * rules.ZeroStepMeters;
        }

        // Index of the entry nearest to `meters` (below 0: the default).
        public static int IndexOf(OpticDef def, BallisticsRules rules, float meters)
        {
            int count = Count(def, rules);
            int i = (int)Math.Round((meters - rules.DefaultZeroMeters) / rules.ZeroStepMeters);
            return Math.Max(0, Math.Min(count - 1, i));
        }

        // `steps` entries further (PageUp) or nearer (PageDown); stops at both ends.
        public static float Step(OpticDef def, BallisticsRules rules, float current, int steps) =>
            Distance(def, rules, IndexOf(def, rules, current) + steps);
    }

    // The breath held in the sights. Fraction 1 = full lungs.
    public class BreathHold
    {
        public float Fraction { get; private set; } = 1f;
        public bool Holding { get; private set; }
        // Out of breath: cannot hold again until Fraction is back to the restart level.
        public bool Winded { get; private set; }

        public void Tick(float dt, bool want, BallisticsRules rules)
        {
            if (want && !Winded && Fraction > 0f)
            {
                Holding = true;
                Fraction = Math.Max(0f, Fraction - dt / rules.BreathHoldSeconds);
                if (Fraction <= 0f) { Winded = true; Holding = false; }
                return;
            }
            Holding = false;
            Fraction = Math.Min(1f, Fraction + dt / rules.BreathRecoverSeconds);
            if (Winded && Fraction >= rules.BreathRestartFraction) Winded = false;
        }

        public void Reset() { Fraction = 1f; Holding = false; Winded = false; }
    }

    public static class ScopeSway
    {
        // Largest sway (degrees) through a scope of this zoom; 0 below SwayMinZoom.
        public static float Amplitude(BallisticsRules rules, float zoom, float stanceFactor, bool holding, bool winded)
        {
            if (zoom < rules.SwayMinZoom) return 0f;
            float a = rules.SwayDegPerZoom * zoom * stanceFactor;
            return holding ? a * rules.HeldSwayFactor : winded ? a * rules.WindedSwayFactor : a;
        }

        // A slow wander (two sines per axis) in degrees; deterministic in time.
        public static void Offset(float amplitude, float t, out float yaw, out float pitch)
        {
            yaw = amplitude * (float)(Math.Sin(t * 1.3) + 0.5 * Math.Sin(t * 2.9 + 1.0)) / 1.5f;
            pitch = amplitude * (float)(Math.Sin(t * 0.9 + 2.0) + 0.5 * Math.Sin(t * 2.3)) / 1.5f;
        }
    }
}
