using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum BungeePhase { Board, Fall, Bounce, Hang, Lower, Done }

    // The bungee jump as a fixed curve (ADR-386/387/495, #419): no rope physics. Height above the landing pad (the deck under the jump
    // board) as a function of the seconds since the jump began: a beat on the board (the lean over the edge), a free fall for the
    // length of the rope, a hard catch and a damped bounce, a hang until the jumper asks to come down (F) or HangSeconds pass, and the
    // lowering to the pad. People reach the top by the lift (BungeeLift). Pure and deterministic, so the server, the jumper's client
    // and the tests agree; `lowerAt` is when the jumper asked to come down (seconds since the jump, +infinity = not asked).
    public static class BungeeCurve
    {
        public const float Top = 60f;             // the platform's height above the pad
        public const float Rope = 40f;            // free fall before the rope pulls (~28 m/s at the catch)
        public const float Gravity = 9.81f;
        public const float HoldSeconds = 0.8f;    // on the board: the lean over the edge before the drop
        public const float BouncePeriod = 4.5f;   // of the spring after the rope pulled (a ~2.2 g catch)
        public const float Damping = 0.35f;       // fraction of critical (lowest point about 4 m above the pad)
        public const float SettleBelow = 0.6f;    // metres: the bounce is over (hanging) when its swing is below this
        public const float HangSeconds = 6f;      // hanging this long lowers on its own: nobody stays up there
        public const float LowerSeconds = 3.5f;

        static readonly double W = 2.0 * Math.PI / BouncePeriod;
        static readonly double Wd = W * Math.Sqrt(1.0 - Damping * Damping);
        static readonly double Deq = Rope + Gravity / (W * W);              // where the jumper hangs at rest (metres below the platform)
        static readonly double A = Rope - Deq;
        static readonly double V1 = Math.Sqrt(2.0 * Gravity * Rope);        // speed when the rope pulls
        static readonly double B = (V1 + Damping * W * A) / Wd;
        static readonly float BottomTau = FirstLowest();                    // seconds from the rope pulling to the lowest point

        public static float FallSeconds => (float)Math.Sqrt(2.0 * Rope / Gravity);
        public static float BounceSeconds => (float)(Math.Log(Math.Sqrt(A * A + B * B) / SettleBelow) / (Damping * W));
        public static float RestHeight => (float)(Top - Deq);
        public static float TautAt => HoldSeconds + FallSeconds;           // the rope starts to pull
        public static float SnapAt => TautAt + BottomTau;                  // the lowest point: the rope's hardest pull, the climax
        public static float ReadyAt => TautAt + BounceSeconds;             // hanging: F may lower from here
        public static float AutoLowerAt => ReadyAt + HangSeconds;
        public static float Duration => AutoLowerAt + LowerSeconds;        // the longest jump (nobody pressed F)

        public static float LowerStart(float lowerAt) => Math.Min(Math.Max(lowerAt, ReadyAt), AutoLowerAt);
        public static float End(float lowerAt) => LowerStart(lowerAt) + LowerSeconds;
        public static bool Over(float t, float lowerAt = float.PositiveInfinity) => t >= End(lowerAt);

        public static float Height(float t, float lowerAt = float.PositiveInfinity)
        {
            if (t < HoldSeconds) return Top;
            if (t < TautAt) { float tau = t - HoldSeconds; return Top - 0.5f * Gravity * tau * tau; }
            float ls = LowerStart(lowerAt);
            if (t < ls) return Top - Dist(t - TautAt);
            // Lowered from wherever the swing was (no jump in the height), eased out onto the pad.
            float u = (t - ls) / LowerSeconds;
            if (u >= 1f) return 0f;
            return (Top - Dist(ls - TautAt)) * (1f - u * u * (3f - 2f * u));
        }

        public static BungeePhase PhaseAt(float t, float lowerAt = float.PositiveInfinity)
        {
            if (t < HoldSeconds) return BungeePhase.Board;
            if (t < TautAt) return BungeePhase.Fall;
            if (t < ReadyAt) return BungeePhase.Bounce;
            if (t < LowerStart(lowerAt)) return BungeePhase.Hang;
            return t < End(lowerAt) ? BungeePhase.Lower : BungeePhase.Done;
        }

        // Upward speed (m/s) and acceleration (m/s^2) along the curve: what the camera and the sound follow.
        public static float Velocity(float t, float lowerAt = float.PositiveInfinity) => (Height(t + 0.005f, lowerAt) - Height(t - 0.005f, lowerAt)) / 0.01f;
        public static float Acceleration(float t, float lowerAt = float.PositiveInfinity)
            => (Height(t + 0.02f, lowerAt) - 2f * Height(t, lowerAt) + Height(t - 0.02f, lowerAt)) / 0.0004f;

        // Slow motion in the jumper's own view as the rope bites (ADR-498): their body follows FeelTime(t), which runs down to about a third
        // of real speed over the last metres of the plunge, then a little fast through the rebound until it is back in step. Only the
        // jumper's own clock: the server and everyone else stay on t (the gap is never more than SlowLag seconds, near the slowest point).
        public const float SlowLag = 0.28f, SlowBefore = 0.55f, SlowAfter = 0.1f, CatchUpSeconds = 1.6f;
        public static float FeelTime(float t)
        {
            float a = SnapAt - SlowBefore, b = SnapAt + SlowAfter, c = b + CatchUpSeconds;
            if (t <= a || t >= c) return t;
            return t - (t < b ? SlowLag * Smooth((t - a) / (b - a)) : SlowLag * (1f - Smooth((t - b) / (c - b))));
        }

        static float Smooth(float u) => u * u * (3f - 2f * u);

        static float Dist(float tau) => (float)(Deq + Math.Exp(-Damping * W * tau) * (A * Math.Cos(Wd * tau) + B * Math.Sin(Wd * tau)));

        static float FirstLowest()
        {
            float best = 0f, deepest = float.MinValue;
            for (float tau = 0f; tau < BouncePeriod; tau += 0.002f) { float d = Dist(tau); if (d > deepest) { deepest = d; best = tau; } else if (d < deepest - 0.5f) break; }
            return best;
        }

        // The lowest point and the first rebound's top (heights above the pad): what the board shows as the jump's best.
        public static float Bottom => Top - Dist(BottomTau);
        public static float ReboundApex
        {
            get
            {
                float best = Bottom;
                for (float t = BottomTau; t < BounceSeconds; t += 0.01f) { float h = Top - Dist(t); if (h >= best) best = h; else if (h < best - 0.05f) break; }
                return best;
            }
        }
    }

    // How a jumper's body is turned (ADR-498): head first over the edge, head-down through the fall, the bounce and the hang, and back
    // upright in the last metres of the lowering so the feet touch the pad. From the height alone, so every client poses (and ropes) a
    // jumper it only sees move, with nothing extra sent.
    public static class BungeePose
    {
        public const float ColumnRadius = 1.6f;   // this close (horizontally) to the pad's middle, a body is on the rope
        public const float PivotHeight = 1.0f;    // the body turns over at the hips
        public const float AnkleHeight = 0.1f;    // where the cord is tied, above the soles
        public const float DetachBelow = 1.2f;    // the cord comes off this close above the pad (and is not shown for a hop on the pad)

        // 0 = upright, 1 = head-down. `h` is the feet's height above the pad.
        public static float Flip(float h)
        {
            float over = Smooth01((BungeeCurve.Top + 0.05f - h) / 4f);   // the first 4 m of the fall
            float land = Smooth01((h - 0.5f) / 3.1f);                    // upright again from 3.6 m down to 0.5 m
            return Math.Min(over, land);
        }

        public static bool OnRope(float dx, float dz, float h) => dx * dx + dz * dz <= ColumnRadius * ColumnRadius && h > DetachBelow && h < BungeeCurve.Top + 1f;

        // The tied ankles relative to the feet, in the body's own frame (y up, z forward): the body turns forward (head first) about the hips.
        public static void Ankles(float flip, out float up, out float forward)
        {
            double a = Math.PI * flip, r = AnkleHeight - PivotHeight;
            up = (float)(PivotHeight + r * Math.Cos(a));
            forward = (float)(r * Math.Sin(a));
        }

        static float Smooth01(float u) { u = Math.Min(1f, Math.Max(0f, u)); return u * u * (3f - 2f * u); }
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
        float _lowerAt = float.PositiveInfinity;
        public float Best { get; private set; }   // the best rebound of this room (metres above the pad), 0 = none yet

        public bool Active => _jumper >= 0;
        public int Jumper => _jumper;
        public float ElapsedOf(double now) => _jumper >= 0 ? (float)(now - _startedAt) : 0f;
        public float LowerAt => _lowerAt;   // when the jumper asked to come down (seconds into the jump), +infinity = not asked

        public Result TryStart(int id, double now)
        {
            if (_jumper >= 0) return Result.Busy;
            if (now < _freeAt) return Result.Cooling;
            _jumper = id; _startedAt = now; _lowerAt = float.PositiveInfinity;
            return Result.Started;
        }

        // The jumper pressed F while hanging (ADR-498): the lowering starts now (or as soon as they hang, if the press came early).
        public bool RequestLower(int id, double now)
        {
            if (_jumper != id || _jumper < 0 || !float.IsPositiveInfinity(_lowerAt)) return false;
            _lowerAt = BungeeCurve.LowerStart(ElapsedOf(now));
            return true;
        }

        // Call every tick: ends the jump when the curve is over (records the rebound). Returns true on the tick it ended.
        public bool Tick(double now)
        {
            if (_jumper < 0 || !BungeeCurve.Over((float)(now - _startedAt), _lowerAt)) return false;
            Best = Math.Max(Best, BungeeCurve.ReboundApex);
            _jumper = -1; _freeAt = now + CooldownSeconds;
            return true;
        }

        // The jumper left (disconnected): the line frees at once, no record.
        public void Abort(int id, double now) { if (_jumper == id) { _jumper = -1; _freeAt = now + CooldownSeconds; } }
    }
}
