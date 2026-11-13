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
    // ADR-114 (#151): a wooden board lets the shot through with 70% of its damage, concrete stops it.
    public class PenetrationPlayTests
    {
        const int Rifle = 3;

        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
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

        static GameObject Slab(Vector3 at, float thick, BulletSurface surface)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = at;
            go.transform.localScale = new Vector3(4f, 4f, thick);
            go.AddComponent<BulletSurfaceTag>().Surface = surface;
            return go;
        }

        [UnityTest]
        public IEnumerator DummyBehindWood_TakesReducedDamage_BehindConcrete_TakesNone()
        {
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            var me = Combatant.LocalPlayer;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            Combatant dummy = null;
            yield return WaitUntil(() =>
            {
                foreach (var c in Combatant.All) if (!c.IsPlayer) { dummy = c; return true; }
                return false;
            }, 5f, "a training dummy");
            foreach (var part in dummy.GetComponents<MonoBehaviour>())
                if (part.GetType().Name == "TrainingDummy") part.enabled = false; // by name: no AI reference
            PlayerController.Local.Teleport(dummy.transform.position + dummy.transform.forward * 6f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.6f);
            var chest = dummy.transform.position + Vector3.up * 1.1f;
            var toward = (chest - me.EyePosition).normalized;
            float hp0 = dummy.Hp.Value;
            var weapon = GameData.Weapons.Find(Rifle);

            // 3 m in front of the dummy, on the line of fire.
            var wood = Slab(me.EyePosition + toward * 3f, 0.2f, BulletSurface.Wood);
            Physics.SyncTransforms();
            me.StanceSpreadFactor = 0f;
            Assert.IsTrue(me.ServerFire(chest - me.EyePosition), "shot through wood fired");
            Assert.AreEqual(weapon.Damage * 0.70f, hp0 - dummy.Hp.Value, 0.5f, "70% of a body hit");
            UnityEngine.Object.Destroy(wood);
            yield return new WaitForSeconds(0.3f);

            float hp1 = dummy.Hp.Value;
            var concrete = Slab(me.EyePosition + toward * 3f, 0.2f, BulletSurface.Concrete);
            Physics.SyncTransforms();
            Assert.IsTrue(me.ServerFire(chest - me.EyePosition), "shot into concrete fired");
            Assert.AreEqual(hp1, dummy.Hp.Value, 1e-4f, "concrete stops it");
            UnityEngine.Object.Destroy(concrete);
        }
    }
}
