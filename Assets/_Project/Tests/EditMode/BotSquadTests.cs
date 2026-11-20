using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-546 (#375, #376): squad regroup target and back-off, the revive excuse for the stall detector, and the noise share's denominator.
    public class BotSquadTests
    {
        static readonly TeamRules Rules = new TeamRules(); // follow 30 m, regroup 60 m

        [Test]
        public void Regroups_only_past_follow_distance_and_not_while_farming_or_backing_off()
        {
            Assert.IsFalse(BotSquad.Regroups(BotState.Roam, false, false, 25f, Rules, out _));
            Assert.IsTrue(BotSquad.Regroups(BotState.Roam, false, false, 35f, Rules, out bool sprint));
            Assert.IsFalse(sprint);
            Assert.IsTrue(BotSquad.Regroups(BotState.Investigate, false, false, 70f, Rules, out sprint));
            Assert.IsTrue(sprint);
            Assert.IsFalse(BotSquad.Regroups(BotState.Roam, true, false, 70f, Rules, out _), "farming is each bot's own");
            Assert.IsFalse(BotSquad.Regroups(BotState.Roam, false, true, 70f, Rules, out _), "backing off after a stalled regroup");
            Assert.IsFalse(BotSquad.Regroups(BotState.Engage, false, false, 70f, Rules, out _), "fighting comes first");
        }

        [Test]
        public void A_looting_bot_leaves_the_pickup_only_when_far()
        {
            Assert.IsFalse(BotSquad.Regroups(BotState.Loot, false, false, 45f, Rules, out _));
            Assert.IsTrue(BotSquad.Regroups(BotState.Loot, false, false, 65f, Rules, out _));
        }

        [Test]
        public void It_walks_to_the_teammate_nearest_the_centre_not_the_centre()
        {
            // Two teammates on either bank of a river: the centre is in the water, the nearer bank's mate is the target.
            var mates = new List<BotVec> { new BotVec(-40, 0, 0), new BotVec(12, 0, 3), new BotVec(30, 0, 30) };
            Assert.AreEqual(1, BotSquad.NearestTo(new BotVec(0, 0, 0), mates));
            Assert.AreEqual(-1, BotSquad.NearestTo(new BotVec(0, 0, 0), new List<BotVec>()));
        }

        [Test]
        public void Reviving_is_not_a_stall()
        {
            Assert.Greater(Rules.ReviveSeconds, 6f, "a revive outlasts the 6 s stall window, so it must be excused");
            Assert.IsTrue(BotSquad.StallExcused(false, false, true, 100f, 6f));
            Assert.IsTrue(BotSquad.StallExcused(true, false, false, 100f, 6f));
            Assert.IsTrue(BotSquad.StallExcused(false, true, false, 100f, 6f));
            Assert.IsTrue(BotSquad.StallExcused(false, false, false, 2f, 6f));
            Assert.IsFalse(BotSquad.StallExcused(false, false, false, 100f, 6f));
        }

        static BotPercept Armed(double t) => new BotPercept
        {
            Time = t, Position = new BotVec(0, 0, 0), HealthFraction = 1f, HasWeapon = true, Mag = 30, MagSize = 30, Reserve = 90, WeaponRange = 100f,
            Heard = true, HeardPosition = new BotVec(40, 0, 0),
        };

        [Test]
        public void A_noise_heard_while_busy_is_not_in_the_share()
        {
            var farming = Armed(1); farming.FarmActive = true;
            var brain = new BotBrain(new BotRules(), 3);
            brain.Think(farming);
            Assert.AreEqual(0, brain.NoiseFreeHearings, "farming bots do not go (ADR-294), so they do not test the roll");

            var unarmed = Armed(1); unarmed.HasWeapon = false;
            brain = new BotBrain(new BotRules(), 3);
            brain.Think(unarmed);
            Assert.AreEqual(0, brain.NoiseFreeHearings);
        }

        [Test]
        public void A_free_bot_tests_its_roll_once_per_noise()
        {
            var deaf = Armed(1); deaf.IgnoresNoise = true;
            var brain = new BotBrain(new BotRules(), 3);
            brain.Think(deaf);
            Assert.AreEqual(1, brain.NoiseFreeHearings);
            Assert.AreEqual(0, brain.NoiseInvestigations);

            var goes = Armed(1);
            brain = new BotBrain(new BotRules(), 3);
            brain.Think(goes);
            goes.Heard = false; goes.Time = 2;
            brain.Think(goes);
            Assert.AreEqual(1, brain.NoiseFreeHearings);
            Assert.AreEqual(1, brain.NoiseInvestigations);
        }

        [Test]
        public void The_measured_share_matches_the_chance_when_every_bot_is_free()
        {
            foreach (float chance in new[] { 0.4f, 0.7f, 1.0f })
            {
                int heard = 0, went = 0, n = 2000;
                for (int seed = 0; seed < n; seed++)
                {
                    var p = Armed(1); p.IgnoresNoise = !BotPriority.Investigates(BotPriority.Roll(seed ^ 0x5bd1e995), chance);
                    var brain = new BotBrain(new BotRules(), seed);
                    brain.Think(p);
                    if (brain.NoiseFreeHearings > 0) heard++;
                    if (brain.NoiseInvestigations > 0) went++;
                }
                Assert.AreEqual(n, heard);
                Assert.AreEqual(chance, went / (float)heard, 0.03f, "chance " + chance);
            }
        }
    }
}
