using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // The Sentinel precision rifle (weapon 5, ADR-058): data rows, optic list, loot rows and the damage arithmetic.
    public class SniperRifleTests
    {
        const string DataFolder = "Assets/_Project/Data/Resources/GameData/";
        const int Sentinel = 5, X8 = 7, X16 = 8;

        static WeaponTable Weapons() => JsonUtility.FromJson<WeaponTable>(File.ReadAllText(DataFolder + "weapons.json"));

        [Test]
        public void WeaponTable_Id5_IsASemiAutoPrimaryRifle762()
        {
            var w = Weapons().Find(Sentinel);
            Assert.IsNotNull(w);
            Assert.AreEqual("Sentinel", w.Name);
            Assert.AreEqual(AmmoType.Rifle762, w.Ammo);
            Assert.AreEqual(WeaponCategory.Primary, w.Category);
            Assert.IsFalse(w.Auto, "bolt-action feel: single shots only");
            Assert.AreEqual(1, w.Pellets);
            Assert.AreEqual(5, w.MagSize);
            Assert.AreEqual(1.3f, w.FireInterval, 1e-4f);
            Assert.AreEqual(3.4f, w.ReloadTime, 1e-4f);
            Assert.AreEqual(400f, w.Range, 1e-4f);
            Assert.AreEqual(6.5f, w.RecoilPitchDeg, 1e-4f);
            Assert.IsTrue(Loadout.SlotTakes(0, w.Category) && Loadout.SlotTakes(1, w.Category), "goes in a primary slot");
            Assert.IsFalse(Loadout.SlotTakes(2, w.Category), "not in the sidearm slot");
        }

        [Test]
        public void WeaponTable_IdsAreUnique_AndTheOtherFourAreUntouched()
        {
            var table = Weapons();
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, table.Weapons.Select(w => w.Id).ToArray());
            Assert.AreEqual(27f, table.Find(3).Damage, 1e-4f);
            Assert.AreEqual(AmmoType.Rifle556, table.Find(3).Ammo);
        }

        [Test]
        public void Damage_BodyLeavesAnUnarmouredTargetAlive_HeadKillsEvenThroughATierThreeHelmet()
        {
            var w = Weapons().Find(Sentinel);
            Assert.GreaterOrEqual(w.HeadFactor, 2f);
            // Literals: 82 x 1.0 = 82 of 100 HP; 82 x 2.5 = 205; a tier-3 helmet takes 50% -> 102.5 >= 100.
            Assert.AreEqual(82f, DamageRules.ForZone(w, 40f, HitZone.Body), 1e-3f);
            Assert.AreEqual(205f, DamageRules.ForZone(w, 40f, HitZone.Head), 1e-3f);
            Assert.AreEqual(57.4f, DamageRules.ForZone(w, 40f, HitZone.Limb), 1e-3f);
            Assert.Less(DamageRules.ForZone(w, 40f, HitZone.Body), 100f, "two body shots needed");
            Assert.GreaterOrEqual(DamageRules.ForZone(w, 40f, HitZone.Head) * (1f - 0.5f), 100f, "head through a tier-3 helmet");
        }

        [Test]
        public void Damage_FallsOffOnlyPastOneHundredMeters_AndNothingBeyondRange()
        {
            var w = Weapons().Find(Sentinel);
            Assert.AreEqual(82f, DamageRules.AtDistance(w, 100f), 1e-3f);
            Assert.AreEqual(82f * 0.9f, DamageRules.AtDistance(w, 200f), 1e-3f);
            Assert.AreEqual(82f * 0.8f, DamageRules.AtDistance(w, 300f), 1e-3f);
            Assert.AreEqual(0f, DamageRules.AtDistance(w, 401f));
        }

        [Test]
        public void ScopedSpread_IsAQuarterOfASixTenthsDegree()
        {
            var w = Weapons().Find(Sentinel);
            // Sights spread factor 0.25 in rules.json: 0.6 x 0.25 = 0.15 degrees.
            Assert.AreEqual(0.15f, w.SpreadDeg * 0.25f, 1e-4f);
            Assert.Greater(w.MoveSpreadFactor, 3f, "a moving sniper is inaccurate");
        }

        [Test]
        public void Optics_SentinelTakesEveryOptic_IncludingEightAndSixteen_InCodeAndInTheJson()
        {
            var fromJson = JsonUtility.FromJson<OpticRules>(File.ReadAllText(DataFolder + "optics.json"));
            foreach (var rules in new[] { new OpticRules(), fromJson })
            {
                CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, rules.AllowedFor(Sentinel));
                Assert.IsTrue(Optics.CanAttach(Sentinel, X16, rules));
                Assert.IsTrue(Optics.CanAttach(Sentinel, X8, rules));
                Assert.IsFalse(Optics.CanAttach(3, X8, rules), "the Warden still stops at 6x");
            }
            Assert.AreEqual(16f, new OpticRules().Find(X16).Zoom);
        }

        [Test]
        public void Loot_HasTheSentinel_7_62Ammo_And_TheLongScopes_WithPositiveWeight()
        {
            var loot = JsonUtility.FromJson<LootTable>(File.ReadAllText(DataFolder + "loot.json"));
            var weapon = loot.Entries.Single(e => e.Kind == LootKind.Weapon && e.WeaponId == Sentinel);
            Assert.AreEqual(5, weapon.Amount, "a full magazine");
            Assert.That(weapon.Weight, Is.InRange(1f, 2f), "rare");
            Assert.Greater(loot.Entries.Single(e => e.Kind == LootKind.Ammo && e.Ammo == AmmoType.Rifle762).Weight, 0f);
            Assert.AreEqual(2f, loot.Entries.Single(e => e.Kind == LootKind.Optic && e.Optic == X8).Weight);
            Assert.AreEqual(1f, loot.Entries.Single(e => e.Kind == LootKind.Optic && e.Optic == X16).Weight);
        }
    }
}
