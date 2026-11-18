using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-310 (#374): empty-handed fights, running out of sight, going to where somebody fell.
    public class BotBrawlTests
    {
        static readonly BotVec Origin = new BotVec(0, 0, 0);

        static BotPercept Unarmed(float enemyDistance, bool enemyArmed) => new BotPercept
        {
            Time = 1, Position = Origin, HealthFraction = 1f, EnemyVisible = true, EnemyId = 9,
            EnemyPosition = new BotVec(enemyDistance, 0, 0), EnemyArmed = enemyArmed,
        };

        static BotBrain Brain() => new BotBrain(new BotRules(), 11);

        [Test]
        public void Unarmed_against_an_unarmed_enemy_closes_in_for_a_fist_fight()
        {
            var intent = Brain().Think(Unarmed(10f, false));
            Assert.AreEqual(BotState.Engage, intent.State);
            Assert.IsTrue(intent.Move);
            Assert.AreEqual(10f, intent.MoveTarget.X);
            Assert.IsFalse(intent.Fire);
            Assert.IsTrue(intent.Sprint, "far enough to run at it");
        }

        [Test]
        public void The_first_minutes_are_peaceful_beyond_a_few_metres_unless_hurt()
        {
            var rules = new BotRules();
            Assert.IsTrue(BotPeace.Holds(10, 1000, rules));
            Assert.IsFalse(BotPeace.Holds(rules.EarlyPeaceSeconds + 1, 1000, rules), "after the early minutes");
            Assert.IsFalse(BotPeace.Holds(10, 3, rules), "just hurt");
            Assert.IsFalse(BotPeace.Holds(-1, 1000, rules), "not dropped yet: no peace to speak of");
            Assert.IsTrue(BotPeace.Ignores(true, rules.EarlyPeaceMeters + 1f, false, rules));
            Assert.IsFalse(BotPeace.Ignores(true, rules.EarlyPeaceMeters - 1f, false, rules), "an armed one right next to it is fought");
            Assert.IsTrue(BotPeace.Ignores(true, 0.5f, true, rules), "two with empty hands do not start a fist fight");
            Assert.IsFalse(BotPeace.Ignores(false, 50f, true, rules));
        }

        [Test]
        public void Unarmed_leaves_a_far_unarmed_enemy_be()
        {
            var rules = new BotRules();
            var p = Unarmed(rules.UnarmedFistChaseMeters + 8f, false);
            p.LootVisible = true; p.LootIsWeapon = true; p.LootId = 3; p.LootPosition = new BotVec(0, 0, -20);
            var intent = new BotBrain(rules, 11).Think(p);
            Assert.AreEqual(BotState.Loot, intent.State, "it goes on to the weapon instead of running at the enemy");
            Assert.AreEqual(3, intent.LootId);
            Assert.AreEqual(BotState.Engage, new BotBrain(rules, 11).Think(Unarmed(rules.UnarmedFistChaseMeters - 2f, false)).State, "near enough: fists");
        }

        [Test]
        public void Unarmed_against_an_armed_enemy_runs_unless_it_is_within_a_punch()
        {
            Assert.AreEqual(BotState.Retreat, Brain().Think(Unarmed(15f, true)).State);
            Assert.AreEqual(BotState.Engage, Brain().Think(Unarmed(1.2f, true)).State, "inside 1.35 m the punch is the better bet");
        }

        [Test]
        public void Running_from_an_armed_enemy_picks_up_a_weapon_that_lies_close()
        {
            var p = Unarmed(15f, true);
            p.LootVisible = true; p.LootIsWeapon = true; p.LootId = 4; p.LootPosition = new BotVec(-5, 0, 0);
            var intent = Brain().Think(p);
            Assert.AreEqual(BotState.Loot, intent.State);
            Assert.AreEqual(4, intent.LootId);
            Assert.IsFalse(intent.PickUp, "5 m away: still running to it");
            p.LootPosition = new BotVec(-1.5f, 0, 0);
            Assert.IsTrue(Brain().Think(p).PickUp);
            p.LootPosition = new BotVec(-30f, 0, 0);
            Assert.AreEqual(BotState.Retreat, Brain().Think(p).State, "too far to be worth the dash");
            p.LootPosition = new BotVec(-5, 0, 0); p.LootIsWeapon = false;
            Assert.AreEqual(BotState.Retreat, Brain().Think(p).State, "only a weapon is worth it");
        }

        [Test]
        public void A_weapon_close_by_comes_before_a_fist_fight()
        {
            var p = Unarmed(10f, false);
            p.LootVisible = true; p.LootIsWeapon = true; p.LootId = 4; p.LootPosition = new BotVec(-4, 0, 0);
            Assert.AreEqual(BotState.Loot, Brain().Think(p).State);
        }

        [Test]
        public void A_bot_with_a_gun_is_not_made_to_fight_with_fists()
        {
            var p = Unarmed(10f, false);
            p.HasWeapon = true; p.Mag = 30; p.MagSize = 30; p.Reserve = 90; p.WeaponRange = 100f;
            var intent = Brain().Think(p);
            Assert.AreEqual(BotState.Engage, intent.State);
            Assert.IsTrue(intent.Aim);
        }

        static FleeCandidate At(float x, float z, bool hidden, float walk) => new FleeCandidate { Point = new BotVec(x, 0, z), Hidden = hidden, WalkMeters = walk };

        [Test]
        public void Fleeing_prefers_a_place_out_of_sight_and_then_the_nearest_one()
        {
            var threat = new BotVec(20, 0, 0);
            var list = new List<FleeCandidate> { At(-15, 0, false, 15), At(-4, 4, true, 6), At(-8, -8, true, 11) };
            Assert.AreEqual(1, BotFlee.Pick(list, Origin, threat));
        }

        [Test]
        public void Fleeing_never_picks_a_place_that_runs_at_the_threat()
        {
            var threat = new BotVec(20, 0, 0);
            var list = new List<FleeCandidate> { At(15, 0, true, 15), At(-5, 0, false, 5) };
            Assert.AreEqual(1, BotFlee.Pick(list, Origin, threat), "the hidden one is 5 m nearer the threat");
            Assert.AreEqual(-1, BotFlee.Pick(new List<FleeCandidate> { At(15, 0, true, 15) }, Origin, threat));
        }

        [Test]
        public void With_nothing_hidden_it_opens_the_most_distance()
        {
            var threat = new BotVec(20, 0, 0);
            var list = new List<FleeCandidate> { At(-4, 0, false, 4), At(-13, 0, false, 13), At(0, 12, false, 12) };
            Assert.AreEqual(1, BotFlee.Pick(list, Origin, threat));
        }

        [Test]
        public void A_fallen_bots_loot_is_approached_only_when_it_is_safe_and_needed()
        {
            var rules = new BotRules();
            Assert.IsTrue(BotCorpse.Approach(true, 20f, 10, 30, false, rules));
            Assert.IsFalse(BotCorpse.Approach(false, 20f, 10, 30, false, rules), "already kitted out");
            Assert.IsFalse(BotCorpse.Approach(true, 20f, 10, 2, false, rules), "shots just heard near it");
            Assert.IsFalse(BotCorpse.Approach(true, 20f, 10, 30, true, rules), "an enemy in sight or just seen");
            Assert.IsFalse(BotCorpse.Approach(true, rules.CorpseLootMeters + 1f, 10, 30, false, rules), "too far");
            Assert.IsFalse(BotCorpse.Approach(true, 20f, rules.CorpseMemorySeconds + 1, 30, false, rules), "too long ago");
            Assert.Less(BotCorpse.Rank(10f, 5), BotCorpse.Rank(10f, 50));
        }

        [Test]
        public void A_bot_on_a_corpse_trip_walks_to_it_when_nothing_else_is_going_on()
        {
            var p = new BotPercept { Time = 1, Position = Origin, HealthFraction = 1f, CorpseActive = true, CorpseTarget = new BotVec(5, 0, 5) };
            var intent = Brain().Think(p);
            Assert.AreEqual(BotState.Roam, intent.State);
            Assert.AreEqual(5f, intent.MoveTarget.X);
            p.FarmActive = true; p.FarmTarget = new BotVec(-9, 0, 0);
            Assert.AreEqual(-9f, Brain().Think(p).MoveTarget.X, "farming comes first");
        }
    }
}
