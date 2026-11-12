using System;

namespace Game.Core
{
    public class Health
    {
        public float Max { get; }
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;

        public Health(float max)
        {
            Max = max;
            Current = max;
        }

        // Returns the damage actually applied. The dead take no further damage.
        public float ApplyDamage(float amount)
        {
            if (IsDead || amount <= 0f) return 0f;
            float applied = Math.Min(amount, Current);
            Current -= applied;
            return applied;
        }

        // Returns the amount actually healed. The dead cannot be healed.
        public float Heal(float amount)
        {
            if (IsDead || amount <= 0f) return 0f;
            float healed = Math.Min(amount, Max - Current);
            Current += healed;
            return healed;
        }
    }
}
