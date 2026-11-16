using System;

namespace Game.Core
{
    // Stopping power (ADR-182): a hit shakes the victim's aim and slows their walk. Numbers are proposals (playtest).
    [Serializable]
    public class StoppingRules
    {
        public static StoppingRules Current = new StoppingRules();

        public float MaxShakeDeg = 4f;   // aim wander at stopping 10
        public float MaxSlow = 0.35f;    // share of walking speed lost at stopping 10
        public float Seconds = 0.5f;     // how long one hit lasts
        public float LimbFactor = 0.5f;  // a limb hit counts this share; head and body count in full
    }

    // What one hit does to the victim. None (all zero) for stopping 1 and for weapons without the stat.
    public struct StoppingEffect
    {
        public float ShakeDeg, Slow, Seconds;
        public bool IsNone => ShakeDeg <= 0f && Slow <= 0f;
    }

    public static class WeaponStopping
    {
        // 0 at stopping 1 (and below), 1 at stopping 10, linear between.
        public static float Strength(int stopping) => Math.Max(0f, Math.Min(1f, (stopping - 1) / 9f));

        public static StoppingEffect Of(WeaponDef weapon, HitZone zone, StoppingRules rules)
        {
            if (weapon == null || weapon.IsMelee) return default;
            float s = Strength(weapon.Stopping) * (zone == HitZone.Limb ? rules.LimbFactor : 1f);
            if (s <= 0f) return default;
            return new StoppingEffect { ShakeDeg = rules.MaxShakeDeg * s, Slow = rules.MaxSlow * s, Seconds = rules.Seconds };
        }
    }

    // One victim's current stagger. Hits never add up: a stronger hit replaces the running one, a weaker one is ignored
    // while the stronger lasts, an equal one only renews the time. Server-side: the server owns movement and aim.
    public class StaggerState
    {
        StoppingEffect _effect;
        double _until = double.NegativeInfinity;

        public bool Active(double now) => now < _until;
        public float ShakeDeg(double now) => Active(now) ? _effect.ShakeDeg : 0f;
        // Multiplier on walking speed: 1 when not staggered.
        public float SpeedFactor(double now) => Active(now) ? 1f - _effect.Slow : 1f;

        public void Apply(StoppingEffect e, double now)
        {
            if (e.IsNone) return;
            if (Active(now) && e.ShakeDeg < _effect.ShakeDeg) return;
            _effect = e;
            _until = now + e.Seconds;
        }

        public void Clear() => _until = double.NegativeInfinity;
    }
}
