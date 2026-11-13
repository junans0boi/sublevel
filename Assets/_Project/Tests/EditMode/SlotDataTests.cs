using System.IO;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // The shipped data files carry the ADR-058 values (categories, unarmed speed, prone).
    public class SlotDataTests
    {
        const string DataFolder = "Assets/_Project/Data/Resources/GameData/";

        [Test]
        public void WeaponData_OnlyThePistolIsASidearm()
        {
            var table = JsonUtility.FromJson<WeaponTable>(File.ReadAllText(DataFolder + "weapons.json"));
            Assert.AreEqual(WeaponCategory.Sidearm, table.Find(1).Category, "VK-9");
            Assert.AreEqual(WeaponCategory.Primary, table.Find(2).Category, "Ordo");
            Assert.AreEqual(WeaponCategory.Primary, table.Find(3).Category, "Warden");
            Assert.AreEqual(WeaponCategory.Primary, table.Find(4).Category, "Breacher");
            var loadout = new Loadout(table);
            Assert.IsTrue(loadout.CanHold(Loadout.SidearmSlot, 1));
            Assert.IsFalse(loadout.CanHold(Loadout.PrimarySlotA, 1));
            Assert.IsFalse(loadout.CanHold(Loadout.SidearmSlot, 2));
        }

        [Test]
        public void WeaponData_OrdoAndWardenAreAutomatic_VkAndBreacherAreNot()
        {
            var table = JsonUtility.FromJson<WeaponTable>(File.ReadAllText(DataFolder + "weapons.json"));
            Assert.IsFalse(table.Find(1).Auto);
            Assert.IsTrue(table.Find(2).Auto);
            Assert.IsTrue(table.Find(3).Auto);
            Assert.IsFalse(table.Find(4).Auto);
            var loadout = new Loadout(table);
            loadout.AddWeapon(2, 30);
            loadout.AddWeapon(1, 12);
            Assert.IsTrue(loadout.CanSwitchFireMode(0));
            Assert.IsFalse(loadout.CanSwitchFireMode(2));
        }

        [Test]
        public void RulesData_UnarmedFactorSlotsAndProne()
        {
            var rules = JsonUtility.FromJson<GameRules>(File.ReadAllText(DataFolder + "rules.json"));
            Assert.AreEqual(1.12f, rules.Locomotion.UnarmedMoveFactor, 1e-5f);
            Assert.AreEqual(0.12f, rules.Slots.SingleModeMinInterval, 1e-5f);
            Assert.IsTrue(rules.Stance.ProneEnabled);
            Assert.AreEqual(1.2f, rules.Stance.Prone.EyeDropMeters, 1e-5f);
            Assert.AreEqual(0.7f, rules.Stance.Prone.CapsuleHeight, 1e-5f);
            Assert.AreEqual(0.18f, rules.Stance.Prone.MoveFactor, 1e-5f);
            Assert.AreEqual(85f, rules.Stance.ProneModelPitchDeg, 1e-5f);
            Assert.AreEqual(40f, rules.Stance.ProneHeadLiftDeg, 1e-5f);
            Assert.GreaterOrEqual(rules.Stance.Prone.CapsuleHeight, 0.7f);
            Assert.Less(1.6f - rules.Stance.Prone.EyeDropMeters, rules.Stance.Prone.CapsuleHeight);
        }
    }
}
