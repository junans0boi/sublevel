using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // The optic table, which weapon takes which optic, and the view numbers (field of view, mouse sensitivity).
    // Numbers are the defaults in OpticRules.
    public class OpticsTests
    {
        const int VK9 = 1, Ordo = 2, Warden = 3, Breacher = 4, Sentinel = 5;
        const int RedDot = 1, Holo = 2, X2 = 3, X3 = 4, X4 = 5, X6 = 6, X8 = 7, X16 = 8;

        static readonly OpticRules Rules = new OpticRules();

        [Test]
        public void Table_HasTheEightOptics_WithUniqueIdsAndKoreanNames()
        {
            Assert.AreEqual(8, Rules.Optics.Length);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Rules.Optics.Select(o => o.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "반사 조준 사이트", "투영 조준 사이트", "2배율", "3배율", "4배율", "6배율", "8배율", "15배율" },
                Rules.Optics.Select(o => o.Name).ToArray());
            CollectionAssert.AreEqual(new float[] { 1, 1, 2, 3, 4, 6, 8, 15 }, Rules.Optics.Select(o => o.Zoom).ToArray());
        }

        [Test]
        public void Table_KindsFollowZoom_AndSensitivityMatchesTheFormula()
        {
            Assert.AreEqual(OpticKind.Dot, Rules.Find(RedDot).Kind);
            Assert.AreEqual(OpticKind.Holo, Rules.Find(Holo).Kind);
            Assert.AreEqual(ReticleStyle.Dot, Rules.Find(RedDot).Reticle);
            Assert.AreEqual(ReticleStyle.Ring, Rules.Find(Holo).Reticle);
            foreach (var o in Rules.Optics)
            {
                bool scope = o.Kind == OpticKind.Scope;
                Assert.AreEqual(scope, o.Zoom > 1f, o.Name + ": only scopes magnify");
                Assert.AreEqual(Optics.SensitivityFor(o.Zoom), o.SensitivityFactor, 0.005f, o.Name + " sensitivity");
            }
            Assert.IsNull(Rules.Find(0));
            Assert.IsNull(Rules.Find(99));
        }

        // ADR-063: from 6x up the mouse wheel sets the zoom (6x 3-6, 8x 4-8, 15x 8-15); the rest are fixed.
        [Test]
        public void VariableScopes_RangesFromTheTable_AndJson()
        {
            var fromJson = JsonUtility.FromJson<OpticRules>(File.ReadAllText("Assets/_Project/Data/Resources/GameData/optics.json"));
            foreach (var rules in new[] { Rules, fromJson })
            {
                Assert.AreEqual(3f, Optics.MinZoomOf(rules.Find(X6)));
                Assert.AreEqual(4f, Optics.MinZoomOf(rules.Find(X8)));
                Assert.AreEqual(8f, Optics.MinZoomOf(rules.Find(X16)));
                Assert.AreEqual(15f, rules.Find(X16).Zoom);
                foreach (int fixedId in new[] { RedDot, Holo, X2, X3, X4 })
                {
                    Assert.IsFalse(Optics.IsVariable(rules.Find(fixedId)), "fixed: " + fixedId);
                    Assert.AreEqual(Mathf.Max(1f, rules.Find(fixedId).Zoom), Optics.MinZoomOf(rules.Find(fixedId)));
                }
                foreach (int variable in new[] { X6, X8, X16 }) Assert.IsTrue(Optics.IsVariable(rules.Find(variable)));
            }
        }

        [Test]
        public void StepZoom_OneNotchIsOneX_ClampedToTheRange()
        {
            var x6 = Rules.Find(X6);
            Assert.AreEqual(5f, Optics.StepZoom(x6, 6f, -1));
            Assert.AreEqual(3f, Optics.StepZoom(x6, 4f, -5), "not below 3x");
            Assert.AreEqual(6f, Optics.StepZoom(x6, 6f, 1), "not above 6x");
            var x8 = Rules.Find(X8);
            Assert.AreEqual(6f, Optics.StepZoom(x8, Optics.StepZoom(x8, 8f, -1), -1));
            Assert.AreEqual(4f, Optics.StepZoom(x8, 5f, -3));
            var x15 = Rules.Find(X16);
            float z = 15f;
            for (int i = 0; i < 10; i++) z = Optics.StepZoom(x15, z, -1);
            Assert.AreEqual(8f, z, "15x goes down to 8x");
            Assert.AreEqual(9f, Optics.StepZoom(x15, 8f, 1));
            // Fixed optics never move.
            Assert.AreEqual(4f, Optics.StepZoom(Rules.Find(X4), 4f, -1));
            Assert.AreEqual(1f, Optics.StepZoom(Rules.Find(RedDot), 1f, 1));
            Assert.AreEqual(1f, Optics.StepZoom(null, 3f, 1));
        }

        [Test]
        public void SensitivityAt_FollowsTheCurrentZoom()
        {
            var x8 = Rules.Find(X8);
            Assert.AreEqual(x8.SensitivityFactor, Optics.SensitivityAt(x8, 8f), 1e-6f);
            Assert.AreEqual(Optics.SensitivityFor(5f), Optics.SensitivityAt(x8, 5f), 1e-6f);
            Assert.Greater(Optics.SensitivityAt(x8, 4f), Optics.SensitivityAt(x8, 8f), "zoomed out turns faster");
            Assert.AreEqual(1f, Optics.SensitivityAt(null, 4f));
        }

        [Test]
        public void PerWeaponLists_ReferenceOnlyRealOptics_WithoutDuplicates()
        {
            Assert.AreEqual(5, Rules.Weapons.Length);
            foreach (var w in Rules.Weapons)
            {
                Assert.IsNotEmpty(w.Optics, "weapon " + w.WeaponId);
                Assert.AreEqual(w.Optics.Length, w.Optics.Distinct().Count(), "weapon " + w.WeaponId + " lists an optic twice");
                foreach (int id in w.Optics) Assert.IsNotNull(Rules.Find(id), "weapon " + w.WeaponId + " names optic " + id);
            }
        }

        [Test]
        public void CanAttach_FollowsEachWeaponsList()
        {
            Assert.IsTrue(Optics.CanAttach(VK9, RedDot, Rules));
            Assert.IsTrue(Optics.CanAttach(VK9, Holo, Rules));
            Assert.IsFalse(Optics.CanAttach(VK9, X2, Rules), "a pistol takes no scope");

            Assert.IsTrue(Optics.CanAttach(Ordo, X4, Rules));
            Assert.IsFalse(Optics.CanAttach(Ordo, X6, Rules));

            Assert.IsTrue(Optics.CanAttach(Warden, X6, Rules));
            Assert.IsFalse(Optics.CanAttach(Warden, X8, Rules));

            Assert.IsTrue(Optics.CanAttach(Breacher, X2, Rules));
            Assert.IsFalse(Optics.CanAttach(Breacher, X3, Rules));
        }

        [Test]
        public void CanAttach_RedDotAndHolo_FitEveryWeapon()
        {
            foreach (var w in Rules.Weapons)
            {
                Assert.IsTrue(Optics.CanAttach(w.WeaponId, RedDot, Rules), "red dot on " + w.WeaponId);
                Assert.IsTrue(Optics.CanAttach(w.WeaponId, Holo, Rules), "holographic on " + w.WeaponId);
            }
        }

        [Test]
        public void CanAttach_EightAndSixteenTimes_FitOnlyTheSniperRifle()
        {
            foreach (var w in Rules.Weapons)
            {
                bool sniper = w.WeaponId == Sentinel;
                Assert.AreEqual(sniper, Optics.CanAttach(w.WeaponId, X8, Rules), "8x on " + w.WeaponId);
                Assert.AreEqual(sniper, Optics.CanAttach(w.WeaponId, X16, Rules), "15x on " + w.WeaponId);
            }
        }

        [Test]
        public void CanAttach_RefusesNothingAndUnknowns()
        {
            Assert.IsFalse(Optics.CanAttach(0, RedDot, Rules), "no weapon");
            Assert.IsFalse(Optics.CanAttach(Warden, 0, Rules), "no optic");
            Assert.IsFalse(Optics.CanAttach(Warden, 99, Rules), "unknown optic");
            Assert.IsFalse(Optics.CanAttach(99, RedDot, Rules), "unknown weapon");
            Assert.IsFalse(Optics.CanAttach(Warden, -1, Rules));
        }

        [Test]
        public void FovFor_NarrowsByTheTangent()
        {
            Assert.AreEqual(38f, Optics.FovFor(38f, 1f), 1e-4f);
            Assert.AreEqual(38f, Optics.FovFor(38f, 0f), 1e-4f, "no zoom leaves the view alone");
            Assert.AreEqual(32.204f, Optics.FovFor(60f, 2f), 0.01f);
            Assert.AreEqual(9.840f, Optics.FovFor(38f, 4f), 0.01f);
            Assert.AreEqual(6.569f, Optics.FovFor(38f, 6f), 0.01f);
            Assert.AreEqual(2.466f, Optics.FovFor(38f, 16f), 0.01f);
            // The image really is `zoom` times larger: tan(fov / 2) shrinks by exactly that factor.
            double half = System.Math.Tan(Optics.FovFor(38f, 4f) * 0.5 * System.Math.PI / 180.0);
            double base_ = System.Math.Tan(38.0 * 0.5 * System.Math.PI / 180.0);
            Assert.AreEqual(4.0, base_ / half, 1e-3);
        }

        [Test]
        public void SensitivityFor_IsOneOverZoomToTheZeroPointEight_NeverBelowTheFloor()
        {
            Assert.AreEqual(1f, Optics.SensitivityFor(1f), 1e-6f);
            Assert.AreEqual(1f, Optics.SensitivityFor(0.5f), 1e-6f);
            Assert.AreEqual(0.5743f, Optics.SensitivityFor(2f), 1e-3f);
            Assert.AreEqual(0.3299f, Optics.SensitivityFor(4f), 1e-3f);
            Assert.AreEqual(0.2385f, Optics.SensitivityFor(6f), 1e-3f);
            Assert.AreEqual(0.1895f, Optics.SensitivityFor(8f), 1e-3f);
            Assert.AreEqual(0.1088f, Optics.SensitivityFor(16f), 1e-3f);
            Assert.AreEqual(Optics.MinSensitivity, Optics.SensitivityFor(1000f), 1e-6f);
        }

        [Test]
        public void ZoomOf_AndText()
        {
            var saved = OpticRules.Current;
            try
            {
                OpticRules.Current = Rules;
                Assert.AreEqual(1f, Optics.ZoomOf(0));
                Assert.AreEqual(1f, Optics.ZoomOf(RedDot));
                Assert.AreEqual(4f, Optics.ZoomOf(X4));
                Assert.AreEqual(1f, Optics.ZoomOf(99));
            }
            finally { OpticRules.Current = saved; }
            Assert.AreEqual("×4", Optics.ZoomText(4f));
            Assert.AreEqual("×1", Optics.ZoomText(1f));
        }

        [Test]
        public void ToLoot_MakesAnOpticEntry()
        {
            var entry = Optics.ToLoot(X4);
            Assert.AreEqual(LootKind.Optic, entry.Kind);
            Assert.AreEqual(11, (int)entry.Kind);
            Assert.AreEqual(X4, entry.Optic);
            Assert.AreEqual(1, entry.Amount);
        }

        // ---- ADR-082 (#113): optics loose in the bag ----

        [Test]
        public void PickupSlot_PrefersTheWeaponInHand_ThenAnotherFreeOne_ElseTheBag()
        {
            var weapons = new[] { Warden, Sentinel, VK9, 0, 0 };
            Assert.AreEqual(1, Optics.PickupSlot(weapons, new int[5], 1, X4), "the weapon in hand takes it");
            Assert.AreEqual(0, Optics.PickupSlot(weapons, new int[5], 2, X4), "the pistol does not: the first rifle does");
            Assert.AreEqual(1, Optics.PickupSlot(weapons, new[] { X2, 0, 0, 0, 0 }, 0, X4), "a fitted slot is skipped");
            Assert.AreEqual(-1, Optics.PickupSlot(weapons, new[] { X2, X3, 0, 0, 0 }, 0, X4), "every taker is fitted: into the bag");
            Assert.AreEqual(-1, Optics.PickupSlot(new int[5], new int[5], 0, X8), "no weapon at all: into the bag");
        }

        [Test]
        public void OpticBag_CountsEachIdOnItsOwn()
        {
            ulong bag = 0;
            Assert.IsTrue(OpticBag.TryAdd(ref bag, X4));
            Assert.IsTrue(OpticBag.TryAdd(ref bag, X4));
            Assert.IsTrue(OpticBag.TryAdd(ref bag, X16), "the id of the 15x uses its own nibble");
            Assert.AreEqual(2, OpticBag.Count(bag, X4));
            Assert.AreEqual(1, OpticBag.Count(bag, X16));
            Assert.AreEqual(0, OpticBag.Count(bag, RedDot));
            Assert.AreEqual(3, OpticBag.Total(bag));
            Assert.IsTrue(OpticBag.TryRemove(ref bag, X4));
            Assert.IsFalse(OpticBag.TryRemove(ref bag, RedDot), "none to take");
            Assert.AreEqual(1, OpticBag.Count(bag, X4));
            Assert.AreEqual(1, OpticBag.Count(bag, X16));
            Assert.IsFalse(OpticBag.TryAdd(ref bag, 0));
            Assert.IsTrue(OpticBag.TryAdd(ref bag, OpticBag.MaxId), "ADR-100: ids up to 16 (attachments) share the bag");
            Assert.IsFalse(OpticBag.TryAdd(ref bag, OpticBag.MaxId + 1));
        }

        [Test]
        public void OpticBag_StopsAtTheMostOneIdHolds()
        {
            ulong bag = 0;
            for (int i = 0; i < OpticBag.MaxCount; i++) Assert.IsTrue(OpticBag.TryAdd(ref bag, X2));
            Assert.IsFalse(OpticBag.TryAdd(ref bag, X2));
            Assert.AreEqual(0, OpticBag.Count(bag, X3), "never spills into the next id");
        }

        [Test]
        public void LooseOptic_TakesTwoPointsOfBagRoom()
        {
            Assert.AreEqual(20, Carry.Tenths(new CarryRules().Optic));
        }
    }
}
