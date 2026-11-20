using System;
using System.Collections.Generic;

namespace Game.Core
{
    // One driver's lap on the race track (ADR-507): the server feeds its copy of the car's place (RaceTrack.Project) every frame. A lap
    // starts at the first forward crossing of the line and counts when the car crosses it again after passing the four sectors in order.
    // Reversing past a sector boundary or jumping over one stops the clock; a lap faster than MinLapSeconds is not real (a teleport).
    public sealed class RaceLap
    {
        public const float MinLapSeconds = 15f;   // even flat out on boost all the way (29 m/s, no corners) a lap takes about 19 s

        int _sector = -1, _passed;
        public bool Timing { get; private set; }
        public double Start { get; private set; }

        // The lap time when this step completes a valid lap, else -1.
        public float Step(float s, double now)
        {
            int sector = RaceTrack.Sector(s);
            if (_sector < 0 || sector == _sector) { _sector = sector; return -1f; }
            bool forward = sector == (_sector + 1) % 4;
            _sector = sector;
            if (!forward) { Timing = false; _passed = 0; return -1f; }
            if (sector != 0) { _passed++; return -1f; }
            float lap = Timing && _passed == 3 ? (float)(now - Start) : -1f;
            Timing = true; Start = now; _passed = 0;
            return lap >= MinLapSeconds ? lap : -1f;
        }
    }

    // The room's best laps (ADR-509): one best per person (key: their connection), kept only while the room is open.
    public sealed class RaceRecords
    {
        public struct Entry { public int Key; public string Name; public float Seconds; public int Order; }
        readonly Dictionary<int, Entry> _best = new Dictionary<int, Entry>();
        int _order;

        public bool Any => _best.Count > 0;

        // True when this is the person's best so far.
        public bool Submit(int key, string name, float seconds)
        {
            if (_best.TryGetValue(key, out var e) && e.Seconds <= seconds) return false;
            _best[key] = new Entry { Key = key, Name = name ?? "", Seconds = seconds, Order = _order++ };
            return true;
        }

        public float BestOf(int key) => _best.TryGetValue(key, out var e) ? e.Seconds : 0f;

        public List<Entry> Top(int n)
        {
            var list = new List<Entry>(_best.Values);
            list.Sort((a, b) => a.Seconds != b.Seconds ? a.Seconds.CompareTo(b.Seconds) : a.Order.CompareTo(b.Order));
            return list.GetRange(0, Math.Min(n, list.Count));
        }
    }

    // The big board's lap page (ADR-509): every other PageSeconds it shows the lap TOP3 in place of the people and bungee lines.
    public static class RaceBoard
    {
        public const float PageSeconds = 6f;

        public static bool ShowLapPage(double t, bool hasRecords) => hasRecords && ((long)(t / PageSeconds) & 1) == 1;

        public static string Time(float seconds)
        {
            if (seconds < 60f) return seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            int m = (int)(seconds / 60f);
            return m + ":" + (seconds - m * 60f).ToString("00.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        // "1  Name  19.50" per line, empty entries (no name) left out.
        public static string Lines(IReadOnlyList<(string name, float seconds)> top)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < top.Count; i++)
            {
                if (string.IsNullOrEmpty(top[i].name)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(i + 1).Append("  ").Append(top[i].name).Append("  ").Append(Time(top[i].seconds));
            }
            return sb.ToString();
        }
    }
}
