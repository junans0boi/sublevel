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
    // ADR-120 (#154): what is fitted to the weapon in hand is drawn on its model, each place its own shape.
    public class AttachmentVisualPlayTests
    {
        const int Rifle = 3, Suppressor = 11, VerticalGrip = 12, Stock = 15, BigMag = 16;

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
        public IEnumerator FittedParts_AppearOnTheHeldGun_OneModelPerPlace_AndGoWhenTakenOff()
        {
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            var me = Combatant.LocalPlayer;
            var view = me.GetComponent<AttachmentVisual>();
            var gear = me.GetComponent<CombatantOptics>();
            Assert.IsNotNull(view, "the combatant view adds it");
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            yield return new WaitForSeconds(0.3f);
            foreach (var slot in new[] { AttachSlot.Muzzle, AttachSlot.Grip, AttachSlot.Stock, AttachSlot.Mag })
                Assert.IsNull(view.Part(slot), "bare gun: " + slot);

            foreach (int id in new[] { Suppressor, VerticalGrip, Stock, BigMag }) me.ServerGive(Optics.ToLoot(id));
            yield return new WaitForSeconds(0.5f);
            var meshes = new System.Collections.Generic.HashSet<Mesh>();
            foreach (var slot in new[] { AttachSlot.Muzzle, AttachSlot.Grip, AttachSlot.Stock, AttachSlot.Mag })
            {
                var part = view.Part(slot);
                Assert.IsNotNull(part, "fitted: " + slot);
                Assert.IsTrue(part.GetComponent<MeshRenderer>().enabled);
                Assert.IsTrue(part.transform.IsChildOf(me.GetComponent<WeaponVisual>().ModelFor(Rifle).transform), "travels with the gun: " + slot);
                meshes.Add(part.GetComponent<MeshFilter>().sharedMesh);
            }
            Assert.AreEqual(4, meshes.Count, "each place has its own shape");

            // The suppressor is a long can, the flash hider a different model.
            var can = view.Part(AttachSlot.Muzzle).GetComponent<MeshFilter>().sharedMesh;
            Assert.AreSame(LootMeshes.Attachment(Suppressor), can);
            Assert.AreNotSame(LootMeshes.Attachment(10), can);

            gear.ServerRemove(Attachments.GearIndex(me.ActiveSlot.Value, AttachSlot.Muzzle));
            yield return new WaitForSeconds(0.5f);
            Assert.IsNull(view.Part(AttachSlot.Muzzle), "taken off");
            Assert.IsNotNull(view.Part(AttachSlot.Grip), "the rest stays");
        }
    }
}
