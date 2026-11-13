using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    // The built vehicle prefabs against their models (#102 #103, ADR-077): seat count per model, the driver's grips
    // within arm's reach of the shoulders, every car seat (eye and cushion) inside the cabin box, and every tyre's
    // bottom on the ground. Reads the prefabs by their part names (VehicleBuilder), so no game code is needed.
    public class VehicleLayoutTests
    {
        const string Dir = "Assets/_Project/Prefabs/M1/Vehicles/";
        const float ShoulderDown = 0.24f, ShoulderSide = 0.15f, ArmReach = 0.42f;

        static readonly Dictionary<string, int> Seats = new Dictionary<string, int>
        {
            ["FairheavenLT80"] = 4, ["FairheavenSW84"] = 4, ["LightbodyLifted85"] = 2, ["Shvan92"] = 2, ["GoblinV12"] = 2,
            ["DirtBike"] = 2, ["RoadMotorcycle"] = 2,
        };

        static GameObject Load(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + name + ".prefab");
            Assert.IsNotNull(prefab, name);
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }

        static List<Transform> SeatsOf(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true).Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.name, @"^Seat\d$")).OrderBy(t => t.name).ToList();

        static IEnumerable<string> Names => Seats.Keys;

        [Test]
        public void EachModel_HasItsSeats_WithCushions([ValueSource(nameof(Names))] string name)
        {
            var go = Load(name);
            try
            {
                var seats = SeatsOf(go);
                Assert.AreEqual(Seats[name], seats.Count, name + " seats");
                foreach (var s in seats) Assert.IsNotNull(s.Find("Cushion"), name + " " + s.name + " cushion");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Driver_ReachesBothGrips([ValueSource(nameof(Names))] string name)
        {
            var go = Load(name);
            try
            {
                var eye = SeatsOf(go)[0];
                var root = go.transform;
                foreach (var (grip, side) in new[] { ("GripLeft", -1f), ("GripRight", 1f) })
                {
                    var g = go.GetComponentsInChildren<Transform>(true).First(t => t.name == grip);
                    var shoulder = eye.position + root.right * side * ShoulderSide - root.up * ShoulderDown;
                    float d = Vector3.Distance(shoulder, g.position);
                    Assert.LessOrEqual(d, ArmReach, $"{name} {grip}: {d:F2} m from the shoulder");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void CarSeats_AreInsideTheCabin([ValueSource(nameof(Names))] string name)
        {
            var go = Load(name);
            try
            {
                var cabin = go.transform.Find("Cabin");
                if (Seats[name] == 2 && cabin == null) Assert.Pass("motorcycle");
                Assert.IsNotNull(cabin, name + " cabin");
                var box = new Bounds(cabin.localPosition, cabin.localScale);
                foreach (var s in SeatsOf(go))
                {
                    Assert.IsTrue(box.Contains(go.transform.InverseTransformPoint(s.position)), $"{name} {s.name} eye {s.localPosition} outside {box}");
                    var c = s.Find("Cushion");
                    Assert.IsTrue(box.Contains(go.transform.InverseTransformPoint(c.position)), $"{name} {s.name} cushion outside {box}");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void EveryTyre_TouchesTheGround([ValueSource(nameof(Names))] string name)
        {
            var go = Load(name);
            try
            {
                var wheels = go.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Wheel_")).ToList();
                Assert.GreaterOrEqual(wheels.Count, 2);
                foreach (var w in wheels)
                {
                    var spin = w.Find("Spin").GetComponent<MeshFilter>().sharedMesh;
                    var t = w.Find("Spin");
                    float bottom = spin.vertices.Min(v => t.TransformPoint(v).y);
                    Assert.AreEqual(0f, bottom, 0.02f, $"{name} {w.name} bottom");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
