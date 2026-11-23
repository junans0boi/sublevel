using System;

namespace Game.Core
{
    // An automatic gearbox (ADR-558, 준환: RPM and gear changes you can see; manual later). Each gear covers a share of the
    // top speed (longer gears higher up, as EngineNote shaped them). Up at ShiftUpRpm with the throttle on, down below
    // ShiftDownRpm; a shift cuts the engine's pull for ShiftSeconds and the needle drops. Pure: the vehicle steps it on
    // every side (the simulating side with the real throttle, the others with the throttle read off the speed).
    [Serializable]
    public class GearboxRules
    {
        public int Gears = 6;
        public float IdleRpm = 850f;
        public float RedlineRpm = 7000f;
        public float ShiftUpShare = 0.92f;     // of the redline
        public float ShiftDownShare = 0.42f;   // of the redline
        public float ShiftSeconds = 0.22f;
        public float ShiftPull = 0.2f;         // share of the engine's pull left while a shift is under way
        public float RevRisePerSecond = 9000f; // how fast the needle climbs (rpm/s) toward where the wheels put it
        public float RevFallPerSecond = 14000f;

        public GearboxRules Copy() => (GearboxRules)MemberwiseClone();
    }

    public struct GearboxState
    {
        public int Gear;          // 1..Gears forward, -1 reverse, 0 neutral (engine off)
        public float Rpm;
        public float ShiftLeft;   // seconds of the current shift still to run
        public int LastShift;     // +1 the last change went up, -1 down (the HUD and the camera feel it)
        public int ShiftCount;    // goes up by one per change, so a reader can tell a new shift from the old one

        public bool Shifting => ShiftLeft > 0f;
    }

    public static class Gearbox
    {
        // The top of gear `g` (1-based) as a share of the top speed.
        public static float GearTop(int g, int gears) => (float)Math.Pow(Math.Max(1, Math.Min(g, gears)) / (double)Math.Max(1, gears), 0.8);

        // Engine speed the wheels give in gear `g` at `speed` (m/s); never below idle.
        public static float WheelRpm(int g, float speed, float top, GearboxRules r)
        {
            if (top <= 0f) return r.IdleRpm;
            float span = GearTop(g, r.Gears) * top;
            float rpm = r.RedlineRpm * Math.Abs(speed) / Math.Max(0.1f, span);
            return Math.Max(r.IdleRpm, rpm);
        }

        // One step: picks the gear, moves the needle. `throttle` -1..1, `speed` signed forward m/s, `top` the top speed now.
        public static GearboxState Step(GearboxState s, float speed, float throttle, float top, bool engine, float dt, GearboxRules r)
        {
            if (s.ShiftLeft > 0f) s.ShiftLeft = Math.Max(0f, s.ShiftLeft - dt);
            if (!engine) { s.Gear = 0; s.Rpm = Approach(s.Rpm, 0f, r.RevFallPerSecond * 0.5f * dt); return s; }
            if (speed < -0.5f || (throttle < -0.05f && speed < 0.5f)) { s.Gear = -1; }
            else if (s.Gear <= 0) s.Gear = 1;

            float target;
            if (s.Gear < 0) target = Math.Max(r.IdleRpm, r.RedlineRpm * 0.5f * Math.Abs(speed) / Math.Max(1f, top * 0.25f));
            else
            {
                float wheel = WheelRpm(s.Gear, speed, top, r);
                if (!s.Shifting)
                {
                    if (s.Gear < r.Gears && wheel >= r.RedlineRpm * r.ShiftUpShare && throttle > 0.05f) Shift(ref s, +1, r);
                    else if (s.Gear > 1 && wheel < r.RedlineRpm * r.ShiftDownShare) Shift(ref s, -1, r);
                    wheel = WheelRpm(s.Gear, speed, top, r);
                }
                target = wheel;
                // Clutch slipping off the line: the engine revs ahead of the wheels in first.
                if (s.Gear == 1 && throttle > 0.05f) target = Math.Max(target, r.IdleRpm + (r.RedlineRpm * 0.45f - r.IdleRpm) * Math.Min(1f, throttle));
            }
            target = Math.Min(target, r.RedlineRpm);
            s.Rpm = s.Rpm < target ? Approach(s.Rpm, target, r.RevRisePerSecond * dt) : Approach(s.Rpm, target, r.RevFallPerSecond * dt);
            return s;
        }

        // Share of the engine's pull the gearbox passes on now (1 normally, ShiftPull mid-shift).
        public static float Pull(GearboxState s, GearboxRules r) => s.Shifting ? r.ShiftPull : 1f;

        static void Shift(ref GearboxState s, int dir, GearboxRules r)
        {
            s.Gear += dir;
            s.LastShift = dir;
            s.ShiftCount++;
            s.ShiftLeft = r.ShiftSeconds;
        }

        static float Approach(float from, float to, float step) => from < to ? Math.Min(to, from + step) : Math.Max(to, from - step);
    }
}
