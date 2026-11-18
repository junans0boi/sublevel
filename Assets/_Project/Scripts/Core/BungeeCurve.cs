using System;

namespace Game.Core
{
    // The bungee jump as a fixed curve (ADR-386, #419): no rope physics. Height above the pad as a function of the seconds since the
    // jump began: carried up to the platform, a beat on the edge, a free fall for the length of the rope, then a damped bounce, and
    // a gentle lowering back to the pad. Pure and deterministic, so the server, the jumper's client and the tests agree.
    public static class BungeeCurve
    {
        public const float Top = 24f;             // the platform's height above the pad
        public const float Rope = 11f;            // free fall before the rope pulls
        public const float Gravity = 9.81f;
        public const float AscentSeconds = 4f;
        public const float HoldSeconds = 1.2f;
        public const float BouncePeriod = 3f;     // of the spring after the rope pulled
        public const float Damping = 0.25f;       // fraction of critical
        public const float SettleBelow = 0.2f;    // metres: the bounce is over when its swing is below this
        public const float LowerSeconds = 2.5f;

        static readonly double W = 2.0 * Math.PI / BouncePeriod;
        static readonly double Wd = W * Math.Sqrt(1.0 - Damping * Damping);
        static readonly double Deq = Rope + Gravity / (W * W);              // where the jumper hangs at rest (metres below the platform)
        static readonly double A = Rope - Deq;
        static readonly double V1 = Math.Sqrt(2.0 * Gravity * Rope);        // speed when the rope pulls
        static readonly double B = (V1 + Damping * W * A) / Wd;

        public static float FallSeconds => (float)Math.Sqrt(2.0 * Rope / Gravity);
        public static float BounceSeconds => (float)(Math.Log(Math.Sqrt(A * A + B * B) / SettleBelow) / (Damping * W));
        public static float Duration => AscentSeconds + HoldSeconds + FallSeconds + BounceSeconds + LowerSeconds;
        public static float RestHeight => (float)(Top - Deq);

        public static float Height(float t)
        {
            if (t <= 0f) return 0f;
            float t1 = AscentSeconds, t2 = t1 + HoldSeconds, t3 = t2 + FallSeconds, t4 = t3 + BounceSeconds, t5 = t4 + LowerSeconds;
            if (t < t1) { float u = t / t1; return Top * u * u * (3f - 2f * u); }
            if (t < t2) return Top;
            if (t < t3) { float tau = t - t2; return Top - 0.5f * Gravity * tau * tau; }
            if (t < t4) return Top - Dist(t - t3);
            if (t < t5) { float u = (t - t4) / LowerSeconds; return RestHeight * (1f - u * u * (3f - 2f * u)); }
            return 0f;
        }

        static float Dist(float tau) => (float)(Deq + Math.Exp(-Damping * W * tau) * (A * Math.Cos(Wd * tau) + B * Math.Sin(Wd * tau)));

        public static bool Over(float t) => t >= Duration;

        // The lowest point and the first rebound's top (heights above the pad): what the board shows as the jump's best.
        public static float Bottom { get { float m = float.MaxValue; for (float t = 0f; t < BounceSeconds; t += 0.01f) m = Math.Min(m, Top - Dist(t)); return m; } }
        public static float ReboundApex
        {
            get
            {
                float tBottom = 0f, low = float.MaxValue;
                for (float t = 0f; t < BounceSeconds; t += 0.01f) { float h = Top - Dist(t); if (h < low) { low = h; tBottom = t; } else if (h > low + 0.05f) break; }
                float best = low;
                for (float t = tBottom; t < BounceSeconds; t += 0.01f) { float h = Top - Dist(t); if (h >= best) best = h; else if (h < best - 0.05f) break; }
                return best;
            }
        }
    }

    // Who may jump (ADR-386): one at a time, a short pause after each jump. Pure; the caller passes the clock.
    public sealed class BungeeLine
    {
        public const float CooldownSeconds = 4f;
        public enum Result { Started, Busy, Cooling }

        int _jumper = -1;
        double _startedAt, _freeAt = double.NegativeInfinity;
        public float Best { get; private set; }   // the best rebound of this room (metres above the pad), 0 = none yet

        public bool Active => _jumper >= 0;
        public int Jumper => _jumper;
        public float ElapsedOf(double now) => _jumper >= 0 ? (float)(now - _startedAt) : 0f;

        public Result TryStart(int id, double now)
        {
            if (_jumper >= 0) return Result.Busy;
            if (now < _freeAt) return Result.Cooling;
            _jumper = id; _startedAt = now;
            return Result.Started;
        }

        // Call every tick: ends the jump when the curve is over (records the rebound). Returns true on the tick it ended.
        public bool Tick(double now)
        {
            if (_jumper < 0 || !BungeeCurve.Over((float)(now - _startedAt))) return false;
            Best = Math.Max(Best, BungeeCurve.ReboundApex);
            _jumper = -1; _freeAt = now + CooldownSeconds;
            return true;
        }

        // The jumper left (disconnected): the line frees at once, no record.
        public void Abort(int id, double now) { if (_jumper == id) { _jumper = -1; _freeAt = now + CooldownSeconds; } }
    }
}
