using System;
using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class WeaponSoundsTests
    {
        const string Shots = "Assets/_Project/Audio/Weapons/Freesound/Resources/Shots";

        static int[] GunIdsInData()
        {
            var json = File.ReadAllText("Assets/_Project/Data/Resources/GameData/weapons.json");
            var ids = new System.Collections.Generic.List<int>();
            // Every weapon row has "Id": n and "Category": c (2 = melee); read the pairs in order.
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json, "\"Id\"\\s*:\\s*(\\d+)[^{}]*?\"Category\"\\s*:\\s*(\\d)"))
                if (m.Groups[2].Value != "2") ids.Add(int.Parse(m.Groups[1].Value));
            return ids.ToArray();
        }

        [Test]
        public void Every_firearm_in_weapons_json_has_a_gun_sound()
        {
            var ids = GunIdsInData();
            Assert.GreaterOrEqual(ids.Length, 21);
            foreach (var id in ids) Assert.IsTrue(WeaponSounds.TryGet(id, out _), "weapon " + id + " has no WeaponSounds entry");
        }

        [Test]
        public void No_entry_for_a_weapon_that_is_not_a_firearm()
        {
            var ids = GunIdsInData();
            foreach (var e in WeaponSounds.Table) Assert.Contains(e.WeaponId, ids, "WeaponSounds has weapon " + e.WeaponId + " but weapons.json has no such firearm");
            Assert.AreEqual(WeaponSounds.Table.Length, WeaponSounds.Table.Select(e => e.WeaponId).Distinct().Count());
        }

        [Test]
        public void Pitch_and_loudness_stay_in_a_believable_range()
        {
            foreach (var e in WeaponSounds.Table)
            {
                Assert.That(e.Pitch, Is.InRange(0.8f, 1.15f), "pitch of weapon " + e.WeaponId);
                Assert.That(e.Loudness, Is.InRange(0.8f, 1.3f), "loudness of weapon " + e.WeaponId);
            }
        }

        [Test]
        public void Same_class_weapons_are_told_apart_by_pitch()
        {
            foreach (var g in WeaponSounds.Table.GroupBy(e => e.Class).Where(g => g.Count() > 1))
                Assert.AreEqual(g.Count(), g.Select(e => e.Pitch).Distinct().Count(), g.Key + " has two weapons with the same pitch");
        }

        [Test]
        public void Every_class_has_near_and_far_clips_in_the_repository()
        {
            foreach (var c in Enum.GetValues(typeof(GunClass)).Cast<GunClass>())
            {
                Assert.IsTrue(Directory.GetFiles(Shots, WeaponSounds.NearPrefix(c) + "*.wav").Length > 0, c + " has no near clip");
                Assert.IsTrue(Directory.GetFiles(Shots, WeaponSounds.FarPrefix(c) + "*.wav").Length > 0, c + " has no far clip");
            }
            Assert.IsTrue(Directory.GetFiles(Shots, WeaponSounds.SuppressedNear + "*.wav").Length > 0);
            Assert.IsTrue(Directory.GetFiles(Shots, WeaponSounds.IndoorTail + "*.wav").Length > 0);
            foreach (var c in Enum.GetValues(typeof(GunClass)).Cast<GunClass>())
                Assert.IsTrue(File.Exists(Path.Combine(Shots, WeaponSounds.ReloadName(c) + ".wav")), c + " reload clip");
        }
    }
}
