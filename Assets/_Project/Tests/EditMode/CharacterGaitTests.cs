using Game.Core;
using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests
{
    // CHARACTER_AUDIT group A: pose math that needs no scene.
    public class CharacterGaitTests
    {
        [Test]
        public void TurnStep_OnlyWhenNearlyStillAndTurningFastEnough()
        {
            Assert.AreEqual(0f, CombatantAnimation.TurnStepAmount(10f, 0f), 1e-4f, "slow turn: no step");
            Assert.AreEqual(1f, CombatantAnimation.TurnStepAmount(-120f, 0.1f), 1e-4f, "fast turn either way: full step");
            Assert.AreEqual(0f, CombatantAnimation.TurnStepAmount(120f, 3f), 1e-4f, "running: the legs already move");
            Assert.That(CombatantAnimation.TurnStepAmount(50f, 0f), Is.GreaterThan(0f).And.LessThan(1f));
        }

        [Test]
        public void Breath_StaysInRange_AndIsPeriodic()
        {
            for (float t = 0f; t < 10f; t += 0.37f) Assert.That(CombatantAnimation.BreathAt(t, 0.4f), Is.InRange(-1f, 1f));
            Assert.AreEqual(CombatantAnimation.BreathAt(1f, 0f), CombatantAnimation.BreathAt(1f + 3.8f, 0f), 1e-3f);
            Assert.AreNotEqual(CombatantAnimation.BreathAt(1f, 0f), CombatantAnimation.BreathAt(1f, 1.2f), "each combatant has its own phase");
        }

        [Test]
        public void Plant_StillFootGoesToTheGround_SwingFootStaysUp()
        {
            Assert.AreEqual(0.1f, CombatantAnimation.PlantMove(0.02f, 0.02f, 0.1f, true), 1e-4f, "ground 10 cm up: foot lifted onto it");
            Assert.AreEqual(-0.08f, CombatantAnimation.PlantMove(0.10f, 0.02f, 0f, true), 1e-4f, "hanging foot goes down when still");
            Assert.AreEqual(0f, CombatantAnimation.PlantMove(0.30f, 0.02f, 0f, false), 1e-4f, "a swing foot high over the ground is left to the clip");
            Assert.That(CombatantAnimation.PlantMove(0.02f, 0.02f, 0.1f, false), Is.GreaterThan(0.09f), "planted foot follows a slope when moving");
            Assert.AreEqual(0.3f, CombatantAnimation.PlantMove(0f, 0f, 2f, true), 1e-4f, "limited by the leg's reach");
        }

        [Test]
        public void Pistol_HeldCloserAndHigherThanARifle_SprintCarriesDown()
        {
            var pistol = RifleHoldRules.Pistol();
            var rifle = new RifleHoldRules();
            var p = RifleHold.Pose(0f, 0f, 0f, pistol);
            var r = RifleHold.Pose(0f, 0f, 0f, rifle);
            Assert.Greater(p.Z, r.Z, "arms out further, the short gun is held in front");
            Assert.Greater(p.Y, r.Y, "higher than a long gun's low ready");
            Assert.Greater(RifleHold.Pose(7f, 1f, 0f, pistol).Pitch, p.Pitch);
            Assert.AreEqual(0f, RifleHold.Pose(0f, 0f, 1f, pistol).Pitch, 1e-4f, "aiming is level");
        }
    }
}
