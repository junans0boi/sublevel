using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-294 (#373): the pure rules of farming.
    public class BotFarmTests
    {
        static BuildingLot Lot(float x, float z, BuildingKind kind) => new BuildingLot { Kind = kind, X = x, Z = z, SizeX = 10f, SizeZ = 10f };

        // Three buildings along x: a house at 0, a barracks at 60, a house at 200; three loot points each.
        static BuildingLootIndex World()
        {
            var lots = new[] { Lot(0, 0, BuildingKind.House), Lot(60, 0, BuildingKind.Barracks), Lot(200, 0, BuildingKind.House) };
            var xs = new List<float>(); var zs = new List<float>();
            foreach (var lot in lots) foreach (float dx in new[] { -3f, 0f, 3f }) { xs.Add(lot.X + dx); zs.Add(0f); }
            return new BuildingLootIndex(lots, xs, zs);
        }

        [Test]
        public void Ready_needs_a_gun_and_the_levels_extras()
        {
            var normal = new BotRules();
            Assert.IsFalse(BotFarm.Ready(false, 100, 30, 2, 5, normal), "no gun");
            Assert.IsFalse(BotFarm.Ready(true, 29, 30, 2, 5, normal), "less than one magazine of rounds");
            Assert.IsFalse(BotFarm.Ready(true, 60, 30, 2, 0, normal), "normal wants a healing item");
            Assert.IsTrue(BotFarm.Ready(true, 30, 30, 0, 1, normal));
            var hard = BotLevels.For(normal, BotDifficulty.Hard);
            Assert.IsFalse(BotFarm.Ready(true, 60, 30, 1, 2, hard), "hard wants both armour pieces");
            Assert.IsTrue(BotFarm.Ready(true, 60, 30, 2, 2, hard));
            Assert.IsTrue(BotFarm.Ready(true, 30, 30, 0, 0, BotLevels.For(normal, BotDifficulty.Easy)), "easy: a gun and a magazine");
        }

        [Test]
        public void The_levels_set_the_farming_time_and_the_weapon_hint()
        {
            var normal = new BotRules();
            Assert.AreEqual(210f, BotLevels.For(normal, BotDifficulty.Normal).FarmSeconds, 0.01f);
            Assert.AreEqual(240f, BotLevels.For(normal, BotDifficulty.Easy).FarmSeconds, 0.01f);
            Assert.AreEqual(180f, BotLevels.For(normal, BotDifficulty.Hard).FarmSeconds, 0.01f);
            Assert.IsTrue(BotLevels.For(normal, BotDifficulty.Easy).FarmHint);
            Assert.IsTrue(normal.FarmHint);
            Assert.IsTrue(BotLevels.For(normal, BotDifficulty.Hard).FarmHint, "ADR-321: every level gets the hint");
            Assert.AreEqual(210f, BotLevels.For(normal, BotDifficulty.Easy).FarmHintSeconds, 0.01f);
            Assert.AreEqual(25f, BotLevels.For(normal, BotDifficulty.Hard).FarmHintSeconds, 0.01f, "ADR-548: hard arms clearly sooner");
        }

        [Test]
        public void An_unarmed_bot_gets_the_weapon_hint_after_farming_or_after_the_hint_time()
        {
            var r = new BotRules();
            Assert.IsFalse(BotFarm.HintDue(false, false, r.FarmHintSeconds - 1f, r), "still farming, early");
            Assert.IsTrue(BotFarm.HintDue(false, false, r.FarmHintSeconds, r), "still farming, hint time reached");
            Assert.IsTrue(BotFarm.HintDue(false, true, 0f, r), "farming over");
            Assert.IsFalse(BotFarm.HintDue(true, true, 999f, r), "armed");
            r.FarmHint = false;
            Assert.IsFalse(BotFarm.HintDue(false, true, 999f, r), "hint off");
        }

        [Test]
        public void A_bot_picks_a_near_rich_building_and_not_one_already_visited()
        {
            var index = World();
            var rules = new BotRules();
            // At x = 20: the house at 0 is 20 m away, the barracks (weight 2.2) 40 m: the barracks wins on score.
            Assert.AreEqual(1, BotFarm.PickBuilding(index, 20f, 0f, b => 0, null, rules, new Random(1)));
            Assert.AreEqual(0, BotFarm.PickBuilding(index, 20f, 0f, b => 0, new HashSet<int> { 1 }, rules, new Random(1)));
            Assert.AreEqual(2, BotFarm.PickBuilding(index, 20f, 0f, b => 0, new HashSet<int> { 0, 1 }, rules, new Random(1)));
            Assert.AreEqual(-1, BotFarm.PickBuilding(index, 20f, 0f, b => 0, new HashSet<int> { 0, 1, 2 }, rules, new Random(1)));
        }

        [Test]
        public void A_building_with_the_cap_of_other_bots_is_passed_over_while_another_is_free()
        {
            var index = World();
            var rules = new BotRules();
            Assert.AreEqual(0, BotFarm.PickBuilding(index, 20f, 0f, b => b == 1 ? rules.FarmBuildingCap : 0, null, rules, new Random(1)));
            // Everything full: the best of the full ones rather than nothing.
            Assert.AreEqual(1, BotFarm.PickBuilding(index, 20f, 0f, b => rules.FarmBuildingCap, null, rules, new Random(1)));
        }

        [Test]
        public void Bots_crowding_one_drop_spread_over_buildings()
        {
            var index = World();
            var rules = new BotRules();
            var counts = new int[3];
            var rng = new Random(5);
            for (int bot = 0; bot < 6; bot++)
            {
                int b = BotFarm.PickBuilding(index, 20f, 0f, k => counts[k], null, rules, rng);
                counts[b]++;
            }
            Assert.LessOrEqual(Array.IndexOf(counts, 3), -1, "no building gets three: " + string.Join(",", counts));
        }

        [Test]
        public void The_loot_points_of_a_building_are_walked_nearest_first_and_run_out()
        {
            var index = World();
            var visited = new HashSet<int>();
            float x = -10f;
            var order = new List<int>();
            for (int s = BotFarm.NextSpawn(index, 0, x, 0f, visited); s >= 0; s = BotFarm.NextSpawn(index, 0, x, 0f, visited))
            {
                order.Add(s); visited.Add(s);
                index.SpawnAt(s, out x, out _, out _);
            }
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, order);
            Assert.AreEqual(-1, BotFarm.NextSpawn(index, 0, 0f, 0f, visited));
        }

        [Test]
        public void A_loot_point_is_reached_close_by_and_on_the_same_floor()
        {
            Assert.IsTrue(BotFarm.Reached(0f, 0f, 0f, 2f, 0.5f, 0f, 2.5f));
            Assert.IsFalse(BotFarm.Reached(0f, 0f, 0f, 2f, 4f, 0f, 2.5f), "another floor");
            Assert.IsFalse(BotFarm.Reached(0f, 0f, 0f, 4f, 0f, 0f, 2.5f));
        }

        [Test]
        public void A_farming_bot_walks_to_its_loot_point_and_does_not_chase_far_noises()
        {
            var brain = new BotBrain(new BotRules(), 3);
            var p = new BotPercept { Time = 1, HealthFraction = 1f, FarmActive = true, FarmTarget = new BotVec(10, 0, 20), HasWeapon = true, Mag = 30, MagSize = 30, Reserve = 90, WeaponRange = 100 };
            p.Heard = true; p.HeardPosition = new BotVec(-50, 0, 0);
            var intent = brain.Think(p);
            Assert.AreEqual(BotState.Roam, intent.State);
            Assert.IsTrue(intent.Move);
            Assert.AreEqual(10f, intent.MoveTarget.X); Assert.AreEqual(20f, intent.MoveTarget.Z);
            // Done farming: the noise is looked into again.
            p.FarmActive = false; p.Time = 2; p.Heard = true;
            Assert.AreEqual(BotState.Investigate, brain.Think(p).State);
        }

        [Test]
        public void Something_on_the_floor_still_comes_before_the_next_loot_point()
        {
            var brain = new BotBrain(new BotRules(), 3);
            var p = new BotPercept { Time = 1, HealthFraction = 1f, FarmActive = true, FarmTarget = new BotVec(10, 0, 20), LootVisible = true, LootId = 7, LootPosition = new BotVec(3, 0, 3) };
            var intent = brain.Think(p);
            Assert.AreEqual(BotState.Loot, intent.State);
            Assert.AreEqual(7, intent.LootId);
        }
    }
}
