using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // Where the 21 guns turn up (ADR-182): ordinary loot holds every non-supply gun, supply crates hold the supply-only ones.
    public class WeaponLootTests
    {
        static readonly string DataFolder = Path.Combine(Application.dataPath, "_Project/Data/Resources/GameData/");
        static LootTable Loot() => JsonUtility.FromJson<LootTable>(File.ReadAllText(DataFolder + "loot.json"));
        static LootTable Supply() => JsonUtility.FromJson<LootTable>(File.ReadAllText(DataFolder + "supply.json"));
        static WeaponTable Weapons() => JsonUtility.FromJson<WeaponTable>(File.ReadAllText(DataFolder + "weapons.json"));

        static LootEntry[] Guns(LootTable t) => t.Entries.Where(e => e.Kind == LootKind.Weapon).ToArray();

        [Test]
        public void OrdinaryLoot_HoldsEveryNonSupplyGun_Once_AndNoSupplyGun()
        {
            var weapons = Weapons();
            var rows = Guns(Loot());
            foreach (var w in weapons.Weapons)
            {
                int n = rows.Count(r => r.WeaponId == w.Id);
                Assert.AreEqual(w.SupplyOnly ? 0 : 1, n, w.Name + " rows in ordinary loot");
            }
        }

        [Test]
        public void SupplyCrate_HoldsEverySupplyOnlyGun()
        {
            var rows = Guns(Supply());
            foreach (var w in Weapons().Weapons.Where(w => w.SupplyOnly))
                Assert.AreEqual(1, rows.Count(r => r.WeaponId == w.Id), w.Name);
        }

        [Test]
        public void EveryGunRow_PointsAtARealWeapon_WithAFullMagazine_AndAPositiveWeight()
        {
            var weapons = Weapons();
            foreach (var t in new[] { Loot(), Supply() })
                foreach (var r in Guns(t))
                {
                    var w = weapons.Find(r.WeaponId);
                    Assert.IsNotNull(w, "weapon " + r.WeaponId);
                    Assert.AreEqual(w.IsMelee ? 0 : w.MagSize, r.Amount, w.Name + " starts with a full magazine");
                    Assert.Greater(r.Weight, 0f, w.Name);
                }
        }

        [Test]
        public void OrdinaryLoot_PistolsAreCommon_SnipersAreRare()
        {
            var weapons = Weapons();
            var rows = Guns(Loot()).Where(r => !weapons.Find(r.WeaponId).IsMelee).ToArray();
            float total = rows.Sum(r => r.Weight);
            float Share(System.Func<WeaponDef, bool> pick) => rows.Where(r => pick(weapons.Find(r.WeaponId))).Sum(r => r.Weight) / total;
            Assert.That(Share(w => w.Category == WeaponCategory.Sidearm), Is.InRange(0.15f, 0.35f), "pistols");
            Assert.That(Share(w => w.Id == 5 || w.Id == 15), Is.InRange(0.01f, 0.06f), "sniper rifles");
            Assert.That(Share(w => w.Id == 12 || w.Id == 13 || w.Id == 14), Is.InRange(0.03f, 0.12f), "marksman rifles");
        }

        [Test]
        public void Roll_FollowsTheWeights_ForGunsOnly()
        {
            var loot = Loot();
            var weapons = Weapons();
            var rng = new System.Random(2026);
            var counts = new int[64];
            int guns = 0;
            for (int i = 0; i < 40000; i++)
            {
                var e = loot.Roll(rng);
                if (e.Kind != LootKind.Weapon) continue;
                counts[e.WeaponId]++; guns++;
                Assert.IsFalse(weapons.Find(e.WeaponId).SupplyOnly, "a supply gun came out of ordinary loot");
            }
            float total = Guns(loot).Sum(r => r.Weight);
            foreach (var r in Guns(loot))
                Assert.AreEqual(r.Weight / total, counts[r.WeaponId] / (float)guns, 0.02f, weapons.Find(r.WeaponId).Name);
        }
    }
}
