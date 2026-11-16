using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ItemIcons lives in Game.Gameplay, which this test assembly does not reference, so it is reached by reflection.
    public class ItemIconTests
    {
        const string IconDir = "Assets/_Project/Resources/Icons";

        static readonly string[] Expected =
        {
            "weapon_1", "weapon_2", "weapon_3", "weapon_4", "weapon_5",
            "weapon_6", "weapon_7", "weapon_8", "weapon_9", "weapon_10", "weapon_11", "weapon_12", "weapon_13", "weapon_14", "weapon_15", "weapon_16", "weapon_31", "weapon_32", "weapon_33", "weapon_34", "weapon_35", // ADR-217
            "weapon_21", "weapon_22", "weapon_23", "weapon_24",
            "ammo_9mm", "ammo_556", "ammo_762", "ammo_12ga",
            "medkit",
            "helmet_1", "helmet_2", "helmet_3",
            "vest_1", "vest_2", "vest_3",
            "grenade",
            "optic_reddot", "optic_holo", "optic_2x", "optic_3x", "optic_4x", "optic_6x", "optic_8x", "optic_15x",
            "bandage", "firstaid", "smoke", "key",
            "jerrycan",
            "drink", "pill", "syringe", "flash", "molotov",
            "backpack_1", "backpack_2", "backpack_3", // ADR-091
            "att_grip_v", "att_grip_a", "att_grip_c", "att_stock", // ADR-100
            "att_comp", "att_flash", "att_supp", "att_mag", // ADR-102
        };

        static Type IconsType()
        {
            var type = Type.GetType("Game.Gameplay.ItemIcons, Game.Gameplay");
            Assert.IsNotNull(type, "Game.Gameplay.ItemIcons not found");
            return type;
        }

        static string[] KnownKeys() => (string[])IconsType().GetField("KnownKeys", BindingFlags.Public | BindingFlags.Static).GetValue(null);

        static Sprite Get(string key) => (Sprite)IconsType().GetMethod("Get", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { key });

        [Test]
        public void OpticKey_MapsEveryOpticAndAttachmentIdToItsOwnIcon()
        {
            var method = IconsType().GetMethod("OpticKey", BindingFlags.Public | BindingFlags.Static);
            string Key(int id) => (string)method.Invoke(null, new object[] { id });
            Assert.AreEqual("optic_reddot", Key(1));
            Assert.AreEqual("optic_15x", Key(8));
            Assert.AreEqual("att_comp", Key(9));
            Assert.AreEqual("att_supp", Key(11));
            Assert.AreEqual("att_grip_v", Key(12));
            Assert.AreEqual("att_stock", Key(15));
            Assert.AreEqual("att_mag", Key(16));
            Assert.AreEqual("", Key(0));
            Assert.AreEqual("", Key(17));
        }

        [Test]
        public void KnownKeys_AreTheExpectedTwentyNine_WithoutDuplicates()
        {
            var keys = KnownKeys();
            CollectionAssert.AreEquivalent(Expected, keys);
            Assert.AreEqual(keys.Length, keys.Distinct().Count());
            Assert.IsTrue(keys.All(k => k == k.ToLowerInvariant()));
        }

        [Test]
        public void Get_UnknownKey_ReturnsNull()
        {
            Assert.IsNull(Get("no_such_icon"));
            Assert.IsNull(Get(""));
            Assert.IsNull(Get(null));
        }

        [Test]
        public void Get_AfterBuilderRun_LoadsEverySprite()
        {
            bool any = Directory.Exists(IconDir) && Directory.GetFiles(IconDir, "*.png").Length > 0;
            if (!any) Assert.Ignore("No icons generated yet: run tools/unity.sh run-gfx ItemIconBuilder.Build");
            foreach (var key in KnownKeys())
            {
                var sprite = Get(key);
                Assert.IsNotNull(sprite, key);
                Assert.GreaterOrEqual(sprite.rect.width, 128f, key);
            }
        }
    }
}
