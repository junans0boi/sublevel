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
    // #90, ADR-074 in the real TestBox scene as a host: shots flatten a tyre, crack the glass and hole the body; at no
    // health the car burns, can no longer be driven or entered, then explodes (hurting who stands next to it) into a wreck.
    public class VehicleDamagePlayTests
    {
        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        static Vehicle Car
        {
            get
            {
                foreach (var v in Vehicle.All)
                    if (v != null && v.Kind == VehicleKind.Car) return v;
                return null;
            }
        }

        static IEnumerator Load()
        {
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return WaitUntil(() => Car != null, 10f, "the test box's car");
            for (int n = 0; SpawnProtection.InputBlocked && n < 80; n++) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(0.5f);
        }

        // Fires at `target` until `done` or `shots` rounds have gone (the rifle's rate limits ServerFire).
        static IEnumerator ShootAt(Combatant me, Func<Vector3> target, Func<bool> done, int shots)
        {
            for (int fired = 0, tries = 0; fired < shots && !done() && tries < shots * 30; tries++)
            {
                if (me.ServerFire(target() - me.EyePosition)) fired++;
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Shots_FlattenATyre_CrackTheGlass_AndHoleTheBody()
        {
            yield return Load();
            var car = Car;
            var damage = car.GetComponent<VehicleDamage>();
            Assert.IsNotNull(damage, "the car prefab has VehicleDamage (VehicleBuilder)");
            Assert.IsNotNull(car.GetComponent<VehicleDamageVisual>(), "and its visual");
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 90 });
            yield return new WaitForSeconds(0.6f);

            // From the car's left, a few metres off.
            var t = car.transform;
            PlayerController.Local.Teleport(t.position - t.right * 5f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.5f);

            // The nearest wheel on this side.
            int wheel = -1;
            float best = float.MaxValue;
            for (int i = 0; i < car.WheelCount; i++)
            {
                float d = (car.WheelPivot(i).position - me.transform.position).sqrMagnitude;
                if (d < best) { best = d; wheel = i; }
            }
            yield return ShootAt(me, () => car.WheelPivot(wheel).position, () => (damage.FlatTyres.Value & (1 << wheel)) != 0, 12);
            Assert.AreNotEqual(0, damage.FlatTyres.Value & (1 << wheel), "the shot tyre is flat");
            var state = car.GetComponent<VehicleState>();
            Assert.Less(state.GripOf(wheel), 1f, "less grip on the flat tyre");
            Assert.Greater(state.DropOf(wheel), 0f, "that corner sits lower");

            // The side window (between the belt line and the roof, at the cabin's middle).
            var b = damage.LocalBounds();
            Vector3 Window() => t.TransformPoint(new Vector3(0f, b.min.y + b.size.y * 0.75f, b.center.z));
            yield return ShootAt(me, Window, () => damage.Panes.Value != 0, 4);
            Assert.AreNotEqual(0, damage.Panes.Value, "a pane cracked");

            // The door below the window.
            int holes = 0;
            damage.Holed += (_, __) => holes++;
            float before = damage.Health.Value;
            Vector3 Door() => t.TransformPoint(new Vector3(0f, b.min.y + b.size.y * 0.3f, b.center.z));
            yield return ShootAt(me, Door, () => holes > 0 && damage.Health.Value < before, 6);
            Assert.Less(damage.Health.Value, before, "the body lost health");
            Assert.Greater(holes, 0, "a bullet hole was placed");
        }

        [UnityTest]
        public IEnumerator NoHealth_Burns_Disables_ThenExplodesIntoAWreck()
        {
            yield return Load();
            var car = Car;
            var damage = car.GetComponent<VehicleDamage>();
            var state = car.GetComponent<VehicleState>();
            var me = Combatant.LocalPlayer;

            damage.ServerSetHealth01(0.2f);
            yield return WaitUntil(() => damage.CurrentStage == VehicleDamageStage.BlackSmoke, 2f, "black smoke");
            Assert.IsFalse(state.Disabled, "smoking cars still drive");
            Assert.AreEqual(0.2f, state.Health01, 1e-3f, "the HUD reads the condition");

            // Standing beside it when it goes up.
            PlayerController.Local.Teleport(car.transform.position - car.transform.right * 3f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.4f);
            float hp = me.Hp.Value;
            damage.ServerSetHealth01(0f);
            yield return WaitUntil(() => damage.CurrentStage == VehicleDamageStage.Burning, 2f, "burning");
            Assert.IsTrue(state.Disabled, "a burning car cannot be driven or entered");
            Assert.AreNotEqual(car, Vehicle.NearestEnterable(car.DoorPosition(0), 3f, out _), "not enterable");
            yield return WaitUntil(() => damage.CurrentStage == VehicleDamageStage.Wrecked, VehicleDamageRules.Current.BurnSeconds + 3f, "the explosion");
            yield return new WaitForSeconds(0.3f);
            Assert.Less(me.Hp.Value, hp, "the blast hurt the player beside it");
            Assert.IsTrue(state.Disabled, "the wreck stays disabled");
        }
    }
}
