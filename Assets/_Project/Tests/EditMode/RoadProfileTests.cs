using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #248 T7: the grade-limited road height (cut and fill) and the switchback count for a climb the straight way cannot make.
    public class RoadProfileTests
    {
        static float[] Distances(int n, float step) => Enumerable.Range(0, n).Select(i => i * step).ToArray();

        [Test]
        public void A_road_already_within_the_limit_follows_the_ground()
        {
            var d = Distances(21, 10f);
            var ground = d.Select(x => x * 0.05f).ToArray(); // 2.9 degrees
            var road = RoadProfile.Solve(d, ground, 12f, out bool feasible);
            Assert.IsTrue(feasible);
            CollectionAssert.AreEqual(ground, road);
        }

        [Test]
        public void A_bump_steeper_than_the_limit_is_cut_down_and_the_ends_stay_on_the_ground()
        {
            var d = Distances(41, 5f);                 // 200 m
            var ground = d.Select(x => x >= 90f && x <= 110f ? 12f * (1f - System.Math.Abs(x - 100f) / 10f) : 0f).ToArray(); // a 12 m spike, 50 degrees
            var road = RoadProfile.Solve(d, ground, 12f, out bool feasible);
            Assert.IsTrue(feasible);
            Assert.LessOrEqual(RoadProfile.MaxGradeDeg(d, road), 12.01f);
            Assert.AreEqual(ground[0], road[0]);
            Assert.AreEqual(ground[40], road[40], 1e-3f);
            Assert.Less(road[20], ground[20] - 1f, "the top of the spike is cut");
            Assert.LessOrEqual(road.Max(), ground.Max() - 1f, "the road never reaches the spike's top; its ramps stand a little above the ground beside it (fill)");
        }

        [Test]
        public void A_hollow_is_filled_up()
        {
            var d = Distances(41, 5f);
            var ground = d.Select(x => x >= 90f && x <= 110f ? -8f * (1f - System.Math.Abs(x - 100f) / 10f) : 0f).ToArray();
            var road = RoadProfile.Solve(d, ground, 12f, out bool feasible);
            Assert.IsTrue(feasible);
            Assert.LessOrEqual(RoadProfile.MaxGradeDeg(d, road), 12.01f);
            Assert.Greater(road[20], ground[20] + 1f);
        }

        [Test]
        public void A_climb_the_length_cannot_make_is_reported_and_needs_switchbacks()
        {
            var d = Distances(21, 5f);                 // 100 m
            var ground = d.Select(x => x * 0.35f).ToArray(); // 35 m climb: 19 degrees
            var road = RoadProfile.Solve(d, ground, 12f, out bool feasible);
            Assert.IsFalse(feasible);
            Assert.LessOrEqual(RoadProfile.MaxGradeDeg(d, road), 12.01f);
            Assert.AreEqual(164.7f, RoadProfile.RequiredLength(35f, 12f), 0.2f);
            Assert.AreEqual(2, RoadProfile.Hairpins(35f, 100f, 12f, 40f), "65 m missing, 40 m per hairpin");
            Assert.AreEqual(0, RoadProfile.Hairpins(35f, 200f, 12f, 40f));
        }

        [Test]
        public void Bad_input_throws()
        {
            Assert.Throws<System.ArgumentException>(() => RoadProfile.Solve(new float[] { 0 }, new float[] { 0 }, 12f, out _));
            Assert.Throws<System.ArgumentException>(() => RoadProfile.Solve(new float[] { 0, 1 }, new float[] { 0 }, 12f, out _));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => RoadProfile.Solve(new float[] { 0, 1 }, new float[] { 0, 0 }, 0f, out _));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => RoadProfile.Hairpins(10f, 10f, 12f, 0f));
        }
    }
}
