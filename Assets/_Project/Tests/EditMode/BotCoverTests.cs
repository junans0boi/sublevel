using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-311 (#375): weapon styles and preferred distances, choosing cover, the expose-hide-peek cycle.
    public class BotCoverTests
    {
        static readonly BotVec Origin = new BotVec(0, 0, 0);

        [TestCase(1, 0, false, 34f, 0.18f, BotWeaponStyle.Pistol)]      // VK-9
        [TestCase(0, 2, false, 20f, 0.55f, BotWeaponStyle.Shotgun)]     // Breacher
        [TestCase(0, 3, false, 82f, 1.3f, BotWeaponStyle.Sniper)]       // Sentinel
        [TestCase(0, 1, false, 52f, 0.2f, BotWeaponStyle.Dmr)]          // Lancer
        [TestCase(0, 0, true, 22f, 0.0632f, BotWeaponStyle.Smg)]        // Brisk
        [TestCase(0, 1, true, 40f, 0.11f, BotWeaponStyle.Rifle)]        // Warden
        [TestCase(0, 3, true, 54f, 0.1395f, BotWeaponStyle.Rifle)]      // Hauler
        public void The_style_of_a_gun_comes_from_its_data(int category, int ammo, bool auto, float damage, float interval, BotWeaponStyle expected)
        {
            Assert.AreEqual(expected, BotWeapons.StyleOf(category, ammo, auto, damage, interval));
        }

        [Test]
        public void Each_style_prefers_its_own_distance_within_the_weapons_reach()
        {
            var rules = new BotRules();
            Assert.AreEqual(rules.PreferredMeters, BotWeapons.PreferredMeters(BotWeaponStyle.Rifle, 200f, rules), 0.01f, "the old single value is the rifle's");
            Assert.Greater(BotWeapons.PreferredMeters(BotWeaponStyle.Sniper, 400f, rules), BotWeapons.PreferredMeters(BotWeaponStyle.Dmr, 280f, rules));
            Assert.Greater(BotWeapons.PreferredMeters(BotWeaponStyle.Dmr, 280f, rules), BotWeapons.PreferredMeters(BotWeaponStyle.Rifle, 200f, rules));
            Assert.Less(BotWeapons.PreferredMeters(BotWeaponStyle.Smg, 90f, rules), BotWeapons.PreferredMeters(BotWeaponStyle.Rifle, 200f, rules));
            Assert.Less(BotWeapons.PreferredMeters(BotWeaponStyle.Shotgun, 45f, rules), BotWeapons.PreferredMeters(BotWeaponStyle.Smg, 90f, rules));
            Assert.AreEqual(36f, BotWeapons.PreferredMeters(BotWeaponStyle.Sniper, 45f, rules), 0.01f, "never past 80 % of the reach");
            Assert.IsTrue(BotWeapons.UsesCover(BotWeaponStyle.Pistol));
            Assert.IsFalse(BotWeapons.UsesCover(BotWeaponStyle.Shotgun));
        }

        [Test]
        public void An_smg_closes_in_where_a_sniper_holds_back()
        {
            BotPercept Seen(BotWeaponStyle style, float range) => new BotPercept
            {
                Time = 1, Position = Origin, HealthFraction = 1f, EnemyVisible = true, EnemyId = 5, EnemyPosition = new BotVec(60, 0, 0),
                HasWeapon = true, Mag = 30, MagSize = 30, Reserve = 90, WeaponRange = range, WeaponStyle = style,
            };
            var smg = new BotBrain(new BotRules(), 1).Think(Seen(BotWeaponStyle.Smg, 90f));
            Assert.AreEqual(60f, smg.MoveTarget.X, "60 m is far for an SMG: it runs at the enemy");
            var sniper = new BotBrain(new BotRules(), 1).Think(Seen(BotWeaponStyle.Sniper, 400f));
            Assert.Less(sniper.MoveTarget.X, 10f, "60 m is inside a sniper's liking (70 m): at most a sidestep");
        }

        static CoverCandidate Hide(float x, float z, bool a, bool b, float walk, float peek = 2f) =>
            new CoverCandidate { Hide = new BotVec(x, 0, z), PeekA = a, PeekB = b, WalkMeters = walk, PeekMeters = peek };

        [Test]
        public void Cover_needs_a_way_to_shoot_again_and_does_not_run_at_the_enemy()
        {
            var threat = new BotVec(30, 0, 0);
            Assert.IsTrue(float.IsNegativeInfinity(BotCover.Score(Hide(-4, 0, false, false, 4), Origin, threat)), "hides but cannot shoot from there");
            Assert.IsTrue(float.IsNegativeInfinity(BotCover.Score(Hide(10, 0, true, true, 10), Origin, threat)), "ten metres at the enemy");
            var list = new List<CoverCandidate> { Hide(-4, 0, true, false, 4), Hide(-6, 3, true, true, 7), Hide(-3, 9, true, true, 9) };
            Assert.AreEqual(1, BotCover.Pick(list, Origin, threat), "both sides beat one side, nearer beats farther");
            Assert.AreEqual(-1, BotCover.Pick(new List<CoverCandidate>(), Origin, threat));
        }

        static BotPercept Fighting(double time, bool visible, BotVec position) => new BotPercept
        {
            Time = time, Position = position, HealthFraction = 1f, EnemyVisible = visible, EnemyId = 5, EnemyPosition = new BotVec(25, 0, 0),
            HasWeapon = true, Mag = 30, MagSize = 30, Reserve = 90, WeaponRange = 200f, WeaponStyle = BotWeaponStyle.Rifle,
            CoverValid = true, CoverHide = new BotVec(-3, 0, 0), CoverPeekAValid = true, CoverPeekA = new BotVec(-3, 0, 2), CoverPeekBValid = true, CoverPeekB = new BotVec(-3, 0, -2),
        };

        [Test]
        public void The_bot_fires_from_the_open_then_hides_then_comes_out_on_the_other_side()
        {
            var brain = new BotBrain(new BotRules(), 7);
            Assert.AreEqual(PeekPhase.None, brain.Phase);
            brain.Think(Fighting(0, true, Origin));
            Assert.AreEqual(PeekPhase.Expose, brain.Phase);
            Assert.IsFalse(brain.Think(Fighting(1, true, Origin)).Move && brain.Phase != PeekPhase.Expose, "still out in the open at 1 s");
            Assert.AreEqual(PeekPhase.Expose, brain.Phase);

            // Past the longest exposure (3 s): it hides, moving to the hiding place, not firing.
            var hide = brain.Think(Fighting(3.1, true, Origin));
            Assert.AreEqual(PeekPhase.Hide, brain.Phase);
            Assert.IsTrue(hide.Move); Assert.IsFalse(hide.Fire);
            Assert.AreEqual(-3f, hide.MoveTarget.X);
            Assert.AreEqual(1, brain.ExposeEpisodes);
            Assert.AreEqual(3.1, brain.ExposeSeconds, 0.001);

            // At the hiding place, out of sight, crouched; after the longest wait (3 s) it comes out.
            var behind = new BotVec(-3, 0, 0);
            var wait = brain.Think(Fighting(4, false, behind));
            Assert.AreEqual(PeekPhase.Hide, brain.Phase);
            Assert.IsTrue(wait.Crouch); Assert.IsFalse(wait.Move);
            brain.Think(Fighting(6.5, false, behind));
            Assert.AreEqual(PeekPhase.Peek, brain.Phase);
            Assert.AreEqual(1, brain.PeekCycles);
            var peek = brain.Think(Fighting(6.6, false, behind));
            Assert.AreEqual(-2f, peek.MoveTarget.Z, "the first peek is on the side it was not on (B)");

            // Seeing the enemy again: back in the open, firing.
            brain.Think(Fighting(7, true, new BotVec(-3, 0, -2)));
            Assert.AreEqual(PeekPhase.Expose, brain.Phase);
            // Hard bots swap sides each time; this default (normal) bot does too: the next peek is on A.
            brain.Think(Fighting(10.2, true, new BotVec(-3, 0, -2)));
            Assert.AreEqual(PeekPhase.Hide, brain.Phase);
            brain.Think(Fighting(10.5, false, behind));
            brain.Think(Fighting(14, false, behind));
            Assert.AreEqual(PeekPhase.Peek, brain.Phase);
            var second = brain.Think(Fighting(14.1, false, behind));
            Assert.AreEqual(2f, second.MoveTarget.Z);
        }

        [Test]
        public void An_empty_magazine_sends_the_bot_behind_cover_to_reload()
        {
            var brain = new BotBrain(new BotRules(), 7);
            brain.Think(Fighting(0, true, Origin));
            var p = Fighting(0.5, true, Origin); p.Mag = 0;
            var intent = brain.Think(p);
            Assert.AreEqual(PeekPhase.Hide, brain.Phase);
            Assert.IsFalse(intent.Fire);
            var behind = Fighting(1.5, false, new BotVec(-3, 0, 0)); behind.Mag = 0;
            Assert.IsTrue(brain.Think(behind).Reload, "reloads behind the cover");
        }

        [Test]
        public void Without_cover_or_with_a_close_range_gun_the_old_fight_goes_on()
        {
            var brain = new BotBrain(new BotRules(), 7);
            var p = Fighting(0, true, Origin); p.CoverValid = false;
            brain.Think(p); brain.Think(Fighting(5, true, Origin).WithoutCover());
            Assert.AreEqual(PeekPhase.None, brain.Phase);
            var smg = Fighting(0, true, Origin); smg.WeaponStyle = BotWeaponStyle.Smg;
            new BotBrain(new BotRules(), 7).Think(smg);
            var b2 = new BotBrain(new BotRules(), 7); b2.Think(smg);
            Assert.AreEqual(PeekPhase.None, b2.Phase);
        }

        [Test]
        public void The_levels_set_the_exposure_and_the_sides()
        {
            var n = new BotRules();
            var easy = BotLevels.For(n, BotDifficulty.Easy);
            var hard = BotLevels.For(n, BotDifficulty.Hard);
            Assert.Greater(easy.PeekHideMin, n.PeekHideMin);
            Assert.Less(hard.PeekHideMin, n.PeekHideMin);
            Assert.IsFalse(easy.PeekAlternate); Assert.IsTrue(hard.PeekAlternate);
            Assert.Less(hard.PeekExposeMax, n.PeekExposeMax);
            Assert.GreaterOrEqual(n.PeekExposeMin, 2f); Assert.LessOrEqual(n.PeekExposeMax, 3f);
        }
    }

    static class PercepExtensions
    {
        public static BotPercept WithoutCover(this BotPercept p) { p.CoverValid = false; return p; }
    }
}
