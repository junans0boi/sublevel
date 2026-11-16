using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-251: floor and wall occlusion numbers and the ray budget.
    public class SoundOcclusionTests
    {
        [Test]
        public void Nothing_in_the_way_is_open()
        {
            var o = SoundOcclusion.Of(0, 0);
            Assert.AreEqual(1f, o.Gain);
            Assert.AreEqual(SoundOcclusion.OpenCutoff, o.CutoffHz);
        }

        [Test]
        public void One_floor_is_minus_six_dB_and_about_two_kilohertz()
        {
            var o = SoundOcclusion.Of(1, 0);
            Assert.AreEqual(0.5f, o.Gain, 1e-4f);
            Assert.That(o.CutoffHz, Is.InRange(1800f, 2200f));
        }

        [Test]
        public void More_barriers_are_quieter_and_duller_and_a_wall_muffles_less_than_a_floor()
        {
            Assert.Less(SoundOcclusion.Of(2, 0).Gain, SoundOcclusion.Of(1, 0).Gain);
            Assert.Less(SoundOcclusion.Of(0, 2).CutoffHz, SoundOcclusion.Of(0, 1).CutoffHz);
            Assert.Greater(SoundOcclusion.Of(0, 1).Gain, SoundOcclusion.Of(1, 0).Gain);
            Assert.GreaterOrEqual(SoundOcclusion.Of(3, 3).CutoffHz, SoundOcclusion.MinCutoff);
        }

        [Test]
        public void Counts_beyond_the_cap_change_nothing()
        {
            Assert.AreEqual(SoundOcclusion.Of(SoundOcclusion.MaxFloors, SoundOcclusion.MaxWalls).Gain, SoundOcclusion.Of(9, 9).Gain);
            Assert.AreEqual(SoundOcclusion.Of(0, 0).Gain, SoundOcclusion.Of(-2, -1).Gain);
        }

        [Test]
        public void Steel_stairs_carry_sound_between_floors()
        {
            Assert.Greater(SoundOcclusion.Of(1, 0, true).Gain, SoundOcclusion.Of(1, 0, false).Gain);
            Assert.Greater(SoundOcclusion.Of(1, 0, true).CutoffHz, SoundOcclusion.Of(1, 0, false).CutoffHz);
        }

        [Test]
        public void Flat_surfaces_are_floors_and_steep_ones_walls()
        {
            Assert.IsTrue(SoundOcclusion.IsFloor(1f));
            Assert.IsTrue(SoundOcclusion.IsFloor(-1f));
            Assert.IsFalse(SoundOcclusion.IsFloor(0f));
            Assert.IsFalse(SoundOcclusion.IsFloor(0.5f));
        }

        [Test]
        public void Probe_range_is_sixty_metres()
        {
            Assert.IsTrue(SoundOcclusion.ShouldProbe(60f));
            Assert.IsFalse(SoundOcclusion.ShouldProbe(60.1f));
        }

        [Test]
        public void Budget_allows_eight_rays_a_frame_and_resets_on_the_next()
        {
            var b = new SoundOcclusion.Budget();
            for (int i = 0; i < SoundOcclusion.RaysPerFrame; i++) Assert.IsTrue(b.TryTake(10), "ray " + i);
            Assert.IsFalse(b.TryTake(10));
            Assert.IsTrue(b.TryTake(11));
        }
    }
}
