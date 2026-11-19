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
    // r5 weapon effects in the real TestBox scene (#199 #200 #287 #288): spent cases, the gunshot takes and the indoor
    // tail, the dry click, grunt and pickup sounds, the magazine taken off the gun and the inspect motion. Batchmode has
    // no audio output: this proves the sounds are triggered; a human must still listen.
    public class WeaponFxPlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Reload = false;
                Frame.SelectSlot = -1;
                return f;
            }
        }

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

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            for (int n = 0; SpawnProtection.InputBlocked && n < 60; n++) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        IEnumerator Hold(int weaponId, int rounds, int reserve = 0)
        {
            var me = Combatant.LocalPlayer;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = weaponId, Amount = rounds });
            if (reserve > 0) me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = GameData.Weapons.Find(weaponId).Ammo, Amount = reserve });
            for (int s = 0; s < 5; s++) if (me.WeaponInSlot(s) == weaponId) me.ServerSelectSlot(s);
            yield return WaitUntil(() => me.ActiveWeaponId == weaponId, 3f, "weapon " + weaponId + " in hand");
            yield return new WaitForSeconds(0.8f); // drawn
        }

        IEnumerator Fire(CombatantAudio audio)
        {
            int shots = audio.ShotsPlayed;
            _input.Frame.FirePressed = _input.Frame.FireHeld = true; // held for automatic weapons
            yield return WaitUntil(() => audio.ShotsPlayed > shots, 2f, "shot sound");
            _input.Frame.FireHeld = false;
            yield return new WaitForSeconds(0.35f);
        }

        [UnityTest]
        public IEnumerator Shots_throw_cases_vary_their_takes_and_ring_indoors()
        {
            var me = Combatant.LocalPlayer;
            var audio = me.GetComponent<CombatantAudio>();
            var fx = me.GetComponent<CasingFx>();
            Assert.IsNotNull(fx, "CombatantView adds the case ejector");
            yield return Hold(12, 10); // the Lancer: semi-automatic, one case per shot
            int ejected = fx.Ejected, tails = audio.TailsPlayed;
            int lastTake = audio.LastNearTake;
            for (int i = 0; i < 3; i++)
            {
                yield return Fire(audio);
                Assert.AreNotEqual(lastTake, audio.LastNearTake, "a different take every shot");
                lastTake = audio.LastNearTake;
            }
            Assert.AreEqual(ejected + 3, fx.Ejected, "a case per shot");
            Assert.Greater(CasingPool.Live, 0, "cases in flight or on the ground");
            Assert.LessOrEqual(CasingPool.Live, CasingPool.Capacity);

            // A roof over the shooter: the third layer joins.
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.position = me.transform.position + Vector3.up * 4f;
            roof.transform.localScale = new Vector3(12f, 0.3f, 12f);
            yield return new WaitForSeconds(0.6f); // past the roof check's cache
            int tailsBefore = audio.TailsPlayed;
            yield return Fire(audio);
            Assert.AreEqual(tailsBefore + 1, audio.TailsPlayed, "indoor tail under a roof");
            Assert.AreEqual(tails, tailsBefore, "no tail in the open");
            UnityEngine.Object.Destroy(roof);

            // The cases are gone after their life.
            yield return new WaitForSeconds(CasingRules.LifeSeconds + 0.3f);
            Assert.AreEqual(0, CasingPool.Live);
        }

        [UnityTest]
        public IEnumerator Empty_trigger_clicks_wounds_grunt_and_reaching_for_loot_rummages()
        {
            var me = Combatant.LocalPlayer;
            var audio = me.GetComponent<CombatantAudio>();
            yield return Hold(1, 1);
            yield return Fire(audio);
            Assert.AreEqual(0, me.ActiveMag);
            int dry = audio.DryFiresPlayed;
            _input.Frame.FirePressed = true;
            yield return WaitUntil(() => audio.DryFiresPlayed > dry, 2f, "dry click on an empty magazine");

            int grunts = audio.GruntsPlayed;
            me.ServerTakeDamage(new DamageInfo { Amount = 10f, Zone = HitZone.Body });
            yield return WaitUntil(() => audio.GruntsPlayed > grunts, 2f, "pain grunt");

            int pickups = audio.PickupsPlayed;
            me.GetComponent<CombatantAnimation>().StartGesture(HandGesture.Pickup);
            Assert.AreEqual(pickups + 1, audio.PickupsPlayed, "rummage on the pickup reach");
        }

        [UnityTest]
        public IEnumerator Reload_takes_the_magazine_off_the_gun_and_inspect_turns_it()
        {
            var me = Combatant.LocalPlayer;
            var audio = me.GetComponent<CombatantAudio>();
            var anim = me.GetComponent<CombatantAnimation>();
            var view = me.GetComponent<WeaponVisual>();
            // Every magazine-fed long gun has a magazine piece (split off its mesh, or a block); the break-action and the
            // machine pistol (35, magazine in the grip) none.
            foreach (var e in WeaponSounds.Table)
                if (e.Class != GunClass.Pistol && e.WeaponId != 16 && e.WeaponId != 35) Assert.IsNotNull(view.MagazineOf(e.WeaponId), "magazine piece on weapon " + e.WeaponId);
            Assert.IsNull(view.MagazineOf(16));

            yield return Hold(9, 30, 60); // the Vanguard, with rounds to reload
            var mag = view.MagazineOf(9);
            Assert.IsNotNull(mag, "the Vanguard's magazine is a piece of its own");
            Assert.AreEqual("Wpn09_Vanguard_Mag", mag.Mesh.name, "split off the model's mesh when the prefab was built");
            yield return Fire(audio);
            _input.Frame.Reload = true;
            yield return WaitUntil(() => anim.ReloadProgress >= 0f, 3f, "reload started");
            // First or third person, the magazine leaves the gun at some point of the reload and is back before its end.
            yield return WaitUntil(() => !mag.OnGun.activeSelf, 6f, "the magazine off the gun while the hand holds it");
            yield return WaitUntil(() => mag.OnGun.activeSelf, 6f, "seated again");
            yield return WaitUntil(() => !me.Reloading.Value, 6f, "reload done");
            yield return new WaitForSeconds(0.5f);

            anim.StartInspect();
            yield return WaitUntil(() => anim.InspectProgress > 0.25f, 3f, "inspecting");
            Assert.Greater(Mathf.Abs(anim.ActionShown.RollDeg), 20f, "the gun is turned over");
            _input.Frame.Reload = true; // a reload with a full magazine is refused; a shot ends it instead
            _input.Frame.FirePressed = true;
            yield return WaitUntil(() => anim.InspectProgress < 0f, 2f, "a shot ends the inspection");
        }
    }
}
