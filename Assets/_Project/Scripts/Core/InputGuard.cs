using System;

namespace Game.Core
{
    // What the server tolerates from a client's inputs and requests (#170, ADR-142). Every limit sits well beyond what a
    // legitimate player produces at 100 ms / 2 % loss; a request past it is refused (or clamped) and counted.
    [Serializable]
    public class GuardRules
    {
        // A deliberate teleport the owner reports (leaving a vehicle): at most this far from where the server holds the body
        // (the car ran on 0.25 s at up to ~40 m/s while the owner stepped out), and not more often than the interval.
        public float TeleportMaxMeters = 25f;
        public float TeleportMinIntervalSeconds = 0.5f;
        // The server's copy of a body trails the owner's by a latency and a send interval: 6.3 m/s * 0.25 s on top of reach limits.
        public float LagSlackMeters = 1.5f;
        // Fire requests: at most this multiple of the weapon's rate on average, with a burst of FireBurst on top (bunched packets).
        public float FireRateMargin = 1.5f;
        public int FireBurst = 4;
        // Bare hands or a melee weapon: requests per second, and the burst.
        public float MeleeRequestsPerSecond = 8f;
        // Inventory requests (#503): per second on average, and the burst (a quick run of clicks or a pickup list emptied fast).
        public float ItemRequestsPerSecond = 10f;
        public int ItemRequestBurst = 20;
        // A look pitch beyond this is not a head a player can turn (the server clamps it to 90 anyway).
        public float MaxLookPitchDegrees = 91f;
        // The log says one line per kind this often, with how many were left out.
        public float LogIntervalSeconds = 5f;
    }

    // A bucket of requests: it refills `perSecond` a second up to `capacity`; a request takes one.
    public struct RequestBucket
    {
        double _tokens;
        double _at;
        bool _started;

        public bool TryTake(double now, double perSecond, double capacity)
        {
            if (!_started) { _started = true; _tokens = capacity; }
            else _tokens = Math.Min(capacity, _tokens + Math.Max(0.0, now - _at) * perSecond);
            _at = now;
            if (_tokens < 1.0) return false;
            _tokens -= 1.0;
            return true;
        }
    }

    public static class InputGuard
    {
        public static bool Finite(float x, float y, float z) =>
            !(float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) || float.IsNaN(z) || float.IsInfinity(z));

        public static bool ValidSlot(int slot, int count) => slot >= 0 && slot < count;

        // A reported teleport: finite, near, and not right after another.
        public static bool TeleportOk(float meters, double sinceLast, GuardRules g) =>
            !float.IsNaN(meters) && meters <= g.TeleportMaxMeters && sinceLast >= g.TeleportMinIntervalSeconds;

        // How many fire requests a second a weapon with this interval may produce (0 interval: the melee rate).
        public static double FireRequestsPerSecond(double interval, GuardRules g) =>
            interval > 0.0 ? g.FireRateMargin / interval : g.MeleeRequestsPerSecond;
    }
}
