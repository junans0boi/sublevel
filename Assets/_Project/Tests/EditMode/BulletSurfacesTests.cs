using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    // #341 / ADR-534: a shot's surface comes from the SurfaceIdTag WorldBuilder puts on collider groups, not the building's name.
    public class BulletSurfacesTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        // A building as WorldBuilder makes it: "<Lot>_<Kind>" with a "Colliders" child per surface.
        BoxCollider Piece(string building, SurfaceId? tag, float thick = 0.2f)
        {
            _root = new GameObject(building);
            _root.transform.position = new Vector3(5000f, 50f, 5000f);
            var colliders = new GameObject("Colliders");
            colliders.transform.SetParent(_root.transform, false);
            if (tag.HasValue) colliders.AddComponent<SurfaceIdTag>().Id = tag.Value;
            var box = colliders.AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 4f, thick);
            Physics.SyncTransforms();
            return box;
        }

        [TestCase(SurfaceId.Wood, BulletSurface.Wood)]
        [TestCase(SurfaceId.Concrete, BulletSurface.Concrete)]
        [TestCase(SurfaceId.TinRoof, BulletSurface.Metal)]
        [TestCase(SurfaceId.Glass, BulletSurface.Glass)]
        [TestCase(SurfaceId.Asphalt, BulletSurface.Concrete)]
        public void Tagged_collider_reads_its_tag(SurfaceId tag, BulletSurface expected) =>
            Assert.AreEqual(expected, BulletSurfaces.Of(Piece("Lot01_Warehouse", tag)));

        // Rule change: a house's outer wall is tagged concrete, so it is concrete now (it was wood from the name).
        [Test]
        public void Building_name_is_not_parsed()
        {
            Assert.AreEqual(BulletSurface.Concrete, BulletSurfaces.Of(Piece("Lot03_House", SurfaceRules.ForBuilding(BuildingKind.House, SurfaceKind.WallOuter))));
            Object.DestroyImmediate(_root);
            Assert.AreEqual(BulletSurface.Wood, BulletSurfaces.Of(Piece("Lot03_House", SurfaceRules.ForBuilding(BuildingKind.House, SurfaceKind.WallInner))));
        }

        [Test]
        public void Untagged_collider_falls_back_to_concrete() =>
            Assert.AreEqual(BulletSurface.Concrete, BulletSurfaces.Of(Piece("Lot03_House", null)));

        // The server's penetration step and the impact/decal message both use BulletSurfaces.Of (Combatant.Ballistics).
        [TestCase(SurfaceId.Wood, true, Debris.WoodChips)]
        [TestCase(SurfaceId.Concrete, false, Debris.ConcreteChunks)]
        [TestCase(SurfaceId.Metal, true, Debris.Sparks)]
        public void Penetration_and_impact_follow_the_tag(SurfaceId tag, bool passes, Debris debris)
        {
            var box = Piece("Lot01_Shed", tag);
            var rules = new PenetrationRules();
            var dir = Vector3.forward;
            Assert.IsTrue(box.Raycast(new Ray(box.transform.position - dir * 3f, dir), out var hit, 10f));
            var surface = BulletSurfaces.Of(hit.collider);
            Assert.AreEqual(debris, DebrisRules.Of(surface));
            float limit = rules.MaxThickness[(int)surface];
            bool through = limit > 0f && Penetration.TryPass(surface, BulletSurfaces.Thickness(hit, dir, limit, out _), 800f, rules, out _, out _);
            Assert.AreEqual(passes, through);
        }
    }
}
