using System.IO;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-073: melee weapons — data, the melee slot, the strike numbers, and the swing timing and pose.
    public class MeleeWeaponTests
    {
        const string DataFolder = "Assets/_Project/Data/Resources/GameData/";
        const int Pistol = 1, Rifle = 3, Machete = 21, Crowbar = 22, Sickle = 23, Pan = 24;
        static readonly MeleeRules Rules = new MeleeRules();

        static WeaponTable Table() => new WeaponTable
        {
            Weapons = new[]
            {
                new WeaponDef { Id = Pistol, Name = "VK", Category = WeaponCategory.Sidearm, FireInterval = 0.2f, MagSize = 12, ReloadTime = 1f },
                new WeaponDef { Id = Rifle, Name = "Warden", Category = WeaponCategory.Primary, Ammo = AmmoType.Rifle556, FireInterval = 0.1f, MagSize = 30, ReloadTime = 2f },
                new WeaponDef { Id = Machete, Name = "마체테", Category = WeaponCategory.Melee, Damage = 50f, Range = 1.6f, ConeDeg = 70f, FireInterval = 0.65f, HeadFactor = 1.5f },
                new WeaponDef { Id = Pan, Name = "프라이팬", Category = WeaponCategory.Melee, Damage = 60f, Range = 1.4f, ConeDeg = 60f, FireInterval = 0.9f, HeadFactor = 1.5f, Swing = MeleeSwingStyle.Chop },
            },
        };

        static WeaponTable DataTable() => JsonUtility.FromJson<WeaponTable>(File.ReadAllText(DataFolder + "weapons.json"));

        [Test]
        public void Data_HasTheFourMeleeWeapons_WithSaneNumbers()
        {
            var table = DataTable();
            var expected = new[] { (Machete, "마체테", MeleeSwingStyle.Slash), (Crowbar, "쇠지렛대", MeleeSwingStyle.Chop), (Sickle, "낫", MeleeSwingStyle.Slash), (Pan, "프라이팬", MeleeSwingStyle.Chop) };
            foreach (var (id, name, swing) in expected)
            {
                var w = table.Find(id);
                Assert.IsNotNull(w, name);
                Assert.AreEqual(name, w.Name);
                Assert.IsTrue(w.IsMelee, name);
                Assert.AreEqual(swing, w.Swing, name);
                Assert.That(w.Damage, Is.InRange(20f, 100f), name);
                Assert.That(w.Range, Is.InRange(1.2f, 2f), name + ": a bit further than a fist");
                Assert.Greater(w.Range, Rules.PunchRange, name);
                Assert.That(w.ConeDeg, Is.InRange(30f, 120f), name);
                Assert.That(w.FireInterval, Is.InRange(0.3f, 1.5f), name);
                Assert.Greater(w.HeadFactor, 1f, name);
                Assert.AreEqual(0, w.MagSize, name + ": no magazine");
                Assert.IsFalse(w.Auto, name);
            }
            foreach (var w in table.Weapons)
                if (!w.IsMelee) Assert.AreNotEqual(WeaponCategory.Melee, w.Category);
        }

        [Test]
        public void Data_LootHoldsEveryMeleeWeapon()
        {
            var loot = JsonUtility.FromJson<LootTable>(File.ReadAllText(DataFolder + "loot.json"));
            foreach (int id in new[] { Machete, Crowbar, Sickle, Pan })
            {
                var entry = System.Array.Find(loot.Entries, e => e.Kind == LootKind.Weapon && e.WeaponId == id);
                Assert.IsNotNull(entry, "weapon " + id);
                Assert.Greater(entry.Weight, 0f);
            }
        }

        [Test]
        public void MeleeSlot_TakesMeleeWeaponsOnly()
        {
            Assert.IsTrue(Loadout.SlotTakes(Loadout.MeleeSlot, WeaponCategory.Melee));
            for (int slot = 0; slot < Loadout.SlotCount; slot++)
                if (slot != Loadout.MeleeSlot) Assert.IsFalse(Loadout.SlotTakes(slot, WeaponCategory.Melee), "slot " + slot);
            Assert.IsFalse(Loadout.SlotTakes(Loadout.MeleeSlot, WeaponCategory.Primary));
            Assert.IsFalse(Loadout.SlotTakes(Loadout.MeleeSlot, WeaponCategory.Sidearm));
        }

        [Test]
        public void PickingUp_AMeleeWeapon_GoesToTheMeleeSlot_InHandWhenUnarmed_AndSwapsTheOldOne()
        {
            var l = new Loadout(Table());
            Assert.AreEqual(Loadout.MeleeSlot, l.PickupSlotFor(Machete));
            Assert.AreEqual(0, l.AddWeapon(Machete, 0));
            Assert.AreEqual(Machete, l.WeaponInSlot(Loadout.MeleeSlot));
            Assert.AreEqual(Loadout.MeleeSlot, l.ActiveSlot, "empty hands take it");
            Assert.AreEqual(Machete, l.AddWeapon(Pan, 0), "the machete comes back out");
            Assert.AreEqual(Pan, l.WeaponInSlot(Loadout.MeleeSlot));

            // A gun picked up while the pan is in hand goes to its own slot; the pan stays in hand.
            l.AddWeapon(Rifle, 30);
            Assert.AreEqual(Rifle, l.WeaponInSlot(0));
            Assert.AreEqual(Loadout.MeleeSlot, l.ActiveSlot);
            Assert.IsFalse(l.SwapSlots(0, Loadout.MeleeSlot), "a gun does not fit the melee slot");
        }

        [Test]
        public void AMeleeWeapon_NeverFiresOrReloads()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Machete, 0);
            l.AddAmmo(AmmoType.Pistol9, 100);
            Assert.IsFalse(l.TryFire(0.0));
            Assert.IsFalse(l.TryStartReload(0.0));
            Assert.IsFalse(l.CanSwitchFireMode(Loadout.MeleeSlot));
        }

        [Test]
        public void Strike_FistsUseThePunch_MeleeWeaponsTheirOwnNumbers()
        {
            var table = Table();
            Assert.IsTrue(Melee.CanStrike(null));
            Assert.IsTrue(Melee.CanStrike(table.Find(Machete)));
            Assert.IsFalse(Melee.CanStrike(table.Find(Rifle)), "a gun in hand fires, never strikes");

            var fist = Melee.StrikeOf(null, Rules);
            Assert.AreEqual(0, fist.WeaponId);
            Assert.AreEqual(Rules.PunchDamage, fist.Damage);
            Assert.AreEqual(Rules.PunchRange, fist.Range);
            Assert.AreEqual(Rules.PunchConeDeg, fist.ConeDeg);
            Assert.AreEqual(Rules.PunchInterval, fist.Interval);

            var machete = Melee.StrikeOf(table.Find(Machete), Rules);
            Assert.AreEqual(Machete, machete.WeaponId);
            Assert.AreEqual(50f, machete.Damage);
            Assert.AreEqual(1.6f, machete.Range, 1e-5f);
            Assert.AreEqual(70f, machete.ConeDeg);
            Assert.AreEqual(0.65f, machete.Interval, 1e-5f);
            Assert.AreEqual(50f, Melee.Damage(HitZone.Body, in machete));
            Assert.AreEqual(50f, Melee.Damage(HitZone.Limb, in machete));
            Assert.AreEqual(75f, Melee.Damage(HitZone.Head, in machete), 1e-4f);
            Assert.AreEqual(Melee.Damage(HitZone.Head, Rules), Melee.Damage(HitZone.Head, in fist), "the punch is unchanged");
        }

        [Test]
        public void Reach_ALongerWeaponReachesWhereAFistDoesNot()
        {
            var machete = Melee.StrikeOf(Table().Find(Machete), Rules);
            var fist = Melee.StrikeOf(null, Rules);
            // A chest 1.5 m straight ahead of the eyes.
            Assert.IsFalse(Melee.InReach(0f, 0f, 1.5f, 0f, 0f, 1f, fist.Range, fist.ConeDeg));
            Assert.IsTrue(Melee.InReach(0f, 0f, 1.5f, 0f, 0f, 1f, machete.Range, machete.ConeDeg));
            // 33 degrees off to the side: inside the machete's 70 degree cone, outside the fist's 60.
            float x = Mathf.Sin(33f * Mathf.Deg2Rad), z = Mathf.Cos(33f * Mathf.Deg2Rad);
            Assert.IsTrue(Melee.InReach(x, 0f, z, 0f, 0f, 1f, machete.Range, machete.ConeDeg));
            Assert.IsFalse(Melee.InReach(x, 0f, z, 0f, 0f, 1f, fist.Range, fist.ConeDeg));
        }

        [Test]
        public void BotReach_IsTheWeaponsReachPlusAMargin_OrThePunchDistance()
        {
            Assert.AreEqual(Rules.BotPunchMeters, Melee.BotReach(null, Rules));
            Assert.AreEqual(1.6f + Rules.BotReachMargin, Melee.BotReach(Table().Find(Machete), Rules), 1e-5f);
        }

        [Test]
        public void SwingDuration_IsCappedByTheWeaponsInterval()
        {
            Assert.AreEqual(Rules.SwingSeconds, Melee.SwingDuration(Table().Find(Pan), Rules));
            var quick = new WeaponDef { Category = WeaponCategory.Melee, FireInterval = 0.3f };
            Assert.AreEqual(0.3f, Melee.SwingDuration(quick, Rules), 1e-5f);
        }

        [Test]
        public void SwingAt_WindsUp_Strikes_AndComesBackToRest()
        {
            const float d = 0.5f;
            Assert.AreEqual(default(MeleeSwingPhase), Melee.SwingAt(0f, d, Rules), "not started");
            Assert.AreEqual(default(MeleeSwingPhase), Melee.SwingAt(d, d, Rules), "over");
            Assert.AreEqual(default(MeleeSwingPhase), Melee.SwingAt(-0.1f, d, Rules));
            Assert.AreEqual(default(MeleeSwingPhase), Melee.SwingAt(0.1f, 0f, Rules), "no duration");

            var wound = Melee.SwingAt(d * Rules.SwingWindUpShare - 1e-4f, d, Rules);
            Assert.AreEqual(-1f, wound.Swing, 0.01f, "fully wound up");
            Assert.AreEqual(1f, wound.Weight, 0.01f);
            var struck = Melee.SwingAt(d * Rules.SwingStrikeShare - 1e-4f, d, Rules);
            Assert.AreEqual(1f, struck.Swing, 0.01f, "through the strike");
            Assert.AreEqual(1f, struck.Weight);
            var middle = Melee.SwingAt(d * (Rules.SwingWindUpShare + Rules.SwingStrikeShare) * 0.5f, d, Rules);
            Assert.AreEqual(0f, middle.Swing, 0.01f, "half way through the strike");
            Assert.AreEqual(1f, middle.Weight, "the swing pose is fully on during the strike");
            var late = Melee.SwingAt(d * 0.99f, d, Rules);
            Assert.That(late.Weight, Is.InRange(0f, 0.01f), "almost back at rest");

            // Every phase stays in range.
            for (float t = 0f; t <= d; t += 0.01f)
            {
                var p = Melee.SwingAt(t, d, Rules);
                Assert.That(p.Swing, Is.InRange(-1f, 1f));
                Assert.That(p.Weight, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void PoseAt_RestIsTheReadyPose()
        {
            foreach (var style in new[] { MeleeSwingStyle.Slash, MeleeSwingStyle.Chop })
            {
                var pose = Melee.PoseAt(style, default, Rules);
                Assert.AreEqual(Rules.ReadyArmDeg, pose.ArmRaiseDeg, 1e-4f, style.ToString());
                Assert.AreEqual(0f, pose.ArmSweepDeg, 1e-4f);
                Assert.AreEqual(0f, pose.TorsoTwistDeg, 1e-4f);
                Assert.AreEqual(0f, pose.TorsoPitchDeg, 1e-4f);
            }
        }

        [Test]
        public void PoseAt_ASlashSweepsFromRightToLeft_AtAboutLevel()
        {
            var wound = Melee.PoseAt(MeleeSwingStyle.Slash, new MeleeSwingPhase { Swing = -1f, Weight = 1f }, Rules);
            var struck = Melee.PoseAt(MeleeSwingStyle.Slash, new MeleeSwingPhase { Swing = 1f, Weight = 1f }, Rules);
            Assert.AreEqual(Rules.SlashArmDeg, wound.ArmRaiseDeg, 1e-4f);
            Assert.AreEqual(Rules.SlashSweepDeg, wound.ArmSweepDeg, 1e-4f, "wound up to the right");
            Assert.AreEqual(-Rules.SlashSweepDeg, struck.ArmSweepDeg, 1e-4f, "followed through to the left");
            Assert.Greater(wound.TorsoTwistDeg, 0f);
            Assert.Less(struck.TorsoTwistDeg, 0f);
        }

        [Test]
        public void PoseAt_AChopGoesOverTheHeadThenDownInFront()
        {
            var wound = Melee.PoseAt(MeleeSwingStyle.Chop, new MeleeSwingPhase { Swing = -1f, Weight = 1f }, Rules);
            var struck = Melee.PoseAt(MeleeSwingStyle.Chop, new MeleeSwingPhase { Swing = 1f, Weight = 1f }, Rules);
            Assert.AreEqual(Rules.ChopWindUpDeg, wound.ArmRaiseDeg, 1e-4f);
            Assert.AreEqual(Rules.ChopStrikeDeg, struck.ArmRaiseDeg, 1e-4f);
            Assert.Greater(wound.ArmRaiseDeg, 90f, "above level");
            Assert.Less(struck.ArmRaiseDeg, 90f, "below level");
            Assert.Less(wound.TorsoPitchDeg, 0f, "leans back to wind up");
            Assert.Greater(struck.TorsoPitchDeg, 0f, "bends into the chop");
            Assert.AreEqual(0f, struck.ArmSweepDeg);
            // Straight through: the arm only ever comes down during the strike.
            float last = float.MaxValue;
            for (float s = -1f; s <= 1f; s += 0.1f)
            {
                float raise = Melee.PoseAt(MeleeSwingStyle.Chop, new MeleeSwingPhase { Swing = s, Weight = 1f }, Rules).ArmRaiseDeg;
                Assert.LessOrEqual(raise, last + 1e-4f);
                last = raise;
            }
        }
    }
}
