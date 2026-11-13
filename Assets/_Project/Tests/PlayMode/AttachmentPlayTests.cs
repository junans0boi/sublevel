using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // ADR-100 (#129): grips and the stock through the real pickup / bag / equip rules, as a host in TestBox.
    public class AttachmentPlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() { var f = Frame; Frame.FirePressed = Frame.Jump = false; return f; }
        }

        const int Rifle = 3, Pistol = 1;
        const int VerticalGrip = 12, AngledGrip = 13, Stock = 15;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator LoadTestBox()
        {
            PlayerController.InputOverride = new ScriptedInput();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static LootItem FindLoot(int from, int id)
        {
            for (int i = from; i < LootItem.All.Count; i++)
                if (LootItem.All[i].Kind.Value == (int)LootKind.Optic && LootItem.All[i].Optic.Value == id) return LootItem.All[i];
            return null;
        }

        [UnityTest]
        public IEnumerator Grip_FitsTheRifle_ChangesItsMods_AndAGripOnlyReplacesAGrip()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var gear = me.GetComponent<CombatantOptics>();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            int slot = me.ActiveSlot.Value;
            Assert.AreEqual(1f, gear.ActiveMods.Pitch);

            Assert.IsTrue(me.ServerGive(Optics.ToLoot(VerticalGrip)));
            Assert.IsTrue(me.ServerGive(Optics.ToLoot(Stock)));
            Assert.IsTrue(me.ServerGive(Optics.ToLoot(5)), "an optic still goes on its own place");
            yield return WaitUntil(() => gear.AttachmentInSlot(slot, AttachSlot.Grip) == VerticalGrip && gear.AttachmentInSlot(slot, AttachSlot.Stock) == Stock && gear.ActiveOptic == 5, 2f, "grip, stock and optic fitted");
            Assert.Less(gear.ActiveMods.Pitch, 0.8f, "grip and stock stack on the pitch");
            Assert.AreEqual(0, gear.InBag(VerticalGrip));

            // A second grip goes to the bag while the place is taken; the optic is untouched.
            Assert.IsTrue(me.ServerGive(Optics.ToLoot(AngledGrip)));
            yield return WaitUntil(() => gear.InBag(AngledGrip) == 1, 2f, "angled grip in the bag");
            Assert.AreEqual(VerticalGrip, gear.AttachmentInSlot(slot, AttachSlot.Grip));
            Assert.AreEqual(5, gear.ActiveOptic);

            // From the bag onto the rifle: the vertical grip goes to the bag in its place.
            Assert.IsTrue(gear.ServerAttachFromBag(AngledGrip, slot));
            yield return WaitUntil(() => gear.AttachmentInSlot(slot, AttachSlot.Grip) == AngledGrip && gear.InBag(VerticalGrip) == 1 && gear.InBag(AngledGrip) == 0, 2f, "grips swapped");

            // Detach by gear index into the bag; the stock stays.
            gear.ServerDetachToBag(Attachments.GearIndex(slot, AttachSlot.Grip));
            yield return WaitUntil(() => gear.AttachmentInSlot(slot, AttachSlot.Grip) == 0 && gear.InBag(AngledGrip) == 1, 2f, "grip detached into the bag");
            Assert.AreEqual(Stock, gear.AttachmentInSlot(slot, AttachSlot.Stock));

            // Dropping from the bag puts it on the ground.
            int before = LootItem.All.Count;
            Assert.IsTrue(gear.ServerDropFromBag(AngledGrip));
            yield return WaitUntil(() => FindLoot(before, AngledGrip) != null, 2f, "angled grip on the ground");
        }

        [UnityTest]
        public IEnumerator Suppressor_ShrinksTheBotHearingRadius_AndSetsTheAudioHook()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var gear = me.GetComponent<CombatantOptics>();
            var audio = me.GetComponent<CombatantAudio>();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            float loud = me.ShotHearingRadius(BotRules.Current.GunshotHearingRadius);
            Assert.AreEqual(BotRules.Current.GunshotHearingRadius, loud, 1e-4f);

            Assert.IsTrue(me.ServerGive(Optics.ToLoot(11)));
            yield return WaitUntil(() => gear.ActiveMods.Suppressed, 2f, "suppressor fitted");
            Assert.AreEqual(loud / SoundRules.SuppressedDistanceFactor, me.ShotHearingRadius(BotRules.Current.GunshotHearingRadius), 1e-4f, "bots hear it from the shrunk radius");
            if (audio != null) yield return WaitUntil(() => audio.Suppressed, 2f, "the audio hook is set");

            gear.ServerDetachToBag(Attachments.GearIndex(me.ActiveSlot.Value, AttachSlot.Muzzle));
            yield return WaitUntil(() => !gear.ActiveMods.Suppressed, 2f, "suppressor off");
            Assert.AreEqual(loud, me.ShotHearingRadius(BotRules.Current.GunshotHearingRadius), 1e-4f);
            if (audio != null) yield return WaitUntil(() => !audio.Suppressed, 2f, "the audio hook is cleared");
        }

        [UnityTest]
        public IEnumerator ExtendedMagazine_ReloadsToForty_SlowerThanThirty_AndGivesTheRoundsBackWhenRemoved()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var gear = me.GetComponent<CombatantOptics>();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            int slot = me.ActiveSlot.Value;
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 100 });
            int reserve = me.ReserveFor(AmmoType.Rifle556);
            Assert.IsTrue(me.ServerGive(Optics.ToLoot(16)));
            yield return WaitUntil(() => gear.AttachmentInSlot(slot, AttachSlot.Mag) == 16, 2f, "magazine fitted");
            yield return new WaitForSeconds(0.6f); // the draw delay
            float t0 = Time.realtimeSinceStartup;
            me.CmdReload();
            yield return WaitUntil(() => me.Reloading.Value, 2f, "reload started");
            yield return WaitUntil(() => !me.Reloading.Value, 6f, "reload done");
            float took = Time.realtimeSinceStartup - t0;
            Assert.AreEqual(40, me.MagInSlot(slot));
            Assert.GreaterOrEqual(took, 2.4f * 1.1f - 0.1f, "10% longer than the 2.4 s reload of a plain magazine");

            gear.ServerDetachToBag(Attachments.GearIndex(slot, AttachSlot.Mag));
            yield return WaitUntil(() => me.MagInSlot(slot) == 30, 2f, "back to 30 rounds");
            yield return WaitUntil(() => me.ReserveFor(AmmoType.Rifle556) == reserve, 2f, "the extra rounds went back to the reserve");
        }

        [UnityTest]
        public IEnumerator GripGoesInTheBag_WhenTheWeaponTakesNone_AndLeavesWithADroppedWeapon()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var gear = me.GetComponent<CombatantOptics>();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Pistol, Amount = 12 });
            yield return WaitUntil(() => me.ActiveWeaponId == Pistol, 2f, "pistol in hand");
            Assert.IsTrue(me.ServerGive(Optics.ToLoot(VerticalGrip)), "no weapon takes it: into the bag");
            yield return WaitUntil(() => gear.InBag(VerticalGrip) == 1, 2f, "grip in the bag");
            Assert.AreEqual(0, gear.AttachmentInSlot(me.ActiveSlot.Value, AttachSlot.Grip));
            Assert.IsFalse(gear.ServerAttachFromBag(VerticalGrip, me.ActiveSlot.Value), "a pistol refuses the grip");

            // A rifle picked up and fitted, then dropped: the grip goes to the bag (ADR-082 (4) for every attachment).
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            int slot = -1;
            yield return WaitUntil(() => { for (int i = 0; i < Loadout.SlotCount; i++) if (me.WeaponInSlot(i) == Rifle) slot = i; return slot >= 0; }, 2f, "rifle picked up");
            Assert.IsTrue(gear.ServerAttachFromBag(VerticalGrip, slot));
            yield return WaitUntil(() => gear.AttachmentInSlot(slot, AttachSlot.Grip) == VerticalGrip, 2f, "fitted");
            me.CmdDropWeapon(slot);
            yield return WaitUntil(() => me.WeaponInSlot(slot) != Rifle, 2f, "rifle dropped");
            yield return WaitUntil(() => gear.InBag(VerticalGrip) == 1 && gear.AttachmentInSlot(slot, AttachSlot.Grip) == 0, 2f, "grip back in the bag");
        }
    }
}
