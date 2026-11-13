using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-105 (PUBG_GAP_V2 G9): the flash grenade's blindness, ringing, white-out and volume duck as plain numbers.
    public class FlashTests
    {
        static readonly FlashRules R = new FlashRules();

        [Test]
        public void FacingItAtFiveMetres_BlindsForMoreThanFourSeconds()
        {
            Assert.Greater(Flash.Blind(5f, 0f, true, R), 4f);
            Assert.AreEqual(5.5f, Flash.Blind(0f, 0f, true, R), 1e-4f, "point blank, looking at it");
        }

        [Test]
        public void TurnedAway_IsMuchShorter_ButNeverUnderTheMinimum()
        {
            float front = Flash.Blind(5f, 0f, true, R);
            float back = Flash.Blind(5f, 180f, true, R);
            Assert.Less(back, front * 0.4f);
            Assert.GreaterOrEqual(back, R.MinBlindSeconds);
            Assert.AreEqual(front, Flash.Blind(5f, 60f, true, R), 1e-4f, "inside the front cone: the same as head on");
            Assert.Greater(Flash.Blind(5f, 100f, true, R), back, "in between");
            Assert.Less(Flash.Blind(5f, 100f, true, R), front);
        }

        [Test]
        public void FurtherAway_ShorterDown_ToAboutOneSecondAtTwentyMetres()
        {
            Assert.AreEqual(1.225f, Flash.Blind(19f, 0f, true, R), 0.01f);
            Assert.Greater(Flash.Blind(5f, 0f, true, R), Flash.Blind(12f, 0f, true, R));
            Assert.AreEqual(0f, Flash.Blind(20f, 0f, true, R), "beyond the radius");
            Assert.AreEqual(0f, Flash.Blind(60f, 0f, true, R));
        }

        [Test]
        public void NoLineOfSight_NoBlindness_ButRingingCloseBy()
        {
            Assert.AreEqual(0f, Flash.Blind(2f, 0f, false, R), "behind a wall");
            Assert.AreEqual(3f, Flash.Tinnitus(0f, 0f, R), 1e-4f, "right next to it");
            Assert.AreEqual(1.5f, Flash.Tinnitus(3.25f, 0f, R), 1e-4f, "half way out");
            Assert.AreEqual(0f, Flash.Tinnitus(6.5f, 0f, R));
            Assert.AreEqual(0f, Flash.Tinnitus(30f, 0f, R));
        }

        [Test]
        public void Blindness_BringsRingingThatOutlastsIt()
        {
            Assert.AreEqual(4.4f + R.TinnitusExtraSeconds, Flash.Tinnitus(5f, 4.4f, R), 1e-4f);
        }

        [Test]
        public void Whiteness_IsSolid_ThenFades_ThenGone()
        {
            Assert.AreEqual(1f, Flash.Whiteness(0f, 4f, R));
            Assert.AreEqual(1f, Flash.Whiteness(2.4f, 4f, R), "the hold share of 4 s");
            float mid = Flash.Whiteness(3.2f, 4f, R);
            Assert.That(mid, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.Less(Flash.Whiteness(3.8f, 4f, R), mid);
            Assert.AreEqual(0f, Flash.Whiteness(4f, 4f, R));
            Assert.AreEqual(0f, Flash.Whiteness(1f, 0f, R), "never blinded");
        }

        [Test]
        public void Volume_IsDucked_ThenComesBackAndEndsAtOne()
        {
            Assert.AreEqual(R.DuckVolume, Flash.Volume(0.1f, 6f, R), 1e-4f);
            Assert.AreEqual(R.DuckVolume, Flash.Volume(3.5f, 6f, R), 1e-4f, "still ducked before the last 40%");
            float back = Flash.Volume(5f, 6f, R);
            Assert.That(back, Is.GreaterThan(R.DuckVolume).And.LessThan(1f));
            Assert.AreEqual(1f, Flash.Volume(6f, 6f, R));
            Assert.AreEqual(1f, Flash.Volume(1f, 0f, R), "no ringing: untouched");
        }

        [Test]
        public void TheKind_IsPickedUpCarriedAndCycled()
        {
            Assert.AreEqual(ThrowableKind.Flash, Throwables.Next(ThrowableKind.Smoke));
            Assert.AreEqual(ThrowableKind.Molotov, Throwables.Next(ThrowableKind.Flash));
            Assert.AreEqual(ThrowableKind.Frag, Throwables.Next(ThrowableKind.Molotov));
            Assert.IsTrue(Throwables.TryKindOfLoot(LootKind.Flash, out var kind));
            Assert.AreEqual(ThrowableKind.Flash, kind);
            Assert.IsFalse(Throwables.TryKindOfLoot(LootKind.Ammo, out _));
            Assert.AreEqual(LootKind.Flash, Throwables.LootKindOf(ThrowableKind.Flash));
            Assert.AreEqual("섬광탄", Throwables.NameOf(ThrowableKind.Flash));
            Assert.AreEqual(2f, Throwables.FuseOf(ThrowableKind.Flash, new ThrowableRules(), new SmokeRules()), 1e-4f);
            Assert.AreEqual(20, Carry.UnitTenths(LootKind.Flash, AmmoType.Pistol9, new CarryRules()));
        }
    }
}
