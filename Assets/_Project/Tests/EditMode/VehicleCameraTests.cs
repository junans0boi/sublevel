using Game.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    // ADR-222: the third-person chase distance comes from the model but stays in a fixed range, whatever yaw the vehicle was
    // parked at (the old world-box measure grew with the yaw: a vehicle at 45 deg got a camera ~2 m further out).
    public class VehicleCameraTests
    {
        const string Dir = "Assets/_Project/Prefabs/M1/Vehicles/";
        static readonly string[] Cars = { "FairheavenLT80", "FairheavenSW84", "LightbodyLifted85", "Shvan92", "GoblinV12" };
        static readonly string[] Bikes = { "DirtBike", "RoadMotorcycle" };

        static void Measure(string name, bool bike, float yaw, out float distance, out float pivotHeight, out float lengthZ)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Dir + name + ".prefab"));
            try
            {
                go.transform.SetPositionAndRotation(new Vector3(10f, 0f, 20f), Quaternion.Euler(0f, yaw, 0f));
                var b = VehicleCamera.LocalBounds(go.transform);
                VehicleCamera.Orbit(b, bike, out pivotHeight, out distance);
                lengthZ = b.size.z;
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ChaseDistance_InRange_AndTheSameAtAnyYaw([ValueSource(nameof(Cars))] string name)
        {
            float d0 = 0f, l0 = 0f;
            foreach (float yaw in new[] { 0f, 45f, 90f, 137f })
            {
                Measure(name, false, yaw, out float d, out float h, out float l);
                Assert.That(d, Is.InRange(VehicleCamera.CarMinDistance, VehicleCamera.CarMaxDistance), $"{name} yaw {yaw}: distance {d:F2}");
                Assert.That(h, Is.InRange(1.4f, 3.2f), $"{name} pivot height {h:F2} (just over the roof)");
                if (yaw == 0f) { d0 = d; l0 = l; }
                else Assert.AreEqual(l0, l, 0.1f, $"{name} length at yaw {yaw} (the same model, the same length)");
            }
            Debug.Log($"[camera] {name}: length {l0:F2} m, chase distance {d0:F2} m");
        }

        [Test]
        public void ChaseDistance_Motorcycle_InItsOwnRange([ValueSource(nameof(Bikes))] string name)
        {
            foreach (float yaw in new[] { 0f, 45f, 90f })
            {
                Measure(name, true, yaw, out float d, out float h, out _);
                Assert.That(d, Is.InRange(VehicleCamera.BikeMinDistance, VehicleCamera.BikeMaxDistance), $"{name} yaw {yaw}: distance {d:F2}");
                Assert.GreaterOrEqual(h, 1.5f, "over the rider's head");
            }
        }

        [Test]
        public void SpeedExtra_GrowsWithSpeed_AndNeverPastTwoMetres()
        {
            Assert.AreEqual(0f, VehicleCamera.SpeedExtra(0f), 1e-4f);
            Assert.Greater(VehicleCamera.SpeedExtra(60f), VehicleCamera.SpeedExtra(20f));
            Assert.AreEqual(2f, VehicleCamera.SpeedExtra(300f), 1e-4f);
            Assert.AreEqual(VehicleCamera.SpeedExtra(80f), VehicleCamera.SpeedExtra(-80f), 1e-4f, "reversing too");
            Assert.That(VehicleCamera.CarMaxDistance + VehicleCamera.SpeedExtraMeters, Is.LessThanOrEqualTo(9.5f), "farthest chase camera");
        }

        [Test]
        public void DrivingFov_IsWide() => Assert.That(VehicleCamera.DrivingFov, Is.InRange(75f, 80f));

        // The cause of the "drone" camera (ADR-222), as numbers: the old framing took the renderers' world boxes, so the length it
        // measured (and the distance = length + 2.2 with it) depended on the yaw the vehicle was in when the player got in.
        [Test]
        public void OldWorldBoxFraming_GrewWithYaw_Logged([ValueSource(nameof(Cars))] string name)
        {
            foreach (float yaw in new[] { 0f, 45f, 90f })
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Dir + name + ".prefab"));
                try
                {
                    go.transform.SetPositionAndRotation(new Vector3(10f, 0f, 20f), Quaternion.Euler(0f, yaw, 0f));
                    Bounds local = default; bool any = false;
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                    {
                        var b = r.bounds;
                        for (int i = 0; i < 8; i++)
                        {
                            var p = go.transform.InverseTransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                            if (!any) { local = new Bounds(p, Vector3.zero); any = true; } else local.Encapsulate(p);
                        }
                    }
                    Debug.Log($"[camera-old] {name} yaw {yaw}: framed length {local.size.z:F2} m -> distance {local.size.z + 2.2f:F2} m");
                }
                finally { Object.DestroyImmediate(go); }
            }
        }
    }
}
