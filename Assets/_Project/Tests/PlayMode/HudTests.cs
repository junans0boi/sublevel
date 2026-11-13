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
    // The game HUD in the real TestBox scene: it shows what the server decided and reacts to combat feedback.
    public class HudTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read() => Frame;
        }

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        static IEnumerator LoadTestBox()
        {
            PlayerController.InputOverride = new ScriptedInput();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            yield return null; // one LateUpdate of the HUD with the player present
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UiFocus.Close();
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Hud_AppearsWithTheLocalPlayer_UnarmedAtFullHealth()
        {
            Assert.DoesNotThrow(() => Hud.HitMarker(true), "feedback calls are safe without a HUD");
            Assert.DoesNotThrow(() => Hud.DamageFrom(Vector3.zero));

            yield return LoadTestBox();
            var hud = Hud.Instance;
            Assert.IsNotNull(hud, "the scene has a HUD");
            Assert.IsTrue(hud.Visible);
            Assert.AreEqual(1f, hud.HealthFill, 0.001f);
            Assert.AreEqual("—", hud.AmmoText, "no weapon in hand");
            Assert.IsTrue(hud.CrosshairVisible);
            Assert.IsFalse(hud.HitMarkerVisible);
            Assert.IsFalse(hud.LowHealthShown);
            Assert.IsFalse(hud.DeathShown);
            Assert.Greater(hud.AliveShown, 0, "at least the player is alive");
        }

        [UnityTest]
        public IEnumerator Damage_LowersTheHealthBar_AndLowHealthShowsTheWarning()
        {
            yield return LoadTestBox();
            var hud = Hud.Instance;
            var me = Combatant.LocalPlayer;

            me.ServerTakeDamage(40);
            yield return WaitUntil(() => hud.HealthFill < 0.99f, 2f, "health bar reacted");
            Assert.AreEqual(0.6f, hud.HealthFill, 0.01f);
            Assert.IsFalse(hud.LowHealthShown, "60 HP is not low");

            me.ServerTakeDamage(40);
            yield return WaitUntil(() => hud.HealthFill < 0.5f, 2f, "health bar reacted again");
            Assert.AreEqual(0.2f, hud.HealthFill, 0.01f);
            Assert.IsTrue(hud.LowHealthShown, "below 30 HP the screen edges warn");

            me.ServerTakeDamage(40);
            // The death cam (ADR-094), when there is one, takes the death band's place.
            yield return WaitUntil(() => hud.DeathShown || (ResultScreen.Instance != null && ResultScreen.Instance.Current != ResultScreen.Phase.None), 2f, "death message or death cam");
            Assert.AreEqual(0f, hud.HealthFill, 0.001f);
            Assert.IsFalse(hud.CrosshairVisible, "no crosshair for the dead");
        }

        [UnityTest]
        public IEnumerator PickingUpARifle_ShowsItsNameAndAmmo_InTheActiveSlot()
        {
            yield return LoadTestBox();
            var hud = Hud.Instance;
            var me = Combatant.LocalPlayer;

            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == 3, 2f, "rifle in hand");
            yield return WaitUntil(() => hud.AmmoText != "—", 2f, "ammo readout");
            Assert.AreEqual("30 / 0", hud.AmmoText);
            Assert.AreEqual("5.56mm", hud.AmmoTypeText);
            Assert.AreEqual("weapon_3", hud.ActiveWeaponIconKey);
            Assert.AreEqual(0, hud.ActiveSlotShown);
            Assert.AreEqual("Warden", hud.ActiveWeaponName);
            Assert.AreEqual("연사", hud.FireModeText);

            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = InventoryBackend.AmmoValue(1), Amount = 60 });
            yield return WaitUntil(() => hud.AmmoText != "30 / 0", 2f, "reserve readout");
            Assert.AreEqual("30 / 60", hud.AmmoText);
        }

        [UnityTest]
        public IEnumerator HitMarker_ShowsBriefly()
        {
            yield return LoadTestBox();
            var hud = Hud.Instance;

            Hud.HitMarker(false);
            Assert.IsTrue(hud.HitMarkerVisible);
            yield return WaitUntil(() => !hud.HitMarkerVisible, 0.5f, "hit marker gone");

            Hud.HitMarker(true);
            Assert.IsTrue(hud.HitMarkerVisible, "a kill shows it too");
            yield return WaitUntil(() => !hud.HitMarkerVisible, 0.5f, "kill marker gone");
        }

        [UnityTest]
        public IEnumerator DamageDirection_ShowsThenFades()
        {
            yield return LoadTestBox();
            var hud = Hud.Instance;
            Assert.AreEqual(0, hud.DamageIndicatorsShown);

            Hud.DamageFrom(PlayerController.Local.transform.position + Vector3.right * 10f);
            Assert.AreEqual(1, hud.DamageIndicatorsShown);
            yield return WaitUntil(() => hud.DamageIndicatorsShown == 0, 2f, "indicator faded");
        }

        [UnityTest]
        public IEnumerator Crosshair_HidesWhileAScreenIsOpen()
        {
            yield return LoadTestBox();
            var hud = Hud.Instance;
            Assert.IsTrue(hud.CrosshairVisible);

            UiFocus.Set(UiScreen.Menu);
            yield return null;
            yield return null;
            Assert.IsFalse(hud.CrosshairVisible, "hidden behind the menu");

            UiFocus.Close();
            yield return null;
            yield return null;
            Assert.IsTrue(hud.CrosshairVisible, "back when the menu closes");
        }
    }
}
