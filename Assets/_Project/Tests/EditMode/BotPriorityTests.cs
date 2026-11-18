using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-312 (#376): the order of what a bot is after, and how many bots go to noises.
    public class BotPriorityTests
    {
        static PriorityInput In(bool escape = false, bool combat = false, bool farming = false, bool noise = false, bool investigates = true, bool gather = false) =>
            new PriorityInput { EscapeUrgent = escape, Combat = combat, Farming = farming, NoiseHeard = noise, Investigates = investigates, GatherGoal = gather };

        [Test]
        public void The_order_is_escape_then_combat_then_farm_then_noise_then_gather_then_travel()
        {
            Assert.AreEqual(BotGoal.Escape, BotPriority.Choose(In(escape: true, combat: true, farming: true, noise: true, gather: true)));
            Assert.AreEqual(BotGoal.Combat, BotPriority.Choose(In(combat: true, farming: true, noise: true, gather: true)));
            Assert.AreEqual(BotGoal.Farm, BotPriority.Choose(In(farming: true, noise: true, gather: true)));
            Assert.AreEqual(BotGoal.Investigate, BotPriority.Choose(In(noise: true, gather: true)));
            Assert.AreEqual(BotGoal.Gather, BotPriority.Choose(In(gather: true)));
            Assert.AreEqual(BotGoal.Travel, BotPriority.Choose(In()));
        }

        [Test]
        public void A_bot_that_does_not_go_to_noises_skips_that_step()
        {
            Assert.AreEqual(BotGoal.Gather, BotPriority.Choose(In(noise: true, investigates: false, gather: true)));
            Assert.AreEqual(BotGoal.Travel, BotPriority.Choose(In(noise: true, investigates: false)));
        }

        [Test]
        public void The_share_of_bots_that_go_to_noises_follows_the_levels_chance()
        {
            var normal = new BotRules();
            Assert.AreEqual(0.7f, normal.InvestigateChance, 1e-6f);
            Assert.AreEqual(0.4f, BotLevels.For(normal, BotDifficulty.Easy).InvestigateChance, 1e-6f);
            Assert.AreEqual(1.0f, BotLevels.For(normal, BotDifficulty.Hard).InvestigateChance, 1e-6f);
            foreach (float chance in new[] { 0.4f, 0.7f, 1.0f })
            {
                int goes = 0, n = 20000;
                for (int seed = 0; seed < n; seed++) if (BotPriority.Investigates(BotPriority.Roll(seed * 7919 + 13), chance)) goes++;
                Assert.AreEqual(chance, goes / (float)n, 0.02f, "chance " + chance);
            }
            Assert.AreEqual(BotPriority.Roll(42), BotPriority.Roll(42), "a bot's number never changes");
        }

        [Test]
        public void A_bot_that_ignores_noises_does_not_go_to_one_but_still_fights()
        {
            var p = new BotPercept
            {
                Time = 1, Position = new BotVec(0, 0, 0), HealthFraction = 1f, HasWeapon = true, Mag = 30, MagSize = 30, Reserve = 90, WeaponRange = 100f,
                Heard = true, HeardPosition = new BotVec(40, 0, 0),
            };
            Assert.AreEqual(BotState.Investigate, new BotBrain(new BotRules(), 3).Think(p).State, "goes by default");
            var deaf = p; deaf.IgnoresNoise = true;
            var brain = new BotBrain(new BotRules(), 3);
            Assert.AreEqual(BotState.Roam, brain.Think(deaf).State);
            Assert.AreEqual(0, brain.NoiseInvestigations);
            var seen = deaf; seen.EnemyVisible = true; seen.EnemyId = 2; seen.EnemyPosition = new BotVec(30, 0, 0); seen.Heard = false;
            Assert.AreEqual(BotState.Engage, brain.Think(seen).State);
        }

        [Test]
        public void A_bot_that_goes_to_noises_is_counted_once()
        {
            var p = new BotPercept { Time = 1, Position = new BotVec(0, 0, 0), HealthFraction = 1f, HasWeapon = true, Mag = 30, MagSize = 30, Reserve = 90, WeaponRange = 100f, Heard = true, HeardPosition = new BotVec(40, 0, 0) };
            var brain = new BotBrain(new BotRules(), 3);
            brain.Think(p);
            p.Heard = false; p.Time = 2;
            brain.Think(p);
            Assert.AreEqual(1, brain.NoiseInvestigations);
        }
    }
}
