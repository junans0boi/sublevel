using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class MaterialTableTests
    {
        static MaterialTable Shipped() => JsonUtility.FromJson<MaterialTable>(Resources.Load<TextAsset>("GameData/materials").text);

        [Test]
        public void ShippedTable_IsValid() => CollectionAssert.IsEmpty(MaterialRules.Validate(Shipped()));

        [Test]
        public void Table_CoversAllTwelveCategories()
            => Assert.AreEqual(12, Shipped().materials.Select(m => m.category).Distinct().Count());

        [Test]
        public void EveryMaterial_MapsToARealSurface()
            => Assert.IsTrue(Shipped().materials.All(m => System.Enum.IsDefined(typeof(SurfaceId), MaterialRules.SurfaceOf(m))));

        [Test]
        public void SliceCategories_AreAllPriorityZero()
        {
            // ADR-184: walls, roofs, asphalt, pavement, dirt road, vegetation, floor and metal come first.
            foreach (var c in new[] { "Wall", "Roof", "Asphalt", "Pavement", "DirtRoad", "Vegetation", "Floor", "Metal" })
                Assert.IsTrue(Shipped().materials.Any(m => m.category == c && m.priority == 0), c);
        }

        [Test]
        public void Validate_RejectsDuplicateIdBadSurfaceAndOversizeResolution()
        {
            var table = Shipped();
            table.maxResolution = 4096;
            table.materials[1].id = table.materials[0].id;
            table.materials[2].surface = "Lava";
            var problems = MaterialRules.Validate(table);
            Assert.IsTrue(problems.Any(p => p.Contains("2K")));
            Assert.IsTrue(problems.Any(p => p.Contains("duplicate")));
            Assert.IsTrue(problems.Any(p => p.Contains("Lava")));
        }
    }
}
