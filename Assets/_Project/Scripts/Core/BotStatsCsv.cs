using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core
{
    // One row of the 80-player bot statistics (ADR-313, #377): ordered name/value pairs written as a header line and a value line.
    public sealed class BotStatsCsv
    {
        readonly List<string> _names = new List<string>();
        readonly List<string> _values = new List<string>();

        public BotStatsCsv Add(string name, string value) { _names.Add(name); _values.Add(value ?? ""); return this; }
        public BotStatsCsv Add(string name, int value) => Add(name, value.ToString(CultureInfo.InvariantCulture));
        public BotStatsCsv Add(string name, float value) => Add(name, value.ToString("0.###", CultureInfo.InvariantCulture));

        public string Header => string.Join(",", _names);
        public string Line => string.Join(",", _values);

        // value / total as a 0..1 share (0 when there is no total).
        public static float Share(int part, int total) => total > 0 ? part / (float)total : 0f;

        // Median and nearest-rank percentile of a list (0 when empty).
        public static float Percentile(IReadOnlyList<float> values, float p)
        {
            if (values == null || values.Count == 0) return 0f;
            var sorted = new List<float>(values);
            sorted.Sort();
            int rank = (int)System.Math.Ceiling(System.Math.Max(0f, System.Math.Min(100f, p)) / 100f * sorted.Count);
            return sorted[System.Math.Max(0, rank - 1)];
        }

        public static int CountAtMost(IReadOnlyList<float> values, float limit)
        {
            int n = 0;
            foreach (var v in values) if (v <= limit) n++;
            return n;
        }
    }
}
