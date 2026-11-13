using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // M2-2: what a bot decides from what it senses. Literal scenarios against the pure brain.
    public class BotBrainTests
    {
        static readonly BotVec Origin = new BotVec(0f, 0f, 0f);

        static BotRules Rules() => new BotRules();

        static BotBrain Brain(BotRules rules = null, int seed = 7) =>
            new BotBrain(rules ?? Rules(), seed) { PointsOfInterest = new[] { new BotVec(40f, 0f, 0f), new BotVec(0f, 0f, 55f), new BotVec(-30f, 0f, -30f) } };

        static BotPercept Unarmed(double time) => new BotPercept { Time = time, Position = Origin, HealthFraction = 1f };

        // A rifle-like weapon in hand: 30 rounds, 90 in reserve, 90 m range.
        static BotPercept Armed(double time)
        {
            var p = Unarmed(time);
            p.HasWeapon = true;
            p.WeaponId = 2;
            p.Mag = 30;
            p.MagSize = 30;
            p.Reserve = 90;
            p.WeaponRange = 90f;
            return p;
        }

        static BotPercept Seeing(BotPercept p, float distance, int enemyId = 9)
        {
            p.EnemyVisible = true;
            p.EnemyId = enemyId;
            p.EnemyPosition = new BotVec(0f, 0f, distance);
            return p;
        }

        static void AssertAt(BotVec expected, BotVec actual)
        {
            Assert.AreEqual(expected.X, actual.X, 1e-4f);
            Assert.AreEqual(expected.Y, actual.Y, 1e-4f);
            Assert.AreEqual(expected.Z, actual.Z, 1e-4f);
        }

        [Test]
        public void UnarmedBot_GoesToAVisibleWeapon_AndPicksItUpWhenInReach()
        {
            var brain = Brain();
            var p = Unarmed(0);
            p.LootVisible = true;
            p.LootId = 42;
            p.LootPosition = new BotVec(8f, 0.3f, 0f);
            var intent = brain.Think(p);
            Assert.AreEqual(BotState.Loot, intent.State);
            Assert.IsTrue(intent.Move);
            AssertAt(p.LootPosition, intent.MoveTarget);
            Assert.AreEqual(42, intent.LootId);
            Assert.IsFalse(intent.PickUp, "8 m away is out of reach");

            p.Time = 2;
            p.Position = new BotVec(6.5f, 0f, 0f);
            intent = brain.Think(p);
            Assert.AreEqual(BotState.Loot, intent.State);
            Assert.IsTrue(intent.PickUp, "1.5 m away is within reach");
        }

        [Test]
        public void UnarmedBot_ArmsItselfBeforeLookingIntoANoise()
        {
            var brain = Brain();
            var p = Unarmed(0);
            p.Heard = true;
            p.HeardPosition = new BotVec(0f, 0f, 40f);
            p.LootVisible = true;
            p.LootId = 3;
            p.LootPosition = new BotVec(5f, 0f, 0f);
            Assert.AreEqual(BotState.Loot, brain.Think(p).State);
        }

        [Test]
        public void ArmedBot_EngagesAVisibleEnemy_OnlyAfterTheReactionTime()
        {
            var rules = Rules();
            var brain = Brain(rules);
            var first = brain.Think(Seeing(Armed(10.0), 20f));
            Assert.AreEqual(BotState.Engage, first.State);
            Assert.IsTrue(first.Look);
            Assert.IsFalse(first.Fire, "no shot in the moment the enemy is first seen");

            var early = brain.Think(Seeing(Armed(10.0 + rules.ReactionSeconds * 0.5), 20f));
            Assert.IsFalse(early.Fire, "no shot before the reaction time has passed");

            var late = brain.Think(Seeing(Armed(10.0 + rules.ReactionSeconds + 0.01), 20f));
            Assert.AreEqual(BotState.Engage, late.State);
            Assert.IsTrue(late.Fire, "fires once the reaction time has passed");
            Assert.IsTrue(late.Aim);
            Assert.IsFalse(late.Move, "20 m is inside the distance it holds");
        }

        [Test]
        public void ArmedBot_ClosesIn_WhenTheEnemyIsBeyondItsWeaponsRange()
        {
            var brain = Brain();
            brain.Think(Seeing(Armed(0), 120f));
            var intent = brain.Think(Seeing(Armed(1), 120f));
            Assert.AreEqual(BotState.Engage, intent.State);
            Assert.IsTrue(intent.Move);
            AssertAt(new BotVec(0f, 0f, 120f), intent.MoveTarget);
            Assert.IsFalse(intent.Fire, "out of range: no shot, however long it has looked");
        }

        [Test]
        public void Bursts_StayWithinTheRules_AndPauseInBetween()
        {
            var rules = Rules();
            var brain = Brain(rules);
            double time = 0;
            brain.Think(Seeing(Armed(time), 20f));
            time += rules.ReactionSeconds + 0.01;

            int mag = 30, shots = 0;
            // One shot leaves the magazine every call for as long as the brain holds the trigger.
            for (int i = 0; i < 20; i++)
            {
                var p = Seeing(Armed(time), 20f);
                p.Mag = mag;
                if (!brain.Think(p).Fire) break;
                mag--;
                shots++;
                time += 0.01;
            }
            Assert.That(shots, Is.InRange(rules.BurstMin, rules.BurstMax), "burst length");

            var paused = Seeing(Armed(time + rules.BurstPauseMin * 0.5), 20f);
            paused.Mag = mag;
            Assert.IsFalse(brain.Think(paused).Fire, "pauses after a burst");

            var resumed = Seeing(Armed(time + rules.BurstPauseMax + 0.05), 20f);
            resumed.Mag = mag;
            Assert.IsTrue(brain.Think(resumed).Fire, "fires again after the pause");
        }

        [Test]
        public void EmptyMagazine_InAFight_ReloadsCrouched()
        {
            var brain = Brain();
            var p = Seeing(Armed(0), 20f);
            p.Mag = 0;
            var intent = brain.Think(p);
            Assert.AreEqual(BotState.Engage, intent.State);
            Assert.IsTrue(intent.Reload);
            Assert.IsTrue(intent.Crouch);
            Assert.IsFalse(intent.Fire);

            p.Time = 0.2;
            p.Reloading = true;
            intent = brain.Think(p);
            Assert.IsTrue(intent.Crouch, "stays down while reloading");
            Assert.IsFalse(intent.Fire);
        }

        [Test]
        public void NoRoundsLeft_SwitchesToTheOtherWeapon()
        {
            var brain = Brain();
            var p = Seeing(Armed(0), 20f);
            p.Mag = 0;
            p.Reserve = 0;
            p.OtherSlotUsable = true;
            var intent = brain.Think(p);
            Assert.AreEqual(BotState.Engage, intent.State);
            Assert.IsTrue(intent.SwitchWeapon);
            Assert.IsFalse(intent.Fire);
        }

        [Test]
        public void LowHealthWithAMedkit_RetreatsFromAVisibleEnemy_ThenHealsOutOfSight()
        {
            var rules = Rules();
            var brain = Brain(rules);
            var hurt = Armed(0);
            hurt.HealthFraction = 0.2f;
            hurt.Medkits = 1;

            var retreat = brain.Think(Seeing(hurt, 20f));
            Assert.AreEqual(BotState.Retreat, retreat.State);
            Assert.IsTrue(retreat.Move);
            Assert.IsTrue(retreat.SeekCover);
            Assert.IsFalse(retreat.Fire);
            Assert.Less(retreat.MoveTarget.Z, 0f, "the fallback runs away from the enemy at +Z");

            // Out of sight: gets down, and uses the medkit after a moment.
            hurt.Time = 1;
            var down = brain.Think(hurt);
            Assert.AreEqual(BotState.Heal, down.State);
            Assert.IsTrue(down.Crouch);
            Assert.IsFalse(down.Move);
            Assert.IsFalse(down.Heal, "not in the same instant");

            hurt.Time = 1 + rules.HealSeconds + 0.01;
            var heal = brain.Think(hurt);
            Assert.AreEqual(BotState.Heal, heal.State);
            Assert.IsTrue(heal.Heal);
        }

        [Test]
        public void LowHealthWithoutAMedkit_KeepsFighting()
        {
            var brain = Brain();
            var hurt = Armed(0);
            hurt.HealthFraction = 0.2f;
            Assert.AreEqual(BotState.Engage, brain.Think(Seeing(hurt, 20f)).State);
        }

        [Test]
        public void ARetreatThatNeverBreaksLineOfSight_TurnsIntoAFight()
        {
            var rules = Rules();
            var brain = Brain(rules);
            var hurt = Armed(0);
            hurt.HealthFraction = 0.2f;
            hurt.Medkits = 1;
            Assert.AreEqual(BotState.Retreat, brain.Think(Seeing(hurt, 20f)).State);
            hurt.Time = rules.RetreatMaxSeconds + 0.1;
            Assert.AreEqual(BotState.Engage, brain.Think(Seeing(hurt, 20f)).State);
        }

        [Test]
        public void UnarmedBot_RunsFromAVisibleEnemy()
        {
            var intent = Brain().Think(Seeing(Unarmed(0), 15f));
            Assert.AreEqual(BotState.Retreat, intent.State);
            Assert.IsTrue(intent.Sprint);
            Assert.IsFalse(intent.Fire);
        }

        [Test]
        public void HearingAShot_WithNobodyInSight_InvestigatesWhereItCameFrom()
        {
            var brain = Brain();
            var p = Armed(0);
            p.Heard = true;
            p.HeardPosition = new BotVec(30f, 0f, -12f);
            var intent = brain.Think(p);
            Assert.AreEqual(BotState.Investigate, intent.State);
            Assert.IsTrue(intent.Move);
            AssertAt(p.HeardPosition, intent.MoveTarget);
            Assert.IsFalse(intent.Fire);

            // The noise is remembered after the moment it was heard...
            intent = brain.Think(Armed(3));
            Assert.AreEqual(BotState.Investigate, intent.State);
            AssertAt(new BotVec(30f, 0f, -12f), intent.MoveTarget);

            // ...until the bot has been there.
            var there = Armed(6);
            there.Position = new BotVec(29.5f, 0f, -12f);
            Assert.AreEqual(BotState.Roam, brain.Think(there).State);
        }

        [Test]
        public void LosingSightOfAnEnemy_GoesToWhereItWasLastSeen_ThenForgets()
        {
            var rules = Rules();
            var brain = Brain(rules);
            brain.Think(Seeing(Armed(0), 30f));
            var intent = brain.Think(Armed(1));
            Assert.AreEqual(BotState.Investigate, intent.State);
            AssertAt(new BotVec(0f, 0f, 30f), intent.MoveTarget);

            Assert.AreEqual(BotState.Roam, brain.Think(Armed(1 + rules.MemorySeconds + 0.1)).State);
        }

        [Test]
        public void NothingToDo_RoamsTowardAPointOfInterest()
        {
            var brain = Brain();
            var intent = brain.Think(Armed(0));
            Assert.AreEqual(BotState.Roam, intent.State);
            Assert.IsTrue(intent.Move);
            bool known = false;
            foreach (var point in brain.PointsOfInterest)
                known |= BotVec.Distance(point, intent.MoveTarget) < 1e-3f;
            Assert.IsTrue(known, "the roam target is one of the points of interest");
            Assert.GreaterOrEqual(BotVec.PlanarDistance(Origin, intent.MoveTarget), Rules().RoamMinMeters);

            // It keeps the same goal until it gets there, then picks another one.
            var goal = intent.MoveTarget;
            AssertAt(goal, brain.Think(Armed(1)).MoveTarget);
            var arrived = Armed(20);
            arrived.Position = goal;
            var next = brain.Think(arrived);
            Assert.AreEqual(BotState.Roam, next.State);
            Assert.Greater(BotVec.PlanarDistance(goal, next.MoveTarget), 1f, "a new goal after arriving");
        }

        [Test]
        public void WithoutPointsOfInterest_WandersWithinTheRoamRadius()
        {
            var rules = Rules();
            var brain = new BotBrain(rules, 3);
            var intent = brain.Think(Armed(0));
            Assert.AreEqual(BotState.Roam, intent.State);
            Assert.That(BotVec.PlanarDistance(Origin, intent.MoveTarget), Is.InRange(rules.RoamRadius * 0.5f - 1e-3f, rules.RoamRadius + 1e-3f));
        }

        [Test]
        public void SameSeedAndPercepts_GiveTheSameIntents()
        {
            var a = Brain(seed: 99);
            var b = Brain(seed: 99);
            for (int i = 0; i < 200; i++)
            {
                double time = i * 0.1;
                var p = i % 50 < 25 ? Seeing(Armed(time), 20f) : Armed(time);
                p.Mag = 30 - i % 30;
                p.Stuck = i % 37 == 0;
                var x = a.Think(p);
                var y = b.Think(p);
                Assert.AreEqual(x.State, y.State, "state at step " + i);
                Assert.AreEqual(x.Fire, y.Fire, "fire at step " + i);
                Assert.AreEqual(x.Move, y.Move, "move at step " + i);
                AssertAt(x.MoveTarget, y.MoveTarget);
            }
        }

        [Test]
        public void AimError_GrowsWithDistance_AndSettlesOnTheSameTarget()
        {
            var rules = Rules();
            float near = BotAim.ErrorDeg(rules, 5f, 10f);
            float far = BotAim.ErrorDeg(rules, 100f, 10f);
            Assert.AreEqual(rules.AimErrorNearDeg, near, 1e-4f);
            Assert.AreEqual(rules.AimErrorFarDeg, far, 1e-4f);
            Assert.Greater(far, near);
            Assert.AreEqual(near * rules.FirstShotErrorFactor, BotAim.ErrorDeg(rules, 5f, 0f), 1e-4f, "the first shot is the least accurate");
        }

        [Test]
        public void Loot_IsWantedOnlyWhenTheBotHasAUseForIt()
        {
            var rules = Rules();
            Assert.IsTrue(BotBrain.WantsLoot(LootKind.Weapon, true, false, 0, 0, rules));
            Assert.IsFalse(BotBrain.WantsLoot(LootKind.Weapon, false, false, 0, 0, rules), "both slots full");
            Assert.IsTrue(BotBrain.WantsLoot(LootKind.Ammo, false, true, 10, 0, rules));
            Assert.IsFalse(BotBrain.WantsLoot(LootKind.Ammo, false, false, 0, 0, rules), "ammo for a weapon it does not carry");
            Assert.IsFalse(BotBrain.WantsLoot(LootKind.Ammo, false, true, rules.AmmoWanted, 0, rules), "enough already");
            Assert.IsTrue(BotBrain.WantsLoot(LootKind.Medkit, false, false, 0, 0, rules));
            Assert.IsFalse(BotBrain.WantsLoot(LootKind.Medkit, false, false, 0, rules.MedkitsWanted, rules));
        }

        [Test]
        public void Noise_IsReadOnceByEachListener_AndOldOnesFallOut()
        {
            BotNoise.Clear();
            Assert.AreEqual(0, BotNoise.Count);
            BotNoise.Emit(1f, 2f, 3f, 80f, 5);
            BotNoise.Emit(4f, 5f, 6f, 0f, 5); // no reach: not a noise
            Assert.AreEqual(1, BotNoise.Count);
            Assert.AreEqual(0, BotNoise.FirstUnread(0));
            var noise = BotNoise.At(0);
            Assert.AreEqual(3f, noise.Z, 1e-6f);
            Assert.AreEqual(5, noise.SourceId);
            Assert.AreEqual(NoiseKind.Gunshot, noise.Kind);
            Assert.AreEqual(1, BotNoise.FirstUnread(BotNoise.Count), "nothing new for a listener that is up to date");

            for (int i = 0; i < BotNoise.Capacity + 10; i++) BotNoise.Emit(i, 0f, 0f, 10f, 1, NoiseKind.Footstep);
            Assert.AreEqual(BotNoise.Count - BotNoise.Capacity, BotNoise.FirstUnread(0), "a slow listener gets only what is still kept");
            Assert.AreEqual(BotNoise.Capacity + 9, BotNoise.At(BotNoise.Count - 1).X, 1e-4f);
            BotNoise.Clear();
        }
    }
}
