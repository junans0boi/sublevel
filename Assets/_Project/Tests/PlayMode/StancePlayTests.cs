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
    // ADR-053 in the real TestBox scene with injected input: crouching (eyes, capsule, speed, pose, head room),
    // fall damage, sprint against firing, free look and the map key.
    public class StancePlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = Frame.Crouch = Frame.Prone = Frame.Map = false;
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

        static CombatantStance StanceOf(Combatant combatant)
        {
            var stance = combatant.GetComponent<CombatantStance>();
            Assert.IsNotNull(stance, "the combatant prefab needs a CombatantStance component");
            return stance;
        }

        IEnumerator Crouch(CombatantStance stance)
        {
            _input.Frame.Crouch = true;
            yield return WaitUntil(() => stance.Current == Stance.Crouch && stance.CrouchAmount >= 0.999f, 2f, "fully crouched");
            yield return null;
        }

        // A box in the level for this test only; it goes away with the scene.
        static void Box(string name, Vector3 center, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = center;
            box.transform.localScale = size;
            Physics.SyncTransforms();
        }

        static float EyeHeight(Combatant c) => c.EyePosition.y - c.transform.position.y;

        [UnityTest]
        public IEnumerator Crouching_LowersTheServersEyes_ShrinksTheCapsule_SlowsMovement_AndBendsTheKnees()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = StanceOf(me);
            var body = me.GetComponent<CharacterController>();
            var animation = me.GetComponent<CombatantAnimation>();
            Transform hips = RigBones.Bone(me, HumanBodyBones.Hips), knee = RigBones.Bone(me, HumanBodyBones.LeftLowerLeg);
            Assert.IsNotNull(hips, "Hips bone");
            Assert.IsNotNull(knee, "knee bone");
            yield return new WaitForSeconds(0.5f); // settled on the floor
            Assert.IsTrue(me.IsServerInitialized, "the host's combatant is the server's");
            Assert.AreEqual(Stance.Stand, stance.Current);
            Assert.AreEqual(1.6f, EyeHeight(me), 0.01f);
            Assert.AreEqual(1.8f, body.height, 1e-3f);
            Assert.AreEqual(1f, me.StanceSpreadFactor, 1e-4f);
            Assert.AreEqual(1f, stance.FootstepVolumeFactor, 1e-4f);
            float standingHips = hips.position.y - pc.transform.position.y;
            float standingKnee = knee.position.y - hips.position.y;
            float standingCamera = pc.Camera.transform.position.y;

            yield return Crouch(stance);
            Assert.AreEqual(Stance.Crouch, pc.BodyStance);
            Assert.AreEqual(0.95f, EyeHeight(me), 0.01f, "the server's eye position is 0.65 m lower");
            Assert.AreEqual(1.0f, body.height, 1e-3f);
            Assert.AreEqual(0.5f, body.center.y, 1e-3f, "the feet stay where they were");
            Assert.AreEqual(0.5f, me.StanceSpreadFactor, 1e-4f);
            Assert.AreEqual(0.4f, stance.FootstepVolumeFactor, 1e-4f);
            yield return null; // a frame for LateUpdate to pose the legs and for the camera to follow
            yield return null;
            Assert.AreEqual(1f, animation.CrouchPose, 0.01f);
            yield return new WaitForSeconds(0.4f); // the code bend fades out as the clip takes over
            // Empty hands crouch with the UAL crouch clip (ADR-235): the model is not lowered in code, the clip's own hips are low.
            Assert.Less(animation.ModelDrop, 0.03f, "the clip crouches, the model is not dropped");
            Assert.Greater(standingHips - (hips.position.y - pc.transform.position.y), 0.3f, "the hips came down");
            Assert.Less(hips.position.y - pc.transform.position.y, 0.65f, "the hips are low (the UAL crouch kneels)");
            Assert.Less(knee.position.y - pc.transform.position.y, 0.4f, "the legs are folded: the knee is close to the floor");
            Assert.AreEqual(0.65f, standingCamera - pc.Camera.transform.position.y, 0.1f, "the camera comes down with the eyes");

            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.3f);
            float z0 = pc.transform.position.z, t0 = Time.time;
            yield return new WaitForSeconds(0.5f);
            float speed = (pc.transform.position.z - z0) / (Time.time - t0);
            float walkHips = standingHips - (hips.position.y - pc.transform.position.y);
            _input.Frame.Move = Vector2.zero;
            Assert.Greater(walkHips, 0.25f, "crouch-walking, the hips stay low");
            Assert.AreEqual(4.7f * 0.745f, speed, 0.3f, "walking 4.7 m/s with empty hands times the crouch factor 0.745 (ADR-095)");

            _input.Frame.Crouch = true;
            yield return WaitUntil(() => stance.Current == Stance.Stand && stance.CrouchAmount <= 0.001f, 2f, "standing again");
            Assert.AreEqual(1.6f, EyeHeight(me), 0.01f);
            Assert.AreEqual(1.8f, body.height, 1e-3f);
            yield return null;
            yield return null;
            Assert.Less(animation.ModelDrop, 0.01f);
        }

        [UnityTest]
        public IEnumerator Jumping_GetsUpFromACrouch_SprintingCrouchSprints()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = StanceOf(me);
            yield return new WaitForSeconds(0.3f);
            float ground = pc.transform.position.y;

            yield return Crouch(stance);
            _input.Frame.Jump = true;
            float highest = ground;
            yield return WaitUntil(() =>
            {
                highest = Mathf.Max(highest, pc.transform.position.y);
                return stance.Current == Stance.Stand && stance.CrouchAmount <= 0.001f;
            }, 2f, "the jump key stands up");
            Assert.Less(highest - ground, 0.2f, "and does not jump");

            yield return Crouch(stance);
            _input.Frame.Sprint = true;
            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(Stance.Crouch, stance.Current, "Shift crouched sprints crouched (ADR-095)");
            Assert.IsTrue(pc.Sprinting);
            Assert.AreEqual(4.8f, me.PlanarSpeed, 0.4f, "crouch-sprint speed, empty hands");
        }

        // #105 (PUBG): C mid-sprint crouch-walks on with Shift still held, C again stands and runs; the inventory
        // keeps the held keys going while the pause menu stops the body.
        [UnityTest]
        public IEnumerator CrouchWhileSprinting_KeepsMoving_AndTheInventoryKeepsTheMovementKeys()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = StanceOf(me);
            yield return new WaitForSeconds(0.3f);
            // Running east along the spawn area, south of its sight blocker.
            pc.Teleport(new Vector3(-10f, 0.1f, -20f));
            pc.LookAt(new Vector3(20f, 1.6f, -20f));
            yield return new WaitForSeconds(0.2f);

            _input.Frame.Sprint = true;
            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(pc.Sprinting);
            _input.Frame.Crouch = true;
            yield return null;
            yield return null;
            float x0 = pc.transform.position.x, t0 = Time.time;
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(Stance.Crouch, stance.Current, "C with Shift held crouches and stays down");
            Assert.IsFalse(pc.Sprinting);
            Assert.Greater((pc.transform.position.x - x0) / (Time.time - t0), 2f, "and keeps crouch-walking");

            _input.Frame.Crouch = true;
            yield return null;
            yield return null;
            x0 = pc.transform.position.x; t0 = Time.time;
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(Stance.Stand, stance.Current, "C again stands up on the move");
            Assert.IsTrue(pc.Sprinting);
            Assert.Greater((pc.transform.position.x - x0) / (Time.time - t0), 4f, "running again");

            _input.Frame.Sprint = false;
            float yaw = pc.Yaw;
            UiFocus.Set(UiScreen.Inventory);
            _input.Frame.Look = new Vector2(5f, 0f);
            _input.Frame.FireHeld = true;
            _input.Frame.Crouch = true;
            yield return null;
            _input.Frame.Look = Vector2.zero;
            _input.Frame.FireHeld = false;
            x0 = pc.transform.position.x; t0 = Time.time;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater((pc.transform.position.x - x0) / (Time.time - t0), 2f, "W keeps walking over the inventory");
            Assert.AreEqual(Stance.Crouch, stance.Current, "C crouches over the inventory");
            Assert.AreEqual(yaw, pc.Yaw, 1e-3f, "the mouse does not turn the view");

            UiFocus.Set(UiScreen.Menu);
            yield return new WaitForSeconds(0.15f); // a run takes about 0.1 s to stop (#111, ADR-083)
            x0 = pc.transform.position.x;
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(x0, pc.transform.position.x, 0.05f, "the pause menu holds the body still");
            UiFocus.Close();
        }

        [UnityTest]
        public IEnumerator Crouched_PassesA1_2mGap_AndCannotStandUpUnderIt()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = StanceOf(me);
            // A slab across the way north of the player, its underside 1.2 m above the floor, from z = -20 to -18.
            // (Behind the spawn blocker, where no dummy sees.)
            Box("LowSlab", new Vector3(3f, 1.7f, -19f), new Vector3(4f, 1f, 2f));
            pc.Teleport(new Vector3(3f, 0.1f, -22f));
            pc.LookAt(new Vector3(3f, 1.7f, 0f));
            yield return new WaitForSeconds(0.2f);

            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(1.2f);
            Assert.Less(pc.transform.position.z, -20.2f, "standing: stopped by the slab");

            _input.Frame.Move = Vector2.zero;
            yield return Crouch(stance);
            _input.Frame.Move = new Vector2(0, 1);
            yield return WaitUntil(() => pc.transform.position.z > -17.5f, 4f, "crouched: through under the slab");
            _input.Frame.Move = Vector2.zero;
            Assert.AreEqual(Stance.Crouch, stance.Current);

            // Under the slab there is no room to stand: the crouch key, the jump key and sprinting all leave it as it is.
            pc.Teleport(new Vector3(3f, 0.1f, -19f));
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(stance.CanStand(), "the slab is right overhead");
            float floor = pc.transform.position.y;
            _input.Frame.Crouch = true;
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(Stance.Crouch, stance.Current, "crouch key under the slab");
            Assert.AreEqual(Stance.Crouch, pc.BodyStance);
            _input.Frame.Jump = true;
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(Stance.Crouch, stance.Current, "jump key under the slab");
            Assert.Less(pc.transform.position.y - floor, 0.1f, "and no jump into the slab either");
            Assert.AreEqual(1.0f, me.GetComponent<CharacterController>().height, 1e-3f);

            // Sprinting crouched (ADR-095): the sprint key does not stand up; out from under the slab only the crouch key does.
            _input.Frame.Sprint = true;
            _input.Frame.Move = new Vector2(0, 1);
            yield return WaitUntil(() => pc.transform.position.z > -17.5f, 4f, "sprints crouched out from under the slab");
            Assert.AreEqual(Stance.Crouch, stance.Current, "sprint key does not stand up");
            _input.Frame.Sprint = false;
            _input.Frame.Move = Vector2.zero;
            _input.Frame.Crouch = true;
            yield return WaitUntil(() => stance.Current == Stance.Stand, 3f, "stands up once out from under the slab");
        }

        [UnityTest]
        public IEnumerator CrouchedBehindLowCover_IsNotSeenByTheDummy()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = StanceOf(me);
            var director = MatchDirector.Instance;
            yield return WaitUntil(() => Combatant.All.Count == 1 + director.DummySpawns.Length, 5f, "dummies spawned");
            // The west dummy stands at (-15, 0, 0); the other two are behind the middle and north blockers from here.
            Combatant dummy = null;
            foreach (var c in Combatant.All)
                if (!c.IsPlayer && (c.transform.position - new Vector3(-15f, 0f, 0f)).sqrMagnitude < 1f) dummy = c;
            Assert.IsNotNull(dummy, "the dummy at (-15, 0, 0)");
            int dummyShots = 0;
            void OnDummyShot(Vector3 from, Vector3 to) => dummyShots++;
            dummy.ShotFired += OnDummyShot;

            // 10 m south of the dummy, with a 1.1 m high cover between, right in front of the player.
            Box("LowCover", new Vector3(-15f, 0.55f, -9.4f), new Vector3(2f, 1.1f, 0.2f));
            pc.Teleport(new Vector3(-15f, 0.1f, -10f));
            pc.LookAt(new Vector3(-15f, 1.6f, 0f));

            // Standing, the chest shows over the cover: the dummy sees the player and shoots.
            yield return WaitUntil(() => dummyShots > 0, 5f, "standing behind low cover: the dummy opens fire");
            yield return WaitUntil(() => me.Hp.Value < 100f, 10f, "and hits over the cover");

            yield return Crouch(stance);
            yield return new WaitForSeconds(0.1f);
            Assert.IsFalse(me.Dead.Value, "still alive to hide");
            float hp = me.Hp.Value;
            dummyShots = 0;
            yield return new WaitForSeconds(3f); // five of the dummy's shot intervals
            dummy.ShotFired -= OnDummyShot;
            Assert.AreEqual(0, dummyShots, "crouched behind the cover: the dummy does not see the player");
            Assert.AreEqual(hp, me.Hp.Value, "and nothing hits");
        }

        [UnityTest]
        public IEnumerator ASixMetreFall_Hurts_WithCauseFall_AndAShortDropDoesNot()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = StanceOf(me);
            var motion = me.GetComponent<CombatantMotion>();
            yield return new WaitForSeconds(0.3f);
            float ground = pc.transform.position.y;
            Assert.AreEqual(100f, me.Hp.Value);

            // A drop below the safe 3 m.
            pc.Teleport(new Vector3(3f, ground + 2.5f, -24f));
            yield return WaitUntil(() => motion.Airborne, 1f, "in the air");
            yield return WaitUntil(() => !motion.Airborne, 3f, "down again");
            yield return new WaitForSeconds(1.2f); // longer than the server waits to settle a report
            Assert.AreEqual(0, stance.FallsHurt, "2.5 m does not hurt");
            Assert.AreEqual(100f, me.Hp.Value);

            // Six metres: (6 - 3) / (12 - 3) of 100 = 33.3 damage.
            pc.Teleport(new Vector3(3f, ground + 6f, -24f));
            yield return WaitUntil(() => stance.FallsHurt == 1, 4f, "fall damage applied on the server");
            Assert.AreEqual(6f, stance.LastFallMeters, 0.2f);
            Assert.AreEqual(DamageCause.Fall, stance.LastFall.Cause);
            Assert.IsNull(stance.LastFall.Attacker);
            Assert.AreEqual(33.3f, stance.LastFall.Amount, 2.5f);
            yield return WaitUntil(() => me.Hp.Value < 100f, 1f, "health went down");
            Assert.AreEqual(66.7f, me.Hp.Value, 2.5f);
            Assert.IsFalse(me.Dead.Value);
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(1, stance.FallsHurt, "one fall, one damage");
        }

        [UnityTest]
        public IEnumerator Firing_DropsOutOfTheSprint_AndSprintingResumesAfterwards()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == 3, 2f, "rifle in hand");
            // Along the south wall, heading east past the parked car (z = -27): out of every dummy's sight range.
            pc.Teleport(new Vector3(-25f, 0.1f, -29f));
            pc.LookAt(new Vector3(25f, 1.7f, -29f));
            int shots = 0;
            void OnShot(Vector3 from, Vector3 to) => shots++;
            me.ShotFired += OnShot;

            _input.Frame.Move = new Vector2(0, 1);
            _input.Frame.Sprint = true;
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(pc.Sprinting);
            float x0 = pc.transform.position.x, t0 = Time.time;
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(6.3f * GameData.Rules.Locomotion.RifleMoveFactor, (pc.transform.position.x - x0) / (Time.time - t0), 0.6f, "sprint speed with a rifle");
            Assert.AreEqual(0, shots);

            _input.Frame.FireHeld = true;
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(pc.Sprinting, "the trigger ends the sprint, with the sprint key still held");
            x0 = pc.transform.position.x;
            t0 = Time.time;
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(4.7f * GameData.Rules.Locomotion.RifleMoveFactor, (pc.transform.position.x - x0) / (Time.time - t0), 0.6f, "firing at walking speed");
            Assert.Greater(shots, 0, "and the weapon fires");

            _input.Frame.FireHeld = false;
            me.ShotFired -= OnShot;
            yield return WaitUntil(() => pc.Sprinting, 1f, "sprinting again after letting go of the trigger");
        }

        [UnityTest]
        public IEnumerator FreeLook_TurnsTheView_NotTheCharacter_AndSwingsBack()
        {
            yield return LoadTestBox();
            var pc = PlayerController.Local;
            yield return new WaitForSeconds(0.2f);
            float yaw = pc.Yaw;
            float bodyYaw = pc.transform.eulerAngles.y;
            float cameraYaw = pc.Camera.transform.eulerAngles.y;

            _input.Frame.FreeLook = true;
            _input.Frame.Look = new Vector2(4f, 0f);
            for (int i = 0; i < 10; i++) yield return null;
            _input.Frame.Look = Vector2.zero;
            yield return null;
            yield return null;
            Assert.IsTrue(pc.FreeLooking);
            Assert.AreEqual(yaw, pc.Yaw, 1e-3f, "the character keeps its facing");
            Assert.AreEqual(0f, Mathf.DeltaAngle(bodyYaw, pc.transform.eulerAngles.y), 1e-2f);
            float turned = Mathf.DeltaAngle(cameraYaw, pc.Camera.transform.eulerAngles.y);
            Assert.Greater(turned, 30f, "the camera turned");
            Assert.AreEqual(pc.ViewYaw - pc.Yaw, turned, 0.5f);

            _input.Frame.FreeLook = false;
            yield return WaitUntil(() => Mathf.Abs(pc.ViewYaw - pc.Yaw) < 0.01f, 1f, "the view swings back");
            Assert.IsFalse(pc.FreeLooking);
            Assert.AreEqual(yaw, pc.Yaw, 1e-3f);

            // Without the key the mouse turns the character as before.
            _input.Frame.Look = new Vector2(4f, 0f);
            yield return null;
            yield return null;
            _input.Frame.Look = Vector2.zero;
            yield return null;
            Assert.Greater(Mathf.DeltaAngle(yaw, pc.Yaw), 3f);
        }

        // The map screen only exists in scenes with a WorldInfo (World), so in TestBox the key does nothing
        // (MapScreenTests covers the World scene).
        [UnityTest]
        public IEnumerator TheMapKey_DoesNothing_UntilAMapScreenExists()
        {
            yield return LoadTestBox();
            Assert.AreEqual(UiScreen.None, UiFocus.Current);
            _input.Frame.Map = true;
            yield return null;
            yield return null;
            Assert.AreEqual(UiScreen.None, UiFocus.Current);
            _input.Frame.Map = true;
            yield return null;
            yield return null;
            Assert.AreEqual(UiScreen.None, UiFocus.Current);
        }
    }
}
