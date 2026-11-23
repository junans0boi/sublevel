using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-557: each car model has its own engine and tyres; race cars keep the type's numbers.
    public class VehicleHandlingRulesTests
    {
        static readonly string[] Cars = { "FairheavenLT80", "FairheavenSW84", "LightbodyLifted85", "Shvan92", "GoblinV12" };

        [Test]
        public void EveryCarModel_HasItsOwnRow_AndTheyDiffer()
        {
            var rows = VehicleHandling.Defaults();
            foreach (var m in Cars) Assert.IsNotNull(rows.FirstOrDefault(r => r.Model == m), m);
            Assert.GreaterOrEqual(rows.Select(r => r.MaxSpeedKmh).Distinct().Count(), Cars.Length - 1, "top speeds differ (two cars may share one)");
            var sports = rows.First(r => r.Model == "GoblinV12");
            var van = rows.First(r => r.Model == "Shvan92");
            Assert.Greater(sports.MaxSpeedKmh, van.MaxSpeedKmh);
            Assert.Greater(sports.EngineAccel, van.EngineAccel);
            Assert.Greater(sports.TyreGrip, van.TyreGrip);
        }

        [Test]
        public void Fit_AppliesTheModelsRow_UnlessItIsARaceCar()
        {
            var type = new VehicleRules();
            var fit = new VehicleFit { Model = "GoblinV12" };
            var road = fit.Apply(type);
            var race = fit.Apply(type, handling: false);
            var row = VehicleHandling.For("GoblinV12");
            Assert.AreEqual(row.MaxSpeedKmh, road.MaxSpeedKmh);
            Assert.AreEqual(row.TyreGrip, road.TyreGrip);
            Assert.AreEqual(type.MaxSpeedKmh, race.MaxSpeedKmh, "race cars keep the type's top speed");
            Assert.AreEqual(type.TyreGrip, race.TyreGrip);
            Assert.AreEqual(type.SteerDegPerSecond, road.SteerDegPerSecond, "fields a row leaves at 0 keep the type's value");
        }

        [Test]
        public void UnknownModel_KeepsTheTypesNumbers()
        {
            var type = new VehicleRules();
            var tuned = new VehicleFit { Model = "NoSuchCar" }.Apply(type);
            Assert.AreEqual(type.MaxSpeedKmh, tuned.MaxSpeedKmh);
            Assert.AreEqual(type.EngineAccel, tuned.EngineAccel);
        }
    }
}
