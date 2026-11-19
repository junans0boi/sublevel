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
    // ADR-065 (1)-(4) in the real TestBox scene with injected input: auto-run, the 360-degree free look, slow
    // walking, and parkour (vault, take-back, refusal, a glazed window). All of it happens south of the spawn
    // blocker (z < -15), where no dummy can see.
    public class MovementExtrasTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = Frame.Crouch = Frame.Prone = Frame.Map = Frame.AutoRun = false;
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

        static GameObject Box(string name, Vector3 center, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = center;
            box.transform.localScale = size;
            Physics.SyncTransforms();
            return box;
        }

        // Standing at `feet`, facing north (+z), settled on the floor.
        IEnumerator StandAt(PlayerController pc, Vector3 feet)
        {
            pc.Teleport(feet);
            pc.LookAt(new Vector3(feet.x, 1.6f, feet.z + 20f));
            yield return new WaitForSeconds(0.3f);
        }

        static float PlanarSpeedOver(Vector3 from, Vector3 to, float seconds) =>
            new Vector2(to.x - from.x, to.z - from.z).magnitude / seconds;

        [UnityTest]
        public IEnumerator AutoRun_SprintsForwardWithNoKeyHeld_AndStopsOnS()
        {
            yield return LoadTestBox();
            var pc = PlayerController.Local;
            yield return StandAt(pc, new Vector3(-6f, 0.1f, -27f));

            _input.Frame.AutoRun = true;
            yield return WaitUntil(() => pc.AutoRunning, 1f, "auto-run on");
            yield return new WaitForSeconds(0.3f);
            var p0 = pc.transform.position;
            float t0 = Time.time;
            yield return new WaitForSeconds(0.5f);
            float speed = PlanarSpeedOver(p0, pc.transform.position, Time.time - t0);
            Assert.Greater(speed, 5.5f, "sprint speed with no key held (ADR-350)");
            Assert.IsTrue(pc.Sprinting, "auto-run is a sprint");
            Assert.Greater(pc.transform.position.z - p0.z, 2f, "straight ahead (north)");

            // A press of S ends it; let go, the character stands.
            _input.Frame.Move = new Vector2(0f, -1f);
            yield return null;
            yield return null;
            Assert.IsFalse(pc.AutoRunning, "S cancels auto-run");
            _input.Frame.Move = Vector2.zero;
            yield return new WaitForSeconds(0.3f);
            var p1 = pc.transform.position;
            yield return new WaitForSeconds(0.4f);
            Assert.Less(PlanarSpeedOver(p1, pc.transform.position, 0.4f), 0.2f, "standing still");

            // #105: the inventory / map keep the movement, so auto-run goes on; only the pause menu ends it.
            _input.Frame.AutoRun = true;
            yield return WaitUntil(() => pc.AutoRunning, 1f, "auto-run on again");
            UiFocus.Toggle(UiScreen.Inventory);
            yield return null;
            yield return null;
            Assert.IsTrue(pc.AutoRunning, "the inventory keeps auto-run");
            UiFocus.Close();
            UiFocus.Toggle(UiScreen.Menu);
            yield return null;
            yield return null;
            Assert.IsFalse(pc.AutoRunning, "the pause menu cancels auto-run");
            UiFocus.Close();
        }

        [UnityTest]
        public IEnumerator FreeLook_OrbitsTheCamera180Degrees_WhileTheBodyKeepsItsHeadingAndWay_AndHoldsFire()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == 3, 2f, "rifle in hand");
            yield return StandAt(pc, new Vector3(6f, 0.1f, -27f));
            float bodyYaw = pc.transform.eulerAngles.y;
            float cameraYaw = pc.Camera.transform.eulerAngles.y;
            int shots = 0;
            void OnShot(Vector3 from, Vector3 to) => shots++;
            me.ShotFired += OnShot;

            _input.Frame.FreeLook = true;
            _input.Frame.Look = new Vector2(18f, 0f);
            for (int i = 0; i < 10; i++) yield return null;
            _input.Frame.Look = Vector2.zero;
            yield return null;
            yield return null;
            Assert.IsTrue(pc.FreeLooking);
            Assert.AreEqual(0f, Mathf.DeltaAngle(bodyYaw, pc.transform.eulerAngles.y), 0.01f, "the body keeps its heading");
            Assert.AreEqual(180f, Mathf.Abs(Mathf.DeltaAngle(cameraYaw, pc.Camera.transform.eulerAngles.y)), 1f, "the camera looks back");

            // Walking while looking back still goes the body's way (north).
            var p0 = pc.transform.position;
            _input.Frame.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.4f);
            _input.Frame.Move = Vector2.zero;
            var moved = pc.transform.position - p0;
            Assert.Greater(moved.z, 1f, "moved north");
            Assert.Less(Mathf.Abs(moved.x), 0.2f);

            // No shooting while free looking.
            _input.Frame.FireHeld = true;
            _input.Frame.FirePressed = true;
            yield return new WaitForSeconds(0.3f);
            _input.Frame.FireHeld = false;
            Assert.AreEqual(0, shots, "the trigger does nothing while free looking");

            // Let go: the view swings back to the body.
            _input.Frame.FreeLook = false;
            yield return WaitUntil(() => Mathf.Abs(Mathf.DeltaAngle(pc.ViewYaw, pc.Yaw)) < 0.01f, 1.5f, "the view returns to the body heading");
            me.ShotFired -= OnShot;
        }

        [UnityTest]
        public IEnumerator SlowWalk_Is2MetresASecond_CannotSprint_AndTreadsQuieter()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = me.GetComponent<CombatantStance>();
            var audio = me.GetComponent<CombatantAudio>();
            Assert.IsNotNull(audio);
            yield return StandAt(pc, new Vector3(-9f, 0.1f, -27f));

            // Ordinary walking first, for the footstep volume to compare with.
            _input.Frame.Move = new Vector2(0f, 1f);
            int steps = audio.FootstepsPlayed;
            yield return WaitUntil(() => audio.FootstepsPlayed > steps + 1, 3f, "footsteps while walking");
            float walkVolume = audio.LastFootstepVolume;
            _input.Frame.Move = Vector2.zero;
            yield return StandAt(pc, new Vector3(-9f, 0.1f, -27f));

            _input.Frame.SlowWalk = true;
            _input.Frame.Sprint = true; // held too: slow walking never sprints
            _input.Frame.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(pc.SlowWalking);
            Assert.IsFalse(pc.Sprinting, "no sprint while slow walking");
            Assert.IsTrue(stance.SlowWalk.Value, "the server knows (replicated for bots and footsteps)");
            var p0 = pc.transform.position;
            float t0 = Time.time;
            steps = audio.FootstepsPlayed;
            yield return new WaitForSeconds(1.0f);
            Assert.AreEqual(1.7f, PlanarSpeedOver(p0, pc.transform.position, Time.time - t0), 0.25f, "1.7 m/s (ADR-095)");
            yield return WaitUntil(() => audio.FootstepsPlayed > steps, 3f, "footsteps while slow walking");
            Assert.AreEqual(walkVolume * 0.25f, audio.LastFootstepVolume, 1e-3f, "a quarter of the volume");
            Assert.AreEqual(0.25f, stance.FootstepVolumeFactor, 1e-4f);

            _input.Frame.SlowWalk = false;
            _input.Frame.Sprint = false;
            _input.Frame.Move = Vector2.zero;
            yield return WaitUntil(() => !stance.SlowWalk.Value, 1f, "slow walk off on the server");
        }

        [UnityTest]
        public IEnumerator Space_VaultsOverA0_8mBox_AndLandsOnTheFarSide()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = me.GetComponent<CombatantStance>();
            var animation = me.GetComponent<CombatantAnimation>();
            // A box 0.8 m high and 0.5 m deep across the way, its near face at z = -22.25.
            Box("VaultBox", new Vector3(0f, 0.4f, -22f), new Vector3(3f, 0.8f, 0.5f));
            yield return StandAt(pc, new Vector3(0f, 0.1f, -23.1f));
            float floor = pc.transform.position.y;

            _input.Frame.Jump = true;
            yield return WaitUntil(() => pc.InParkour, 1f, "the vault starts");
            Assert.AreEqual(ParkourMove.Vault, pc.ParkourUnderWay);
            Assert.AreEqual(ParkourMove.Vault, pc.LastParkourPlan.Move);
            Assert.AreEqual(0.8f, pc.LastParkourPlan.Profile.TopHeight, 0.05f, "measured the top");
            float highest = floor, pose = 0f;
            yield return WaitUntil(() =>
            {
                highest = Mathf.Max(highest, pc.transform.position.y);
                pose = Mathf.Max(pose, animation.ParkourPose);
                return !pc.InParkour;
            }, 2f, "the vault ends");
            Assert.Greater(highest - floor, 0.8f, "the feet went over the top");
            Assert.Greater(pose, 0.5f, "the vault pose was shown");
            Assert.AreEqual(ParkourMove.None, stance.ParkourMove, "the replicated state is cleared");
            Assert.Greater(pc.transform.position.z, -21.75f + 0.3f, "on the far side of the box");
            yield return new WaitForSeconds(0.3f);
            Assert.Less(pc.transform.position.y - floor, 0.05f, "down on the floor");
            Assert.Greater(pc.transform.position.z, -21.6f, "still beyond it");
            Assert.AreEqual(1, stance.ParkoursAccepted, "the server believed it");
            Assert.AreEqual(0, stance.ParkoursRejected);
        }

        [UnityTest]
        public IEnumerator S_DuringAVault_ReturnsToTheStart()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = me.GetComponent<CombatantStance>();
            Box("VaultBox", new Vector3(0f, 0.4f, -22f), new Vector3(3f, 0.8f, 0.5f));
            yield return StandAt(pc, new Vector3(0f, 0.1f, -23.1f));
            var start = pc.transform.position;

            _input.Frame.Jump = true;
            yield return WaitUntil(() => pc.InParkour, 1f, "the vault starts");
            // S from the very next frame: slow batch-mode frames would otherwise run past the cancel window.
            _input.Frame.Move = new Vector2(0f, -1f);
            bool wentBack = false;
            var end = Vector3.zero;
            yield return WaitUntil(() =>
            {
                wentBack |= pc.ParkourGoingBack;
                end = pc.transform.position;
                return !pc.InParkour;
            }, 2f, "the vault ends");
            _input.Frame.Move = Vector2.zero;
            Assert.IsTrue(wentBack || pc.LastParkourTakenBack, "S took it back");
            Assert.AreEqual(start.x, end.x, 0.05f);
            Assert.AreEqual(start.z, end.z, 0.05f, "back where it started");
            Assert.AreEqual(start.y, end.y, 0.05f);
            Assert.Less(pc.transform.position.z, -22.25f, "on the near side of the box");
            Assert.AreEqual(1, stance.ParkoursAccepted, "a taken-back move is a valid end too");
        }

        [UnityTest]
        public IEnumerator A2_5mWall_IsRefused()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = me.GetComponent<CombatantStance>();
            Box("HighWall", new Vector3(0f, 1.25f, -22f), new Vector3(3f, 2.5f, 0.5f));
            yield return StandAt(pc, new Vector3(0f, 0.1f, -23.1f));
            float floor = pc.transform.position.y;

            _input.Frame.Jump = true;
            yield return null;
            yield return null;
            Assert.IsFalse(pc.InParkour, "no vault or climb");
            Assert.AreEqual(ParkourMove.Refuse, pc.LastParkourPlan.Move);
            yield return new WaitForSeconds(1f);
            Assert.Less(pc.transform.position.z, -22.25f, "still in front of the wall");
            Assert.Less(pc.transform.position.y - floor, 0.05f, "on the floor");
            Assert.AreEqual(0, stance.ParkoursAccepted);
        }

        [UnityTest]
        public IEnumerator AGlazedWindow_IsVaultedThrough_AndTheGlassShatters_AndAShotBreaksGlassToo()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            // A wall 0.25 m thick across the way with a 1.2 m high opening over a 1.0 m sill, glazed.
            Box("Sill", new Vector3(0f, 0.5f, -22f), new Vector3(3f, 1.0f, 0.25f));
            Box("Lintel", new Vector3(0f, 2.6f, -22f), new Vector3(3f, 0.8f, 0.25f));
            var pane = NewPane(9001, new Vector3(0f, 1.6f, -22f), new Vector3(2.9f, 1.15f, 0.03f));
            yield return StandAt(pc, new Vector3(0f, 0.1f, -23.0f));
            int shown = GlassFx.ShattersShown;
            int noises = BotNoise.Count;

            _input.Frame.Jump = true;
            yield return WaitUntil(() => pc.InParkour, 1f, "the window vault starts");
            Assert.AreEqual(ParkourMove.Window, pc.ParkourUnderWay);
            Assert.AreEqual(1.0f, pc.LastParkourPlan.Profile.TopHeight, 0.05f, "the sill");
            Assert.AreEqual(1.2f, pc.LastParkourPlan.Profile.OpeningHeight, 0.05f, "the opening");
            Assert.AreSame(pane, pc.LastParkourPlan.Glass, "the pane in the way was found");
            yield return WaitUntil(() => pane.Broken, 1f, "the glass breaks");
            yield return WaitUntil(() => GlassFx.ShattersShown > shown, 1f, "shards shown");
            Assert.Greater(GlassFx.CrashesPlayed, 0, "and the crash heard");
            bool heard = false;
            for (int i = BotNoise.FirstUnread(noises); i < BotNoise.Count; i++)
                heard |= BotNoise.At(i).Kind == NoiseKind.Other && Mathf.Abs(BotNoise.At(i).Radius - 40f) < 1e-3f;
            Assert.IsTrue(heard, "a noise for the bots, audible 40 m away");
            yield return WaitUntil(() => !pc.InParkour, 2f, "through");
            Assert.Greater(pc.transform.position.z, -21.6f, "on the far side of the wall");

            // A second pane, shot at from behind the wall: the bullet breaks it.
            var second = NewPane(9002, new Vector3(4f, 1.5f, -20f), new Vector3(1.5f, 1.2f, 0.03f));
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == 3, 2f, "rifle in hand");
            yield return StandAt(pc, new Vector3(4f, 0.1f, -21.5f));
            pc.LookAt(new Vector3(4f, 1.5f, -20f));
            yield return null;
            _input.Frame.FireHeld = true;
            _input.Frame.FirePressed = true;
            yield return WaitUntil(() => second.Broken, 2f, "a shot breaks the pane");
            _input.Frame.FireHeld = false;
            Assert.IsFalse(second.GetComponent<Collider>().enabled, "and it no longer blocks");
        }

        // #108: a house window and a yard wall are got over from outside in as well as from inside out.
        // The house floor is 0.15 m over the ground outside, so its 1.0 m sill is 1.15 m high from outside, and a
        // 1.4 m hall window's lintel is 2.55 m up.
        [UnityTest]
        public IEnumerator AHouseWindow_IsGoneThrough_FromOutsideIn_AndBackOut()
        {
            yield return LoadTestBox();
            var pc = PlayerController.Local;
            var stance = Combatant.LocalPlayer.GetComponent<CombatantStance>();
            Box("Floor", new Vector3(0f, 0.075f, -20.5f), new Vector3(3f, 0.15f, 2.75f));
            Box("Sill", new Vector3(0f, 0.575f, -22f), new Vector3(3f, 1.15f, 0.25f));
            Box("Lintel", new Vector3(0f, 2.95f, -22f), new Vector3(3f, 0.8f, 0.25f));
            yield return StandAt(pc, new Vector3(0f, 0.1f, -22.9f));

            yield return Over(pc, ParkourMove.Window, "in");
            Assert.AreEqual(1.15f, pc.LastParkourPlan.Profile.TopHeight, 0.05f, "the sill from outside");
            Assert.AreEqual(1.4f, pc.LastParkourPlan.Profile.OpeningHeight, 0.05f, "the opening");
            Assert.Greater(pc.transform.position.z, -21.6f, "inside");
            Assert.AreEqual(0.15f, pc.transform.position.y, 0.1f, "on the floor");

            yield return Face(pc, -1f);
            yield return Over(pc, ParkourMove.Window, "out");
            Assert.Less(pc.transform.position.z, -22.4f, "outside again");
            Assert.AreEqual(2, stance.ParkoursAccepted);
        }

        // A yard wall 0.65 m over a raised yard, the ground outside sloping up to its foot: the slope is not the
        // obstacle, the wall is.
        [UnityTest]
        public IEnumerator AYardWall_IsVaulted_FromTheSlopeOutside_AndBack()
        {
            yield return LoadTestBox();
            var pc = PlayerController.Local;
            var stance = Combatant.LocalPlayer.GetComponent<CombatantStance>();
            Box("Yard", new Vector3(0f, 0.2f, -20.5f), new Vector3(3f, 0.4f, 2.75f));
            Box("Wall", new Vector3(0f, 0.525f, -22f), new Vector3(3f, 1.05f, 0.24f));
            // The slope: from the floor at z = -25 up to 0.4 m at the wall.
            float run = 2.88f, rise = 0.4f, angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            var slope = Box("Slope", Vector3.zero, new Vector3(3f, 0.2f, Mathf.Sqrt(run * run + rise * rise)));
            slope.transform.rotation = Quaternion.Euler(-angle, 0f, 0f);
            slope.transform.position = new Vector3(0f, rise / 2f, -22.12f - run / 2f) - slope.transform.up * 0.1f;
            Physics.SyncTransforms();
            yield return StandAt(pc, new Vector3(0f, 0.4f, -23.0f));

            yield return Over(pc, ParkourMove.Vault, "in");
            Assert.Greater(pc.transform.position.z, -21.6f, "in the yard");
            Assert.AreEqual(0.4f, pc.transform.position.y, 0.1f, "on the yard");

            yield return Face(pc, -1f);
            yield return Over(pc, ParkourMove.Vault, "out");
            Assert.Less(pc.transform.position.z, -22.3f, "outside again");
            Assert.AreEqual(2, stance.ParkoursAccepted);
        }

        // #115 (ADR-087): Space at a 2.3 m block jumps (too high to climb from the ground), the hands catch its edge
        // in the air and the body hangs against it; Space again pulls up onto the top.
        [UnityTest]
        public IEnumerator A2_3mBlock_IsCaughtInAJump_HungFrom_AndClimbedOnto()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = me.GetComponent<CombatantStance>();
            var animation = me.GetComponent<CombatantAnimation>();
            yield return HangFrom(pc, 2.3f);
            var hang = pc.transform.position;
            Assert.AreEqual(0.5f, hang.y, 0.1f, "the feet 1.8 m under the edge");
            Assert.AreEqual(-22f - 0.35f - 0.05f, hang.z, 0.05f, "against the wall");
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(pc.OnLedge && pc.ParkourUnderWay == ParkourMove.Hang, "still hanging");
            Assert.Less(Vector3.Distance(hang, pc.transform.position), 0.01f, "held still");
            Assert.AreEqual(ParkourMove.Hang, stance.ParkourMove, "the server shows the hang");
            Assert.Greater(animation.LedgePose, 0.9f, "the hang pose");
            Assert.AreEqual(0, stance.ParkoursRejected);

            _input.Frame.Jump = true;
            yield return WaitUntil(() => pc.ParkourUnderWay == ParkourMove.LedgeClimb, 1f, "the pull-up starts");
            float started = Time.time;
            yield return WaitUntil(() => !pc.InParkour, 2f, "the pull-up ends");
            Assert.That(Time.time - started, Is.InRange(0.5f, 1.1f), "about 0.8 s");
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(2.3f, pc.transform.position.y, 0.1f, "standing on top");
            Assert.Greater(pc.transform.position.z, -21.8f, "on the block");
            Assert.AreEqual(1, stance.ParkoursAccepted, "the server believed it");
            Assert.AreEqual(0, stance.ParkoursRejected);
            Assert.AreEqual(ParkourMove.None, stance.ParkourMove);
        }

        // S while hanging lets go: the body drops to the floor in front of the block and does not catch it again.
        [UnityTest]
        public IEnumerator S_WhileHanging_LetsGo_AndDrops()
        {
            yield return LoadTestBox();
            var pc = PlayerController.Local;
            var stance = Combatant.LocalPlayer.GetComponent<CombatantStance>();
            yield return HangFrom(pc, 2.3f);
            yield return new WaitForSeconds(0.2f);
            _input.Frame.Move = new Vector2(0f, -1f);
            yield return WaitUntil(() => !pc.InParkour, 1f, "let go");
            yield return new WaitForSeconds(0.15f);
            _input.Frame.Move = Vector2.zero;
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(pc.OnLedge, "not caught again");
            Assert.Less(pc.transform.position.y, 0.15f, "down on the floor");
            Assert.Less(pc.transform.position.z, -22.1f, "in front of the block");
            Assert.AreEqual(ParkourMove.None, stance.ParkourMove);
        }

        // A 3 m block is out of reach: Space is a plain jump and the body comes back down in front of it.
        [UnityTest]
        public IEnumerator A3mBlock_IsNotCaught()
        {
            yield return LoadTestBox();
            var pc = PlayerController.Local;
            Box("Block", new Vector3(0f, 1.5f, -21f), new Vector3(3f, 3f, 2f));
            yield return StandAt(pc, new Vector3(0f, 0.1f, -22.6f));
            _input.Frame.Jump = true;
            bool caught = false;
            float end = Time.time + 1.5f;
            while (Time.time < end) { caught |= pc.OnLedge; yield return null; }
            Assert.IsFalse(caught, "no hang");
            Assert.Less(pc.transform.position.y, 0.15f, "on the floor");
            Assert.Less(pc.transform.position.z, -22.1f, "in front of it");
        }

        // A block `height` high, 2 m deep, its near face at z = -22; Space from 0.6 m in front of it until hanging.
        IEnumerator HangFrom(PlayerController pc, float height)
        {
            Box("Block", new Vector3(0f, height / 2f, -21f), new Vector3(3f, height, 2f));
            yield return StandAt(pc, new Vector3(0f, 0.1f, -22.6f));
            _input.Frame.Jump = true;
            yield return WaitUntil(() => pc.OnLedge, 1.5f, "the edge is caught");
            Assert.AreEqual(ParkourMove.Hang, pc.ParkourUnderWay);
            Assert.AreEqual(height, pc.LastParkourPlan.TopY, 0.05f, "the edge");
        }

        IEnumerator Over(PlayerController pc, ParkourMove move, string way)
        {
            _input.Frame.Jump = true;
            yield return null;
            yield return null;
            var plan = pc.LastParkourPlan;
            Assert.AreEqual(move, plan.Move, $"{way}: top {plan.Profile.TopHeight:F2}, depth {plan.Profile.Depth:F2}, opening {plan.Profile.Opening} {plan.Profile.OpeningHeight:F2}");
            yield return WaitUntil(() => !pc.InParkour, 2f, way + " ends");
            yield return new WaitForSeconds(0.3f);
        }

        // Turned to face north (+1) or south (-1) where it stands.
        IEnumerator Face(PlayerController pc, float z)
        {
            var p = pc.transform.position;
            pc.LookAt(new Vector3(p.x, p.y + 1.5f, p.z + 20f * z));
            yield return new WaitForSeconds(0.2f);
        }

        // A pane like the world builder's: thin box on the Ignore Raycast layer.
        static GlassPane NewPane(int id, Vector3 center, Vector3 size)
        {
            var go = Box("Pane" + id, center, size);
            go.layer = GlassPane.Layer;
            var pane = go.AddComponent<GlassPane>();
            pane.SetId(id);
            Physics.SyncTransforms();
            return pane;
        }
    }
}
