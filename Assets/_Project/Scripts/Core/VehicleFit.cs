using System;
using System.Reflection;

namespace Game.Core
{
    // What one vehicle model changes in its type's rules (#89): every car shares the "Vehicles" block of rules.json,
    // but a van is heavier than a sports car and a lifted pickup stands on bigger wheels. The prefab builder measures
    // the model and stores these numbers on the Vehicle; zero (or less) keeps the type's value.
    [Serializable]
    public struct VehicleFit
    {
        public float MassKg;
        public float WheelRadius;
        public float CentreOfMassHeight;
        public int SeatCount;   // the model's seats (the prefab's seat list); 0 keeps the type's
        public string Model;    // the prefab's name: its engine and handling row in VehicleHandling (ADR-557, race rows ADR-562)

        public bool Any => MassKg > 0f || WheelRadius > 0f || CentreOfMassHeight > 0f || SeatCount > 0 || !string.IsNullOrEmpty(Model);

        // A copy of `rules` with this model's numbers. The springs and dampers scale with the mass so every model
        // sits as deep on its suspension at rest as the type's numbers were tuned for.
        // `race`: the model's race-only row (ADR-562), whose mass also replaces the prefab's.
        public VehicleRules Apply(VehicleRules rules, bool race = false)
        {
            if (rules == null) return null;
            var tuned = Copy(rules);
            var row = race ? VehicleHandling.RaceFor(Model) : VehicleHandling.For(Model);
            // A race car's dial keeps its battle-royale redline unless its race row sets one (spec #505).
            if (race) new VehicleHandling { RedlineRpm = VehicleHandling.For(Model)?.RedlineRpm ?? 0f }.ApplyTo(tuned);
            row?.ApplyTo(tuned);
            float mass = row != null && row.MassKg > 0f ? row.MassKg : MassKg;
            if (mass > 0f)
            {
                float scale = mass / Math.Max(1f, rules.MassKg);
                tuned.MassKg = mass;
                tuned.SpringStrength = rules.SpringStrength * scale;
                tuned.SpringDamper = rules.SpringDamper * scale;
            }
            if (WheelRadius > 0f) tuned.WheelRadius = WheelRadius;
            if (CentreOfMassHeight > 0f) tuned.CentreOfMassHeight = CentreOfMassHeight;
            if (SeatCount > 0) tuned.SeatCount = SeatCount;
            return tuned;
        }

        static readonly FieldInfo[] Fields = typeof(VehicleRules).GetFields(BindingFlags.Public | BindingFlags.Instance);

        public static VehicleRules Copy(VehicleRules rules)
        {
            var copy = new VehicleRules();
            foreach (var f in Fields) f.SetValue(copy, f.GetValue(rules));
            return copy;
        }
    }
}
