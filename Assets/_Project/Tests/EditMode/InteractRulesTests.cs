using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-276: what F may reach: same storey, in front, one target per press.
    public class InteractRulesTests
    {
        static readonly InteractRules R = new InteractRules();

        // Player at the origin looking along +z; the pickup at (x, y, z) above the feet.
        static bool Cand(float x, float y, float z, out float bearing) =>
            Interact.Candidate(0f, 0f, 0f, 0f, 0f, 0f, 1f, x, y, z, R, out bearing);

        [Test]
        public void Pickup_in_front_on_the_same_floor_is_a_candidate()
        {
            Assert.IsTrue(Cand(0f, 0.1f, 2f, out float b));
            Assert.AreEqual(0f, b, 0.1f);
        }

        [Test]
        public void Pickup_on_the_storey_above_is_not_a_candidate_even_straight_over_the_head()
        {
            Assert.IsFalse(Cand(0f, 3.1f, 0.5f, out _), "next storey (3 m up) right above");
            Assert.IsFalse(Cand(0.5f, 3.1f, 2f, out _), "next storey, in reach");
        }

        [Test]
        public void Pickup_on_a_table_or_a_step_down_is_a_candidate_but_a_deep_pit_is_not()
        {
            Assert.IsTrue(Cand(0f, 0.9f, 1.5f, out _), "a table");
            Assert.IsTrue(Cand(0f, -0.5f, 1.5f, out _), "a step down");
            Assert.IsFalse(Cand(0f, -2.5f, 1.5f, out _), "storey below");
        }

        [Test]
        public void Pickup_behind_or_far_to_the_side_is_not_a_candidate_unless_at_the_feet()
        {
            Assert.IsFalse(Cand(0f, 0.1f, -2f, out _), "behind");
            Assert.IsFalse(Cand(2f, 0.1f, 0.2f, out _), "off to the side (about 84 degrees)");
            Assert.IsTrue(Cand(-0.5f, 0.1f, -0.4f, out _), "at the feet the view does not matter");
        }

        [Test]
        public void Pickup_out_of_reach_is_not_a_candidate()
        {
            Assert.IsFalse(Cand(0f, 0.1f, 3.6f, out _));
        }

        [Test]
        public void Pickup_nearest_the_middle_of_the_view_ranks_first()
        {
            Cand(0.3f, 0.1f, 2f, out float near);
            Cand(1.6f, 0.1f, 2f, out float wide);
            Assert.Less(Interact.Rank(near, 2f), Interact.Rank(wide, 2f));
        }

        [Test]
        public void Door_in_the_view_does_not_steal_a_pickup_looked_at_but_wins_when_clearly_nearer_the_middle()
        {
            Assert.IsTrue(Interact.PickupBeatsDoor(5f, 3f, R), "looking at the pickup, the door a little off");
            Assert.IsTrue(Interact.PickupBeatsDoor(20f, 20f, R), "tie goes to the pickup");
            Assert.IsFalse(Interact.PickupBeatsDoor(45f, 5f, R), "looking at the door, the pickup at the edge");
        }

        [Test]
        public void Bearing_is_flat_and_signless()
        {
            Assert.AreEqual(90f, Interact.BearingDegrees(0f, 0f, 0f, 1f, 3f, 0f), 0.01f);
            Assert.AreEqual(90f, Interact.BearingDegrees(0f, 0f, 0f, 1f, -3f, 0f), 0.01f);
            Assert.AreEqual(180f, Interact.BearingDegrees(0f, 0f, 0f, 0f, 3f, 0f), 0.01f, "no view direction");
        }
    }
}
