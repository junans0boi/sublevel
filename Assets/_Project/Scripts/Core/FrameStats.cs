using System;

namespace Game.Core
{
    [Serializable]
    public struct FrameStats
    {
        public int Count;
        public float AvgMs;
        public float P99Ms;
        public float MaxMs;
        public float Fps;

        public static FrameStats Compute(float[] frameMs, int warmup)
        {
            int n = frameMs.Length - warmup;
            if (n <= 0) return default;

            var sorted = new float[n];
            Array.Copy(frameMs, warmup, sorted, 0, n);
            double sum = 0;
            foreach (var t in sorted) sum += t;
            Array.Sort(sorted);

            float avg = (float)(sum / n);
            int p99Index = Math.Min(n - 1, (int)Math.Ceiling(n * 0.99) - 1);
            return new FrameStats
            {
                Count = n,
                AvgMs = avg,
                P99Ms = sorted[p99Index],
                MaxMs = sorted[n - 1],
                Fps = 1000f / avg,
            };
        }
    }
}
