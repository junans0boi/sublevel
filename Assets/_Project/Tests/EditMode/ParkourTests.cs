using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-065 (2)(3)(4): what Space does in front of an obstacle (literal heights and depths), the path of a move,
    // the server's sanity check, slow walking and the 360-degree free look.
    public class ParkourTests
    {
        static readonly ParkourRules Rules = new ParkourRules();

        static ObstacleProfile Box(float height, float depth, float clearance = 3f) =>
            new ObstacleProfile { Blocked = true, TopHeight = height, Depth = depth, Clearance = clearance, OpeningHeight = clearance };

        static ObstacleProfile Window(float sill, float opening, float wall = 0.25f) =>
            new ObstacleProfile { Blocked = true, TopHeight = sill, Depth = wall, Opening = true, OpeningHeight = opening, Clearance = opening };

        static ParkourMove Standing(ObstacleProfile p) => Parkour.Classify(p, false, Rules);
        static ParkourMove Crouched(ObstacleProfile p) => Parkour.Classify(p, true, Rules);

        [Test]
        public void NothingAhead_OrSomethingLow_IsAPlainJump()
        {
            Assert.AreEqual(ParkourMove.None, Standing(new ObstacleProfile()));
            Assert.AreEqual(ParkourMove.None, Standing(Box(0.2f, 0.3f)), "a kerb");
            Assert.AreEqual(ParkourMove.None, Standing(Box(0.45f, 0.5f)), "under 0.5 m");
        }

        [Test]
        public void LowAndShallow_IsVaultedOver()
        {
            Assert.AreEqual(ParkourMove.Vault, Standing(Box(0.5f, 0.3f)), "lowest vault");
            Assert.AreEqual(ParkourMove.Vault, Standing(Box(0.8f, 0.5f)), "a 0.8 m fence");
            Assert.AreEqual(ParkourMove.Vault, Standing(Box(1.0f, 0.8f)), "the limits themselves");
            Assert.AreEqual(ParkourMove.Vault, Crouched(Box(0.8f, 0.5f)), "also from a crouch");
        }

        [Test]
        public void TallerOrDeeper_IsClimbedOnto_WhenThereIsRoomToStand()
        {
            Assert.AreEqual(ParkourMove.Climb, Standing(Box(1.2f, 1.0f)), "a 1.2 m crate");
            Assert.AreEqual(ParkourMove.Climb, Standing(Box(0.8f, 1.5f)), "low but deep: onto it");
            Assert.AreEqual(ParkourMove.Climb, Standing(Box(2.0f, 2.0f)), "the highest climb");
            Assert.AreEqual(ParkourMove.Climb, Standing(Box(1.5f, 0.6f, 1.8f)), "just enough top and head room");
        }

        [Test]
        public void TooTall_OrNoRoomOnTop_IsRefused()
        {
            Assert.AreEqual(ParkourMove.Refuse, Standing(Box(2.5f, 1.0f)), "a 2.5 m wall");
            Assert.AreEqual(ParkourMove.Refuse, Standing(Box(2.1f, 3.0f)), "just over the climb limit");
            Assert.AreEqual(ParkourMove.Refuse, Standing(Box(1.5f, 2.0f, 1.2f)), "a low ceiling over the top");
            Assert.AreEqual(ParkourMove.Refuse, Standing(Box(1.5f, 0.3f)), "a thin high wall: nothing to stand on");
            Assert.AreEqual(ParkourMove.Refuse, Standing(Box(1.1f, 0.5f)), "over the vault height and too thin to stand on");
        }

        [Test]
        public void AWindow_IsVaultedThrough_SmallOnesOnlyCrouched()
        {
            Assert.AreEqual(ParkourMove.Window, Standing(Window(1.0f, 1.2f)), "a house window: sill 1.0, 1.2 tall");
            Assert.AreEqual(ParkourMove.Window, Standing(Window(0.9f, 1.4f)), "low sill");
            Assert.AreEqual(ParkourMove.Window, Standing(Window(1.1f, 1.0f)), "high sill, exactly not small");
            Assert.AreEqual(ParkourMove.Window, Standing(Window(1.15f, 1.2f)), "a house window from outside: the floor is 0.15 over the ground (#108)");
            Assert.AreEqual(ParkourMove.Window, Standing(Window(1.15f, 1.4f)), "a hall window from outside");
            Assert.AreEqual(ParkourMove.Window, Crouched(Window(1.0f, 1.2f)), "crouched too");

            Assert.AreEqual(ParkourMove.Refuse, Standing(Window(1.0f, 0.8f)), "a small window standing");
            Assert.AreEqual(ParkourMove.Window, Crouched(Window(1.0f, 0.8f)), "a small window crouched");
            Assert.AreEqual(ParkourMove.Window, Crouched(Window(1.0f, 0.6f)), "the smallest opening");

            Assert.AreEqual(ParkourMove.Refuse, Crouched(Window(1.0f, 0.5f)), "a slot, not a window");
            Assert.AreEqual(ParkourMove.Refuse, Standing(Window(1.3f, 1.2f)), "sill too high");
            Assert.AreEqual(ParkourMove.Refuse, Standing(Window(0.7f, 1.4f)), "sill too low for a window, a wall above");
            Assert.AreEqual(ParkourMove.None, Standing(Window(0.3f, 1.6f)), "a low step under a lintel is jumped");
        }

        [Test]
        public void Durations_AreBetween0_45And0_9Seconds_ByType()
        {
            Assert.AreEqual(0.55f, Parkour.Duration(ParkourMove.Vault, 0.8f, Rules), 1e-4f);
            Assert.AreEqual(0.65f, Parkour.Duration(ParkourMove.Window, 1.0f, Rules), 1e-4f);
            Assert.AreEqual(0.6f, Parkour.Duration(ParkourMove.Climb, 1.0f, Rules), 1e-4f);
            Assert.AreEqual(0.75f, Parkour.Duration(ParkourMove.Climb, 1.5f, Rules), 1e-4f);
            Assert.AreEqual(0.9f, Parkour.Duration(ParkourMove.Climb, 2.0f, Rules), 1e-4f);
            Assert.AreEqual(0f, Parkour.Duration(ParkourMove.Refuse, 1.0f, Rules));
            foreach (var move in new[] { ParkourMove.Vault, ParkourMove.Window, ParkourMove.Climb })
                for (float h = 0.5f; h <= 2.0f; h += 0.25f)
                {
                    float s = Parkour.Duration(move, h, Rules);
                    Assert.GreaterOrEqual(s, 0.45f, move + " " + h);
                    Assert.LessOrEqual(s, 0.9f, move + " " + h);
                }
        }

        [Test]
        public void TheVaultPath_ClearsTheTop_AndEndsOnTheFarSide()
        {
            // Feet at 0, a 0.8 m box, landing on the floor beyond.
            Parkour.PathPoint(ParkourMove.Vault, 0f, 0f, 0f, 0.8f, 0.15f, out float along, out float y);
            Assert.AreEqual(0f, along, 1e-5f);
            Assert.AreEqual(0f, y, 1e-5f);
            Parkour.PathPoint(ParkourMove.Vault, 0.5f, 0f, 0f, 0.8f, 0.15f, out along, out y);
            Assert.AreEqual(0.5f, along, 1e-5f);
            Assert.AreEqual(0.95f, y, 1e-4f, "feet 0.15 m over the top at the middle");
            Parkour.PathPoint(ParkourMove.Vault, 1f, 0f, 0f, 0.8f, 0.15f, out along, out y);
            Assert.AreEqual(1f, along, 1e-5f);
            Assert.AreEqual(0f, y, 1e-5f);
        }

        [Test]
        public void TheClimbPath_GoesUpFirst_ThenForward()
        {
            Parkour.PathPoint(ParkourMove.Climb, 0.35f, 0f, 1.2f, 1.2f, 0.15f, out float along, out float y);
            Assert.AreEqual(0f, along, 1e-5f, "still at the face while rising");
            Assert.Greater(y, 0.6f);
            Parkour.PathPoint(ParkourMove.Climb, 0.7f, 0f, 1.2f, 1.2f, 0.15f, out along, out y);
            Assert.AreEqual(1.2f, y, 1e-4f, "up by now");
            Assert.Greater(along, 0.3f);
            Parkour.PathPoint(ParkourMove.Climb, 1f, 0f, 1.2f, 1.2f, 0.15f, out along, out y);
            Assert.AreEqual(1f, along, 1e-5f);
            Assert.AreEqual(1.2f, y, 1e-5f);
        }

        [Test]
        public void S_TakesTheMoveBack_OnlyInItsFirst60Percent()
        {
            Assert.IsTrue(Parkour.CanCancel(0f, Rules));
            Assert.IsTrue(Parkour.CanCancel(0.59f, Rules));
            Assert.IsFalse(Parkour.CanCancel(0.6f, Rules));
            Assert.IsFalse(Parkour.CanCancel(0.9f, Rules));
        }

        [Test]
        public void TheServer_BelievesOnlyMovesAParkourCanMake()
        {
            Assert.IsTrue(Parkour.Plausible(0f, 0f, 2.1f, Rules), "a vault over a 0.5 m box");
            Assert.IsTrue(Parkour.Plausible(0.6f, 2.0f, 0.6f, Rules), "a 2 m climb");
            Assert.IsTrue(Parkour.Plausible(0f, -1.5f, 2f, Rules), "out of a window onto lower ground");
            Assert.IsFalse(Parkour.Plausible(0f, 0f, 5f, Rules), "too far");
            Assert.IsFalse(Parkour.Plausible(0f, 3f, 1f, Rules), "too high");
            Assert.IsFalse(Parkour.Plausible(float.NaN, 0f, 1f, Rules));
            Assert.IsFalse(Parkour.IsMotion(ParkourMove.Refuse));
            Assert.IsFalse(Parkour.IsMotion(ParkourMove.None));
            Assert.IsTrue(Parkour.IsMotion(ParkourMove.Window));
        }

        // #115 (ADR-087): an edge caught in the air. `overFeet`: the edge over the feet now; `overGround`: over the ground below.
        static bool Grabs(float overFeet, float overGround, float depth = 1.0f, float clearance = 3f) =>
            Parkour.CanGrab(new ObstacleProfile { Blocked = true, TopHeight = overFeet, Depth = depth, Clearance = clearance }, overGround, Rules);

        [Test]
        public void AHighEdge_IsCaughtInTheAir_From2To2_5mOverTheGround()
        {
            Assert.IsTrue(Grabs(1.9f, 2.3f), "a 2.3 m container, the feet 0.4 m up in a jump");
            Assert.IsTrue(Grabs(1.7f, 2.5f), "a 2.5 m roof edge at the top of a jump");
            Assert.IsTrue(Grabs(1.9f, 2.0f), "the lowest: what a climb from the ground does not take");
            Assert.IsFalse(Grabs(1.7f, 1.8f), "lower edges are climbed or vaulted from the ground");
            Assert.IsFalse(Grabs(1.9f, 2.6f), "over 2.5 m: too high");
            Assert.IsFalse(Grabs(1.9f, 3.0f), "a 3 m wall");
        }

        [Test]
        public void TheEdge_MustBeWithinTheHandsReach()
        {
            Assert.IsTrue(Grabs(1.95f, 2.3f), "just within reach over the head");
            Assert.IsFalse(Grabs(2.1f, 2.4f), "still above the hands");
            Assert.IsTrue(Grabs(1.4f, 2.4f), "at the chest, falling past it");
            Assert.IsFalse(Grabs(1.2f, 2.4f), "already below the chest");
        }

        [Test]
        public void AnEdge_WithNoRoomToStandOnTop_IsNotCaught()
        {
            Assert.IsFalse(Grabs(1.9f, 2.3f, depth: 0.4f), "a thin wall top");
            Assert.IsFalse(Grabs(1.9f, 2.3f, clearance: 1.2f), "a low ceiling over it");
            Assert.IsFalse(Parkour.CanGrab(new ObstacleProfile(), 2.3f, Rules), "nothing there");
        }

        [Test]
        public void ThePullUp_IsAMove_HangingIsNot_AndTakes0_6To0_9Seconds()
        {
            Assert.IsTrue(Parkour.IsMotion(ParkourMove.LedgeClimb));
            Assert.IsFalse(Parkour.IsMotion(ParkourMove.Hang));
            Assert.IsTrue(Parkour.IsLedge(ParkourMove.Hang) && Parkour.IsLedge(ParkourMove.LedgeClimb));
            Assert.IsFalse(Parkour.IsLedge(ParkourMove.Climb));
            float s = Parkour.Duration(ParkourMove.LedgeClimb, 2.0f, Rules);
            Assert.That(s, Is.InRange(0.6f, 0.9f));
            // The pull-up: up along the wall first (the feet still under the edge at half way), then over onto the top.
            Parkour.PathPoint(ParkourMove.LedgeClimb, 0.5f, 0.5f, 2.32f, 2.3f, 0.15f, out float along, out float y);
            Assert.Less(y, 2.3f);
            Assert.AreEqual(0f, along, 1e-4f);
            Parkour.PathPoint(ParkourMove.LedgeClimb, 1f, 0.5f, 2.32f, 2.3f, 0.15f, out along, out y);
            Assert.AreEqual(1f, along, 1e-4f);
            Assert.AreEqual(2.32f, y, 1e-4f);
            // From hanging (feet 1.8 m under the edge) onto the top: within what the server believes.
            Assert.IsTrue(Parkour.Plausible(0f, Rules.HangDrop + 0.02f, 0.8f, Rules));
        }

        [Test]
        public void SlowWalk_Is1_7MetresASecond_WhateverIsInTheHands()
        {
            var p = new LocomotionParams();
            Assert.AreEqual(1.7f, p.SlowWalkSpeed, 1e-4f);
            float armed = Locomotion.MoveSpeedFactor(1f, 1f, 1f, p.RifleMoveFactor, p, slowWalk: true);
            float empty = Locomotion.MoveSpeedFactor(1f, 1f, 1f, 1f, p, slowWalk: true);
            Assert.AreEqual(1.7f, p.WalkSpeed * armed, 1e-4f);
            Assert.AreEqual(1.7f, p.WalkSpeed * empty, 1e-4f, "no weapon factor while slow walking");
            Assert.AreEqual(1.7f * 0.74f, p.WalkSpeed * Locomotion.MoveSpeedFactor(1f, 1f, 0.74f, 1f, p, true), 1e-4f, "crouched slower still");
            Assert.AreEqual(4.7f * p.RifleMoveFactor, p.WalkSpeed * Locomotion.MoveSpeedFactor(1f, 1f, 1f, p.RifleMoveFactor, p), 1e-4f, "unchanged without it");
        }

        [Test]
        public void SlowWalk_QuietensFootsteps_AndBotsHearThemLessFar()
        {
            var p = new LocomotionParams();
            var bots = new BotRules();
            var stances = new StanceRules();
            Assert.AreEqual(0.25f, p.SlowWalkFootstepVolume, 1e-4f);
            Assert.AreEqual(14f, BotHearing.FootstepRadius(0f, false, bots, stances), 1e-4f);
            Assert.AreEqual(4.2f, BotHearing.FootstepRadius(0f, true, bots, stances), 1e-4f, "14 m x 0.3");
            Assert.AreEqual(5.6f, BotHearing.FootstepRadius(1f, false, bots, stances), 1e-4f, "crouched: x 0.4");
            Assert.AreEqual(1.68f, BotHearing.FootstepRadius(1f, true, bots, stances), 1e-4f, "both");
            Assert.AreEqual(40f, bots.GlassHearingRadius, 1e-4f);
        }

        [Test]
        public void FreeLook_GoesAllTheWayRound_AndSwingsBackTheShortWay()
        {
            float yaw = 0f;
            for (int i = 0; i < 9; i++) yaw = Locomotion.WrapFreeYaw(yaw, 20f);
            Assert.AreEqual(-180f, yaw, 1e-3f, "180 degrees round: no limit");
            yaw = Locomotion.WrapFreeYaw(yaw, 30f);
            Assert.AreEqual(-150f, yaw, 1e-3f, "past behind, the view keeps going");
            Assert.AreEqual(170f, Locomotion.WrapFreeYaw(-170f, -20f), 1e-3f);

            var rules = new FreeLookRules();
            float back = Locomotion.FreeLookReturn(-150f, 1f / 60f, rules);
            Assert.Greater(back, -150f, "toward 0 the short way");
            Assert.AreEqual(9f, back + 150f, 1e-3f, "at most 540 degrees a second");
            float v = 170f;
            int frames = 0;
            while (v != 0f && frames < 600) { v = Locomotion.FreeLookReturn(v, 1f / 60f, rules); frames++; }
            Assert.AreEqual(0f, v);
            Assert.Less(frames, 60, "back within a second");
        }
    }
}
