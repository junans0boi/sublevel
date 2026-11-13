using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // #105: what gameplay still gets of the input while a full-screen UI is open.
    public class UiFocusTests
    {
        static PlayerInputFrame Everything() => new PlayerInputFrame
        {
            Move = new Vector2(0.5f, 1f), Look = new Vector2(3f, -2f), Sprint = true, SlowWalk = true, Crouch = true, Prone = true,
            Jump = true, FireHeld = true, FirePressed = true, AimHeld = true, Interact = true, Reload = true, SelectSlot = 2,
            LeanLeft = true, ThrowHeld = true, Ping = true, ZoomSteps = 1, FreeLook = true, Heal = true,
        };

        [TestCase(UiScreen.Inventory)]
        [TestCase(UiScreen.Map)]
        public void InventoryAndMap_KeepTheMovementKeys_AndDropTheMouseAndActions(UiScreen screen)
        {
            var f = UiFocus.GameplayInput(screen, Everything());
            Assert.AreEqual(new Vector2(0.5f, 1f), f.Move);
            Assert.IsTrue(f.Sprint && f.SlowWalk && f.Crouch && f.Prone, "sprint, slow walk, crouch and prone carry on");
            Assert.AreEqual(Vector2.zero, f.Look, "the mouse drives the screen, not the view");
            Assert.IsFalse(f.FireHeld || f.FirePressed || f.AimHeld || f.ThrowHeld || f.Ping || f.FreeLook, "no mouse actions");
            Assert.IsFalse(f.Jump || f.Interact || f.Reload || f.LeanLeft || f.Heal);
            Assert.AreEqual(-1, f.SelectSlot);
            Assert.AreEqual(0, f.ZoomSteps);
        }

        [Test]
        public void PauseMenu_HoldsThePlayerStill()
        {
            var f = UiFocus.GameplayInput(UiScreen.Menu, Everything());
            Assert.AreEqual(Vector2.zero, f.Move);
            Assert.IsFalse(f.Sprint || f.Crouch || f.Prone || f.FireHeld);
            Assert.AreEqual(-1, f.SelectSlot);
        }

        [Test]
        public void NoScreen_PassesEverything()
        {
            var f = UiFocus.GameplayInput(UiScreen.None, Everything());
            Assert.AreEqual(new Vector2(3f, -2f), f.Look);
            Assert.IsTrue(f.FireHeld && f.Jump);
            Assert.AreEqual(2, f.SelectSlot);
        }
    }
}
