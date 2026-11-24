using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-557: each car model has its own engine and tyres. ADR-562: race cars take their model's race-only row instead.
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
        public void Fit_AppliesTheModelsRow_AndARaceCarItsRaceRow()
        {
            var type = new VehicleRules();
            foreach (var m in Cars)
            {
                var fit = new VehicleFit { Model = m, MassKg = 1500f };
                var road = fit.Apply(type);
                var race = fit.Apply(type, race: true);
                var row = VehicleHandling.For(m);
                var rr = VehicleHandling.RaceFor(m);
                Assert.IsNotNull(rr, m + " has a race row");
                Assert.AreEqual(row.MaxSpeedKmh, road.MaxSpeedKmh, m);
                Assert.AreEqual(1500f, road.MassKg, m + ": a battle-royale car keeps the prefab's mass");
                Assert.AreEqual(rr.MaxSpeedKmh, race.MaxSpeedKmh, m);
                Assert.AreEqual(rr.BoostMaxKmh, race.BoostMaxKmh, m);
                Assert.AreEqual(rr.EngineAccel, race.EngineAccel, m);
                Assert.AreEqual(rr.TyreGrip, race.TyreGrip, m);
                Assert.AreEqual(rr.SteerMaxDeg, race.SteerMaxDeg, m);
                Assert.AreEqual(rr.SteerAtTopSpeedDeg, race.SteerAtTopSpeedDeg, m);
                Assert.AreEqual(rr.BrakeDecel, race.BrakeDecel, m);
                Assert.AreEqual(rr.OffRoadSpeedFactor, race.OffRoadSpeedFactor, m);
                Assert.AreEqual(rr.MassKg, race.MassKg, m + ": the race row's mass (its 내구)");
                Assert.AreEqual(type.SpringStrength * rr.MassKg / type.MassKg, race.SpringStrength, 0.1f, m + ": springs follow the mass");
                Assert.AreEqual(road.Gearbox.RedlineRpm, race.Gearbox.RedlineRpm, m + ": the dial keeps the battle-royale redline");
            }
            Assert.AreEqual(type.SteerDegPerSecond, new VehicleFit { Model = "GoblinV12" }.Apply(type).SteerDegPerSecond, "fields a row leaves at 0 keep the type's value");
        }

        [Test]
        public void RaceRows_StayNearTheTypesNumbers_AndDiffer()
        {
            var type = new VehicleRules { MaxSpeedKmh = 90f };
            var rows = VehicleHandling.RaceDefaults();
            Assert.AreEqual(Cars.Length, rows.Length);
            foreach (var r in rows) Assert.That(r.MaxSpeedKmh, Is.InRange(type.MaxSpeedKmh * 0.9f, type.MaxSpeedKmh * 1.12f), r.Model + " within about ±10 %");
            Assert.AreEqual(Cars.Length, rows.Select(r => r.MaxSpeedKmh).Distinct().Count());
        }

        [Test]
        public void MissingRaceList_FallsBackToTheDefaults()
        {
            var saved = VehicleHandling.Race;
            try
            {
                VehicleHandling.Race = null;
                Assert.IsNull(VehicleHandling.RaceFor("GoblinV12"));
                VehicleHandling.Race = VehicleHandling.RaceOrDefaults(null);
                Assert.AreEqual(100f, VehicleHandling.RaceFor("GoblinV12").MaxSpeedKmh);
                Assert.AreEqual(5, VehicleHandling.RaceOrDefaults(new VehicleHandling[0]).Length);
                var custom = new[] { new VehicleHandling { Model = "GoblinV12", MaxSpeedKmh = 99f } };
                Assert.AreSame(custom, VehicleHandling.RaceOrDefaults(custom));
            }
            finally { VehicleHandling.Race = saved; }
        }

        [Test]
        public void RulesFile_SeedsTheSameRaceRowsAsTheDefaults()
        {
            var rules = UnityEngine.JsonUtility.FromJson<GameRules>(System.IO.File.ReadAllText("Assets/_Project/Data/Resources/GameData/rules.json"));
            var defaults = VehicleHandling.RaceDefaults();
            Assert.AreEqual(defaults.Length, rules.RaceVehicleModels.Length);
            foreach (var d in defaults)
            {
                var r = rules.RaceVehicleModels.First(x => x.Model == d.Model);
                Assert.AreEqual(UnityEngine.JsonUtility.ToJson(d), UnityEngine.JsonUtility.ToJson(r), d.Model);
            }
        }

        [Test]
        public void RaceStats_OrderTheCarsLikeTheGarageMockup()
        {
            var s = Cars.ToDictionary(m => m, m => RaceStats.Of(VehicleHandling.RaceFor(m)));
            foreach (var kv in s)
                foreach (var bar in new[] { kv.Value.Speed, kv.Value.Accel, kv.Value.Cornering, kv.Value.Grip, kv.Value.Durability })
                    Assert.That(bar, Is.InRange(1, 10), kv.Key);
            foreach (var m in Cars.Where(c => c != "GoblinV12")) Assert.Greater(s["GoblinV12"].Speed, s[m].Speed, "GoblinV12 fastest vs " + m);
            foreach (var m in Cars.Where(c => c != "Shvan92")) Assert.Greater(s["Shvan92"].Durability, s[m].Durability, "Shvan92 most durable vs " + m);
            foreach (var m in Cars.Where(c => c != "LightbodyLifted85")) Assert.Greater(s["LightbodyLifted85"].Grip, s[m].Grip, "Lifted85 best grip vs " + m);
            foreach (var m in Cars.Where(c => c != "FairheavenSW84")) Assert.Greater(s["FairheavenSW84"].Cornering, s[m].Cornering, "SW84 best cornering vs " + m);
            Assert.Less(s["Shvan92"].Speed, s["FairheavenLT80"].Speed, "the van is slowest of the road cars");
            Assert.AreEqual(s["GoblinV12"].Accel, Cars.Max(m => s[m].Accel), "GoblinV12 quickest off the line");
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
