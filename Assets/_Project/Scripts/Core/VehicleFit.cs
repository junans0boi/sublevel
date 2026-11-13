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

        public bool Any => MassKg > 0f || WheelRadius > 0f || CentreOfMassHeight > 0f || SeatCount > 0;

        // A copy of `rules` with this model's numbers. The springs and dampers scale with the mass so every model
        // sits as deep on its suspension at rest as the type's numbers were tuned for.
        public VehicleRules Apply(VehicleRules rules)
        {
            if (rules == null) return null;
            var tuned = Copy(rules);
            if (MassKg > 0f)
            {
                float scale = MassKg / Math.Max(1f, rules.MassKg);
                tuned.MassKg = MassKg;
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
