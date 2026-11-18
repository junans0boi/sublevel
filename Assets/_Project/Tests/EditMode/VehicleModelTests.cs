using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #89: per-model vehicle numbers over the type's rules, and the credits list against the asset ledger.
    public class VehicleModelTests
    {
        [Test]
        public void Fit_NothingSet_KeepsTheTypeRules()
        {
            var rules = new VehicleRules();
            var fit = new VehicleFit();
            Assert.IsFalse(fit.Any);
            var tuned = fit.Apply(rules);
            Assert.AreEqual(rules.MassKg, tuned.MassKg);
            Assert.AreEqual(rules.WheelRadius, tuned.WheelRadius);
            Assert.AreEqual(rules.SpringStrength, tuned.SpringStrength);
        }

        [Test]
        public void Fit_HeavierModel_SpringsScaleSoItSitsAsDeep()
        {
            var rules = new VehicleRules();
            var tuned = new VehicleFit { MassKg = rules.MassKg * 1.5f, WheelRadius = 0.36f, CentreOfMassHeight = 0.6f }.Apply(rules);
            Assert.AreEqual(rules.MassKg * 1.5f, tuned.MassKg, 1e-3f);
            Assert.AreEqual(0.36f, tuned.WheelRadius, 1e-6f);
            Assert.AreEqual(0.6f, tuned.CentreOfMassHeight, 1e-6f);
            Assert.AreEqual(Vehicles.RestLength(rules), Vehicles.RestLength(tuned), 1e-5f, "same sag at rest");
            Assert.AreEqual(rules.SpringDamper * 1.5f, tuned.SpringDamper, 1e-2f);
        }

        // #350: the model's mass (DirtBike 190) is what the physics uses; the type's 230 is only the default of a model without one.
        [Test]
        public void Fit_DirtBike190_WinsOverTheMotorcycleTypeMass()
        {
            var type = VehicleRules.MotorcycleDefaults();
            Assert.AreEqual(230f, type.MassKg);
            var tuned = new VehicleFit { MassKg = 190f }.Apply(type);
            Assert.AreEqual(190f, tuned.MassKg);
            Assert.AreEqual(Vehicles.RestLength(type), Vehicles.RestLength(tuned), 1e-5f, "springs scaled with the mass");
        }

        [Test]
        public void Fit_LeavesTheSharedRulesUntouched()
        {
            var rules = new VehicleRules();
            float before = rules.MassKg;
            new VehicleFit { MassKg = 3000f }.Apply(rules);
            Assert.AreEqual(before, rules.MassKg);
            Assert.AreEqual(rules.ExitLethalKmh, VehicleFit.Copy(rules).ExitLethalKmh, "every other field copied");
        }

        // Every ledger row that needs credit has an entry on the credits screen with the same source link.
        [Test]
        public void Credits_CoverEveryLedgerRowThatNeedsCredit()
        {
            var ledger = File.ReadAllText("docs/ASSET_LEDGER.md");
            int needing = 0;
            foreach (var line in ledger.Split('\n'))
            {
                if (!line.StartsWith("| ")) continue;
                var cells = line.Split('|').Select(c => c.Trim()).ToArray();
                if (cells.Length < 8 || !Regex.IsMatch(cells[1], @"^[A-Z]-\d{4}$")) continue;
                if (!cells[6].StartsWith("**예**") && !cells[6].StartsWith("예")) continue;
                needing++;
                string url = cells[4];
                Assert.IsTrue(Credits.Entries.Any(e => e.Url == url), $"{cells[1]} ({url}) needs a credits entry");
            }
            Assert.GreaterOrEqual(needing, 8, "the eight CC BY vehicle models");
            foreach (var e in Credits.Entries)
            {
                Assert.IsFalse(string.IsNullOrEmpty(e.Title) || string.IsNullOrEmpty(e.Author) || string.IsNullOrEmpty(e.Licence), e.Url);
                StringAssert.Contains(e.Author, Credits.Line(e));
            }
        }
    }
}
