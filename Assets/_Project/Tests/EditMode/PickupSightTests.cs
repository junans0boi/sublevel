using Game.Net;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-276: the world side of the pickup rules (storey + line of sight), on a small two-storey box house.
    public class PickupSightTests
    {
        GameObject _root;

        [SetUp]
        public void Build()
        {
            _root = new GameObject("PickupSightWorld");
            Box("floor0", new Vector3(0, -0.1f, 0), new Vector3(20, 0.2f, 20));
            Box("floor1", new Vector3(0, 3.0f - 0.1f, 0), new Vector3(20, 0.2f, 20)); // the next storey, top at 3.0
            Box("wall", new Vector3(0, 1.5f, 2.5f), new Vector3(6, 3f, 0.2f));        // a wall across z = 2.5
            Physics.SyncTransforms();
        }

        [TearDown]
        public void Clean() => Object.DestroyImmediate(_root);

        void Box(string name, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = centre;
            go.AddComponent<BoxCollider>().size = size;
        }

        Transform Item(Vector3 atPivot)
        {
            var go = new GameObject("loot");
            go.transform.SetParent(_root.transform, false);
            go.transform.position = atPivot; // pivot = 0.3 above the floor it lies on
            return go.transform;
        }

        static readonly Vector3 Feet = new Vector3(0f, 0f, 0f), Eye = new Vector3(0f, 1.6f, 0f);

        [Test]
        public void Pickup_in_the_same_room_is_reachable()
        {
            Assert.IsTrue(PickupSight.Reachable(Feet, Eye, Item(new Vector3(0.5f, 0.3f, 1.5f)), null, 0f));
        }

        [Test]
        public void Pickup_straight_above_on_the_next_storey_is_not_reachable()
        {
            Assert.IsFalse(PickupSight.Reachable(Feet, Eye, Item(new Vector3(0.2f, 3.3f, 0.4f)), null, 0f), "upstairs, 3 m up, 0.4 m away");
        }

        [Test]
        public void Pickup_behind_a_wall_is_not_reachable_even_within_3_metres()
        {
            Assert.IsFalse(PickupSight.Reachable(Feet, Eye, Item(new Vector3(0f, 0.3f, 3.0f)), null, 0f));
        }

        [Test]
        public void Pickup_beside_the_wall_with_a_clear_line_is_reachable()
        {
            Assert.IsTrue(PickupSight.Reachable(Feet, Eye, Item(new Vector3(-2.2f, 0.3f, 2.0f)), null, 0f), "the wall is 6 m wide; this one is on this side of it");
            Assert.IsTrue(PickupSight.Reachable(Feet, Eye, Item(new Vector3(0f, 0.3f, 2.1f)), null, 0f), "right in front of the wall");
        }

        [Test]
        public void Pickup_over_the_reach_is_not_reachable_but_the_lag_slack_allows_a_little_more()
        {
            var far = Item(new Vector3(-2.9f, 0.3f, 0.9f)); // about 3.2 m
            Assert.IsFalse(PickupSight.Reachable(Feet, Eye, far, null, 0f));
            Assert.IsTrue(PickupSight.Reachable(Feet, Eye, far, null, 1.5f));
        }

        [Test]
        public void Pickup_on_a_table_in_the_room_is_reachable()
        {
            Box("table", new Vector3(0.5f, 0.45f, 1.5f), new Vector3(1.2f, 0.9f, 0.8f));
            Physics.SyncTransforms();
            Assert.IsTrue(PickupSight.Reachable(Feet, Eye, Item(new Vector3(0.5f, 1.2f, 1.5f)), null, 0f));
        }

        [Test]
        public void Pickup_hidden_behind_a_tall_cabinet_is_not_reachable()
        {
            Box("cabinet", new Vector3(0f, 1f, 1.0f), new Vector3(2f, 2f, 0.5f));
            Physics.SyncTransforms();
            Assert.IsFalse(PickupSight.Reachable(Feet, Eye, Item(new Vector3(0f, 0.3f, 1.8f)), null, 0f));
        }
    }
}
