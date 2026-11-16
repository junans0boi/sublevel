namespace Game.Core
{
    // The wire shape and client mirror of a victim's stagger (ADR-182, ADR-240). The server's StaggerState decides; the
    // victim's owner and the bots only read this mirror and multiply their walking speed by SpeedFactor.
    public struct StaggerReplica
    {
        float _shake, _slow;
        double _until;

        public bool Active(double now) => now < _until;
        public float ShakeDeg(double now) => Active(now) ? _shake : 0f;
        public float SpeedFactor(double now) => Active(now) ? 1f - _slow : 1f;

        // `remain` is seconds left on the server when the value was sent; the local clock counts it down.
        public void Set(float shakeDeg, float slow, float remain, double now)
        {
            _shake = shakeDeg;
            _slow = slow;
            _until = now + remain;
        }

        public void Clear() => _until = double.NegativeInfinity;
    }

    public static class StaggerHits
    {
        // The server's one call per landed hit. False when the hit changed nothing (stopping 1, melee, or weaker than the
        // running stagger): then nothing is sent. True also when an equal hit only renewed the time.
        public static bool Register(StaggerState state, WeaponDef weapon, HitZone zone, double now, StoppingRules rules = null)
        {
            var effect = WeaponStopping.Of(weapon, zone, rules ?? StoppingRules.Current);
            if (effect.IsNone) return false;
            state.Apply(effect, now);
            return System.Math.Abs(state.ShakeDeg(now) - effect.ShakeDeg) < 1e-4f; // otherwise a stronger running stagger ignored this hit
        }
    }
}
