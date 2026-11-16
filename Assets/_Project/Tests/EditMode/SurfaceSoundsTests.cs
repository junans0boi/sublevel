using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class SurfaceSoundsTests
    {
        static readonly SurfaceId[] All = (SurfaceId[])Enum.GetValues(typeof(SurfaceId));

        [Test]
        public void Every_surface_has_exactly_one_entry()
        {
            Assert.AreEqual(All.Length, SurfaceSounds.Table.Length);
            foreach (var id in All) Assert.AreEqual(id, SurfaceSounds.Of(id).Surface, id.ToString());
        }

        [Test]
        public void Folders_are_unique_lowercase_resource_names()
        {
            var seen = new HashSet<string>();
            foreach (var e in SurfaceSounds.Table)
            {
                Assert.AreEqual(e.Folder.ToLowerInvariant(), e.Folder, e.Surface.ToString());
                Assert.IsFalse(e.Folder.Contains(" "), e.Surface.ToString());
                Assert.IsTrue(seen.Add(e.Folder), "duplicate folder " + e.Folder);
            }
        }

        [Test]
        public void A_stand_in_surface_has_clips_of_its_own()
        {
            foreach (var e in SurfaceSounds.Table)
                Assert.AreEqual(SurfaceSounds.Of(e.Fallback).Fallback, e.Fallback, $"{e.Surface} falls back to {e.Fallback}, which itself falls back");
        }

        // The six old footstep surfaces keep their loudness (bots hear by it, FootstepSurfaceTests).
        [TestCase(SurfaceId.Concrete)]
        [TestCase(SurfaceId.Metal)]
        [TestCase(SurfaceId.Wood)]
        [TestCase(SurfaceId.Grass)]
        [TestCase(SurfaceId.Dirt)]
        [TestCase(SurfaceId.Water)]
        public void Old_surfaces_keep_their_loudness(SurfaceId id)
        {
            Assert.AreEqual(SoundRules.SurfaceLoudness(SurfaceRules.ToFoot(id)), SurfaceSounds.Loudness(id), 1e-4f, id.ToString());
        }

        [Test]
        public void Loudness_is_sane()
        {
            foreach (var e in SurfaceSounds.Table) Assert.That(e.Loudness, Is.InRange(0.5f, 1.6f), e.Surface.ToString());
        }

        [Test]
        public void A_surface_without_clips_plays_its_stand_in()
        {
            Assert.AreEqual("Steps/wood", SurfaceSounds.StepFolder(SurfaceId.Carpet, f => f != "Steps/carpet"));
            Assert.AreEqual("Steps/carpet", SurfaceSounds.StepFolder(SurfaceId.Carpet, f => true));
            Assert.AreEqual("Steps/metal", SurfaceSounds.StepFolder(SurfaceId.TinRoof, f => f == "Steps/metal"));
        }

        [Test]
        public void An_unknown_id_is_neutral_concrete()
        {
            Assert.AreEqual(SurfaceId.Concrete, SurfaceSounds.Of((SurfaceId)250).Surface);
        }
    }
}
