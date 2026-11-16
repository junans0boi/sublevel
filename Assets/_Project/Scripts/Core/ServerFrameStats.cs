using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Frame-time statistics of one window (server measurement, ADR-242). Pure.
    public sealed class ServerFrameStats
    {
        readonly List<float> _ms = new List<float>();

        public int Count => _ms.Count;
        public void Add(float ms) => _ms.Add(ms);
        public void Clear() => _ms.Clear();

        public float Mean() { if (_ms.Count == 0) return 0f; double s = 0; foreach (var v in _ms) s += v; return (float)(s / _ms.Count); }
        public float Max() { float m = 0f; foreach (var v in _ms) if (v > m) m = v; return m; }

        // Nearest-rank percentile, p in 0..100.
        public float Percentile(float p)
        {
            if (_ms.Count == 0) return 0f;
            var sorted = new List<float>(_ms);
            sorted.Sort();
            int rank = (int)Math.Ceiling(Math.Max(0f, Math.Min(100f, p)) / 100f * sorted.Count);
            return sorted[Math.Max(0, rank - 1)];
        }
    }
}
