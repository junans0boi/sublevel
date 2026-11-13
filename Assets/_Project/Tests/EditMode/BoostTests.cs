using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-099 (PUBG_GAP_V2 G8): the boost gauge in four steps, the boost items and the bandage numbers.
    public class BoostTests
    {
        static readonly BoostRules R = new BoostRules();
        static readonly HealRules Heal = new HealRules();

        [Test]
        public void Step_IsOneToFour_ByQuarters()
        {
            Assert.AreEqual(0, Boost.Step(0f, R));
            Assert.AreEqual(1, Boost.Step(0.1f, R));
            Assert.AreEqual(1, Boost.Step(25f, R));
            Assert.AreEqual(2, Boost.Step(25.1f, R));
            Assert.AreEqual(3, Boost.Step(60f, R));
            Assert.AreEqual(4, Boost.Step(75.5f, R));
            Assert.AreEqual(4, Boost.Step(100f, R));
        }

        [Test]
        public void HealRate_GrowsByStep_AndSpeedOnlyOnTheTopTwo()
        {
            Assert.AreEqual(0f, Boost.HealRate(0f, R), 1e-5f);
            Assert.AreEqual(0.25f, Boost.HealRate(10f, R), 1e-5f);
            Assert.AreEqual(0.5f, Boost.HealRate(40f, R), 1e-5f);
            Assert.AreEqual(0.75f, Boost.HealRate(60f, R), 1e-5f);
            Assert.AreEqual(1f, Boost.HealRate(90f, R), 1e-5f);
            Assert.AreEqual(1f, Boost.SpeedFactor(0f, R));
            Assert.AreEqual(1f, Boost.SpeedFactor(40f, R), "step 2: no speed yet");
            Assert.AreEqual(1.025f, Boost.SpeedFactor(60f, R), 1e-5f);
            Assert.AreEqual(1.05f, Boost.SpeedFactor(90f, R), 1e-5f);
        }

        [Test]
        public void Tick_DrainsTheGauge_AndGivesHealthByTheStepItWasIn()
        {
            float gauge = 100f;
            float heal = Boost.Tick(ref gauge, 10f, R);
            Assert.AreEqual(92f, gauge, 1e-4f, "0.8 per second");
            Assert.AreEqual(10f, heal, 1e-4f, "step 4: 1 HP per second");
            gauge = 0.4f;
            Boost.Tick(ref gauge, 5f, R);
            Assert.AreEqual(0f, gauge, 1e-5f, "never below zero");
            Assert.AreEqual(0f, Boost.Tick(ref gauge, 5f, R), "an empty gauge heals nothing");
        }

        [Test]
        public void ADrinkTakesAboutTwoMinutesToGoThrough_Syringe()
        {
            float gauge = 100f, total = 0f;
            int seconds = 0;
            while (gauge > 0f && seconds < 1000) { total += Boost.Tick(ref gauge, 1f, R); seconds++; }
            Assert.That(seconds, Is.InRange(110, 140), "a full gauge lasts about two minutes");
            Assert.That(total, Is.InRange(60f, 90f), "and heals a good part of a body in that time");
        }

        [Test]
        public void Add_FillsUpToTheMax()
        {
            Assert.AreEqual(40f, Boost.Add(0f, 40f, R));
            Assert.AreEqual(100f, Boost.Add(70f, 60f, R));
            Assert.AreEqual(70f, Boost.Add(70f, -5f, R), "never negative");
        }

        [Test]
        public void TheItems_AreTheAgreedNumbers()
        {
            Assert.AreEqual(4f, Heal.Bandage.Seconds, 1e-4f);
            Assert.AreEqual(10f, Heal.Bandage.Amount, 1e-4f);
            Assert.AreEqual(75f, Heal.FirstAid.Amount, 1e-4f, "up to 75");
            Assert.AreEqual(40f, Heal.Drink.Boost); Assert.AreEqual(4f, Heal.Drink.Seconds);
            Assert.AreEqual(60f, Heal.Pill.Boost); Assert.AreEqual(6f, Heal.Pill.Seconds);
            Assert.AreEqual(100f, Heal.Syringe.Boost); Assert.AreEqual(6f, Heal.Syringe.Seconds);
            Assert.AreEqual(55f, Healing.Amount(HealKind.FirstAid, 20f, Heal), 1e-4f, "20 -> 75");
            Assert.AreEqual(0f, Healing.Amount(HealKind.Drink, 20f, Heal), "a boost item gives no health itself");
        }

        [Test]
        public void BoostItems_CanBeDrunkAtFullHealth_UntilTheGaugeIsFull()
        {
            Assert.IsTrue(Healing.CanUseBoost(HealKind.Drink, 100f, 0f, Heal, R));
            Assert.IsTrue(Healing.CanUseBoost(HealKind.Syringe, 100f, 99f, Heal, R));
            Assert.IsFalse(Healing.CanUseBoost(HealKind.Drink, 100f, 100f, Heal, R));
            Assert.IsFalse(Healing.CanUseBoost(HealKind.Drink, 0f, 0f, Heal, R), "the dead drink nothing");
            Assert.IsFalse(Healing.CanUseBoost(HealKind.Bandage, 50f, 0f, Heal, R));
            Assert.AreEqual(HealKind.Drink, Healing.BestBoost(1, 1, 1));
            Assert.AreEqual(HealKind.Pill, Healing.BestBoost(0, 2, 1));
            Assert.AreEqual(HealKind.Syringe, Healing.BestBoost(0, 0, 1));
            Assert.AreEqual(HealKind.None, Healing.BestBoost(0, 0, 0));
            Assert.AreEqual(HealKind.Pill, Healing.KindOfLoot(LootKind.Pill));
            Assert.AreEqual(LootKind.Syringe, Healing.LootOf(HealKind.Syringe));
        }
    }
}
