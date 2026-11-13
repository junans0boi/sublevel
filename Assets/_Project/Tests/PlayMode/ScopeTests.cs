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
    // Optics (ADR-056 / ADR-058) in the real TestBox scene as a host: attaching by pickup, what a weapon refuses,
    // what happens to the optic when the weapon is dropped, and the view through it.
    // Needs CombatantOptics on the Player prefab (TestBoxBuilder). The view tests also need ScopeView created by
    // PlayerController and the camera reading ScopeView.FovOverride; they are ignored until that is wired.
    public class ScopeTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = false;
                return f;
            }
        }

        const int Rifle = 3;
        const int RedDot = 1, X4 = 5, X8 = 7;

        ScriptedInput _input;

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
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
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

        static CombatantOptics OpticsOf(Combatant combatant)
        {
            var optics = combatant.GetComponent<CombatantOptics>();
            Assert.IsNotNull(optics, combatant.name + " has no CombatantOptics: add it to the prefab in TestBoxBuilder");
            return optics;
        }

        static LootEntry Optic(int id) => Optics.ToLoot(id);

        IEnumerator GiveRifle()
        {
            Combatant.LocalPlayer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => Combatant.LocalPlayer.ActiveWeaponId == Rifle, 2f, "rifle in hand");
        }

        IEnumerator Tap()
        {
            _input.Frame.AimHeld = true;
            yield return null;
            yield return null;
            _input.Frame.AimHeld = false;
            yield return null;
        }

        static LootItem FindOpticLoot(int from, int opticId)
        {
            for (int i = from; i < LootItem.All.Count; i++)
            {
                var item = LootItem.All[i];
                if (item.Kind.Value == (int)LootKind.Optic && item.Optic.Value == opticId) return item;
            }
            return null;
        }

        [UnityTest]
        public IEnumerator FourTimesOptic_GivenWithARifle_Attaches()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = OpticsOf(me);
            yield return GiveRifle();
            Assert.AreEqual(0, optics.ActiveOptic);
            Assert.AreEqual(1f, optics.ActiveZoom, "no optic, no magnification");

            Assert.IsTrue(me.ServerGive(Optic(X4)));
            yield return WaitUntil(() => optics.ActiveZoom == 4f, 2f, "the 4x optic replicated");
            Assert.AreEqual(X4, optics.ActiveOptic);
            Assert.AreEqual(X4, optics.OpticInSlot(me.ActiveSlot.Value));
        }

        [UnityTest]
        public IEnumerator EightTimesOptic_IsRefusedByTheRifle_AndStaysOnTheGround()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = OpticsOf(me);
            yield return GiveRifle();

            Assert.IsFalse(me.ServerGive(Optic(X8)), "the rifle takes up to 6x");
            yield return null;
            Assert.AreEqual(0, optics.ActiveOptic);

            // A pickup that is refused is not despawned.
            int before = LootItem.All.Count;
            MatchDirector.Instance.ServerSpawnLoot(Optic(X8), me.transform.position + me.transform.forward * 1f + Vector3.up * 0.3f);
            yield return WaitUntil(() => FindOpticLoot(before, X8) != null, 2f, "the 8x pickup");
            var item = FindOpticLoot(before, X8);
            me.CmdPickup(item.NetworkObject);
            yield return null;
            Assert.IsTrue(item != null && item.IsSpawned, "the refused optic stays on the ground");
            Assert.AreEqual(0, optics.ActiveOptic);
        }

        [UnityTest]
        public IEnumerator SecondOptic_IsRefusedWhileTheSlotIsTaken()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = OpticsOf(me);
            yield return GiveRifle();
            Assert.IsTrue(me.ServerGive(Optic(X4)));
            Assert.IsFalse(me.ServerGive(Optic(RedDot)), "the only weapon already has an optic");
            yield return null;
            Assert.AreEqual(X4, optics.ActiveOptic);
        }

        [UnityTest]
        public IEnumerator PickingUpAnOpticFromTheGround_AttachesIt_AndRemovesThePickup()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = OpticsOf(me);
            yield return GiveRifle();

            int before = LootItem.All.Count;
            MatchDirector.Instance.ServerSpawnLoot(Optic(X4), me.transform.position + me.transform.forward * 1f + Vector3.up * 0.3f);
            yield return WaitUntil(() => FindOpticLoot(before, X4) != null, 2f, "the 4x pickup");
            var item = FindOpticLoot(before, X4);
            Assert.AreEqual("4배율", LootNames.Of(item));
            Assert.AreEqual("4배율", LootNames.Label(item));
            me.CmdPickup(item.NetworkObject);
            yield return WaitUntil(() => optics.ActiveOptic == X4, 2f, "the optic attached");
            yield return WaitUntil(() => item == null || !item.IsSpawned, 2f, "the pickup removed");
        }

        [UnityTest]
        public IEnumerator DroppingTheWeapon_TakesItsOpticToTheGround()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = OpticsOf(me);
            yield return GiveRifle();
            Assert.IsTrue(me.ServerGive(Optic(X4)));
            yield return WaitUntil(() => optics.ActiveOptic == X4, 2f, "the 4x optic attached");

            int before = LootItem.All.Count;
            int slot = me.ActiveSlot.Value;
            me.CmdDropWeapon(slot);
            yield return WaitUntil(() => me.ActiveWeaponId == 0, 2f, "the weapon dropped");
            // The optic does not stay behind on an empty slot: it lies on the ground as an optic pickup.
            yield return WaitUntil(() => FindOpticLoot(before, X4) != null, 2f, "the optic lying on the ground");
            Assert.AreEqual(0, optics.OpticInSlot(slot));
            Assert.AreEqual(0, optics.ActiveOptic);
        }

        [UnityTest]
        public IEnumerator DroppingTheOptic_PutsItOnTheGround_AndTheRifleKeepsShooting()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = OpticsOf(me);
            yield return GiveRifle();
            Assert.IsTrue(me.ServerGive(Optic(X4)));
            yield return WaitUntil(() => optics.ActiveOptic == X4, 2f, "the 4x optic attached");

            int before = LootItem.All.Count;
            optics.CmdDropOptic(me.ActiveSlot.Value);
            yield return WaitUntil(() => optics.ActiveOptic == 0, 2f, "the optic came off");
            yield return WaitUntil(() => FindOpticLoot(before, X4) != null, 2f, "the optic lying on the ground");
            Assert.AreEqual(Rifle, me.ActiveWeaponId);
        }

        [UnityTest]
        public IEnumerator TapAim_WithA4xOptic_NarrowsTheView_AndReleasingRestoresIt()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            yield return GiveRifle();
            Assert.IsTrue(me.ServerGive(Optic(X4)));
            float sights = GameData.Rules.Aim.Sights.Fov;
            float scoped = Optics.FovFor(sights, 4f);

            yield return Tap();
            yield return WaitUntil(() => me.AimMode == AimMode.Sights, 2f, "sights on");
            yield return WaitUntil(() => Mathf.Abs(pc.Camera.fieldOfView - scoped) < 0.5f, 3f, "field of view narrowed to the 4x value");
            Assert.IsTrue(ScopeView.Active);
            Assert.AreEqual(scoped, ScopeView.FovOverride, 0.01f);
            Assert.AreEqual(Optics.SensitivityFor(4f), ScopeView.SensitivityFactor, 0.02f);

            yield return Tap();
            yield return WaitUntil(() => me.AimMode == AimMode.Hip, 2f, "sights off");
            yield return WaitUntil(() => Mathf.Abs(pc.Camera.fieldOfView - GameData.Rules.Aim.Hip.Fov) < 1f, 3f, "field of view back");
            yield return WaitUntil(() => !ScopeView.Active, 2f, "the overlay gone");
            Assert.AreEqual(0f, ScopeView.FovOverride);
            Assert.AreEqual(1f, ScopeView.SensitivityFactor, 0.01f);
        }

        [UnityTest]
        public IEnumerator TapAim_WithARedDot_ShowsTheReticle_WithoutMagnification()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            yield return GiveRifle();
            Assert.IsTrue(me.ServerGive(Optic(RedDot)));

            yield return Tap();
            yield return WaitUntil(() => me.AimMode == AimMode.Sights, 2f, "sights on");
            yield return WaitUntil(() => ScopeView.Active, 2f, "the reticle shown");
            yield return WaitUntil(() => Mathf.Abs(pc.Camera.fieldOfView - GameData.Rules.Aim.Sights.Fov) < 1f, 3f, "sights field of view");
            Assert.AreEqual(0f, ScopeView.FovOverride, "a 1x sight does not zoom");
            Assert.AreEqual(1f, ScopeView.SensitivityFactor, 0.01f);
        }

        [UnityTest]
        public IEnumerator ShoulderAim_WithAScope_DoesNotMagnify()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            yield return GiveRifle();
            Assert.IsTrue(me.ServerGive(Optic(X4)));

            _input.Frame.AimHeld = true;
            yield return WaitUntil(() => me.AimMode == AimMode.Shoulder, 2f, "shouldered");
            yield return WaitUntil(() => Mathf.Abs(pc.Camera.fieldOfView - GameData.Rules.Aim.Shoulder.Fov) < 1f, 2f, "shoulder field of view");
            Assert.IsFalse(ScopeView.Active);
            Assert.AreEqual(0f, ScopeView.FovOverride);
        }
    }
}
