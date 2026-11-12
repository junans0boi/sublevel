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
    // M1.6b in the real TestBox scene with injected input: shots and hits can be seen (muzzle flash, tracer,
    // impact, hit confirmation) and held automatic fire keeps the weapon's rate.
    public class CombatFeedbackTests
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

        // The sight blocker in front of the spawn point, about 10 m away.
        static readonly Vector3 WallPoint = new Vector3(0f, 1.5f, -14f);

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

        IEnumerator GiveRifle()
        {
            Combatant.LocalPlayer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return WaitUntil(() => Combatant.LocalPlayer.ActiveWeaponId == 3, 2f, "rifle in hand");
        }

        IEnumerator FaceTheWall()
        {
            PlayerController.Local.LookAt(WallPoint);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator OneRifleShotAtAWall_ShowsAFlash_ATracer_AndAnImpact()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var view = me.GetComponent<CombatantView>();
            yield return GiveRifle();
            yield return FaceTheWall();

            Combatant.ShotResult seen = default;
            int resolved = 0;
            void OnResolved(in Combatant.ShotResult shot) { seen = shot; resolved++; }
            me.ShotResolved += OnResolved;
            _input.Frame.FireHeld = true; // one frame is far shorter than the fire interval: one shot
            yield return null;
            _input.Frame.FireHeld = false;
            yield return WaitUntil(() => view.TracersShown > 0, 2f, "tracer shown");
            yield return new WaitForSeconds(0.3f);
            me.ShotResolved -= OnResolved;

            Assert.AreEqual(29, me.ActiveMag, "exactly one shot");
            Assert.AreEqual(1, view.MuzzleFlashes);
            Assert.AreEqual(1, view.TracersShown);
            Assert.AreEqual(1, view.ImpactsShown);
            Assert.AreEqual(0, view.HitConfirms, "a wall is not a hit");
            Assert.AreEqual(1, resolved);
            Assert.AreEqual(ShotHit.World, seen.Hit);
            Assert.IsNull(seen.Victim);
            Assert.Less(seen.Normal.z, -0.9f, "the wall faces the shooter");
            Assert.AreEqual(-14.5f, seen.End.z, 0.05f, "ended on the wall's near face");
        }

        // Regression for "the fire rate is not smooth": no request is dropped and the rhythm does not drift.
        [UnityTest]
        public IEnumerator HoldingFire_ForASecond_FiresAtTheWeaponsRate()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            yield return GiveRifle();
            yield return FaceTheWall();
            Assert.AreEqual(0.11f, GameData.Weapons.Find(3).FireInterval, 1e-4f);

            int shots = 0;
            void OnShot(Vector3 from, Vector3 to) => shots++;
            me.ShotFired += OnShot;
            _input.Frame.FireHeld = true;
            float start = Time.time;
            while (Time.time - start < 1f) yield return null;
            _input.Frame.FireHeld = false;
            yield return new WaitForSeconds(0.2f);
            me.ShotFired -= OnShot;

            // 1 s at 0.11 s per shot is 10 shots (the first plus nine more).
            Assert.That(shots, Is.InRange(9, 11));
            Assert.AreEqual(30 - shots, me.ActiveMag, "every shot seen was one round, none refused");
        }

        [UnityTest]
        public IEnumerator ShootingADummy_ConfirmsTheHit()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var view = me.GetComponent<CombatantView>();
            yield return GiveRifle();

            Combatant dummy = null;
            foreach (var c in Combatant.All)
                if (!c.IsPlayer) { dummy = c; break; }
            Assert.IsNotNull(dummy, "a training dummy in the test box");
            var dummyView = dummy.GetComponent<CombatantView>();
            var chest = dummy.transform.position + Vector3.up * 1.1f;
            pc.Teleport(dummy.transform.position + dummy.transform.forward * 5f + Vector3.up * 0.1f);
            pc.LookAt(chest);
            yield return null;

            _input.Frame.FireHeld = true;
            float end = Time.realtimeSinceStartup + 3f;
            while (view.HitConfirms == 0)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "no hit confirmed; dummy hp " + dummy.Hp.Value + ", mag " + me.ActiveMag);
                pc.LookAt(chest); // recoil climbs; keep the aim on the chest
                yield return null;
            }
            _input.Frame.FireHeld = false;

            Assert.Less(dummy.Hp.Value, 100f, "the confirmed hit did damage");
            Assert.GreaterOrEqual(view.ImpactsShown, 1, "an impact where the shot landed");
            Assert.AreEqual(0, dummyView.HitConfirms, "only the shooter is told");

            // The dummy shoots back from 5 m: its hits tell the local player where they came from.
            yield return WaitUntil(() => dummyView.DamageDirectionsShown > 0 || me.Dead.Value, 10f, "hit by the dummy");
            Assert.Greater(dummyView.DamageDirectionsShown, 0);
            Assert.Greater(dummyView.MuzzleFlashes, 0, "the dummy's shots are shown too");
        }
    }
}
