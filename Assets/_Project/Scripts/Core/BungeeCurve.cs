using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The bungee jump as a fixed curve (ADR-386/387, #419): no rope physics. Height above the landing pad (the deck under the jump
    // board) as a function of the seconds since the jump began: a beat on the board at the top, a free fall for the length of the
    // rope, a damped bounce, and a gentle lowering to the pad. People reach the top by the lift (BungeeLift). Pure and
    // deterministic, so the server, the jumper's client and the tests agree.
    public static class BungeeCurve
    {
        public const float Top = 60f;             // the platform's height above the pad
        public const float Rope = 35f;            // free fall before the rope pulls
        public const float Gravity = 9.81f;
        public const float HoldSeconds = 0.4f;
        public const float BouncePeriod = 6f;     // of the spring after the rope pulled
        public const float Damping = 0.4f;        // fraction of critical (lowest point about 4 m above the pad)
        public const float SettleBelow = 0.3f;    // metres: the bounce is over when its swing is below this
        public const float LowerSeconds = 3.5f;

        static readonly double W = 2.0 * Math.PI / BouncePeriod;
        static readonly double Wd = W * Math.Sqrt(1.0 - Damping * Damping);
        static readonly double Deq = Rope + Gravity / (W * W);              // where the jumper hangs at rest (metres below the platform)
        static readonly double A = Rope - Deq;
        static readonly double V1 = Math.Sqrt(2.0 * Gravity * Rope);        // speed when the rope pulls
        static readonly double B = (V1 + Damping * W * A) / Wd;

        public static float FallSeconds => (float)Math.Sqrt(2.0 * Rope / Gravity);
        public static float BounceSeconds => (float)(Math.Log(Math.Sqrt(A * A + B * B) / SettleBelow) / (Damping * W));
        public static float Duration => HoldSeconds + FallSeconds + BounceSeconds + LowerSeconds;
        public static float RestHeight => (float)(Top - Deq);

        public static float Height(float t)
        {
            if (t <= 0f) return Top;
            float t2 = HoldSeconds, t3 = t2 + FallSeconds, t4 = t3 + BounceSeconds, t5 = t4 + LowerSeconds;
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

    public enum LiftPhase { OpenBottom, Rising, OpenTop, Descending }

    // The open steel cage lift beside the bungee tower (ADR-387): a fast ride (a high-rise express: S-curve start and stop, cruise
    // ~15 m/s, 60 m in ~4.8 s) between the deck and the top. Since ADR-490 it moves on call (LiftCall), not on a fixed cycle. Who
    // stands in the cage when the doors close rides (at most Capacity). Pure.
    public static class BungeeLift
    {
        public const int Capacity = 4;
        public const float DoorSeconds = 6f;          // doors open at each end
        public const float CruiseSpeed = 15f;         // m/s
        public const float EaseSeconds = 0.8f;        // the S-curve start and stop
        public const float HalfSize = 1.4f;           // the cage floor is 2.8 m square (the boarding footprint)

        public static float Height => BungeeCurve.Top;
        // Distance covered by one ease ramp: speed follows smoothstep(0..1) over EaseSeconds, so the ramp covers half of cruise*ease.
        public static float TravelSeconds => 2f * EaseSeconds + (Height - CruiseSpeed * EaseSeconds) / CruiseSpeed;
        public static float Cycle => 2f * (DoorSeconds + TravelSeconds);   // the longest wait for a call: a full trip each way

        // Metres covered after t seconds of a ride (0..Height), speed continuous and zero at both ends.
        public static float Travelled(float t)
        {
            float T = TravelSeconds, ramp = CruiseSpeed * EaseSeconds * 0.5f;
            if (t <= 0f) return 0f;
            if (t >= T) return Height;
            if (t < EaseSeconds) { float u = t / EaseSeconds; return CruiseSpeed * EaseSeconds * (u * u * u - 0.5f * u * u * u * u); }
            if (t > T - EaseSeconds) return Height - Travelled(T - t);
            return ramp + CruiseSpeed * (t - EaseSeconds);
        }

        // Speed (m/s, positive) after t seconds of a ride.
        public static float Speed(float t)
        {
            float T = TravelSeconds;
            if (t <= 0f || t >= T) return 0f;
            if (t < EaseSeconds) { float u = t / EaseSeconds; return CruiseSpeed * u * u * (3f - 2f * u); }
            if (t > T - EaseSeconds) return Speed(T - t);
            return CruiseSpeed;
        }

        // The car's floor height above the deck in this phase, `into` seconds after it began (a ride past its end has arrived).
        public static float HeightOf(LiftPhase phase, float into)
        {
            switch (phase)
            {
                case LiftPhase.OpenBottom: return 0f;
                case LiftPhase.Rising: return Travelled(into);
                case LiftPhase.OpenTop: return Height;
                default: return Height - Travelled(into);
            }
        }

        // Doors are open at either end, and on a ride the clients see as over (their clock ran past the trip).
        public static bool DoorsOpenOf(LiftPhase phase, float into) => phase == LiftPhase.OpenBottom || phase == LiftPhase.OpenTop || into >= TravelSeconds;

        // The riders of one trip: those in the cage when the doors close, first come first served, at most Capacity.
        public static List<T> Board<T>(IEnumerable<T> inCage)
        {
            var riders = new List<T>(Capacity);
            foreach (var x in inCage) { if (riders.Count == Capacity) break; riders.Add(x); }
            return riders;
        }

        // A waiting person who is up in the air and not on the top floor (platform, bridge, lift landing), not riding and not jumping,
        // has fallen: the server puts them back on the deck at once (damage is zero anyway, ADR-383).
        public static bool FellOff(float heightAboveDeck, bool onTopFloor) => heightAboveDeck > 2.5f && !onTopFloor;
    }

    // The lift on call (ADR-490): the car rests with its doors open at the floor it last reached. A call from the other floor (the F
    // button beside the doors) brings it there; a call from its own floor holds the doors. People in the cage ride to the other floor
    // once they have stood in it DoorSeconds. Pure; the caller passes the clock and whether anyone stands in the cage.
    public sealed class LiftCall
    {
        public const float CallDepartSeconds = 1.5f;   // a call from the other floor shuts the doors this long after they opened, at the earliest

        public LiftPhase Phase { get; private set; } = LiftPhase.OpenBottom;
        public bool CalledBottom { get; private set; }  // the button's light: the car is on its way (or about to leave) for that floor
        public bool CalledTop { get; private set; }
        double _since, _occupiedSince = double.NaN;

        public LiftCall(double now) { _since = now; }

        public float Into(double now) => (float)(now - _since);
        public bool Moving => Phase == LiftPhase.Rising || Phase == LiftPhase.Descending;
        public float Height(double now) => BungeeLift.HeightOf(Phase, Into(now));

        // The button at the bottom (top = false) or the top. True when the call is new (the light comes on).
        public bool Call(bool top, double now)
        {
            var here = top ? LiftPhase.OpenTop : LiftPhase.OpenBottom;
            if (Phase == here) { _since = Math.Max(_since, now - CallDepartSeconds); return false; }   // already here: the doors stay open
            if (top ? CalledTop : CalledBottom) return false;
            if (top) CalledTop = true; else CalledBottom = true;
            return true;
        }

        // Call every tick. True on the tick the phase changed (doors shut and the ride began, or the car arrived and opened).
        public bool Tick(double now, bool occupied)
        {
            float into = Into(now);
            if (Moving)
            {
                if (into < BungeeLift.TravelSeconds) return false;
                bool top = Phase == LiftPhase.Rising;
                Phase = top ? LiftPhase.OpenTop : LiftPhase.OpenBottom;
                if (top) CalledTop = false; else CalledBottom = false;
                _since = now; _occupiedSince = double.NaN;
                return true;
            }
            if (!occupied) _occupiedSince = double.NaN;
            else if (double.IsNaN(_occupiedSince)) _occupiedSince = now;
            bool atTop = Phase == LiftPhase.OpenTop;
            bool called = atTop ? CalledBottom : CalledTop;
            bool ridersReady = occupied && now - _occupiedSince >= BungeeLift.DoorSeconds;
            if (!(called && into >= CallDepartSeconds) && !ridersReady) return false;
            Phase = atTop ? LiftPhase.Descending : LiftPhase.Rising;
            if (atTop) CalledBottom = true; else CalledTop = true;   // the far floor's light shows where it goes
            _since = now;
            return true;
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
