using System;

namespace Game.Core
{
    // Vehicle fuel and the jerrycan (ADR-067, ADR-075). The tank is a share 0..1. A running engine burns a little at
    // idle and more with the throttle (Shift boost burns more); an empty tank stops the engine and the vehicle rolls
    // on. A jerrycan carried in the bag puts a share of a tank back into the vehicle you stand next to or sit in,
    // after a few seconds that anything (damage, a shot, a weapon switch, walking off) interrupts.
    [Serializable]
    public class FuelRules
    {
        // The rules in use. Defaults until the data loader replaces them, so everything runs without a data file.
        public static FuelRules Current = new FuelRules();

        public const LootKind JerrycanLootKind = LootKind.Jerrycan;
        public const string JerrycanName = "제리캔";

        // Share of a tank per second with the engine running.
        public float IdlePerSecond = 1f / 1500f;     // standing still: 25 minutes
        public float ThrottlePerSecond = 1f / 300f;  // full throttle on top of idle: about 5 minutes
        public float BoostFactor = 2f;               // Shift: the throttle part burns this many times faster
        public float MotorcycleFactor = 0.7f;        // a motorcycle's tank lasts longer
        // A vehicle starts the match with a share between these (seeded per vehicle).
        public float SpawnMin = 0.35f;
        public float SpawnMax = 0.9f;
        // Below this the HUD shows the gauge in red.
        public float LowShare = 0.15f;

        // Jerrycan.
        public float JerrycanSeconds = 5f;           // ADR-067: five seconds of pouring
        public float JerrycanAmount = 0.6f;          // share of a tank one can puts back
        public float RefuelRangeMeters = 1.6f;       // from the vehicle's body (closest point), on foot
        public float RefuelKeepMeters = 2.5f;        // walking further than this cancels
        public float RefuelMaxKmh = 10f;             // the vehicle must stay (nearly) still
        public float FullShare = 0.995f;             // a tank this full takes no more

        // Horn (ADR-075): bots hear it like a noise this far.
        public float HornHearingRadius = 60f;
        public float HornNoiseInterval = 1f;
    }

    public static class Fuel
    {
        // The tank after `dt` seconds. Only a running engine burns; never below 0.
        public static float Burn(float fuel, bool engineOn, float throttle, bool boost, bool motorcycle, float dt, FuelRules rules)
        {
            if (!engineOn || dt <= 0f || fuel <= 0f) return Math.Max(0f, fuel);
            float load = Math.Min(1f, Math.Abs(throttle));
            float drive = rules.ThrottlePerSecond * load * (boost && throttle > 0f ? rules.BoostFactor : 1f);
            float rate = (rules.IdlePerSecond + drive) * (motorcycle ? rules.MotorcycleFactor : 1f);
            return Math.Max(0f, fuel - rate * dt);
        }

        // An empty tank or a wrecked vehicle stops the engine; it rolls on without pull.
        public static bool EngineCanRun(float fuel, bool disabled) => fuel > 0f && !disabled;

        // Seconds a full tank lasts at this throttle (for the ADR and tests).
        public static float SecondsPerTank(float throttle, bool boost, bool motorcycle, FuelRules rules)
        {
            float rate = (rules.IdlePerSecond + rules.ThrottlePerSecond * Math.Min(1f, Math.Abs(throttle)) * (boost && throttle > 0f ? rules.BoostFactor : 1f))
                         * (motorcycle ? rules.MotorcycleFactor : 1f);
            return rate > 0f ? 1f / rate : float.PositiveInfinity;
        }

        // The tank after one jerrycan: never over full.
        public static float Refill(float fuel, FuelRules rules) => Math.Min(1f, Math.Max(0f, fuel) + rules.JerrycanAmount);

        // Whether a jerrycan could go in now: not full, the vehicle near enough and slow enough. Seated in the
        // vehicle counts as next to it.
        public static bool CanRefuel(float fuel, bool seatedInIt, float distanceToBody, float speedKmh, FuelRules rules)
        {
            if (fuel >= rules.FullShare) return false;
            if (!seatedInIt && distanceToBody > rules.RefuelRangeMeters) return false;
            return speedKmh <= rules.RefuelMaxKmh;
        }

        // Whether a pour under way must stop (the user walked off or the vehicle drove away).
        public static bool RefuelBroken(bool seatedInIt, float distanceToBody, float speedKmh, FuelRules rules) =>
            (!seatedInIt && distanceToBody > rules.RefuelKeepMeters) || speedKmh > rules.RefuelMaxKmh;

        // The share a vehicle starts the match with, from a seeded generator.
        public static float SpawnShare(Random rng, FuelRules rules)
        {
            double t = rng != null ? rng.NextDouble() : 1.0;
            float lo = Math.Min(rules.SpawnMin, rules.SpawnMax), hi = Math.Max(rules.SpawnMin, rules.SpawnMax);
            return Math.Max(0f, Math.Min(1f, lo + (float)t * (hi - lo)));
        }

        // Whole percent for the gauge; anything left shows as at least 1.
        public static int Percent(float fuel)
        {
            if (fuel <= 0f) return 0;
            int p = (int)Math.Round(fuel * 100f);
            return Math.Max(1, Math.Min(100, p));
        }
    }

    // The engine's note (ADR-075): a made-up gearbox turns road speed into an engine speed 0..1 that rises through
    // each gear and drops at a shift, so the pitch climbs and falls back like a real engine. Pure, for tests.
    public static class EngineNote
    {
        public const float IdleRpm = 0.18f;

        // 0..1 engine speed. `top` = the vehicle's top speed (m/s); `gears` = how many gears share it.
        public static float Rpm01(float speed, float top, int gears, float throttle)
        {
            speed = Math.Abs(speed);
            if (top <= 0f || gears <= 0) return IdleRpm;
            float t = Math.Min(1f, speed / top);
            // Gears get longer: gear i covers t up to ((i+1)/gears)^0.8.
            float lo = 0f;
            for (int i = 0; i < gears; i++)
            {
                float hi = (float)Math.Pow((i + 1) / (double)gears, 0.8);
                if (t <= hi || i == gears - 1)
                {
                    float inGear = hi > lo ? (t - lo) / (hi - lo) : 1f;
                    // First gear starts at idle; higher gears drop to about 45% after a shift.
                    float from = i == 0 ? IdleRpm : 0.45f;
                    float rpm = from + (1f - from) * Math.Max(0f, Math.Min(1f, inGear));
                    // Revving against the throttle at a standstill.
                    if (speed < 0.5f && throttle > 0f) rpm = Math.Max(rpm, IdleRpm + 0.5f * Math.Min(1f, throttle));
                    return rpm;
                }
                lo = hi;
            }
            return 1f;
        }

        // Which gear (0-based) Rpm01 is in at this speed; the audio feels a shift when it changes (#203).
        public static int Gear(float speed, float top, int gears)
        {
            if (top <= 0f || gears <= 0) return 0;
            float t = Math.Min(1f, Math.Abs(speed) / top);
            for (int i = 0; i < gears - 1; i++)
                if (t <= (float)Math.Pow((i + 1) / (double)gears, 0.8)) return i;
            return gears - 1;
        }

        // Playback pitch of the engine loop for an engine speed.
        public static float Pitch(float rpm01, bool motorcycle) => (motorcycle ? 0.8f : 0.6f) + rpm01 * (motorcycle ? 1.4f : 1.1f);

        // Loudness 0..1: idle hums, a pulling engine is louder.
        public static float Volume(float rpm01, float load01) => 0.35f + 0.35f * rpm01 + 0.3f * Math.Max(0f, Math.Min(1f, load01));
    }
}
