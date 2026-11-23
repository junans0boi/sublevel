using System;

namespace Game.Core
{
    // One car model's engine and handling on top of the "Vehicles" block (준환: every car felt the same). Loaded from the
    // "VehicleModels" list of rules.json; a value of 0 keeps the type's number. Race cars ignore it (ADR-557: a mixed grid
    // stays fair), so only the battle royale's cars differ.
    [Serializable]
    public class VehicleHandling
    {
        public string Model;              // the prefab's name, e.g. "GoblinV12"
        public float MaxSpeedKmh;
        public float BoostMaxKmh;
        public float EngineAccel;
        public float TyreGrip;
        public float SteerMaxDeg;
        public float SteerAtTopSpeedDeg;
        public float OffRoadSpeedFactor;
        public float BrakeDecel;
        public float RedlineRpm;          // the gearbox's redline (ADR-558)

        public static VehicleHandling[] Current = Defaults();

        public static VehicleHandling For(string model)
        {
            if (string.IsNullOrEmpty(model) || Current == null) return null;
            foreach (var h in Current) if (h != null && h.Model == model) return h;
            return null;
        }

        public void ApplyTo(VehicleRules r)
        {
            if (MaxSpeedKmh > 0f) r.MaxSpeedKmh = MaxSpeedKmh;
            if (BoostMaxKmh > 0f) r.BoostMaxKmh = BoostMaxKmh;
            if (EngineAccel > 0f) r.EngineAccel = EngineAccel;
            if (TyreGrip > 0f) r.TyreGrip = TyreGrip;
            if (SteerMaxDeg > 0f) r.SteerMaxDeg = SteerMaxDeg;
            if (SteerAtTopSpeedDeg > 0f) r.SteerAtTopSpeedDeg = SteerAtTopSpeedDeg;
            if (OffRoadSpeedFactor > 0f) r.OffRoadSpeedFactor = OffRoadSpeedFactor;
            if (BrakeDecel > 0f) r.BrakeDecel = BrakeDecel;
            if (RedlineRpm > 0f && r.Gearbox != null)
            {
                var g = r.Gearbox.Copy(); // the type's gearbox is shared by every car
                g.RedlineRpm = RedlineRpm;
                r.Gearbox = g;
            }
        }

        // Same numbers as rules.json, for tests and for a missing list.
        public static VehicleHandling[] Defaults() => new[]
        {
            new VehicleHandling { Model = "FairheavenLT80", MaxSpeedKmh = 175f, BoostMaxKmh = 195f, EngineAccel = 7f, RedlineRpm = 7200f, TyreGrip = 1.2f, SteerMaxDeg = 34f },
            new VehicleHandling { Model = "FairheavenSW84", MaxSpeedKmh = 160f, BoostMaxKmh = 175f, EngineAccel = 6.2f, RedlineRpm = 6500f, TyreGrip = 1.15f },
            new VehicleHandling { Model = "GoblinV12", MaxSpeedKmh = 220f, BoostMaxKmh = 240f, EngineAccel = 9f, RedlineRpm = 8200f, TyreGrip = 1.35f, SteerAtTopSpeedDeg = 7f, BrakeDecel = 10f },
            new VehicleHandling { Model = "Shvan92", MaxSpeedKmh = 130f, BoostMaxKmh = 145f, EngineAccel = 5f, RedlineRpm = 5500f, TyreGrip = 1.0f, SteerMaxDeg = 30f, BrakeDecel = 8f },
            new VehicleHandling { Model = "LightbodyLifted85", MaxSpeedKmh = 150f, BoostMaxKmh = 165f, EngineAccel = 5.8f, RedlineRpm = 6000f, TyreGrip = 1.05f, OffRoadSpeedFactor = 1f },
        };
    }
}
