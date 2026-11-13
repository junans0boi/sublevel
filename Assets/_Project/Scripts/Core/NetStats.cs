using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Network status numbers (#171, ADR-128). Pure: the server numbers its heartbeats (pulses); the client feeds the
    // numbers that arrived here and reads the loss, and these rules turn ping and loss into a text and a grade.
    public sealed class PulseLoss
    {
        public const float WindowSeconds = 10f;

        readonly List<uint> _seq = new List<uint>();
        readonly List<float> _time = new List<float>();

        // Pulses received in the window.
        public int Received => _seq.Count;
        public bool HasData => _seq.Count >= 2;

        // A pulse arrived. Late or repeated numbers are ignored (the unreliable channel may reorder).
        public void Add(uint seq, float now)
        {
            if (_seq.Count > 0 && seq <= _seq[_seq.Count - 1]) return;
            _seq.Add(seq);
            _time.Add(now);
            int old = 0;
            while (old < _time.Count - 1 && now - _time[old] > WindowSeconds) old++;
            if (old > 0) { _seq.RemoveRange(0, old); _time.RemoveRange(0, old); }
        }

        // Share of the pulses sent in the window that never arrived, 0..1.
        public float Loss
        {
            get
            {
                if (_seq.Count < 2) return 0f;
                float expected = _seq[_seq.Count - 1] - _seq[0] + 1f;
                return Math.Max(0f, 1f - _seq.Count / expected);
            }
        }

        public void Clear() { _seq.Clear(); _time.Clear(); }
    }

    public enum NetGrade { Good, Fair, Bad }

    public static class NetStatusRules
    {
        public const float PulseInterval = 0.1f; // seconds between the server's pulses

        public const int FairPingMs = 60, BadPingMs = 120;
        public const float FairLoss = 0.01f, BadLoss = 0.04f;

        // The worse of ping and loss.
        public static NetGrade Grade(int pingMs, float loss)
        {
            var ping = pingMs >= BadPingMs ? NetGrade.Bad : pingMs >= FairPingMs ? NetGrade.Fair : NetGrade.Good;
            var drop = loss >= BadLoss ? NetGrade.Bad : loss >= FairLoss ? NetGrade.Fair : NetGrade.Good;
            return ping > drop ? ping : drop;
        }

        // "핑 42 ms   손실 1.0%   틱 12345 (60/s)"; without pulses yet: "핑 42 ms   손실 -   틱 -".
        public static string Format(int pingMs, float loss, bool hasPulses, uint serverTick, int tickRate)
        {
            string lossText = hasPulses ? (loss * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%" : "-";
            string tickText = hasPulses ? serverTick + " (" + tickRate + "/s)" : "-";
            return "핑 " + Math.Max(0, pingMs) + " ms   손실 " + lossText + "   틱 " + tickText;
        }
    }
}
