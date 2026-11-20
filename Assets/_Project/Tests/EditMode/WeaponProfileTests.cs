using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-493: every gun has its own class, fire modes and stat profile, and the tooltip says so.
    public class WeaponProfileTests
    {
        const string DataFolder = "Assets/_Project/Data/Resources/GameData/";

        static WeaponTable Weapons() => JsonUtility.FromJson<WeaponTable>(File.ReadAllText(DataFolder + "weapons.json"));
        static WeaponDef[] Guns() => Weapons().Weapons.Where(WeaponStats.IsFirearm).ToArray();

        static string Profile(WeaponDef w) =>
            $"{w.Class}|{w.Damage}|{w.Pellets}|{w.FireInterval}|{w.MagSize}|{w.MuzzleSpeed}|{w.FalloffEnd}|{w.RecoilPitchDeg}|{w.RecoilYawDeg}|{w.Auto}|{w.Burst}";

        [Test]
        public void EveryGun_HasAClass()
        {
            foreach (var w in Guns()) Assert.AreNotEqual(WeaponClass.None, w.Class, w.Name);
        }

        [Test]
        public void NoTwoGuns_ShareAStatProfile()
        {
            var guns = Guns();
            CollectionAssert.AllItemsAreUnique(guns.Select(Profile).ToArray());
            // Not just one decimal apart: the bars the player compares differ too (damage, rate, speed, kick).
            for (int i = 0; i < guns.Length; i++)
                for (int j = i + 1; j < guns.Length; j++)
                {
                    var a = WeaponStats.Bars(guns[i], AttachMods.None).Select(b => b.Value);
                    var b2 = WeaponStats.Bars(guns[j], AttachMods.None).Select(b => b.Value);
                    Assert.IsFalse(a.SequenceEqual(b2), guns[i].Name + " and " + guns[j].Name + " show the same bars");
                }
        }

        [Test]
        public void EachClass_HasItsFireModes()
        {
            foreach (var w in Guns())
            {
                switch (w.Class)
                {
                    case WeaponClass.Ar:
                        Assert.IsTrue(w.Auto, w.Name + ": an AR fires automatic (and single)");
                        break;
                    case WeaponClass.Smg:
                    case WeaponClass.Lmg:
                        Assert.IsTrue(w.Auto, w.Name + ": automatic");
                        Assert.Less(w.FireInterval, 0.12f, w.Name + ": fast");
                        break;
                    case WeaponClass.Dmr:
                        Assert.IsFalse(w.Auto, w.Name + ": a DMR is single only");
                        Assert.LessOrEqual(w.Burst, 1, w.Name);
                        break;
                    case WeaponClass.Sr:
                        Assert.IsFalse(w.Auto, w.Name + ": bolt");
                        Assert.LessOrEqual(w.Burst, 1, w.Name);
                        Assert.Greater(w.FireInterval, 1f, w.Name + ": a bolt cycle");
                        Assert.AreEqual("볼트액션", WeaponStats.FireModes(w));
                        break;
                    case WeaponClass.Shotgun:
                        Assert.IsFalse(w.Auto, w.Name);
                        Assert.Greater(w.Pellets, 1, w.Name + ": pellets");
                        break;
                    case WeaponClass.Pistol:
                        Assert.AreEqual(WeaponCategory.Sidearm, w.Category, w.Name);
                        Assert.IsFalse(w.Auto, w.Name);
                        break;
                }
            }
            var t = Weapons();
            Assert.Greater(Guns().Count(w => w.Class == WeaponClass.Ar && w.Burst > 1), 0, "some AR has a burst");
            Assert.Greater(Guns().Count(w => w.Class == WeaponClass.Smg && w.Burst > 1), 0, "some SMG has a burst");
            Assert.AreEqual(WeaponClass.Sr, t.Find(5).Class);
            Assert.AreEqual(WeaponClass.Dmr, t.Find(13).Class);
        }

        [Test]
        public void ClassesFollowTheReferencePattern()
        {
            var guns = Guns();
            float Avg(WeaponClass c, System.Func<WeaponDef, float> f) => guns.Where(w => w.Class == c).Average(f);
            Assert.Less(Avg(WeaponClass.Smg, w => w.MuzzleSpeed), Avg(WeaponClass.Ar, w => w.MuzzleSpeed), "SMG bullets are slow");
            Assert.Greater(Avg(WeaponClass.Dmr, w => w.Damage), Avg(WeaponClass.Ar, w => w.Damage), "a DMR hits harder than an AR");
            Assert.Greater(Avg(WeaponClass.Sr, w => w.Damage), Avg(WeaponClass.Dmr, w => w.Damage));
            Assert.Less(Avg(WeaponClass.Smg, w => w.FireInterval), Avg(WeaponClass.Ar, w => w.FireInterval), "SMGs fire fastest");
            Assert.Greater(Avg(WeaponClass.Dmr, w => w.FalloffEnd), Avg(WeaponClass.Ar, w => w.FalloffEnd), "DMRs reach further");
            Assert.Less(Avg(WeaponClass.Smg, w => w.FalloffEnd), Avg(WeaponClass.Ar, w => w.FalloffEnd));
        }

        [Test]
        public void ArmorTiers_FollowTheReference_30_40_55()
        {
            var rules = JsonUtility.FromJson<RulesProbe>(File.ReadAllText(DataFolder + "rules.json")).Armor;
            CollectionAssert.AreEqual(new[] { 0.3f, 0.4f, 0.55f }, rules.Helmets.Select(t => t.Reduction).ToArray());
            CollectionAssert.AreEqual(new[] { 0.3f, 0.4f, 0.55f }, rules.Vests.Select(t => t.Reduction).ToArray());
        }

        [System.Serializable]
        class RulesProbe { public ArmorRules Armor = new ArmorRules(); }

        // ---- the tooltip text builder ----

        static WeaponDef Rifle => new WeaponDef
        {
            Id = 3, Name = "Test", Class = WeaponClass.Ar, Ammo = AmmoType.Rifle556, Auto = true, Burst = 3, Damage = 40, FireInterval = 0.1f,
            MagSize = 30, MuzzleSpeed = 880, FalloffEnd = 120, RecoilPitchDeg = 1f, RecoilYawDeg = 0.5f,
        };

        [Test]
        public void TooltipText_ClassModesAmmoMagAndSlots()
        {
            var parts = WeaponStats.TooltipText(Rifle, 0, new OpticRules()).Split(new[] { " · " }, System.StringSplitOptions.None);
            Assert.AreEqual("AR 돌격소총", parts[0], "the type line is the class");
            CollectionAssert.Contains(parts, "사격 단발/점사 3발/연사");
            CollectionAssert.Contains(parts, "탄약 5.56mm");
            CollectionAssert.Contains(parts, "탄창 30발");
            StringAssert.StartsWith("부착 칸 ", parts.Last(), "a bare gun lists the places that take parts");
            StringAssert.Contains("조준경", parts.Last());
        }

        [Test]
        public void TooltipText_ListsFittedParts_AndAnExtendedMagazineGrowsTheMag()
        {
            var rules = new OpticRules();
            int packed = Attachments.With(Attachments.With(0, AttachSlot.Muzzle, 9), AttachSlot.Mag, 16);
            string text = WeaponStats.TooltipText(Rifle, packed, rules);
            StringAssert.Contains("총구 반동 보정기: 위 반동 -12%", text);
            StringAssert.Contains("탄창 " + rules.FindAttachment(16).Name, text);
            StringAssert.Contains("탄창 " + Mathf.RoundToInt(30 * rules.FindAttachment(16).MagFactor) + "발", text);
            StringAssert.DoesNotContain("부착 칸", text);
        }

        [Test]
        public void TooltipText_OtherClasses()
        {
            var rules = new OpticRules();
            Assert.AreEqual("볼트액션", WeaponStats.FireModes(new WeaponDef { Class = WeaponClass.Sr, FireInterval = 1.3f }));
            Assert.AreEqual("단발", WeaponStats.FireModes(new WeaponDef { Class = WeaponClass.Dmr }));
            Assert.AreEqual("단발/연사", WeaponStats.FireModes(new WeaponDef { Class = WeaponClass.Smg, Auto = true }));
            StringAssert.StartsWith("DMR 지정사수소총", WeaponStats.TooltipText(new WeaponDef { Id = 13, Class = WeaponClass.Dmr, MagSize = 20 }, 0, rules));
            Assert.AreEqual("근접 무기 · 피해 50", WeaponStats.TooltipText(new WeaponDef { Category = WeaponCategory.Melee, Damage = 50 }, 0, rules));
            Assert.AreEqual("", WeaponStats.TooltipText(null, 0, rules));
            Assert.AreEqual("조준경 " + Attachments.NameOf(4), WeaponStats.PartLine(4), "an optic's line is not \"3배율: 3배율\"");
        }

        [Test]
        public void Bars_AreFive_InRange_AndAttachmentsRaiseStability()
        {
            var bare = WeaponStats.Bars(Rifle, AttachMods.None);
            CollectionAssert.AreEqual(WeaponStats.BarLabels, bare.Select(b => b.Label).ToArray());
            foreach (var b in bare) Assert.That(b.Fill, Is.InRange(0f, 1f), b.Label);
            Assert.AreEqual("40", bare[0].Value);
            Assert.AreEqual("120m", bare[1].Value);
            Assert.AreEqual("600rpm", bare[3].Value);
            Assert.AreEqual("880m/s", bare[4].Value);
            var gripped = WeaponStats.Bars(Rifle, new AttachMods { Pitch = 0.8f });
            Assert.Greater(gripped[2].Fill, bare[2].Fill, "a grip shows as more stability");
            var pellets = WeaponStats.Bars(new WeaponDef { Damage = 22, Pellets = 9 }, AttachMods.None);
            Assert.AreEqual("22×9", pellets[0].Value);
            Assert.AreEqual(1f, pellets[0].Fill, 1e-6f, "clamped");
        }

        [Test]
        public void GearTips_ArmorAndHealing()
        {
            Assert.AreEqual("피해 감소 40% · 내구도 150", GearTips.Armor(ArmorSlot.Helmet, 2, new ArmorRules()));
            Assert.AreEqual("", GearTips.Armor(ArmorSlot.Vest, 0, new ArmorRules()));
            var heal = new HealRules();
            Assert.AreEqual("체력 +10 (75까지) · 4초", GearTips.Heal(HealKind.Bandage, heal));
            Assert.AreEqual("체력 75까지 · 6초", GearTips.Heal(HealKind.FirstAid, heal));
            Assert.AreEqual("부스트 +40 · 4초", GearTips.Heal(HealKind.Drink, heal));
            Assert.AreEqual("", GearTips.Heal(HealKind.None, heal));
        }
    }
}
