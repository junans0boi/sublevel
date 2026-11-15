using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #90, ADR-074: vehicle health by zone, smoke -> fire -> wreck, tyres, glass panes.
    public class VehicleDamageTests
    {
        static readonly VehicleDamageRules R = new VehicleDamageRules();

        // ---- Zones and health ----

        [Test]
        public void Zones_EngineFull_BodyThreeQuarters_RoofHalf_GlassAndTyresNone()
        {
            Assert.AreEqual(40f, VehicleDamageMath.BulletDamage(40f, VehicleHitZone.Engine, R), 1e-4f);
            Assert.AreEqual(30f, VehicleDamageMath.BulletDamage(40f, VehicleHitZone.Body, R), 1e-4f);
            Assert.AreEqual(20f, VehicleDamageMath.BulletDamage(40f, VehicleHitZone.Roof, R), 1e-4f);
            Assert.AreEqual(0f, VehicleDamageMath.BulletDamage(40f, VehicleHitZone.Glass, R));
            Assert.AreEqual(0f, VehicleDamageMath.BulletDamage(40f, VehicleHitZone.Tyre, R));
        }

        [Test]
        public void ZoneAt_TopIsRoof_LowNoseIsEngine_RestIsBody()
        {
            Assert.AreEqual(VehicleHitZone.Roof, VehicleDamageMath.ZoneAt(0.9f, 0.5f, R));
            Assert.AreEqual(VehicleHitZone.Engine, VehicleDamageMath.ZoneAt(0.4f, 0.9f, R));
            Assert.AreEqual(VehicleHitZone.Body, VehicleDamageMath.ZoneAt(0.4f, 0.3f, R));
        }

        [Test]
        public void Collision_NothingUpToTheMinimum_ThenLinear_Capped()
        {
            Assert.AreEqual(0f, VehicleDamageMath.CollisionDamage(R.CollisionDamageMinSpeed, R));
            Assert.AreEqual(R.CollisionDamagePerMps * 4f, VehicleDamageMath.CollisionDamage(R.CollisionDamageMinSpeed + 4f, R), 1e-3f);
            Assert.AreEqual(VehicleDamageMath.CollisionDamage(R.MaxReportedImpact, R), VehicleDamageMath.CollisionDamage(1000f, R));
        }

        // ---- Stages ----

        [Test]
        public void Stage_WhiteSmokeBelowHalf_BlackBelowQuarter_BurningAtZero()
        {
            Assert.AreEqual(VehicleDamageStage.Healthy, VehicleDamageMath.StageFor(600f, 1000f, R));
            Assert.AreEqual(VehicleDamageStage.WhiteSmoke, VehicleDamageMath.StageFor(400f, 1000f, R));
            Assert.AreEqual(VehicleDamageStage.BlackSmoke, VehicleDamageMath.StageFor(200f, 1000f, R));
            Assert.AreEqual(VehicleDamageStage.Burning, VehicleDamageMath.StageFor(0f, 1000f, R));
        }

        [Test]
        public void Stage_OnlyMovesForward_AndBurningDisables()
        {
            Assert.AreEqual(VehicleDamageStage.Burning, VehicleDamageMath.Advance(VehicleDamageStage.Burning, VehicleDamageStage.Healthy));
            Assert.AreEqual(VehicleDamageStage.Wrecked, VehicleDamageMath.Advance(VehicleDamageStage.BlackSmoke, VehicleDamageStage.Wrecked));
            Assert.IsFalse(VehicleDamageMath.Disables(VehicleDamageStage.BlackSmoke));
            Assert.IsTrue(VehicleDamageMath.Disables(VehicleDamageStage.Burning));
            Assert.IsTrue(VehicleDamageMath.Disables(VehicleDamageStage.Wrecked));
        }

        [Test]
        public void Burn_ExplodesOnlyAfterBurnSeconds()
        {
            Assert.IsFalse(VehicleDamageMath.BurnedOut(R.BurnSeconds - 0.01f, R));
            Assert.IsTrue(VehicleDamageMath.BurnedOut(R.BurnSeconds, R));
        }

        // ---- Tyres ----

        [Test]
        public void Tyre_GoesFlatWhenItsHealthRunsOut()
        {
            float health = R.TyreHealth;
            Assert.IsFalse(VehicleDamageMath.HitTyre(ref health, R.TyreHealth * 0.5f));
            Assert.IsTrue(VehicleDamageMath.HitTyre(ref health, R.TyreHealth * 0.5f));
            Assert.AreEqual(0f, health);
        }

        [Test]
        public void FlatTyres_LowerGripAndCorner_PullTheSteering_AndSlowTheCar()
        {
            var grip = new float[4];
            var drop = new float[4];
            VehicleDamageMath.WheelEffects(0b0001, 4, R, grip, drop); // front left flat
            Assert.AreEqual(R.FlatGrip, grip[0], 1e-5f);
            Assert.AreEqual(1f, grip[1]);
            Assert.AreEqual(R.FlatDropMeters, drop[0], 1e-5f);
            Assert.AreEqual(0f, drop[3]);
            Assert.Less(VehicleDamageMath.SteerPull(0b0001, 4, R), 0f);  // pulls left
            Assert.Greater(VehicleDamageMath.SteerPull(0b0010, 4, R), 0f); // pulls right
            Assert.AreEqual(0f, VehicleDamageMath.SteerPull(0b0011, 4, R)); // both: straight
            Assert.AreEqual(R.FlatTopSpeedFactor * R.FlatTopSpeedFactor,
                VehicleDamageMath.TopSpeedFactor(VehicleDamageStage.Healthy, VehicleDamageMath.CountBits(0b0101), R), 1e-5f);
        }

        // ---- Glass ----

        [Test]
        public void Glass_FirstHitCracks_SecondShatters()
        {
            Assert.AreEqual(0, VehicleDamageMath.GlassState(0, R));
            Assert.AreEqual(1, VehicleDamageMath.GlassState(1, R));
            Assert.AreEqual(2, VehicleDamageMath.GlassState(2, R));
        }

        [Test]
        public void PanePacking_KeepsTheOtherPanes()
        {
            int packed = VehicleDamageMath.SetPane(0, 3, 2);
            packed = VehicleDamageMath.SetPane(packed, 0, 1);
            Assert.AreEqual(1, VehicleDamageMath.GetPane(packed, 0));
            Assert.AreEqual(2, VehicleDamageMath.GetPane(packed, 3));
            Assert.AreEqual(0, VehicleDamageMath.GetPane(packed, 5));
        }

        // A box from x -1..1, y 0..1.5, z -2..2 (z toward the nose); glass between y 0.9 and 1.4.
        static int Pane(float ox, float oy, float oz, float dx, float dy, float dz) =>
            VehicleDamageMath.PaneHit(ox, oy, oz, dx, dy, dz, -1f, 1f, -2f, 2f, 0.9f, 1.4f);

        [Test]
        public void PaneHit_TheFaceTheShotEntersThrough_InTheGlassBand()
        {
            Assert.AreEqual(0, Pane(0f, 1.1f, 10f, 0f, 0f, -1f));   // from ahead: windshield
            Assert.AreEqual(1, Pane(0f, 1.1f, -10f, 0f, 0f, 1f));   // from behind: rear window
            Assert.AreEqual(2, Pane(-10f, 1.1f, 1f, 1f, 0f, 0f));   // left, front half
            Assert.AreEqual(3, Pane(-10f, 1.1f, -1f, 1f, 0f, 0f));  // left, rear half
            Assert.AreEqual(4, Pane(10f, 1.1f, 1f, -1f, 0f, 0f));   // right, front half
            Assert.AreEqual(5, Pane(10f, 1.1f, -1f, -1f, 0f, 0f));  // right, rear half
        }

        [Test]
        public void PaneHit_BelowTheBeltOrMissing_IsNoPane()
        {
            Assert.AreEqual(-1, Pane(-10f, 0.5f, 0f, 1f, 0f, 0f)); // the door below the window
            Assert.AreEqual(-1, Pane(-10f, 3f, 0f, 1f, 0f, 0f));   // over the roof
            Assert.AreEqual(-1, Pane(-10f, 1.1f, 0f, -1f, 0f, 0f)); // pointing away
        }

        // ---- Dents ----

        [Test]
        public void ImpactDent_NoneUpToTheMinimum_ThenGrowsWithSpeed_RadiusCapped()
        {
            Assert.IsFalse(VehicleDamageMath.ImpactDent(R.CollisionDamageMinSpeed, R, out _, out _));
            Assert.IsTrue(VehicleDamageMath.ImpactDent(10f, R, out float r10, out float d10));
            Assert.IsTrue(VehicleDamageMath.ImpactDent(20f, R, out float r20, out float d20));
            Assert.Greater(d20, d10);
            Assert.GreaterOrEqual(r20, r10);
            VehicleDamageMath.ImpactDent(1000f, R, out float rMax, out _);
            Assert.AreEqual(R.DentImpactMaxRadius, rMax, 1e-5f);
        }

        [Test]
        public void DentPush_FallsOffWithDistance_AndNeverPassesTheMaximumDepth()
        {
            Assert.AreEqual(0.05f, VehicleDamageMath.DentPush(0f, 0.05f, VehicleDamageMath.DentFalloff(0f, 1f), R), 1e-5f);
            Assert.AreEqual(0f, VehicleDamageMath.DentPush(0f, 0.05f, VehicleDamageMath.DentFalloff(1f, 1f), R));
            Assert.AreEqual(0.03f, VehicleDamageMath.DentPush(R.DentMaxDepth - 0.03f, 0.05f, 1f, R), 1e-5f);
            Assert.AreEqual(0f, VehicleDamageMath.DentPush(R.DentMaxDepth, 0.05f, 1f, R));
        }

        // ---- Bullet holes ----

        [Test]
        public void Decals_ReuseTheOldestSlotOnceFull()
        {
            Assert.AreEqual(0, VehicleDamageMath.DecalSlot(0, 30));
            Assert.AreEqual(29, VehicleDamageMath.DecalSlot(29, 30));
            Assert.AreEqual(0, VehicleDamageMath.DecalSlot(30, 30));
        }

        // ---- Late joiners: the history replays oldest first ----

        [Test]
        public void History_ReplaysOldestFirst_FromTheRing()
        {
            Assert.AreEqual(0, VehicleDamageMath.HistorySlot(3, 30, 0));   // not full: from slot 0
            Assert.AreEqual(2, VehicleDamageMath.HistorySlot(3, 30, 2));
            Assert.AreEqual(5, VehicleDamageMath.HistorySlot(35, 30, 0));  // wrapped: the oldest kept is slot 5
            Assert.AreEqual(4, VehicleDamageMath.HistorySlot(35, 30, 29)); // the newest
            Assert.AreEqual(3, VehicleDamageMath.HistoryCount(3, 30));
            Assert.AreEqual(30, VehicleDamageMath.HistoryCount(35, 30));
        }

        // ---- Bullet dents scale with the weapon ----

        [Test]
        public void BulletDent_DeeperForHeavierRounds_Capped()
        {
            VehicleDamageMath.BulletDent(R.DentBulletRefDamage, R, out float r1, out float d1);
            VehicleDamageMath.BulletDent(R.DentBulletRefDamage * 2f, R, out _, out float d2);
            VehicleDamageMath.BulletDent(R.DentBulletRefDamage * 100f, R, out _, out float dMax);
            VehicleDamageMath.BulletDent(0f, R, out _, out float d0);
            Assert.AreEqual(R.DentBulletRadius, r1, 1e-5f);
            Assert.AreEqual(R.DentBulletDepth * R.DeformScale, d1, 1e-5f);
            Assert.Greater(d2, d1);
            Assert.AreEqual(R.DentBulletDepth * R.DeformScale * 2f, dMax, 1e-5f);
            Assert.AreEqual(R.DentBulletDepth * R.DeformScale * 0.5f, d0, 1e-5f);
        }

        // ---- Detachable parts ----

        [Test]
        public void Part_HangsLooseAtHalf_FallsAtZero_AndStaysFallen()
        {
            float health = R.PartHealth;
            Assert.AreEqual(PartState.Fixed, VehicleDamageMath.HitPart(ref health, R.PartHealth * 0.4f, R));
            Assert.AreEqual(PartState.Dangling, VehicleDamageMath.HitPart(ref health, R.PartHealth * 0.2f, R));
            Assert.AreEqual(PartState.Fallen, VehicleDamageMath.HitPart(ref health, R.PartHealth, R));
            Assert.AreEqual(0f, health);
            Assert.AreEqual(PartState.Fallen, VehicleDamageMath.HitPart(ref health, 0f, R));
        }
    }
}
