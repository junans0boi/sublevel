using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // The look of the optics (ADR-060): lens and eye-ring sizes per magnification, reticle per magnification, the
    // optic names players see, and that every optic can be found as loot.
    public class OpticLookTests
    {
        const string DataFolder = "Assets/_Project/Data/Resources/GameData/";

        [Test]
        public void LensRadius_ShrinksFrom3xTo4x_AndGrowsAgainFor6xAnd8x()
        {
            float x3 = OpticLook.For(3f).LensRadius, x4 = OpticLook.For(4f).LensRadius;
            float x6 = OpticLook.For(6f).LensRadius, x8 = OpticLook.For(8f).LensRadius;
            Assert.Less(x4, x3, "4x: small lens window, the body covers much");
            Assert.Greater(x6, x4, "6x: large clean round lens");
            Assert.Greater(x8, x6, "8x: large lens");
            Assert.AreEqual(0.28f, x3, 1e-4f);
            Assert.AreEqual(0.21f, x4, 1e-4f);
            Assert.AreEqual(0.34f, x6, 1e-4f);
            Assert.AreEqual(0.38f, x8, 1e-4f);
        }

        [Test]
        public void EveryMagnification_HasLensInsideRimInsideOuter_AndTheLensFitsTheScreen()
        {
            foreach (float zoom in new[] { 2f, 2.5f, 3f, 4f, 5f, 6f, 8f, 12f, 15f })
            {
                var look = OpticLook.For(zoom);
                Assert.Less(look.LensRadius, look.RimRadius, zoom + "x");
                Assert.Less(look.RimRadius, look.OuterRadius, zoom + "x");
                Assert.Less(look.LensRadius, 0.5f, zoom + "x: the whole lens is on screen");
                Assert.Greater(look.Depth, 0f, zoom + "x");
            }
            // 2x and 15x follow the same rule as their neighbours.
            Assert.AreEqual(ScopeReticle.Arc, OpticLook.For(2f).Reticle);
            Assert.AreEqual(ScopeReticle.Holdover, OpticLook.For(15f).Reticle);
        }

        [Test]
        public void LensSize_FollowsTheScreenHeight()
        {
            // The same share of the screen at 720p and 1440p: twice the pixels.
            Assert.AreEqual(2f * OpticLook.LensRadiusPixels(4f, 720f), OpticLook.LensRadiusPixels(4f, 1440f), 1e-3f);
            Assert.AreEqual(0.21f * 1080f, OpticLook.LensRadiusPixels(4f, 1080f), 1e-2f);
        }

        [Test]
        public void ReticleAndHousingFeatures_FollowTheMagnification()
        {
            Assert.AreEqual(ScopeReticle.Arc, OpticLook.For(3f).Reticle);
            Assert.AreEqual(ScopeReticle.Post, OpticLook.For(4f).Reticle);
            Assert.AreEqual(ScopeReticle.Fine, OpticLook.For(6f).Reticle);
            Assert.AreEqual(ScopeReticle.Holdover, OpticLook.For(8f).Reticle);
            Assert.IsTrue(OpticLook.For(3f).FlipCap);
            Assert.IsTrue(OpticLook.For(4f).BoxBody);
            Assert.IsTrue(OpticLook.For(6f).DoubleBand);
            Assert.IsTrue(OpticLook.For(8f).Knurled && OpticLook.For(8f).SideTurret);
            Assert.Greater(OpticLook.For(8f).OuterRadius, OpticLook.For(6f).OuterRadius, "8x: thick eye ring blocking left and right");
            // Models: 3x short and chunky, 4x long and slim with a top dial, 8x long on two ring mounts.
            var b3 = OpticLook.BodyFor(3f);
            var b4 = OpticLook.BodyFor(4f);
            var b8 = OpticLook.BodyFor(8f);
            Assert.Less(b3.Length, b4.Length);
            Assert.Greater(b3.TubeRadius, b4.TubeRadius);
            Assert.IsTrue(b3.FlipCaps && b4.TopDial && b8.RingMounts && b8.Turrets);
            Assert.Greater(b8.Length, OpticLook.BodyFor(6f).Length);
        }

        [Test]
        public void HousingRadius_CoversTheGivenShareOfTheScreen()
        {
            float h = OpticLook.ViewHeightAt(0.1f, OpticLook.HousingFovDeg);
            Assert.AreEqual(2f * 0.1f * Mathf.Tan(20f * Mathf.Deg2Rad), h, 1e-5f);
            Assert.AreEqual(0.3f * h, OpticLook.HousingRadius(0.3f, 0.1f), 1e-6f);
        }

        [Test]
        public void EveryOptic_HasACodeBuiltModel_AndEveryScopeAnEyepiece()
        {
            foreach (var o in new OpticRules().Optics)
            {
                var mesh = Game.Gameplay.OpticMeshes.Model(o);
                Assert.IsNotNull(mesh, o.Name);
                Assert.AreEqual(Game.Gameplay.OpticMeshes.SubMeshCount, mesh.subMeshCount, o.Name);
                Assert.Greater(mesh.GetIndexCount(Game.Gameplay.OpticMeshes.Metal), 0u, o.Name + " has a body");
                // Small enough for a weapon rail: under 40 cm long and 10 cm tall.
                Assert.Less(mesh.bounds.size.z, 0.4f, o.Name);
                Assert.Less(mesh.bounds.size.y, 0.1f, o.Name);
                if (o.Kind != OpticKind.Scope)
                {
                    Assert.Greater(mesh.GetIndexCount(Game.Gameplay.OpticMeshes.Window), 0u, o.Name + " has a see-through window");
                    continue;
                }
                var eyepiece = Game.Gameplay.OpticMeshes.Eyepiece(o.Zoom);
                Assert.Greater(eyepiece.vertexCount, 0, o.Name);
                // Pivot on the eye ring face: nothing reaches back toward the eye past the knurl and the bolts.
                Assert.Greater(eyepiece.bounds.min.z, -0.02f, o.Name);
            }
        }

        [Test]
        public void OneTimesSights_HaveOurOwnNames_InCodeAndInTheJson()
        {
            var fromJson = JsonUtility.FromJson<OpticRules>(File.ReadAllText(DataFolder + "optics.json"));
            foreach (var rules in new[] { new OpticRules(), fromJson })
            {
                Assert.AreEqual("반사 조준 사이트", rules.Find(1).Name);
                Assert.AreEqual("투영 조준 사이트", rules.Find(2).Name);
                Assert.AreEqual(8, rules.Optics.Length);
                foreach (var o in rules.Optics) Assert.IsFalse(string.IsNullOrEmpty(o.Name), "optic " + o.Id);
            }
        }

        [Test]
        public void EveryOptic_IsLoot_WithAPositiveWeight()
        {
            var loot = JsonUtility.FromJson<LootTable>(File.ReadAllText(DataFolder + "loot.json"));
            foreach (var o in new OpticRules().Optics)
            {
                var rows = loot.Entries.Where(e => e.Kind == LootKind.Optic && e.Optic == o.Id).ToArray();
                Assert.AreEqual(1, rows.Length, o.Name + " has one loot row");
                Assert.Greater(rows[0].Weight, 0f, o.Name + " can be found");
            }
            float dot = loot.Entries.Single(e => e.Kind == LootKind.Optic && e.Optic == 1).Weight;
            Assert.Less(loot.Entries.Single(e => e.Kind == LootKind.Optic && e.Optic == 7).Weight, dot, "8x is rare");
            Assert.Less(loot.Entries.Single(e => e.Kind == LootKind.Optic && e.Optic == 8).Weight, dot, "15x is rare");
        }
    }
}
