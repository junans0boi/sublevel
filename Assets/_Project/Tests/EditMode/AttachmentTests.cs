using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-100 (#129): the attachment slot table per gun, grips and the stock changing the recoil pattern, and the
    // shared id space (bag, loot, gear packing).
    public class AttachmentTests
    {
        const string DataFolder = "Assets/_Project/Data/Resources/GameData/";
        const int Pistol = 1, Smg = 2, Rifle = 3, Shotgun = 4, Sniper = 5;
        const int Compensator = 9, FlashHider = 10, Suppressor = 11, VerticalGrip = 12, AngledGrip = 13, CurvedGrip = 14, Stock = 15, ExtMag = 16;

        static readonly OpticRules Rules = new OpticRules();
        static OpticRules Json() => JsonUtility.FromJson<OpticRules>(File.ReadAllText(DataFolder + "optics.json"));
        static WeaponTable Weapons() => JsonUtility.FromJson<WeaponTable>(File.ReadAllText(DataFolder + "weapons.json"));
        static GameRules GameRulesJson() => JsonUtility.FromJson<GameRules>(File.ReadAllText(DataFolder + "rules.json"));

        [Test]
        public void SlotTable_GunsTakeOnlyTheirOwnAttachments()
        {
            foreach (var rules in new[] { Rules, Json() })
            {
                Assert.IsTrue(Optics.CanAttach(Rifle, VerticalGrip, rules));
                Assert.IsTrue(Optics.CanAttach(Smg, AngledGrip, rules));
                Assert.IsTrue(Optics.CanAttach(Rifle, Stock, rules));
                Assert.IsTrue(Optics.CanAttach(Sniper, Stock, rules));
                Assert.IsFalse(Optics.CanAttach(Sniper, VerticalGrip, rules), "a sniper rifle takes no grip");
                Assert.IsFalse(Optics.CanAttach(Shotgun, CurvedGrip, rules), "a shotgun takes no grip");
                Assert.IsFalse(Optics.CanAttach(Pistol, Stock, rules), "a pistol takes no stock");
                Assert.IsFalse(Optics.CanAttach(0, VerticalGrip, rules), "no weapon, no fit");
                Assert.IsFalse(Optics.CanAttach(Rifle, 77, rules), "unknown id");
                Assert.IsFalse(Optics.CanAttach(21, Stock, rules), "melee weapons take nothing");
                // The optic table is untouched by the new one.
                Assert.IsTrue(Optics.CanAttach(Rifle, 5, rules));
            }
        }

        [Test]
        public void Table_HasUniqueIds_InTheirOwnSpace_WithKoreanNames()
        {
            var ids = Rules.AttachmentDefs.Select(a => a.Id).ToArray();
            Assert.AreEqual(ids.Length, ids.Distinct().Count());
            foreach (var a in Rules.AttachmentDefs)
            {
                Assert.That(a.Id, Is.InRange(9, OpticBag.MaxId), a.Name + ": attachments use ids 9..16");
                Assert.IsNull(Rules.Find(a.Id), a.Name + " is not an optic");
                Assert.IsNotEmpty(a.Name);
                Assert.AreNotEqual(AttachSlot.Optic, a.Slot);
            }
            foreach (var w in Rules.AttachmentSlots)
                foreach (int id in w.Ids) Assert.IsNotNull(Rules.FindAttachment(id), "weapon " + w.WeaponId + " names attachment " + id);
        }

        [Test]
        public void Data_MatchesTheDefaultsInCode()
        {
            var json = Json();
            Assert.AreEqual(Rules.AttachmentDefs.Length, json.AttachmentDefs.Length);
            for (int i = 0; i < Rules.AttachmentDefs.Length; i++)
            {
                var a = Rules.AttachmentDefs[i];
                var b = json.AttachmentDefs[i];
                Assert.AreEqual(a.Id, b.Id);
                Assert.AreEqual(a.Slot, b.Slot);
                Assert.AreEqual(a.RecoilPitch, b.RecoilPitch, 1e-5f, a.Name);
                Assert.AreEqual(a.RecoilYaw, b.RecoilYaw, 1e-5f, a.Name);
                Assert.AreEqual(a.RecoverFactor, b.RecoverFactor, 1e-5f, a.Name);
                Assert.AreEqual(a.Suppressed, b.Suppressed, a.Name);
                Assert.AreEqual(a.MagFactor, b.MagFactor, 1e-5f, a.Name);
                Assert.AreEqual(a.ReloadFactor, b.ReloadFactor, 1e-5f, a.Name);
            }
            foreach (var w in Rules.AttachmentSlots)
                CollectionAssert.AreEqual(w.Ids, json.AttachmentsFor(w.WeaponId), "weapon " + w.WeaponId);
        }

        [Test]
        public void EveryAttachment_IsLoot_WithAPositiveWeight()
        {
            var loot = JsonUtility.FromJson<LootTable>(File.ReadAllText(DataFolder + "loot.json"));
            foreach (var a in Json().AttachmentDefs)
            {
                var rows = loot.Entries.Where(e => e.Kind == LootKind.Optic && e.Optic == a.Id).ToArray();
                Assert.AreEqual(1, rows.Length, a.Name + " has one loot row");
                Assert.Greater(rows[0].Weight, 0f);
            }
        }

        [Test]
        public void GearPacking_KeepsEachPlaceApart()
        {
            int packed = 0;
            packed = Attachments.With(packed, AttachSlot.Optic, 8);
            packed = Attachments.With(packed, AttachSlot.Grip, VerticalGrip);
            packed = Attachments.With(packed, AttachSlot.Stock, Stock);
            Assert.AreEqual(8, Attachments.Get(packed, AttachSlot.Optic));
            Assert.AreEqual(VerticalGrip, Attachments.Get(packed, AttachSlot.Grip));
            Assert.AreEqual(Stock, Attachments.Get(packed, AttachSlot.Stock));
            Assert.AreEqual(0, Attachments.Get(packed, AttachSlot.Muzzle));
            packed = Attachments.With(packed, AttachSlot.Grip, 0);
            Assert.AreEqual(0, Attachments.Get(packed, AttachSlot.Grip));
            Assert.AreEqual(Stock, Attachments.Get(packed, AttachSlot.Stock), "clearing one place leaves the others");
        }

        [Test]
        public void GearIndex_PlainSlotIsTheOptic()
        {
            Assert.AreEqual(3, Attachments.GearIndex(3, AttachSlot.Optic));
            int idx = Attachments.GearIndex(2, AttachSlot.Grip);
            Assert.AreEqual(2, Attachments.WeaponSlotOf(idx));
            Assert.AreEqual(AttachSlot.Grip, Attachments.SlotOfIndex(idx));
            Assert.AreEqual(AttachSlot.Optic, Attachments.SlotOfIndex(4));
        }

        [Test]
        public void SlotOf_NamesTheKindOfPlace()
        {
            Assert.AreEqual(AttachSlot.Optic, Attachments.SlotOf(5, Rules));
            Assert.AreEqual(AttachSlot.Grip, Attachments.SlotOf(AngledGrip, Rules));
            Assert.AreEqual(AttachSlot.Stock, Attachments.SlotOf(Stock, Rules));
            Assert.IsNull(Attachments.SlotOf(77, Rules));
        }

        // ---- recoil: same seed, with and without ----

        static (float[] pitch, float[] yaw) Spray(WeaponDef w, AttachMods mods, RecoilRules rules, float factor, int seed = 42, int shots = 30)
        {
            var recoil = new ViewRecoil();
            var rng = new System.Random(seed);
            var pitch = new float[shots];
            var yaw = new float[shots];
            for (int i = 0; i < shots; i++)
            {
                RecoilPattern.Kick(recoil, w, rng, factor, rules, mods);
                pitch[i] = recoil.Pitch;
                yaw[i] = recoil.Yaw;
                recoil.Tick(w.FireInterval, rules.RecoverRate(w) * (mods ?? AttachMods.None).Recover, rules.RecoverDelaySeconds);
            }
            return (pitch, yaw);
        }

        static AttachMods Fit(params int[] ids)
        {
            int packed = 0;
            foreach (int id in ids) packed = Attachments.With(packed, Rules.FindAttachment(id).Slot, id);
            return Attachments.ModsOf(packed, Rules);
        }

        [Test]
        public void VerticalGrip_SameSeedThirtyShots_ClimbsLess_AndTheYawFollowsTheSameSeed()
        {
            var rules = GameRulesJson();
            var rifle = Weapons().Find(Rifle);
            float factor = rules.Aim.Sights.RecoilFactor;
            var bare = Spray(rifle, null, rules.Recoil, factor);
            var again = Spray(rifle, null, rules.Recoil, factor);
            var grip = Spray(rifle, Fit(VerticalGrip), rules.Recoil, factor);
            CollectionAssert.AreEqual(bare.pitch, again.pitch, "same seed, same climb");
            for (int i = 0; i < 12; i++) Assert.Less(grip.pitch[i], bare.pitch[i], "shot " + i + " climbs less with the grip");
            Assert.AreEqual(bare.pitch[0] * 0.85f, grip.pitch[0], 1e-4f, "the first kick is exactly 15% smaller");
            CollectionAssert.AreNotEqual(bare.pitch, grip.pitch);
            // The yaw random draws are the same, only scaled by the grip (vertical grip leaves it alone).
            CollectionAssert.AreEqual(bare.yaw, grip.yaw, new FloatCompare());
        }

        [Test]
        public void AngledGrip_NarrowsTheSideDrift_AndStockHelpsBothWays()
        {
            var rules = GameRulesJson();
            var rifle = Weapons().Find(Rifle);
            float factor = rules.Aim.Sights.RecoilFactor;
            var bare = Spray(rifle, null, rules.Recoil, factor);
            var angled = Spray(rifle, Fit(AngledGrip), rules.Recoil, factor);
            Assert.Less(Mathf.Abs(angled.yaw.Last()), Mathf.Abs(bare.yaw.Last()) + 1e-5f);
            Assert.Less(angled.yaw.Max(v => Mathf.Abs(v)), bare.yaw.Max(v => Mathf.Abs(v)), "the side drift peaks lower");
            var stocked = Spray(rifle, Fit(Stock), rules.Recoil, factor);
            Assert.Less(stocked.pitch[10], bare.pitch[10]);
            Assert.Less(stocked.yaw.Max(v => Mathf.Abs(v)), bare.yaw.Max(v => Mathf.Abs(v)));
            var both = Spray(rifle, Fit(VerticalGrip, Stock), rules.Recoil, factor);
            Assert.Less(both.pitch[10], Spray(rifle, Fit(VerticalGrip), rules.Recoil, factor).pitch[10], "they stack");
        }

        [Test]
        public void CurvedGrip_OnlySpeedsTheRecovery()
        {
            var mods = Fit(CurvedGrip);
            Assert.AreEqual(1f, mods.Pitch);
            Assert.AreEqual(1f, mods.Yaw);
            Assert.AreEqual(1.35f, mods.Recover, 1e-5f);
            // After the same spray the view comes back sooner.
            var rules = GameRulesJson().Recoil;
            var rifle = Weapons().Find(Rifle);
            float Settled(AttachMods m)
            {
                var recoil = new ViewRecoil();
                var rng = new System.Random(7);
                for (int i = 0; i < 10; i++) RecoilPattern.Kick(recoil, rifle, rng, 1f, rules, m);
                for (int i = 0; i < 20; i++) recoil.Tick(0.05f, rules.RecoverRate(rifle) * m.Recover, rules.RecoverDelaySeconds);
                return recoil.Pitch;
            }
            Assert.Less(Settled(mods), Settled(AttachMods.None));
        }

        [Test]
        public void NothingFitted_ChangesNothing()
        {
            Assert.AreSame(AttachMods.None, Attachments.ModsOf(0, Rules));
            Assert.AreSame(AttachMods.None, Attachments.ModsOf(Attachments.With(0, AttachSlot.Optic, 5), Rules), "an optic has no recoil effect");
            var rules = GameRulesJson();
            var w = Weapons().Find(Smg);
            var a = Spray(w, null, rules.Recoil, 1f);
            var b = Spray(w, AttachMods.None, rules.Recoil, 1f);
            CollectionAssert.AreEqual(a.pitch, b.pitch);
        }

        // ---- G7b: muzzle devices and the magazine ----

        [Test]
        public void MuzzleDevices_FitOnlyTheRightGuns_OnePerPlace()
        {
            foreach (var rules in new[] { Rules, Json() })
            {
                foreach (int id in new[] { Compensator, FlashHider, Suppressor })
                {
                    Assert.AreEqual(AttachSlot.Muzzle, Rules.FindAttachment(id).Slot);
                    Assert.IsTrue(Optics.CanAttach(Rifle, id, rules));
                    Assert.IsTrue(Optics.CanAttach(Pistol, id, rules), "a pistol takes a muzzle device");
                }
                Assert.IsFalse(Optics.CanAttach(Shotgun, FlashHider, rules));
                Assert.IsTrue(Optics.CanAttach(Shotgun, Suppressor, rules));
                Assert.IsTrue(Optics.CanAttach(Pistol, ExtMag, rules));
                Assert.IsFalse(Optics.CanAttach(Shotgun, ExtMag, rules), "a tube-fed shotgun takes no magazine");
            }
        }

        [Test]
        public void Suppressor_IsTheOnlyQuietOne_AndOnTopOfRecoilItStillStacks()
        {
            Assert.IsTrue(Fit(Suppressor).Suppressed);
            Assert.IsFalse(Fit(Compensator).Suppressed);
            Assert.IsFalse(Fit(VerticalGrip, Stock).Suppressed);
            Assert.AreEqual(0.88f * 0.85f * 0.92f, Fit(Compensator, VerticalGrip, Stock).Pitch, 1e-5f);
            Assert.AreEqual(1f, Fit(ExtMag).Pitch, "a magazine does not touch recoil");
        }

        [Test]
        public void SuppressedShot_IsHeardByBotsAndEarsFromTheSameDistance()
        {
            float radius = BotRules.Current.GunshotHearingRadius;
            float quiet = SoundRules.ShotRadius(radius, true);
            Assert.AreEqual(radius / SoundRules.SuppressedDistanceFactor, quiet, 1e-4f);
            Assert.AreEqual(radius, SoundRules.ShotRadius(radius, false));
            // A listener d metres away hears the suppressed shot as one from d x factor metres: inside the bot radius
            // exactly when d is inside the shrunk radius.
            foreach (float d in new[] { 5f, quiet - 0.01f, quiet + 0.01f, 60f })
                Assert.AreEqual(d <= quiet, SoundRules.HeardDistance(d, true) <= radius, "distance " + d);
        }

        static Loadout RifleLoadout(out int slot, int mag = 30)
        {
            var l = new Loadout(Weapons());
            l.AddWeapon(Rifle, mag);
            slot = l.WeaponInSlot(0) == Rifle ? 0 : 1;
            l.AddAmmo(AmmoType.Rifle556, 200);
            return l;
        }

        [Test]
        public void ExtendedMagazine_RaisesTheCapacity_AndTheReloadTakesLongerByTheRatio()
        {
            var weapon = Weapons().Find(Rifle);
            var mods = Fit(ExtMag);
            Assert.AreEqual(40, Mathf.RoundToInt(weapon.MagSize * mods.MagFactor), "30 -> 40");

            var plain = RifleLoadout(out int s0, 10);
            Assert.AreEqual(30, plain.MagCapacity(s0));
            Assert.IsTrue(plain.TryStartReload(0));
            Assert.IsTrue(plain.Tick(weapon.ReloadTime + 0.01));
            Assert.AreEqual(30, plain.MagInSlot(s0));

            var big = RifleLoadout(out int s1, 10);
            Assert.IsTrue(big.SetSlotMods(s1, mods.MagFactor, mods.ReloadFactor));
            Assert.AreEqual(40, big.MagCapacity(s1));
            Assert.IsTrue(big.TryStartReload(0));
            Assert.IsFalse(big.Tick(weapon.ReloadTime + 0.01), "still reloading when the plain one is done");
            Assert.IsFalse(big.Tick(weapon.ReloadTime * 1.1 - 0.01));
            Assert.IsTrue(big.Tick(weapon.ReloadTime * mods.ReloadFactor + 0.01));
            Assert.AreEqual(40, big.MagInSlot(s1), "the bigger magazine fills");
            Assert.AreEqual(200 - 30, big.Reserve(AmmoType.Rifle556));
            Assert.IsFalse(big.TryStartReload(10), "a full extended magazine does not reload");
        }

        [Test]
        public void TakingTheMagazineOff_ReturnsTheExtraRoundsToTheReserve()
        {
            var mods = Fit(ExtMag);
            var l = RifleLoadout(out int slot, 40);
            Assert.AreEqual(40, l.MagInSlot(slot) , "placed with 40 before the magazine is told");
            l.SetSlotMods(slot, mods.MagFactor, mods.ReloadFactor);
            Assert.AreEqual(40, l.MagInSlot(slot));
            Assert.IsTrue(l.SetSlotMods(slot, 1f, 1f));
            Assert.AreEqual(30, l.MagInSlot(slot));
            Assert.AreEqual(210, l.Reserve(AmmoType.Rifle556));
            Assert.IsFalse(l.SetSlotMods(slot, 1f, 1f), "nothing changed");
        }

        [Test]
        public void ShellLoadedShotgun_IgnoresAMagazineFactor()
        {
            var l = new Loadout(Weapons());
            l.AddWeapon(Shotgun, 0);
            int slot = l.WeaponInSlot(0) == Shotgun ? 0 : 1;
            var w = Weapons().Find(Shotgun);
            l.SetSlotMods(slot, 1.33f, 1.1f);
            Assert.AreEqual(w.MagSize, l.MagCapacity(slot));
        }

        sealed class FloatCompare : System.Collections.IComparer
        {
            public int Compare(object x, object y) => Mathf.Abs((float)x - (float)y) < 1e-4f ? 0 : ((float)x).CompareTo((float)y);
        }
    }
}
