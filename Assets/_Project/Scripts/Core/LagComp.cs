using System;

namespace Game.Core
{
    // The last second or so of one body's positions, oldest dropped first (ring), for lag compensation (#162, ADR-141).
    public sealed class PositionHistory
    {
        readonly double[] _time;
        readonly float[] _x, _y, _z;
        int _next, _count;

        public PositionHistory(int capacity)
        {
            capacity = Math.Max(2, capacity);
            _time = new double[capacity];
            _x = new float[capacity];
            _y = new float[capacity];
            _z = new float[capacity];
        }

        public int Count => _count;
        public int Capacity => _time.Length;
        public double Newest => _count == 0 ? double.NaN : _time[(_next - 1 + _time.Length) % _time.Length];
        public double Oldest => _count == 0 ? double.NaN : _time[(_next - _count + _time.Length) % _time.Length];

        // A sample at or before the newest one is ignored (the clock only goes forward).
        public void Record(double time, float x, float y, float z)
        {
            if (_count > 0 && time <= Newest) return;
            _time[_next] = time; _x[_next] = x; _y[_next] = y; _z[_next] = z;
            _next = (_next + 1) % _time.Length;
            if (_count < _time.Length) _count++;
        }

        // Where the body was at `time`: between the two samples around it; before the oldest, the oldest; after the
        // newest, the newest. False when nothing was recorded.
        public bool TryAt(double time, out float x, out float y, out float z)
        {
            x = y = z = 0f;
            if (_count == 0) return false;
            int cap = _time.Length, first = (_next - _count + cap) % cap;
            int lo = first;
            if (time <= _time[first]) { x = _x[first]; y = _y[first]; z = _z[first]; return true; }
            for (int i = 1; i < _count; i++)
            {
                int hi = (first + i) % cap;
                if (_time[hi] >= time)
                {
                    double span = _time[hi] - _time[lo];
                    float k = span > 1e-9 ? (float)((time - _time[lo]) / span) : 1f;
                    x = _x[lo] + (_x[hi] - _x[lo]) * k;
                    y = _y[lo] + (_y[hi] - _y[lo]) * k;
                    z = _z[lo] + (_z[hi] - _z[lo]) * k;
                    return true;
                }
                lo = hi;
            }
            x = _x[lo]; y = _y[lo]; z = _z[lo];
            return true;
        }
    }

    public static class LagComp
    {
        // How far back a shot may be judged (fairness cap): a round trip of ~100 ms plus the interpolation delay is ~0.17 s.
        public const float MaxRewindSeconds = 0.25f; // (ADR-243 on hold: stays 0.25) the default of BallisticsRules.MaxRewindSeconds (data)
        // The interpolation delay of the replicated bodies, in network ticks (the prefab's setting, ADR-126).
        public const int InterpolationTicks = 2;
        // How much history is kept (seconds).
        public const float HistorySeconds = 1f;

        // Seconds to go back for a shot whose sender was looking at server tick `viewTick` while the server is at
        // `serverTick`. Never negative (a claim from the future is no rewind) and never above `max` (a late or lying
        // claim gets the cap, not the history).
        public static float RewindSeconds(uint serverTick, uint viewTick, double tickDelta, float max = MaxRewindSeconds)
        {
            if (viewTick >= serverTick || tickDelta <= 0.0) return 0f;
            return (float)Math.Min(max, (serverTick - viewTick) * tickDelta);
        }

        // The tick a client was showing: the newest one it has heard from the server less the interpolation delay.
        public static uint ViewTick(uint lastServerTick) => lastServerTick > InterpolationTicks ? lastServerTick - InterpolationTicks : 0u;
    }
}
