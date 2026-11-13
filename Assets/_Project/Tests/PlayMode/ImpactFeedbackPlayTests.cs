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
    // ADR-118 (#152): the hit marker takes the colour of the part hit, and an impact knows what it hit.
    public class ImpactFeedbackPlayTests
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

        [UnityTest]
        public IEnumerator HitMarker_BodyWhite_LegsPaleBlue_HeadAccent_AndImpactsKnowTheirSurface()
        {
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            var me = Combatant.LocalPlayer;
            var view = me.GetComponent<CombatantView>();
            var hud = Hud.Instance;
            Assert.IsNotNull(hud);
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            Combatant dummy = null;
            yield return WaitUntil(() =>
            {
                foreach (var c in Combatant.All) if (!c.IsPlayer) { dummy = c; return true; }
                return false;
            }, 5f, "a training dummy");
            foreach (var part in dummy.GetComponents<MonoBehaviour>())
                if (part.GetType().Name == "TrainingDummy") part.enabled = false;
            dummy.ServerDamageFilter = _ => 0f; // three hits must not kill it
            PlayerController.Local.Teleport(dummy.transform.position + dummy.transform.forward * 6f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.6f);
            me.StanceSpreadFactor = 0f;

            bool Shoot(float height) => me.ServerFire(dummy.transform.position + Vector3.up * height - me.EyePosition);

            Assert.IsTrue(Shoot(0.4f), "leg shot");
            Assert.IsTrue(hud.HitMarkerVisible);
            Assert.IsTrue(hud.LimbMarkerShown, "legs");
            Assert.AreEqual(BulletSurface.Body, view.LastImpactSurface);
            Assert.AreNotEqual(Color.white, hud.HitMarkerColor);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(Shoot(1.1f), "body shot");
            Assert.IsFalse(hud.LimbMarkerShown);
            Assert.AreEqual(Color.white, hud.HitMarkerColor);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(Shoot(1.65f), "head shot");
            Assert.IsTrue(hud.HeadMarkerShown);
            Assert.AreEqual(UiKit.Accent, hud.HitMarkerColor);
            yield return new WaitForSeconds(0.3f);

            // A wall: its surface, and a mark that stays.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = me.EyePosition + (dummy.transform.position - me.EyePosition).normalized * 3f;
            wall.transform.localScale = new Vector3(4f, 4f, 0.4f);
            wall.AddComponent<BulletSurfaceTag>().Surface = BulletSurface.Metal;
            Physics.SyncTransforms();
            int marks = BulletMarks.Placed;
            Assert.IsTrue(Shoot(1.1f), "wall shot");
            Assert.AreEqual(BulletSurface.Metal, view.LastImpactSurface);
            Assert.AreEqual(marks + 1, BulletMarks.Placed, "a bullet mark stays on a metal sheet");
            UnityEngine.Object.Destroy(wall);
        }
    }
}
