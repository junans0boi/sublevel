using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // The optic table, which weapon takes which optic, and the view numbers (field of view, mouse sensitivity).
    // Numbers are the defaults in OpticRules.
    public class OpticsTests
    {
        const int VK9 = 1, Ordo = 2, Warden = 3, Breacher = 4;
        const int RedDot = 1, Holo = 2, X2 = 3, X3 = 4, X4 = 5, X6 = 6, X8 = 7, X16 = 8;

        static readonly OpticRules Rules = new OpticRules();

        [Test]
        public void Table_HasTheEightOptics_WithUniqueIdsAndKoreanNames()
        {
            Assert.AreEqual(8, Rules.Optics.Length);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Rules.Optics.Select(o => o.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "레드도트", "홀로그램", "2배율", "3배율", "4배율", "6배율", "8배율", "16배율" },
                Rules.Optics.Select(o => o.Name).ToArray());
            CollectionAssert.AreEqual(new float[] { 1, 1, 2, 3, 4, 6, 8, 16 }, Rules.Optics.Select(o => o.Zoom).ToArray());
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

        [Test]
        public void PerWeaponLists_ReferenceOnlyRealOptics_WithoutDuplicates()
        {
            Assert.AreEqual(4, Rules.Weapons.Length);
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
        public void CanAttach_EightAndSixteenTimes_FitNoWeaponYet()
        {
            foreach (var w in Rules.Weapons)
            {
                Assert.IsFalse(Optics.CanAttach(w.WeaponId, X8, Rules), "8x on " + w.WeaponId);
                Assert.IsFalse(Optics.CanAttach(w.WeaponId, X16, Rules), "16x on " + w.WeaponId);
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
    }
}
