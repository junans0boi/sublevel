using System.Linq;
using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-558 (amended): each car model picks one of four cluster styles and an accent in data; the cluster sits either
    // side of the health bar and never covers the gear panel, hotbar, minimap, health bar or weapon panel.
    public class VehicleClusterTests
    {
        static readonly (string model, char style, bool compact)[] Expected =
        {
            ("GoblinV12", 'C', false), ("FairheavenLT80", 'D', false), ("FairheavenSW84", 'A', false),
            ("Shvan92", 'B', false), ("LightbodyLifted85", 'D', false),
            (VehicleHandling.MotorcycleRow, 'C', true),
        };

        static VehicleHandling[] JsonRows() =>
            JsonUtility.FromJson<GameRules>(System.IO.File.ReadAllText("Assets/_Project/Data/Resources/GameData/rules.json")).VehicleModels;

        [Test]
        public void EveryModel_HasItsStyleAndAccent_InTheDefaultsAndInRulesJson()
        {
            foreach (var rows in new[] { VehicleHandling.Defaults(), JsonRows() })
            foreach (var (model, style, compact) in Expected)
            {
                var row = rows.FirstOrDefault(r => r.Model == model);
                Assert.IsNotNull(row, model);
                Assert.AreEqual(style.ToString(), row.Cluster, model);
                Assert.AreEqual(compact, row.ClusterCompact, model);
                Assert.IsTrue(ColorUtility.TryParseHtmlString(row.ClusterAccent, out _), model + " accent " + row.ClusterAccent);
            }
            var goblin = VehicleHandling.Defaults().First(r => r.Model == "GoblinV12");
            ColorUtility.TryParseHtmlString(goblin.ClusterAccent, out var red);
            Assert.Greater(red.r, red.g + 0.4f, "the Goblin's accent is red");
        }

        [Test]
        public void ClusterFor_MotorcycleElseTheModelsOwn_RaceCarsToo()
        {
            Assert.IsNull(VehicleHandling.For("RaceCar"), "no race-car row: a race car reads its own model's cluster");
            Assert.IsTrue(VehicleHandling.ClusterFor("GoblinV12", true).ClusterCompact, "the motorcycle's is compact");
            Assert.AreEqual(VehicleHandling.MotorcycleRow, VehicleHandling.ClusterFor(null, true).Model);
            Assert.AreEqual("B", VehicleHandling.ClusterFor("Shvan92", false).Cluster);
            Assert.IsNull(VehicleHandling.ClusterFor("NoSuchCar", false));
        }

        [Test]
        public void DialScales_FollowTheModel()
        {
            Assert.AreEqual(9000f, VehicleCluster.RpmMax(8200f));
            Assert.AreEqual(6000f, VehicleCluster.RpmMax(5500f));
            Assert.AreEqual(240f, VehicleCluster.SpeedMax(240f));
            Assert.AreEqual(200f, VehicleCluster.SpeedMax(195f));
        }

        // 1920 = 1920x1080 and 2560x1440 (the canvas is always 1080 high), 1728 = 16:10, 2520 = 21:9.
        [Test]
        public void Gauges_NeverCoverTheHud([Values(1920f, 1728f, 2520f)] float w, [Values('A', 'B', 'C', 'D')] char style, [Values(false, true)] bool compact)
        {
            ClusterLayout.Place(w, style, compact, out var tach, out var speed);
            var lights = ClusterLayout.Lights(w);
            var hud = new[]
            {
                ("gear panel", ClusterLayout.GearPanel), ("health bar", ClusterLayout.Health(w)), ("weapon panel", ClusterLayout.WeaponPanel(w)),
                ("hotbar", ClusterLayout.Hotbar(w)), ("minimap", ClusterLayout.MinimapRect(w)),
            };
            foreach (var (name, gauge) in new[] { ("tachometer", tach), ("speedometer", speed), ("shift lights", lights) })
            {
                foreach (var (what, rect) in hud) Assert.IsFalse(gauge.Overlaps(rect), $"{name} {gauge} covers the {what} {rect} at {w}");
                Assert.IsTrue(gauge.xMin >= 0f && gauge.xMax <= w && gauge.yMin >= 0f && gauge.yMax <= 1080f, $"{name} on screen at {w}");
            }
            Assert.IsFalse(tach.Overlaps(speed));
            Assert.Less(tach.xMax, ClusterLayout.Health(w).xMin, "the tachometer is left of the health bar");
            Assert.Greater(speed.xMin, ClusterLayout.Health(w).xMax, "the speedometer is right of it");
            float full = ClusterLayout.TachSize(style).x * (compact ? ClusterLayout.CompactScale : 1f);
            Assert.GreaterOrEqual(tach.width / full, 0.65f, "still readable");
            if (w >= 1920f) Assert.AreEqual(ClusterLayout.Margin, speed.yMin, "on 16:9 both sit on the bottom line");
        }

        [Test]
        public void EveryStyle_Builds_AndPlacesItsGauges([Values('A', 'B', 'C', 'D')] char style)
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var row = new VehicleHandling { Model = "Test", Cluster = style.ToString(), ClusterAccent = "#4DA3FF" };
                var c = new VehicleCluster(canvas.transform, row, 7200f, 0.92f, 200f);
                Assert.AreEqual(style, c.Style);
                c.Place(1920f);
                ClusterLayout.Place(1920f, style, false, out var tach, out var speed);
                Assert.AreEqual(tach.position, c.Tach.anchoredPosition);
                Assert.AreEqual(speed.position, c.Speed.anchoredPosition);
                Assert.IsTrue(c.BuiltFor(row, 7200f, 200f));
                Assert.IsFalse(c.BuiltFor(row, 8200f, 200f), "a new redline re-draws the dial");
            }
            finally { Object.DestroyImmediate(canvas); }
        }
    }
}
