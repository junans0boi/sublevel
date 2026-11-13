using System.Collections.Generic;
using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests
{
    // G5 / ADR-093: the order of the pickup list and how the selection moves (PickupList.Sync / Step).
    public class PickupListTests
    {
        sealed class Item
        {
            public float Dist;
            public Item(float d) { Dist = d; }
        }

        static float Sqr(Item i) => i.Dist * i.Dist;

        [Test]
        public void FreshList_IsNearestFirst_AndSelectsTheNearest()
        {
            var a = new Item(2f); var b = new Item(0.5f); var c = new Item(1f);
            var rows = new List<Item>();
            int selected = -1;
            PickupList.Sync(rows, new List<Item> { a, b, c }, Sqr, ref selected);
            CollectionAssert.AreEqual(new[] { b, c, a }, rows);
            Assert.AreEqual(0, selected);
        }

        [Test]
        public void SameItems_KeepTheirOrder_WhenDistancesChange()
        {
            var a = new Item(1f); var b = new Item(2f);
            var rows = new List<Item>();
            int selected = -1;
            PickupList.Sync(rows, new List<Item> { a, b }, Sqr, ref selected);
            a.Dist = 3f; // walked past it
            PickupList.Sync(rows, new List<Item> { b, a }, Sqr, ref selected);
            CollectionAssert.AreEqual(new[] { a, b }, rows, "no rows jumping while the same pickups are in reach");
        }

        [Test]
        public void SelectionFollowsTheItem_WhenTheListChanges()
        {
            var a = new Item(1f); var b = new Item(2f); var c = new Item(3f);
            var rows = new List<Item>();
            int selected = -1;
            PickupList.Sync(rows, new List<Item> { a, b, c }, Sqr, ref selected);
            selected = 2; // c
            var d = new Item(0.1f);
            PickupList.Sync(rows, new List<Item> { a, b, c, d }, Sqr, ref selected);
            Assert.AreSame(c, rows[selected]);
        }

        [Test]
        public void TakenItem_SelectsTheRowThatTookItsPlace_AndEmptyIsMinusOne()
        {
            var a = new Item(1f); var b = new Item(2f); var c = new Item(3f);
            var rows = new List<Item>();
            int selected = -1;
            PickupList.Sync(rows, new List<Item> { a, b, c }, Sqr, ref selected);
            selected = 1; // b taken
            PickupList.Sync(rows, new List<Item> { a, c }, Sqr, ref selected);
            Assert.AreSame(c, rows[selected]);
            selected = 1; // c taken: clamps to the last row
            PickupList.Sync(rows, new List<Item> { a }, Sqr, ref selected);
            Assert.AreEqual(0, selected);
            PickupList.Sync(rows, new List<Item>(), Sqr, ref selected);
            Assert.AreEqual(-1, selected);
        }

        [Test]
        public void Wheel_DownMovesDown_UpMovesUp_AndStopsAtTheEnds()
        {
            Assert.AreEqual(1, PickupList.Step(0, 3, -1));
            Assert.AreEqual(2, PickupList.Step(2, 3, -1));
            Assert.AreEqual(1, PickupList.Step(2, 3, 1));
            Assert.AreEqual(0, PickupList.Step(0, 3, 1));
            Assert.AreEqual(-1, PickupList.Step(0, 0, -1));
        }
    }
}
