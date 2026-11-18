using System.Collections.Generic;
using System.Globalization;

namespace Game.Core
{
    // Client command line for feeling a bad connection (#399, ADR-300). Pure: parsing only. REMOVE BEFORE RELEASE (docs/RISKS_OPEN_QUESTIONS.md).
    //   --simulate-ping N     the client's packets leave N ms late, so the round trip is the real one + N (0..500)
    //   --simulate-jitter N   each packet's delay wanders +-N ms around that (0..200)
    // Anything missing or out of range is reported in Error and ignored: the game then runs as if the flags were absent.
    public struct SimulatedPing
    {
        public int PingMs;
        public int JitterMs;
        public string Error;
        public bool Active => Error == null && PingMs > 0;

        public const int MaxPingMs = 500, MaxJitterMs = 200;

        public static SimulatedPing Parse(IReadOnlyList<string> args)
        {
            var o = new SimulatedPing();
            if (args == null) return o;
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i];
                if (a != "--simulate-ping" && a != "--simulate-jitter") continue;
                bool ping = a == "--simulate-ping";
                int max = ping ? MaxPingMs : MaxJitterMs;
                if (i + 1 >= args.Count || !int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out int v) || v > max)
                    return new SimulatedPing { Error = a + " must be 0.." + max + " (ms), got '" + (i < args.Count ? args[i] : "nothing") + "'" };
                if (ping) o.PingMs = v; else o.JitterMs = v;
            }
            return o;
        }

        // The delay one packet gets: ping +- jitter, never below 0. `unit` is a random number in 0..1.
        public int DelayMs(float unit) => System.Math.Max(0, PingMs + (int)System.Math.Round((unit * 2f - 1f) * JitterMs));

        public string Label => Active ? "시뮬레이션 +" + PingMs + "ms" + (JitterMs > 0 ? " ±" + JitterMs : "") : "";
    }
}
