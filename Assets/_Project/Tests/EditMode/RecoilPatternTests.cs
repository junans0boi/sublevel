using System;
using System.IO;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-089 / ADR-090: per-weapon recoil patterns, PUBG spread ratios and the aim punch, from the shipped data.
    public class RecoilPatternTests
    {
        const string DataFolder = "Assets/_Project/Data/Resources/GameData/";
        const int Pistol = 1, Smg = 2, Rifle = 3, Shotgun = 4, Sniper = 5;

        static WeaponTable Weapons() => JsonUtility.FromJson<WeaponTable>(File.ReadAllText(DataFolder + "weapons.json"));
        static GameRules Rules() => JsonUtility.FromJson<GameRules>(File.ReadAllText(DataFolder + "rules.json"));

        // A held trigger: the view offset after each of `shots` shots fired `interval` apart.
        static (float[] pitch, float[] yaw) Spray(WeaponDef w, int seed, float factor, RecoilRules rules, int shots = 30)
        {
            var recoil = new ViewRecoil();
            var rng = new System.Random(seed);
            var pitch = new float[shots];
            var yaw = new float[shots];
            for (int i = 0; i < shots; i++)
            {
                RecoilPattern.Kick(recoil, w, rng, factor, rules);
                pitch[i] = recoil.Pitch;
                yaw[i] = recoil.Yaw;
                recoil.Tick(w.FireInterval, rules.RecoverRate(w), rules.RecoverDelaySeconds);
            }
            return (pitch, yaw);
        }

        [Test]
        public void SameSeed_ThirtyShots_FollowTheWeaponPattern()
        {
            var rules = Rules();
            var rifle = Weapons().Find(Rifle);
            float factor = rules.Aim.Sights.RecoilFactor;
            var a = Spray(rifle, 42, factor, rules.Recoil);
            var b = Spray(rifle, 42, factor, rules.Recoil);
            CollectionAssert.AreEqual(a.pitch, b.pitch, "same seed, same climb");
            CollectionAssert.AreEqual(a.yaw, b.yaw, "same seed, same drift");

            // Vertical: every shot climbs by the weapon's kick (+-10%) until the cap, then stays there.
            float kick = rifle.RecoilPitchDeg * factor;
            for (int i = 1; i < 30; i++)
            {
                float step = a.pitch[i] - a.pitch[i - 1];
                if (a.pitch[i] < rules.Recoil.MaxPitchDeg - 1e-4f)
                    Assert.That(step, Is.InRange(kick * 0.9f - 1e-4f, kick * 1.1f + 1e-4f), "shot " + i + " climbs (no recovery inside the spray)");
                else Assert.LessOrEqual(step, kick * 1.1f + 1e-4f);
            }
            Assert.AreEqual(rules.Recoil.MaxPitchDeg, a.pitch[29], 1e-4f, "30 rounds hit the vertical cap");

            // Horizontal: never past the weapon's side limit, and it drifts the weapon's way (right for the rifle).
            float cap = rifle.RecoilYawMaxDeg * factor;
            foreach (float y in a.yaw) Assert.LessOrEqual(Mathf.Abs(y), cap + 1e-4f);
            Assert.Greater(a.yaw[29], 0f, "Warden drifts right");
        }

        [Test]
        public void SideDrift_FollowsTheBias_OverManySeeds()
        {
            var rules = Rules();
            var table = Weapons();
            float rifleSum = 0f, smgSum = 0f;
            for (int seed = 0; seed < 50; seed++)
            {
                rifleSum += Spray(table.Find(Rifle), seed, 1f, rules.Recoil, 15).yaw[14];
                smgSum += Spray(table.Find(Smg), seed, 1f, rules.Recoil, 15).yaw[14];
            }
            Assert.Greater(rifleSum / 50f, 0.3f, "Warden: to the right");
            Assert.Less(smgSum / 50f, -0.2f, "Ordo: to the left");
        }

        [Test]
        public void Crouching_AndLyingDown_ShrinkTheKick()
        {
            var rules = Rules().Recoil;
            Assert.AreEqual(1f, rules.StanceFactor(Stance.Stand));
            Assert.AreEqual(0.8f, rules.StanceFactor(Stance.Crouch), 1e-5f);
            Assert.AreEqual(0.6f, rules.StanceFactor(Stance.Prone), 1e-5f);
            var rifle = Weapons().Find(Rifle);
            var stand = Spray(rifle, 7, 1f, rules, 5);
            var crouch = Spray(rifle, 7, rules.StanceFactor(Stance.Crouch), rules, 5);
            var prone = Spray(rifle, 7, rules.StanceFactor(Stance.Prone), rules, 5);
            Assert.AreEqual(stand.pitch[4] * 0.8f, crouch.pitch[4], 1e-4f);
            Assert.AreEqual(stand.pitch[4] * 0.6f, prone.pitch[4], 1e-4f);
        }

        [Test]
        public void Recovery_WaitsForTheSprayToEnd_ThenSettlesAtTheWeaponsRate()
        {
            var rules = Rules().Recoil;
            var rifle = Weapons().Find(Rifle);
            var recoil = new ViewRecoil();
            var rng = new System.Random(1);
            for (int i = 0; i < 5; i++) RecoilPattern.Kick(recoil, rifle, rng, 1f, rules);
            float top = recoil.Pitch;
            recoil.Tick(rules.RecoverDelaySeconds * 0.5f, rules.RecoverRate(rifle), rules.RecoverDelaySeconds);
            Assert.AreEqual(top, recoil.Pitch, 1e-5f, "still inside the delay");
            Assert.AreEqual(10f, rules.RecoverRate(rifle), 1e-5f, "the Warden's own rate");
            for (int i = 0; i < 100; i++) recoil.Tick(0.02f, rules.RecoverRate(rifle), rules.RecoverDelaySeconds);
            Assert.AreEqual(0f, recoil.Pitch, 1e-4f);
            Assert.AreEqual(0f, recoil.Yaw, 1e-4f);
        }

        [Test]
        public void StandingStill_InSights_TheConeIsUnderAFiftiethOfADegree()
        {
            var rules = Rules();
            var standing = rules.Stance.For(Stance.Stand).SpreadFactor;
            foreach (var w in Weapons().Weapons)
            {
                if (w.IsMelee || w.Pellets > 1) continue;
                float factor = rules.Aim.Sights.SpreadFactor * standing * DamageRules.MovementSpread(w, 0f, false, rules.Fire);
                Assert.Less(w.SpreadDeg * factor, 0.05f, w.Name);
                // And every sampled shot lands inside that cone.
                var rng = new System.Random(3);
                var yaw = new float[1];
                var pitch = new float[1];
                for (int i = 0; i < 200; i++)
                {
                    DamageRules.ShotOffsets(w, rng, yaw, pitch, factor);
                    Assert.Less(Mathf.Sqrt(yaw[0] * yaw[0] + pitch[0] * pitch[0]), 0.025f, w.Name);
                }
            }
        }

        [Test]
        public void ShotgunPellets_KeepTheirPattern_InSights()
        {
            var rules = Rules();
            var shotgun = Weapons().Find(Shotgun);
            var rng = new System.Random(5);
            var yaw = new float[16];
            var pitch = new float[16];
            int n = DamageRules.ShotOffsets(shotgun, rng, yaw, pitch, rules.Aim.Sights.SpreadFactor);
            float widest = 0f;
            for (int i = 0; i < n; i++) widest = Mathf.Max(widest, Mathf.Sqrt(yaw[i] * yaw[i] + pitch[i] * pitch[i]));
            Assert.Greater(widest, 0.5f, "the pellets still spread");
            Assert.LessOrEqual(widest, shotgun.SpreadDeg * shotgun.MinSpreadFactor * 0.5f + 1e-4f);
        }

        [Test]
        public void HipFire_StanceMovementAndJump_FollowPubgRatios()
        {
            var rules = Rules();
            Assert.AreEqual(0.5f, rules.Stance.For(Stance.Crouch).SpreadFactor, 1e-5f, "crouch x0.5");
            Assert.AreEqual(0.2f, rules.Stance.For(Stance.Prone).SpreadFactor, 1e-5f, "prone x0.2");
            foreach (int id in new[] { Pistol, Smg, Rifle, Sniper })
            {
                var w = Weapons().Find(id);
                Assert.AreEqual(1f, DamageRules.MovementSpread(w, 0f, false, rules.Fire), 1e-5f, w.Name);
                Assert.AreEqual(5f, DamageRules.MovementSpread(w, 4.5f, false, rules.Fire), 1e-4f, w.Name + ": running x5");
                Assert.That(DamageRules.MovementSpread(w, 2.0f, false, rules.Fire), Is.InRange(2f, 2.6f), w.Name + ": walking about x2");
                Assert.AreEqual(15f, DamageRules.MovementSpread(w, 0f, true, rules.Fire), 1e-4f, w.Name + ": jumping x15");
            }
        }

        [Test]
        public void AimPunch_IsSmallerBehindAVest()
        {
            var rules = Rules().Recoil;
            float[] pitch = new float[4];
            for (int tier = 0; tier <= 3; tier++)
            {
                var recoil = new ViewRecoil();
                RecoilPattern.Punch(recoil, new System.Random(9), tier, rules);
                pitch[tier] = recoil.Pitch;
            }
            Assert.Greater(pitch[0], 0.5f, "a hit throws the view up");
            Assert.AreEqual(pitch[0] * 0.7f, pitch[1], 1e-4f);
            Assert.AreEqual(pitch[0] * 0.6f, pitch[2], 1e-4f);
            Assert.AreEqual(pitch[0] * 0.45f, pitch[3], 1e-4f);
        }
    }
}
