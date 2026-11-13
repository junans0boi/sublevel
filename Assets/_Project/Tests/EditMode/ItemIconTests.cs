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
            "weapon_21", "weapon_22", "weapon_23", "weapon_24",
            "ammo_9mm", "ammo_556", "ammo_762", "ammo_12ga",
            "medkit",
            "helmet_1", "helmet_2", "helmet_3",
            "vest_1", "vest_2", "vest_3",
            "grenade",
            "optic_reddot", "optic_holo", "optic_2x", "optic_3x", "optic_4x", "optic_6x", "optic_8x", "optic_15x",
            "bandage", "firstaid", "smoke", "key",
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
