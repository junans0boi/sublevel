using System;

namespace Game.Core
{
    // The garage's five 1..10 bars for a race row (ADR-562): 속도 top speed, 가속 engine, 코너링 steering at speed plus tyres,
    // 접지 tyres times off-road keep, 내구 mass (damage is off in a race, ADR-508, so 내구 = how hard the car is to shove).
    // Fixed scales, not relative to the other cars, so a retuned row moves only its own bars.
    public struct RaceStats
    {
        public int Speed, Accel, Cornering, Grip, Durability;

        public static RaceStats Of(VehicleHandling row, VehicleRules type = null)
        {
            var r = VehicleFit.Copy(type ?? VehicleRules.Current ?? new VehicleRules());
            row?.ApplyTo(r);
            float mass = row != null && row.MassKg > 0f ? row.MassKg : r.MassKg;
            return new RaceStats
            {
                Speed = Bar(r.MaxSpeedKmh, 73f, 100f),
                Accel = Bar(r.EngineAccel, 3.4f, 7f),
                Cornering = Bar(r.SteerMaxDeg + r.SteerAtTopSpeedDeg + 20f * (r.TyreGrip - 1f), 31f, 52f),
                Grip = Bar(r.TyreGrip * r.OffRoadSpeedFactor, 0.6f, 1.2f),
                Durability = Bar(mass, 800f, 2000f),
            };
        }

        // `at1` maps to bar 1 and `at10` to bar 10, linear between, clamped.
        static int Bar(float v, float at1, float at10) =>
            Math.Max(1, Math.Min(10, (int)Math.Round(1f + 9f * (v - at1) / (at10 - at1), MidpointRounding.AwayFromZero)));
    }
}
