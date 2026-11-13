#if UNITY_EDITOR
using System.Collections.Generic;
using Game.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    // The two-seat sports car and the motorcycles have two seats, the other cars four, on the built prefabs: the
    // seat list (Vehicle.SeatCount, which F, Ctrl+1..4 and the HUD go by) and the seat objects agree.
    public class VehicleSeatCountTests
    {
        static readonly Dictionary<string, int> Expected = new Dictionary<string, int>
        {
            ["GoblinV12"] = 2, ["DirtBike"] = 2, ["RoadMotorcycle"] = 2,
            ["FairheavenLT80"] = 4, ["FairheavenSW84"] = 4, ["LightbodyLifted85"] = 4, ["Shvan92"] = 4,
        };

        [Test]
        public void EveryVehiclePrefab_HasItsModelsSeatCount()
        {
            foreach (var pair in Expected)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/M1/Vehicles/" + pair.Key + ".prefab");
                Assert.IsNotNull(prefab, pair.Key);
                var vehicle = prefab.GetComponent<Vehicle>();
                Assert.AreEqual(pair.Value, vehicle.SeatCount, pair.Key + " seats");
                Assert.AreEqual(pair.Value, prefab.GetComponentsInChildren<VehicleSeat>(true).Length, pair.Key + " seat objects");
                for (int s = 0; s < 4; s++)
                    Assert.AreEqual(s < pair.Value, vehicle.Seat(s) != null, pair.Key + " seat " + s);
            }
        }
    }
}
#endif
