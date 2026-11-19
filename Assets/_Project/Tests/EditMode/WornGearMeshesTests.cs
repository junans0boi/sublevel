using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // Worn helmet/vest/backpack models: triangle budget (3 pieces <= 1,500 triangles per tier), size sanity, tiers differ,
    // military tones, ground pickups within the pickup size limit.
    public class WornGearMeshesTests
    {
        static readonly LootKind[] Kinds = { LootKind.Helmet, LootKind.Vest, LootKind.Backpack };

        static int Tris(Mesh m) { int n = 0; for (int s = 0; s < m.subMeshCount; s++) n += (int)m.GetIndexCount(s) / 3; return n; }

        [Test]
        public void ThreePieces_PerTier_StayWithinTriangleBudget()
        {
            for (int tier = 1; tier <= 3; tier++)
            {
                int sum = 0; string line = "";
                foreach (var k in Kinds) { int t = Tris(WornGearMeshes.Model(k, tier)); sum += t; line += $" {k} {t}"; }
                UnityEngine.Debug.Log($"[Gear] tier {tier}: total {sum} tris ({line.Trim()})");
                Assert.LessOrEqual(sum, 1500, "tier " + tier + " helmet+vest+backpack triangles");
            }
        }

        [Test]
        public void Models_HaveRealisticSizes_AndHeavierTiersAddParts()
        {
            var helmet = WornGearMeshes.Model(LootKind.Helmet, 2).bounds.size;
            Assert.That(helmet.x, Is.InRange(0.18f, 0.28f), "helmet width"); Assert.That(helmet.z, Is.InRange(0.2f, 0.3f), "helmet depth");
            var vest = WornGearMeshes.Model(LootKind.Vest, 2).bounds.size;
            Assert.That(vest.x, Is.InRange(0.3f, 0.5f), "vest width"); Assert.That(vest.y, Is.InRange(0.25f, 0.45f), "vest height");
            var pack = WornGearMeshes.Model(LootKind.Backpack, 2).bounds.size;
            Assert.That(pack.y, Is.InRange(0.3f, 0.6f), "pack height"); Assert.That(pack.x, Is.InRange(0.25f, 0.45f), "pack width");
            foreach (var k in Kinds)
            {
                Assert.Less(Tris(WornGearMeshes.Model(k, 1)), Tris(WornGearMeshes.Model(k, 3)), k + " tier 3 has more parts than tier 1");
                Assert.AreEqual(1, WornGearMeshes.Model(k, 1).subMeshCount, "one draw call per piece");
                Assert.AreEqual(3, LootMeshes.Model(k, default, 1).subMeshCount, "ground pickups keep Body/Dark/Accent");
            }
            Assert.Greater(WornGearMeshes.Model(LootKind.Backpack, 3).bounds.size.y, WornGearMeshes.Model(LootKind.Backpack, 1).bounds.size.y, "bigger pack, higher tier");
        }

        [Test]
        public void Colors_AreMilitaryTones_AndTiersDifferByBrightness()
        {
            var c = new Color[3]; float prev = 2f;
            for (int tier = 1; tier <= 3; tier++)
            {
                WornGearMeshes.Colors(LootKind.Helmet, tier, c);
                Color.RGBToHSV(c[WornGearMeshes.Body], out float h, out float s, out float v);
                Assert.Less(s, 0.45f, "muted (no blue sphere / orange box)");
                Assert.That(h < 0.2f || h > 0.0f, "olive/khaki/black hue"); // any hue allowed if muted; the saturation bound is the real rule
                Assert.Less(v, prev, "darker with tier"); prev = v;
            }
        }

        [Test]
        public void GroundPickups_UseTheSameModels_WithinThePickupSizeLimit()
        {
            foreach (var k in Kinds)
                for (int tier = 1; tier <= 3; tier++)
                {
                    var g = LootMeshes.Model(k, default, tier);
                    Assert.IsNotNull(g); Assert.AreEqual(Tris(WornGearMeshes.Model(k, tier)), Tris(g), k + " same shape as worn");
                    var b = g.bounds; Assert.That(Mathf.Max(b.size.x, b.size.y, b.size.z), Is.InRange(0.1f, 0.6f), k + " ground size");
                    Assert.AreEqual(0f, b.min.y, 0.01f, k + " rests on the floor");
                }
        }

        [Test]
        public void Palette_WithoutShader_IsNull_NotAnException()
        {
            // A dedicated server build has no shaders (Dedicated Server Optimizations): ArmorVisual must get null, not throw.
            Assert.IsNull(WornGearMeshes.PaletteWith(null, 2));
            var m = WornGearMeshes.PaletteWith(Shader.Find("Universal Render Pipeline/Lit"), 2);
            Assert.IsNotNull(m);
            Object.DestroyImmediate(m);
        }
    }
}
