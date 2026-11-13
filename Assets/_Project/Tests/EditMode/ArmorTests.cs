using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // Helmet and vest rules: what a piece takes off a hit, how it wears down and breaks, which piece is worth
    // swapping for, and how armour travels as loot. Numbers are the defaults in ArmorRules.
    public class ArmorTests
    {
        static readonly ArmorRules Rules = new ArmorRules();

        [Test]
        public void Defaults_ThreeTiersEach_GettingStrongerAndTougher()
        {
            Assert.AreEqual(3, Rules.TierCount(ArmorSlot.Helmet));
            Assert.AreEqual(3, Rules.TierCount(ArmorSlot.Vest));
            // PUBG (ADR-091): 30/40/55% for both; helmets 80/150/230, vests 200/220/250.
            float[] reduction = { 0.30f, 0.40f, 0.55f }, helmet = { 80f, 150f, 230f }, vest = { 200f, 220f, 250f };
            for (int tier = 1; tier <= 3; tier++)
            {
                Assert.AreEqual(reduction[tier - 1], Rules.Find(ArmorSlot.Helmet, tier).Reduction, 1e-6f);
                Assert.AreEqual(reduction[tier - 1], Rules.Find(ArmorSlot.Vest, tier).Reduction, 1e-6f);
                Assert.AreEqual(helmet[tier - 1], Rules.Find(ArmorSlot.Helmet, tier).Durability, 1e-6f);
                Assert.AreEqual(vest[tier - 1], Rules.Find(ArmorSlot.Vest, tier).Durability, 1e-6f);
            }
            Assert.AreEqual(0.20f, Rules.BrokenVestReduction, 1e-6f);
            foreach (var slot in new[] { ArmorSlot.Helmet, ArmorSlot.Vest })
            {
                for (int tier = 2; tier <= 3; tier++)
                {
                    Assert.Greater(Rules.Find(slot, tier).Reduction, Rules.Find(slot, tier - 1).Reduction, $"{slot} tier {tier}");
                    Assert.Greater(Rules.Find(slot, tier).Durability, Rules.Find(slot, tier - 1).Durability, $"{slot} tier {tier}");
                }
                Assert.Less(Rules.Find(slot, 3).Reduction, 1f, "armour never stops a hit completely");
            }
            Assert.IsNull(Rules.Find(ArmorSlot.Helmet, 0));
            Assert.IsNull(Rules.Find(ArmorSlot.Vest, 4));
            Assert.AreEqual(0f, Rules.MaxDurability(ArmorSlot.Helmet, 0));
            Assert.AreEqual(250f, Rules.MaxDurability(ArmorSlot.Vest, 3), 1e-6f);
        }

        [Test]
        public void Helmet_TakesItsShareOfAHeadHit_AndLosesThatMuchDurability()
        {
            var helmet = ArmorPiece.Fresh(ArmorSlot.Helmet, 2, Rules);
            Assert.AreEqual(2, helmet.Tier);
            Assert.AreEqual(150f, helmet.Durability, 1e-4f);

            // A 54-point head hit (rifle 27 x 2.0) on a tier 2 helmet: 40% = 21.6 absorbed.
            float through = Armor.Absorb(ref helmet, ArmorSlot.Helmet, 54f, Rules);
            Assert.AreEqual(32.4f, through, 1e-4f);
            Assert.AreEqual(128.4f, helmet.Durability, 1e-4f);
            Assert.AreEqual(2, helmet.Tier);

            through = Armor.Absorb(ref helmet, ArmorSlot.Helmet, 10f, Rules);
            Assert.AreEqual(6f, through, 1e-4f);
            Assert.AreEqual(124.4f, helmet.Durability, 1e-4f);
        }

        [Test]
        public void Vest_TakesItsShareOfABodyHit()
        {
            var vest = ArmorPiece.Fresh(ArmorSlot.Vest, 1, Rules);
            float through = Armor.Absorb(ref vest, ArmorSlot.Vest, 40f, Rules);
            Assert.AreEqual(28f, through, 1e-4f, "tier 1 takes 30%");
            Assert.AreEqual(188f, vest.Durability, 1e-4f, "200 - 12");

            var best = ArmorPiece.Fresh(ArmorSlot.Vest, 3, Rules);
            through = Armor.Absorb(ref best, ArmorSlot.Vest, 40f, Rules);
            Assert.AreEqual(18f, through, 1e-4f, "tier 3 takes 55%");
            Assert.AreEqual(228f, best.Durability, 1e-4f, "250 - 22");
        }

        [Test]
        public void AHelmetIsGoneAtZero_AndTakesNoMoreThanItHasLeft()
        {
            var helmet = new ArmorPiece { Tier = 1, Durability = 5f };
            // 30% of 40 would be 12, but only 5 is left: 35 gets through and the helmet is gone.
            float through = Armor.Absorb(ref helmet, ArmorSlot.Helmet, 40f, Rules);
            Assert.AreEqual(35f, through, 1e-4f);
            Assert.AreEqual(0, helmet.Tier, "broken");
            Assert.AreEqual(0f, helmet.Durability);
            Assert.IsFalse(helmet.Worn);

            // Nothing worn: the hit lands in full.
            Assert.AreEqual(40f, Armor.Absorb(ref helmet, ArmorSlot.Helmet, 40f, Rules), 1e-6f);
        }

        [Test]
        public void ABrokenVestStaysOn_AndStillTakesTwentyPercent_WithoutWearing()
        {
            // Exactly used up: 30% of 40 = 12 absorbed, the vest is at zero but still worn (ADR-091).
            var vest = new ArmorPiece { Tier = 1, Durability = 12f };
            Assert.AreEqual(28f, Armor.Absorb(ref vest, ArmorSlot.Vest, 40f, Rules), 1e-4f);
            Assert.IsTrue(vest.Worn, "a broken vest stays on");
            Assert.IsTrue(vest.Broken);
            Assert.AreEqual(1, vest.Tier);
            Assert.AreEqual(0f, vest.Durability);

            // From now on 20% of every hit, whatever the tier, and it wears no further.
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(32f, Armor.Absorb(ref vest, ArmorSlot.Vest, 40f, Rules), 1e-4f, "hit " + i);
            Assert.IsTrue(vest.Broken);
            var t3 = new ArmorPiece { Tier = 3, Durability = 0f };
            Assert.AreEqual(80f, Armor.Absorb(ref t3, ArmorSlot.Vest, 100f, Rules), 1e-4f, "tier 3 broken: also 20%");

            // More left than the hit needs breaks it partway: what it had left is absorbed, the rest passes.
            var nearly = new ArmorPiece { Tier = 3, Durability = 5f };
            Assert.AreEqual(35f, Armor.Absorb(ref nearly, ArmorSlot.Vest, 40f, Rules), 1e-4f);
            Assert.IsTrue(nearly.Broken);

            // Dropped and picked up again it is still the broken vest, not a fresh one (0 in an entry means fresh).
            var entry = Armor.ToLoot(ArmorSlot.Vest, vest);
            Assert.Greater(entry.Durability, 0f);
            var back = Armor.FromLoot(entry, Rules);
            Assert.AreEqual(1, back.Tier);
            Assert.IsTrue(back.Broken, "still broken after the ground");
        }

        [Test]
        public void WearingDown_OverManyHits_EndsWithNoPiece()
        {
            var helmet = ArmorPiece.Fresh(ArmorSlot.Helmet, 1, Rules); // 80 durability, 20 per 66.67-point hit
            const float hit = 200f / 3f;
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(hit - 20f, Armor.Absorb(ref helmet, ArmorSlot.Helmet, hit, Rules), 1e-3f, "hit " + i);
                Assert.IsTrue(helmet.Worn, "hit " + i);
            }
            Assert.AreEqual(20f, helmet.Durability, 1e-3f);
            Assert.AreEqual(hit - 20f, Armor.Absorb(ref helmet, ArmorSlot.Helmet, hit, Rules), 1e-3f);
            Assert.IsFalse(helmet.Worn, "the fourth hit uses it up");
            Assert.AreEqual(60f, Armor.Absorb(ref helmet, ArmorSlot.Helmet, 60f, Rules), 1e-6f);
        }

        [Test]
        public void NoDamage_OrNonsense_LeavesThePieceAlone()
        {
            var vest = ArmorPiece.Fresh(ArmorSlot.Vest, 2, Rules);
            Assert.AreEqual(0f, Armor.Absorb(ref vest, ArmorSlot.Vest, 0f, Rules));
            Assert.AreEqual(-5f, Armor.Absorb(ref vest, ArmorSlot.Vest, -5f, Rules));
            Assert.IsTrue(float.IsNaN(Armor.Absorb(ref vest, ArmorSlot.Vest, float.NaN, Rules)));
            Assert.AreEqual(220f, vest.Durability, 1e-6f);

            // A tier the rules do not know protects nothing.
            var odd = new ArmorPiece { Tier = 9, Durability = 50f };
            Assert.AreEqual(40f, Armor.Absorb(ref odd, ArmorSlot.Vest, 40f, Rules), 1e-6f);
            Assert.AreEqual(50f, odd.Durability, 1e-6f);
            Assert.IsFalse(ArmorPiece.Fresh(ArmorSlot.Vest, 9, Rules).Worn);
        }

        [Test]
        public void HelmetCoversTheHead_VestTheBody_LimbsAreBare()
        {
            Assert.IsTrue(Armor.TrySlotFor(HitZone.Head, DamageCause.Shot, out var slot));
            Assert.AreEqual(ArmorSlot.Helmet, slot);
            Assert.IsTrue(Armor.TrySlotFor(HitZone.Body, DamageCause.Shot, out slot));
            Assert.AreEqual(ArmorSlot.Vest, slot);
            Assert.IsFalse(Armor.TrySlotFor(HitZone.Limb, DamageCause.Shot, out _), "limbs are unprotected");

            Assert.IsTrue(Armor.TrySlotFor(HitZone.Body, DamageCause.Blast, out slot), "blasts are stopped like shots");
            Assert.AreEqual(ArmorSlot.Vest, slot);
            Assert.IsTrue(Armor.TrySlotFor(HitZone.Head, DamageCause.Blast, out slot));
            Assert.AreEqual(ArmorSlot.Helmet, slot);
            Assert.IsFalse(Armor.TrySlotFor(HitZone.Limb, DamageCause.Blast, out _));

            Assert.IsFalse(Armor.TrySlotFor(HitZone.Body, DamageCause.Fall, out _), "armour does not soften a fall");
            Assert.IsFalse(Armor.TrySlotFor(HitZone.Head, DamageCause.Zone, out _), "nor the zone");
        }

        [Test]
        public void Better_MeansHigherTier_OrSameTierInBetterShape()
        {
            var none = ArmorPiece.None;
            var t1 = new ArmorPiece { Tier = 1, Durability = 60f };
            var t2Worn = new ArmorPiece { Tier = 2, Durability = 20f };
            var t2 = new ArmorPiece { Tier = 2, Durability = 110f };
            var t3 = new ArmorPiece { Tier = 3, Durability = 1f };

            Assert.IsTrue(Armor.IsBetter(t1, none), "anything beats nothing");
            Assert.IsFalse(Armor.IsBetter(none, none));
            Assert.IsFalse(Armor.IsBetter(none, t1));
            Assert.IsTrue(Armor.IsBetter(t2Worn, t1), "tier first");
            Assert.IsFalse(Armor.IsBetter(t1, t2Worn));
            Assert.IsTrue(Armor.IsBetter(t2, t2Worn), "same tier: more durability");
            Assert.IsFalse(Armor.IsBetter(t2Worn, t2));
            Assert.IsFalse(Armor.IsBetter(t2, t2), "the same piece is not better");
            Assert.IsTrue(Armor.IsBetter(t3, t2));
            var broken3 = new ArmorPiece { Tier = 3, Durability = 0f };
            var broken1 = new ArmorPiece { Tier = 1, Durability = 0f };
            Assert.IsFalse(Armor.IsBetter(broken3, t1), "a whole piece beats a broken one, whatever the tiers");
            Assert.IsTrue(Armor.IsBetter(t1, broken3));
            Assert.IsTrue(Armor.IsBetter(broken3, none), "a broken vest still beats nothing");
            Assert.IsFalse(Armor.IsBetter(broken3, broken1), "broken is broken: both take 20%");
            Assert.IsFalse(Armor.IsBetter(broken1, broken3));
        }

        [Test]
        public void ArmourAsLoot_FreshFromATable_KeepsItsWearWhenDropped()
        {
            Assert.IsTrue(Armor.IsArmor(LootKind.Helmet));
            Assert.IsTrue(Armor.IsArmor(LootKind.Vest));
            Assert.IsFalse(Armor.IsArmor(LootKind.Weapon));
            Assert.IsFalse(Armor.IsArmor(LootKind.Ammo));
            Assert.IsFalse(Armor.IsArmor(LootKind.Medkit));
            Assert.AreEqual(ArmorSlot.Helmet, Armor.SlotOf(LootKind.Helmet));
            Assert.AreEqual(ArmorSlot.Vest, Armor.SlotOf(LootKind.Vest));
            Assert.AreEqual(LootKind.Helmet, Armor.KindOf(ArmorSlot.Helmet));
            Assert.AreEqual(LootKind.Vest, Armor.KindOf(ArmorSlot.Vest));

            // A table row carries no durability: the piece is fresh.
            var fresh = Armor.FromLoot(new LootEntry { Kind = LootKind.Vest, Tier = 3 }, Rules);
            Assert.AreEqual(3, fresh.Tier);
            Assert.AreEqual(250f, fresh.Durability, 1e-6f);

            // A dropped piece keeps what it had left, there and back.
            var worn = new ArmorPiece { Tier = 2, Durability = 42.5f };
            var entry = Armor.ToLoot(ArmorSlot.Helmet, worn);
            Assert.AreEqual(LootKind.Helmet, entry.Kind);
            Assert.AreEqual(2, entry.Tier);
            Assert.AreEqual(42.5f, entry.Durability, 1e-6f);
            var back = Armor.FromLoot(entry, Rules);
            Assert.AreEqual(2, back.Tier);
            Assert.AreEqual(42.5f, back.Durability, 1e-6f);

            // More than the tier holds is cut down; no such tier, or not armour, is no piece.
            Assert.AreEqual(80f, Armor.FromLoot(new LootEntry { Kind = LootKind.Helmet, Tier = 1, Durability = 999f }, Rules).Durability, 1e-6f);
            Assert.IsFalse(Armor.FromLoot(new LootEntry { Kind = LootKind.Helmet, Tier = 0 }, Rules).Worn);
            Assert.IsFalse(Armor.FromLoot(new LootEntry { Kind = LootKind.Vest, Tier = 4 }, Rules).Worn);
            Assert.IsFalse(Armor.FromLoot(new LootEntry { Kind = LootKind.Medkit, Tier = 2 }, Rules).Worn);
            Assert.IsFalse(Armor.FromLoot(null, Rules).Worn);
        }

        [Test]
        public void LootTable_RollsArmourRows_AndTheFirstPointIsStillAWeapon()
        {
            var table = new LootTable
            {
                Entries = new[]
                {
                    new LootEntry { Kind = LootKind.Helmet, Tier = 2, Weight = 50 },
                    new LootEntry { Kind = LootKind.Vest, Tier = 1, Weight = 50 },
                    new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 3, Weight = 1 },
                }
            };
            bool helmet = false, vest = false;
            for (int seed = 0; seed < 20; seed++)
            {
                var plan = table.Plan(8, seed);
                Assert.AreEqual(LootKind.Weapon, plan[0].Kind, "seed " + seed);
                foreach (var entry in plan)
                {
                    helmet |= entry.Kind == LootKind.Helmet && entry.Tier == 2;
                    vest |= entry.Kind == LootKind.Vest && entry.Tier == 1;
                }
            }
            Assert.IsTrue(helmet && vest, "armour rows are rolled like any other");
        }

        [Test]
        public void CrateRing_SpacesItemsEvenlyAroundTheBody()
        {
            LootScatter.RingOffset(0, 4, 0.9f, out float x, out float z);
            Assert.AreEqual(0f, x, 1e-5f);
            Assert.AreEqual(0.9f, z, 1e-5f, "the first item lies straight ahead");
            LootScatter.RingOffset(1, 4, 0.9f, out x, out z);
            Assert.AreEqual(0.9f, x, 1e-5f);
            Assert.AreEqual(0f, z, 1e-5f);
            LootScatter.RingOffset(2, 4, 0.9f, out x, out z);
            Assert.AreEqual(0f, x, 1e-5f);
            Assert.AreEqual(-0.9f, z, 1e-5f);

            // Every item is on the ring and no two share a spot, for any count the crate can hold.
            for (int count = 1; count <= 8; count++)
            {
                for (int i = 0; i < count; i++)
                {
                    LootScatter.RingOffset(i, count, 0.9f, out float ax, out float az);
                    Assert.AreEqual(0.9f, System.Math.Sqrt(ax * ax + az * az), 1e-4, $"{i} of {count}");
                    for (int j = i + 1; j < count; j++)
                    {
                        LootScatter.RingOffset(j, count, 0.9f, out float bx, out float bz);
                        double apart = System.Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
                        Assert.Greater(apart, 0.5, $"{i} and {j} of {count}");
                    }
                }
            }
            Assert.Greater(new CrateRules().MarkerSeconds, 0f);
        }
    }
}
