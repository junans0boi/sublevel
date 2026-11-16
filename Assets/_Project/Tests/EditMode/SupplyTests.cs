using System.Collections.Generic;
using System.IO;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // The facility supply crate (#144, ADR-113): which lockdown stages send one, where it comes down, what is in it.
    public class SupplyTests
    {
        static readonly string DataFolder = Path.Combine(Application.dataPath, "_Project/Data/Resources/GameData/");

        static LootTable Table() => JsonUtility.FromJson<LootTable>(File.ReadAllText(DataFolder + "supply.json"));

        [Test]
        public void Stages_FollowFirstStageAndInterval()
        {
            var rules = new SupplyRules { FirstStage = 2, EveryStages = 2 };
            var drops = new List<int>();
            for (int stage = 0; stage <= 7; stage++) if (SupplyPlan.DropAt(stage, rules)) drops.Add(stage);
            CollectionAssert.AreEqual(new[] { 2, 4, 6 }, drops);
            Assert.IsTrue(SupplyPlan.DropAt(3, new SupplyRules { FirstStage = 1, EveryStages = 1 }), "every stage");
        }

        [Test]
        public void ARegion_IsPickedOnlyAmongTheSafeOnes()
        {
            var states = new List<ZoneState> { ZoneState.Closed, ZoneState.Safe, ZoneState.Closed, ZoneState.Safe, ZoneState.Warning };
            for (int seed = 0; seed < 50; seed++)
            {
                int r = SupplyPlan.PickRegion(states, new System.Random(seed));
                Assert.IsTrue(r == 1 || r == 3, "region " + r);
            }
            Assert.AreEqual(-1, SupplyPlan.PickRegion(new List<ZoneState> { ZoneState.Closed, ZoneState.Closed }, new System.Random(1)));
        }

        [Test]
        public void SupplyData_IsHighTier()
        {
            foreach (var e in Table().Entries)
            {
                if (e.Kind == LootKind.Helmet || e.Kind == LootKind.Vest) Assert.GreaterOrEqual(e.Tier, 2, "armour in a crate is tier 2+");
                if (e.Kind == LootKind.Backpack) Assert.AreEqual(3, e.Tier);
                if (e.Kind == LootKind.Weapon) Assert.AreNotEqual(1, e.WeaponId, "no pistol in a supply crate");
                Assert.Greater(e.Weight, 0f);
            }
        }

        [Test]
        public void Contents_AlwaysHoldAGun_Armour_Boost_AndAnAttachment()
        {
            var table = Table();
            for (int seed = 0; seed < 200; seed++)
            {
                var items = SupplyPlan.Contents(table, 6, seed);
                Assert.AreEqual(6, items.Count);
                Assert.IsTrue(items.Exists(e => e.Kind == LootKind.Weapon), "gun, seed " + seed);
                Assert.IsTrue(items.Exists(e => e.Kind == LootKind.Helmet || e.Kind == LootKind.Vest || e.Kind == LootKind.Backpack), "armour or pack, seed " + seed);
                Assert.IsTrue(items.Exists(e => e.Kind == LootKind.Drink || e.Kind == LootKind.Pill || e.Kind == LootKind.Syringe), "boost, seed " + seed);
                Assert.IsTrue(items.Exists(e => e.Kind == LootKind.Optic), "attachment, seed " + seed);
            }
        }

        [Test]
        public void Contents_AreTheSameForTheSameSeed()
        {
            var table = Table();
            var a = SupplyPlan.Contents(table, 6, 77);
            var b = SupplyPlan.Contents(table, 6, 77);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Kind, b[i].Kind);
                Assert.AreEqual(a[i].WeaponId, b[i].WeaponId);
                Assert.AreEqual(a[i].Optic, b[i].Optic);
            }
        }

        [Test]
        public void Contents_StopAtWhatTheTableHolds()
        {
            Assert.AreEqual(0, SupplyPlan.Contents(new LootTable(), 6, 1).Count);
            Assert.AreEqual(2, SupplyPlan.Contents(Table(), 2, 1).Count);
        }

        [Test]
        public void Hold_FillsWhileHeldInReach_AndResetsOtherwise()
        {
            float p = 0f;
            p = SupplyPlan.Hold(p, true, true, 1f, 3f);
            Assert.AreEqual(1f / 3f, p, 1e-4f);
            p = SupplyPlan.Hold(p, true, true, 5f, 3f);
            Assert.AreEqual(1f, p, 1e-4f, "capped");
            Assert.AreEqual(0f, SupplyPlan.Hold(0.8f, false, true, 0.1f, 3f), "released");
            Assert.AreEqual(0f, SupplyPlan.Hold(0.8f, true, false, 0.1f, 3f), "stepped away");
        }

        [Test]
        public void Rules_AreInTheDataFile()
        {
            var rules = JsonUtility.FromJson<GameRules>(File.ReadAllText(DataFolder + "rules.json")).Supply;
            Assert.AreEqual(2, rules.FirstStage);
            Assert.AreEqual(3f, rules.OpenSeconds, 1e-4f);
        }
    }
}
