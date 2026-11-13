using System;
using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // #114: every pickup without a model asset gets a shared code-built ground model.
    public class LootMeshTests
    {
        [Test]
        public void EveryKindWithoutAnAssetHasASharedFloorModel()
        {
            foreach (LootKind kind in Enum.GetValues(typeof(LootKind)))
            {
                if (kind == LootKind.Weapon || kind == LootKind.Optic) continue; // weapon and optic models
                var mesh = LootMeshes.Model(kind, AmmoType.Rifle556, 2);
                Assert.IsNotNull(mesh, kind.ToString());
                Assert.AreSame(mesh, LootMeshes.Model(kind, AmmoType.Rifle556, 2), kind + " is built once");
                Assert.AreEqual(3, mesh.subMeshCount, kind.ToString());
                var b = mesh.bounds;
                Assert.That(b.min.y, Is.InRange(-0.005f, 0.01f), kind + " rests on y = 0");
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                Assert.That(longest, Is.InRange(0.1f, 0.6f), kind + " size");
            }
        }

        [Test]
        public void AmmoTypesAndArmourTiersLookDifferent()
        {
            var a = new Color[3];
            var b = new Color[3];
            LootMeshes.Colors(LootKind.Ammo, AmmoType.Pistol9, 0, a);
            LootMeshes.Colors(LootKind.Ammo, AmmoType.Shotgun12, 0, b);
            Assert.AreNotEqual(a[LootMeshes.Body], b[LootMeshes.Body]);
            Assert.AreNotSame(LootMeshes.Model(LootKind.Vest, AmmoType.Pistol9, 1), LootMeshes.Model(LootKind.Vest, AmmoType.Pistol9, 3), "tier bars");
            Assert.IsNull(LootMeshes.Model(LootKind.Weapon, AmmoType.Pistol9, 0));
        }
    }
}
