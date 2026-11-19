using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // r5 weapon effects: gunshot takes and the indoor tail (#287), spent cases (#199), combat sound clips (#288),
    // the magazine split and the inspect motion (#200).
    public class WeaponFxTests
    {
        const string Shots = "Assets/_Project/Audio/Weapons/Freesound/Resources/Shots";
        const string Sfx = "Assets/_Project/Audio/Foley/Freesound/Resources/Sfx";

        static int Files(string dir, string prefix) => Directory.GetFiles(dir, prefix + "*.wav").Length;

        // ---- #287 ----

        [Test]
        public void Every_weapon_has_at_least_three_takes_in_every_gunshot_layer()
        {
            foreach (var e in WeaponSounds.Table)
            {
                int near = Files(Shots, WeaponSounds.NearPrefix(e.Class)), far = Files(Shots, WeaponSounds.FarPrefix(e.Class));
                Assert.GreaterOrEqual(WeaponSounds.VariantCount(near), WeaponSounds.MinVariants, "near takes of weapon " + e.WeaponId);
                Assert.GreaterOrEqual(WeaponSounds.VariantCount(far), WeaponSounds.MinVariants, "far takes of weapon " + e.WeaponId);
                Assert.GreaterOrEqual(WeaponSounds.VariantCount(Files(Shots, WeaponSounds.IndoorTail)), WeaponSounds.MinVariants, "tail takes");
            }
            Assert.GreaterOrEqual(WeaponSounds.VariantCount(Files(Shots, WeaponSounds.SuppressedNear)), WeaponSounds.MinVariants);
        }

        [Test]
        public void Takes_are_all_distinct_and_cover_every_clip()
        {
            foreach (int clips in new[] { 1, 2, 3 })
            {
                int n = WeaponSounds.VariantCount(clips);
                var seen = new HashSet<(int, float, float)>();
                for (int i = 0; i < n; i++)
                {
                    var v = WeaponSounds.VariantOf(clips, i);
                    Assert.That(v.Clip, Is.InRange(0, clips - 1));
                    Assert.That(v.Pitch, Is.InRange(0.94f, 1.06f), "a take stays within the +-5% pitch family");
                    Assert.IsTrue(seen.Add((v.Clip, v.Pitch, v.CutoffHz)), "take " + i + " repeats another");
                }
                Assert.AreEqual(1f, WeaponSounds.VariantOf(clips, 0).Pitch, "the first take is the clip as cut");
            }
            Assert.AreEqual(0, WeaponSounds.VariantCount(0));
        }

        [Test]
        public void The_next_take_is_never_the_last_one()
        {
            var rng = new Random(3);
            int last = 0;
            for (int i = 0; i < 500; i++)
            {
                int next = WeaponSounds.NextVariant(1, last, rng.NextDouble());
                Assert.AreNotEqual(last, next);
                Assert.That(next, Is.InRange(0, WeaponSounds.VariantCount(1) - 1));
                last = next;
            }
            Assert.AreNotEqual(2, WeaponSounds.NextVariant(1, 2, 0.9999999));
        }

        [Test]
        public void Indoor_tail_only_under_a_roof_and_follows_the_louder_layer()
        {
            Assert.AreEqual(0f, WeaponSounds.TailLevel(false, 1f, 0f));
            Assert.AreEqual(WeaponSounds.IndoorTailGain, WeaponSounds.TailLevel(true, 1f, 0f), 1e-5f);
            Assert.AreEqual(WeaponSounds.IndoorTailGain * WeaponSounds.IndoorTailFarShare, WeaponSounds.TailLevel(true, 0f, 1f), 1e-5f);
            // The near/far crossfade itself is unchanged by the tail (SoundRules.ShotLayers).
            SoundRules.ShotLayers(10f, out float near, out float far);
            Assert.AreEqual(1f, near, 1e-4f); Assert.AreEqual(0f, far, 1e-4f);
            SoundRules.ShotLayers(500f, out near, out far);
            Assert.AreEqual(0f, near, 1e-4f); Assert.AreEqual(1f, far, 1e-4f);
        }

        // ---- #199 ----

        [Test]
        public void Every_firearm_throws_a_case_of_its_kind_at_its_own_time()
        {
            foreach (var e in WeaponSounds.Table)
            {
                Assert.IsTrue(CasingRules.TryGet(e.WeaponId, out var spec), "weapon " + e.WeaponId);
                if (e.Class == GunClass.Shotgun) Assert.AreEqual(CasingKind.Shell, spec.Kind, "shotguns drop hulls");
                if (e.Class == GunClass.Sniper) { Assert.AreEqual(EjectTiming.Bolt, spec.Timing); Assert.Greater(spec.DelaySeconds, 0.2f); }
                if (e.Class == GunClass.Pistol) Assert.AreEqual(CasingKind.Pistol, spec.Kind);
            }
            Assert.IsTrue(CasingRules.TryGet(16, out var duo));
            Assert.AreEqual(EjectTiming.OnReload, duo.Timing, "the break-action shotgun drops its shells when opened to reload");
            Assert.IsTrue(CasingRules.TryGet(4, out var breacher));
            Assert.AreEqual(EjectTiming.OnShot, breacher.Timing, "the magazine-fed shotgun ejects with each shot");
            Assert.IsFalse(CasingRules.TryGet(21, out _), "melee throws nothing");
        }

        [Test]
        public void Case_pool_stays_capped_and_short_lived()
        {
            Assert.That(CasingRules.MaxLiveHigh, Is.InRange(8, 64));
            Assert.Less(CasingRules.MaxLiveLow, CasingRules.MaxLiveHigh);
            Assert.That(CasingRules.LifeSeconds, Is.InRange(1f, 6f));
            foreach (CasingKind k in Enum.GetValues(typeof(CasingKind)))
            {
                CasingRules.Size(k, out float d, out float l);
                Assert.That(d, Is.InRange(0.008f, 0.025f)); Assert.That(l, Is.InRange(0.015f, 0.08f));
            }
        }

        [Test]
        public void Case_landing_sounds_by_surface()
        {
            Assert.AreEqual(0f, CasingRules.Sound(SurfaceId.Water, CasingKind.Rifle).Gain, "water swallows it");
            Assert.Greater(CasingRules.Sound(SurfaceId.Metal, CasingKind.Rifle).Gain, CasingRules.Sound(SurfaceId.Concrete, CasingKind.Rifle).Gain);
            Assert.Greater(CasingRules.Sound(SurfaceId.Concrete, CasingKind.Rifle).Gain, CasingRules.Sound(SurfaceId.Grass, CasingKind.Rifle).Gain);
            Assert.Less(CasingRules.Sound(SurfaceId.Grass, CasingKind.Rifle).CutoffHz, CasingRules.Sound(SurfaceId.Tile, CasingKind.Rifle).CutoffHz);
            Assert.Less(CasingRules.Sound(SurfaceId.Concrete, CasingKind.Shell).Pitch, CasingRules.Sound(SurfaceId.Concrete, CasingKind.Rifle).Pitch, "a hull is lower");
            foreach (SurfaceId s in Enum.GetValues(typeof(SurfaceId)))
                foreach (CasingKind k in Enum.GetValues(typeof(CasingKind)))
                {
                    var c = CasingRules.Sound(s, k);
                    Assert.That(c.Gain, Is.InRange(0f, 1.2f)); Assert.That(c.Pitch, Is.InRange(0.4f, 1.2f)); Assert.That(c.CutoffHz, Is.InRange(500f, 22000f));
                }
            Assert.GreaterOrEqual(Files(Shots, WeaponSounds.Casing), 1, "the case clip is in the repository");
        }

        // ---- #288 ----

        [Test]
        public void Every_combat_sound_has_its_clips_in_the_repository()
        {
            Assert.GreaterOrEqual(Files(Shots, WeaponSounds.DryFire), 1);
            Assert.GreaterOrEqual(Files(Shots, WeaponSounds.Ricochet), 3);
            foreach (var p in new[] { WeaponSounds.MeleeSwing, WeaponSounds.PainGrunt, WeaponSounds.HealStart, WeaponSounds.Pickup, WeaponSounds.SwimStroke })
                Assert.GreaterOrEqual(Files(Sfx, p), 1, p + " clip");
            Assert.GreaterOrEqual(Files(Sfx, WeaponSounds.SwimStroke), 3, "swim strokes vary");
            Assert.GreaterOrEqual(Files(Sfx, WeaponSounds.Pickup), 3, "pickups vary");
        }

        [Test]
        public void New_clips_are_in_the_ledger()
        {
            var ledger = File.ReadAllText("docs/ASSET_LEDGER.md");
            foreach (var id in new[] { "478345", "523230", "547209", "800275", "464996", "637958" })
                Assert.IsTrue(ledger.Contains("https://freesound.org/s/" + id + "/"), "ledger row for Freesound " + id);
        }

        [Test]
        public void Ricochets_off_hard_things_only()
        {
            Assert.AreEqual(0f, CasingRules.RicochetChance(BulletSurface.Body));
            Assert.AreEqual(0f, CasingRules.RicochetChance(BulletSurface.Glass));
            Assert.AreEqual(0f, CasingRules.RicochetChance(BulletSurface.Wood));
            Assert.Greater(CasingRules.RicochetChance(BulletSurface.Metal), CasingRules.RicochetChance(BulletSurface.Concrete));
            Assert.Greater(CasingRules.RicochetChance(BulletSurface.Concrete), CasingRules.RicochetChance(BulletSurface.Ground));
            Assert.Less(CasingRules.RicochetChance(BulletSurface.Metal), 1f);
        }

        // ---- #200 ----

        // An axis-aligned box as 8 vertices and 12 triangles, appended.
        static void Box(List<Vector3> v, List<int> t, Vector3 min, Vector3 max)
        {
            int o = v.Count;
            for (int i = 0; i < 8; i++)
                v.Add(new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z));
            int[] q = { 0, 1, 3, 2, 4, 5, 7, 6, 0, 1, 5, 4, 2, 3, 7, 6, 0, 2, 6, 4, 1, 3, 7, 5 };
            for (int f = 0; f < 6; f++)
            {
                t.Add(o + q[f * 4]); t.Add(o + q[f * 4 + 1]); t.Add(o + q[f * 4 + 2]);
                t.Add(o + q[f * 4]); t.Add(o + q[f * 4 + 2]); t.Add(o + q[f * 4 + 3]);
            }
        }

        // A rifle in the fitted frame, grip at the origin: receiver above, pistol grip below, stock behind, magazine ahead.
        static (Vector3[] v, int[] t, int magFirstTri) Rifle(bool bullpup = false, bool withMag = true)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            Box(v, t, new Vector3(-0.02f, 0.02f, -0.1f), new Vector3(0.02f, 0.08f, 0.35f));   // receiver and barrel
            Box(v, t, new Vector3(-0.015f, -0.09f, -0.03f), new Vector3(0.015f, 0.02f, 0.01f)); // pistol grip
            Box(v, t, new Vector3(-0.02f, -0.02f, -0.35f), new Vector3(0.02f, 0.07f, -0.1f));  // stock
            int mag = t.Count / 3;
            float z = bullpup ? -0.12f : 0.06f;
            if (withMag)
            {
                Box(v, t, new Vector3(-0.012f, -0.14f, z), new Vector3(0.012f, 0.02f, z + 0.06f));                 // magazine
                Box(v, t, new Vector3(-0.01f, -0.138f, z + 0.005f), new Vector3(0.01f, -0.13f, z + 0.055f));      // base plate inside it
            }
            return (v.ToArray(), t.ToArray(), mag);
        }

        [Test]
        public void Magazine_island_ahead_of_the_grip_is_found_with_the_pieces_inside_it()
        {
            var (v, t, mag) = Rifle();
            var isMag = MagazineSplit.Find(v, t);
            Assert.IsNotNull(isMag);
            for (int i = 0; i < isMag.Length; i++) Assert.AreEqual(i >= mag, isMag[i], "triangle " + i);
        }

        [Test]
        public void Bullpup_magazine_is_behind_the_grip()
        {
            var (v, t, mag) = Rifle(bullpup: true);
            Assert.IsNull(MagazineSplit.Find(v, t), "not looked for ahead of the grip");
            var isMag = MagazineSplit.Find(v, t, behindGrip: true);
            Assert.IsNotNull(isMag);
            Assert.AreEqual(t.Length / 3 - mag, isMag.Count(x => x));
        }

        [Test]
        public void No_magazine_no_split()
        {
            var (v, t, _) = Rifle(withMag: false);
            Assert.IsNull(MagazineSplit.Find(v, t), "the grip and stock are not taken for a magazine");
            Assert.IsNull(MagazineSplit.Find(new Vector3[0], new int[0]));
        }

        [Test]
        public void Magazine_leaves_the_well_and_is_seated_again_while_the_hand_is_there()
        {
            Assert.IsFalse(ReloadMotion.Reload(ReloadMotion.MagOutAt - 0.01f, ReloadKind.Magazine).MagInHand);
            Assert.IsTrue(ReloadMotion.Reload(ReloadMotion.MagOutAt + 0.01f, ReloadKind.Magazine).MagInHand);
            Assert.IsFalse(ReloadMotion.Reload(ReloadMotion.MagInAt + 0.01f, ReloadKind.Magazine).MagInHand);
            // At both moments the left hand is at the magazine well (the hand takes and seats the magazine there).
            var well = ReloadMotion.Reload(0.7f, ReloadKind.Magazine);
            foreach (var at in new[] { ReloadMotion.MagOutAt, ReloadMotion.MagInAt })
            {
                var p = ReloadMotion.Reload(at, ReloadKind.Magazine);
                Assert.AreEqual(well.LeftU, p.LeftU, 0.01f); Assert.AreEqual(well.LeftA, p.LeftA, 0.01f);
            }
        }

        [Test]
        public void Inspect_turns_the_gun_both_ways_and_ends_in_the_hold()
        {
            var s = ReloadMotion.Inspect(0f); var e = ReloadMotion.Inspect(1f);
            Assert.AreEqual(0f, s.RollDeg, 1e-4f); Assert.AreEqual(0f, e.RollDeg, 1e-4f); Assert.AreEqual(0f, e.Down, 1e-4f);
            Assert.AreEqual(0f, e.LeftBlend, 1e-4f);
            Assert.Less(ReloadMotion.Inspect(0.3f).RollDeg, -30f, "right side up first");
            Assert.Greater(ReloadMotion.Inspect(0.7f).RollDeg, 30f, "then the left");
            var prev = s;
            for (float t = 0.01f; t <= 1f; t += 0.01f)
            {
                var p = ReloadMotion.Inspect(t);
                Assert.Less(Math.Abs(p.RollDeg - prev.RollDeg), 10f, "no jump at t=" + t);
                prev = p;
            }
            Assert.Greater(ReloadMotion.InspectSeconds, 1f);
        }
    }
}
