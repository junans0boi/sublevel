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
        // The driver's instrument cluster (ADR-558 amended): "A" twin round, "B" half-moon, "C" digital sport, "D" classic
        // analog; its accent colour ("#RRGGBB"); compact = a smaller cluster (the motorcycle).
        public string Cluster;
        public string ClusterAccent;
        public bool ClusterCompact;

        // A row that only carries a cluster: every motorcycle. Race cars read their own model's row (cluster only, ADR-558a).
        public const string MotorcycleRow = "Motorcycle";

        public static VehicleHandling[] Current = Defaults();

        public static VehicleHandling For(string model)
        {
            if (string.IsNullOrEmpty(model) || Current == null) return null;
            foreach (var h in Current) if (h != null && h.Model == model) return h;
            return null;
        }

        // The cluster row for a vehicle: the motorcycle's, else its own model's (race cars too); null when none has one.
        public static VehicleHandling ClusterFor(string model, bool motorcycle)
        {
            var row = For(motorcycle ? MotorcycleRow : model);
            return row != null && !string.IsNullOrEmpty(row.Cluster) ? row : null;
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
            new VehicleHandling { Model = "FairheavenLT80", MaxSpeedKmh = 175f, BoostMaxKmh = 195f, EngineAccel = 7f, RedlineRpm = 7200f, TyreGrip = 1.2f, SteerMaxDeg = 34f, Cluster = "D", ClusterAccent = "#F2F2F2" },
            new VehicleHandling { Model = "FairheavenSW84", MaxSpeedKmh = 160f, BoostMaxKmh = 175f, EngineAccel = 6.2f, RedlineRpm = 6500f, TyreGrip = 1.15f, Cluster = "A", ClusterAccent = "#4DA3FF" },
            new VehicleHandling { Model = "GoblinV12", MaxSpeedKmh = 220f, BoostMaxKmh = 240f, EngineAccel = 9f, RedlineRpm = 8200f, TyreGrip = 1.35f, SteerAtTopSpeedDeg = 7f, BrakeDecel = 10f, Cluster = "C", ClusterAccent = "#FF3B30" },
            new VehicleHandling { Model = "Shvan92", MaxSpeedKmh = 130f, BoostMaxKmh = 145f, EngineAccel = 5f, RedlineRpm = 5500f, TyreGrip = 1.0f, SteerMaxDeg = 30f, BrakeDecel = 8f, Cluster = "B", ClusterAccent = "#4CD964" },
            new VehicleHandling { Model = "LightbodyLifted85", MaxSpeedKmh = 150f, BoostMaxKmh = 165f, EngineAccel = 5.8f, RedlineRpm = 6000f, TyreGrip = 1.05f, OffRoadSpeedFactor = 1f, Cluster = "D", ClusterAccent = "#FFB020" },
            new VehicleHandling { Model = MotorcycleRow, Cluster = "C", ClusterAccent = "#FFD60A", ClusterCompact = true },
        };
    }
}
