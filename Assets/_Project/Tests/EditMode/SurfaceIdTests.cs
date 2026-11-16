using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #242 T0 / docs/research/12_surface_tag_design.md: one surface id for footsteps and bullets, no name parsing.
    public class SurfaceIdTests
    {
        static readonly SurfaceId[] All = (SurfaceId[])Enum.GetValues(typeof(SurfaceId));

        [Test]
        public void There_are_eighteen_surfaces_with_unique_values()
        {
            Assert.AreEqual(18, All.Length);
            Assert.AreEqual(All.Length, new System.Collections.Generic.HashSet<byte>(Array.ConvertAll(All, s => (byte)s)).Count);
        }

        [TestCase(SurfaceId.Asphalt, FootSurface.Concrete, BulletSurface.Concrete)]
        [TestCase(SurfaceId.MetalGrate, FootSurface.Metal, BulletSurface.Metal)]
        [TestCase(SurfaceId.Carpet, FootSurface.Wood, BulletSurface.Wood)]
        [TestCase(SurfaceId.Gravel, FootSurface.Dirt, BulletSurface.Ground)]
        [TestCase(SurfaceId.Glass, FootSurface.Concrete, BulletSurface.Glass)]
        [TestCase(SurfaceId.Water, FootSurface.Water, BulletSurface.Ground)]
        [TestCase(SurfaceId.Mud, FootSurface.Dirt, BulletSurface.Ground)]
        [TestCase(SurfaceId.Deck, FootSurface.Wood, BulletSurface.Wood)]
        [TestCase(SurfaceId.MetalStairs, FootSurface.Metal, BulletSurface.Metal)]
        [TestCase(SurfaceId.TinRoof, FootSurface.Metal, BulletSurface.Metal)]
        public void Maps_to_the_existing_enums(SurfaceId id, FootSurface foot, BulletSurface bullet)
        {
            Assert.AreEqual(foot, SurfaceRules.ToFoot(id));
            Assert.AreEqual(bullet, SurfaceRules.ToBullet(id));
        }

        // docs/bible/data/audio.json lists sixteen surface tags; every one must exist (names without separators, case-insensitive).
        [TestCase("concrete")] [TestCase("asphalt")] [TestCase("gravel")] [TestCase("dirt")] [TestCase("mud")] [TestCase("grass")]
        [TestCase("sand")] [TestCase("rock")] [TestCase("wood")] [TestCase("deck")] [TestCase("tile")] [TestCase("metal")]
        [TestCase("metalstairs")] [TestCase("tinroof")] [TestCase("water")] [TestCase("glass")]
        public void Every_surface_of_the_definition_table_exists(string tag) =>
            Assert.IsTrue(Array.Exists(All, s => string.Equals(s.ToString(), tag, StringComparison.OrdinalIgnoreCase)), tag);

        [Test]
        public void The_old_footstep_surfaces_round_trip()
        {
            foreach (FootSurface foot in Enum.GetValues(typeof(FootSurface)))
                Assert.AreEqual(foot, SurfaceRules.ToFoot(SurfaceRules.FromFoot(foot)), foot.ToString());
        }

        // The tag replaces the name parsing, so a building piece must sound the same as before (SoundRules.BuildingSurface).
        [Test]
        public void Building_pieces_keep_their_footsteps()
        {
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind)))
                foreach (SurfaceKind piece in Enum.GetValues(typeof(SurfaceKind)))
                    Assert.AreEqual(SoundRules.BuildingSurface(kind, piece), SurfaceRules.ToFoot(SurfaceRules.ForBuilding(kind, piece)), $"{kind}/{piece}");
        }

        [Test]
        public void Unknown_value_falls_back_to_concrete()
        {
            Assert.AreEqual(FootSurface.Concrete, SurfaceRules.ToFoot((SurfaceId)200));
            Assert.AreEqual(BulletSurface.Concrete, SurfaceRules.ToBullet((SurfaceId)200));
        }
    }
}
