using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // M1.6a in the real TestBox scene with injected input: shoulder/sights aiming, recoil, leaning,
    // layered animation, and that a run always hosts its own fresh match.
    public class AimAndLeanTests
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

        IEnumerator Tap()
        {
            _input.Frame.AimHeld = true;
            yield return null;
            yield return null;
            _input.Frame.AimHeld = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator HoldingAim_Shoulders_NarrowsTheView_AndSlowsMovement()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var rules = GameData.Rules.Aim;
            yield return GiveRifle();

            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.3f);
            float z0 = pc.transform.position.z, t0 = Time.time;
            yield return new WaitForSeconds(0.5f);
            float hipSpeed = (pc.transform.position.z - z0) / (Time.time - t0);

            _input.Frame.AimHeld = true;
            yield return WaitUntil(() => me.AimMode == AimMode.Shoulder, 2f, "shouldered on the server");
            yield return WaitUntil(() => Mathf.Abs(pc.Camera.fieldOfView - rules.Shoulder.Fov) < 1f, 2f, "field of view eased to the shoulder value");
            Assert.AreEqual(ViewMode.ThirdPerson, pc.Rig.Mode);
            Assert.Greater(Vector3.Distance(pc.Camera.transform.position, me.EyePosition), 1f, "shouldering stays third person");
            float z1 = pc.transform.position.z, t1 = Time.time;
            yield return new WaitForSeconds(0.5f);
            float aimSpeed = (pc.transform.position.z - z1) / (Time.time - t1);
            Assert.AreEqual(hipSpeed * rules.Shoulder.MoveFactor, aimSpeed, 0.4f, "movement slowed by the stance factor");

            _input.Frame.AimHeld = false;
            yield return WaitUntil(() => me.AimMode == AimMode.Hip, 2f, "released");
            yield return WaitUntil(() => Mathf.Abs(pc.Camera.fieldOfView - rules.Hip.Fov) < 1f, 2f, "field of view back");
        }

        [UnityTest]
        public IEnumerator TappingAim_TogglesSights_AtTheEyes()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            yield return GiveRifle();

            yield return Tap();
            yield return WaitUntil(() => me.AimMode == AimMode.Sights, 2f, "sights on");
            yield return null;
            Assert.Less(Vector3.Distance(pc.Camera.transform.position, me.EyePosition), 0.05f, "sights look from the eyes even in third-person mode");
            yield return WaitUntil(() => Mathf.Abs(pc.Camera.fieldOfView - GameData.Rules.Aim.Sights.Fov) < 1f, 2f, "sights field of view");

            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(AimMode.Sights, me.AimMode, "sights stay on without holding");
            yield return Tap();
            yield return WaitUntil(() => me.AimMode == AimMode.Hip, 2f, "sights off");
        }

        [UnityTest]
        public IEnumerator AimingNeedsAWeapon()
        {
            yield return LoadTestBox();
            _input.Frame.AimHeld = true;
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(AimMode.Hip, Combatant.LocalPlayer.AimMode);
        }

        [UnityTest]
        public IEnumerator Firing_KicksTheViewUp_ThenItSettles()
        {
            yield return LoadTestBox();
            var pc = PlayerController.Local;
            Combatant.LocalPlayer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 4, Amount = 6 }); // shotgun: biggest kick
            yield return WaitUntil(() => Combatant.LocalPlayer.ActiveWeaponId == 4, 2f, "shotgun in hand");
            pc.LookAt(pc.transform.position + pc.transform.forward * 10f + Vector3.up * 1.6f);
            yield return null;
            yield return null;
            float restPitch = pc.Camera.transform.eulerAngles.x;

            _input.Frame.FirePressed = true;
            float kicked = 0f;
            float end = Time.time + 0.5f;
            while (Time.time < end)
            {
                kicked = Mathf.Max(kicked, Mathf.DeltaAngle(pc.Camera.transform.eulerAngles.x, restPitch)); // looking up lowers euler x
                yield return null;
            }
            Assert.Greater(kicked, 2f, "the shot should kick the view up");
            yield return new WaitForSeconds(1.2f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(pc.Camera.transform.eulerAngles.x, restPitch)), 0.2f, "and the view settles back");
        }

        // Largest upward view kick seen in the half second after one shot.
        IEnumerator FireOnceAndMeasureKick(PlayerController pc, Action<float> done)
        {
            yield return new WaitForSeconds(1.5f); // previous kick settled, weapon ready again
            float restPitch = pc.Camera.transform.eulerAngles.x;
            _input.Frame.FirePressed = true;
            float kicked = 0f, end = Time.time + 0.5f;
            while (Time.time < end)
            {
                kicked = Mathf.Max(kicked, Mathf.DeltaAngle(pc.Camera.transform.eulerAngles.x, restPitch));
                yield return null;
            }
            done(kicked);
        }

        [UnityTest]
        public IEnumerator AimingDownSights_KicksLessThanFiringFromTheHip()
        {
            yield return LoadTestBox();
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 4, Amount = 6 });
            yield return WaitUntil(() => me.ActiveWeaponId == 4, 2f, "shotgun in hand");
            pc.LookAt(pc.transform.position + pc.transform.forward * 10f + Vector3.up * 1.6f);

            float hip = 0f, sights = 0f;
            yield return FireOnceAndMeasureKick(pc, k => hip = k);
            yield return Tap();
            yield return WaitUntil(() => me.AimMode == AimMode.Sights, 2f, "sights on");
            yield return FireOnceAndMeasureKick(pc, k => sights = k);

            float factor = GameData.Rules.Aim.Sights.RecoilFactor;
            Assert.Greater(hip, 2f);
            Assert.AreEqual(hip * factor, sights, hip * 0.2f, "sights kick scaled by the stance's recoil factor");
        }

        [UnityTest]
        public IEnumerator Leaning_MovesTheEyesAndTheShot_AndReturnsOnRelease()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var lean = GameData.Rules.Lean;
            yield return GiveRifle();
            float EyeSide() => pc.transform.InverseTransformPoint(me.EyePosition).x;
            Assert.AreEqual(0f, EyeSide(), 0.01f);
            Transform head = RigBones.Bone(me, HumanBodyBones.Head), hips = RigBones.Bone(me, HumanBodyBones.Hips);
            Assert.IsNotNull(head, "Head bone");
            Assert.IsNotNull(hips, "Hips bone");
            float HeadSide() => pc.transform.InverseTransformPoint(head.position).x - pc.transform.InverseTransformPoint(hips.position).x;
            yield return new WaitForSeconds(0.5f); // weapon pose settled
            float uprightHeadSide = HeadSide();

            _input.Frame.LeanLeft = true;
            yield return WaitUntil(() => me.Lean.Value == -1, 2f, "lean left on the server");
            yield return WaitUntil(() => EyeSide() < -lean.OffsetMeters + 0.02f, 2f, "eyes moved left");
            // PT-3: the lean was too much. Small numbers, and the third-person camera neither rolls nor changes shoulder.
            Assert.AreEqual(0.25f, lean.OffsetMeters, 1e-4f);
            Assert.AreEqual(10f, lean.BodyRollDeg, 1e-4f);
            yield return new WaitForSeconds(0.3f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(pc.Camera.transform.eulerAngles.z, 0f)), 0.5f, "third-person camera stays level");
            Assert.Greater(pc.transform.InverseTransformPoint(pc.Camera.transform.position).x, 0.1f, "camera stays over the right shoulder when leaning left");
            // The body leans with the view: the head moves to the left over the hips.
            yield return null; // a frame for LateUpdate to pose the torso
            Assert.That(HeadSide() - uprightHeadSide, Is.InRange(-0.2f, -0.05f), "torso tilted toward the lean, but only a little");

            Vector3 origin = default;
            bool fired = false;
            void OnShot(Vector3 from, Vector3 to) { origin = from; fired = true; }
            me.ShotFired += OnShot;
            _input.Frame.FireHeld = true;
            yield return WaitUntil(() => fired, 2f, "shot while leaning");
            _input.Frame.FireHeld = false;
            me.ShotFired -= OnShot;
            Assert.Less(pc.transform.InverseTransformPoint(origin).x, -lean.OffsetMeters + 0.05f, "the shot starts from the leaned position");

            _input.Frame.LeanLeft = false;
            yield return WaitUntil(() => me.Lean.Value == 0 && Mathf.Abs(EyeSide()) < 0.01f, 2f, "back upright");

            // First person leans too.
            _input.Frame.ToggleView = true;
            yield return null;
            _input.Frame.ToggleView = false;
            _input.Frame.LeanRight = true;
            yield return WaitUntil(() => EyeSide() > lean.OffsetMeters - 0.02f, 2f, "eyes moved right in first person");
            Assert.Less(Vector3.Distance(pc.Camera.transform.position, me.EyePosition), 0.05f);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(6f, Mathf.Abs(Mathf.DeltaAngle(pc.Camera.transform.eulerAngles.z, 0f)), 0.5f, "the eye view rolls a little");
        }

        [UnityTest]
        public IEnumerator Leaning_DoesNotPutTheEyesThroughAWall()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            // Stand with the west wall (inner face at x = -29.5) 0.37 m to the left, facing north: closer than a full lean reaches.
            pc.Teleport(new Vector3(-29.13f, 0.1f, -24f));
            pc.LookAt(new Vector3(-29.13f, 1.6f, 0f));
            yield return null;
            _input.Frame.LeanLeft = true;
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(-1, me.Lean.Value);
            Assert.Greater(me.EyePosition.x, -29.5f + 0.1f, "eyes stay on this side of the wall");
            Assert.Greater(me.EyePosition.x, -29.13f - 0.2f, "the lean was cut short, not just lucky");
            Assert.Less(me.EyePosition.x, -29.13f - 0.02f, "but there is still some lean");
        }

        [UnityTest]
        public IEnumerator MovingSideways_TurnsTheLegs_WhileTheUpperBodyKeepsFacingTheAim()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var bones = new System.Collections.Generic.Dictionary<string, Transform>();
            bones["UpperLeg.L"] = RigBones.Bone(me, HumanBodyBones.LeftUpperLeg); bones["UpperLeg.R"] = RigBones.Bone(me, HumanBodyBones.RightUpperLeg);
            bones["UpperArm.L"] = RigBones.Bone(me, HumanBodyBones.LeftUpperArm); bones["UpperArm.R"] = RigBones.Bone(me, HumanBodyBones.RightUpperArm);
            // Which way a left-right pair of joints faces, in degrees from the player's facing (right positive).
            float Facing(string left, string right)
            {
                var across = pc.transform.InverseTransformDirection(bones[right].position - bones[left].position);
                return Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg;
            }
            // Averaged over a stride: the hips and shoulders sway with every step.
            IEnumerator Average(string left, string right, Action<float> done)
            {
                float sum = 0f;
                int n = 0;
                for (float end = Time.time + 0.8f; Time.time < end; n++)
                {
                    yield return null; // the bones still hold the last frame's finished pose here
                    sum += Facing(left, right);
                }
                done(sum / n);
            }

            yield return GiveRifle();
            float hips = 0f, shoulders = 0f;
            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.6f);
            yield return Average("UpperLeg.L", "UpperLeg.R", a => hips = a);
            Assert.AreEqual(0f, hips, 12f, "running forward: legs face forward");
            // The weapon pose itself holds the shoulders at an angle; that is the reference for "facing the aim".
            float shouldersForward = 0f;
            yield return Average("UpperArm.L", "UpperArm.R", a => shouldersForward = a);

            // The leg turn the code asked for, averaged over the same window as the hips line: the room's walls stop the forward
            // part of the motion, which swings the direction (and the turn) towards the side, so the test compares the two
            // measurements with each other instead of with a fixed angle.
            float turn = 0f;
            IEnumerator AverageTurn(Action<float> done)
            {
                float sum = 0f; int n = 0;
                for (float end = Time.time + 0.8f; Time.time < end; n++) { yield return null; sum += me.GetComponent<CombatantAnimation>().LegTurnDeg; }
                done(sum / n);
            }
            _input.Frame.Move = new Vector2(1, 1);
            yield return new WaitForSeconds(0.6f);
            yield return Average("UpperLeg.L", "UpperLeg.R", a => hips = a);
            yield return AverageTurn(a => turn = a);
            yield return Average("UpperArm.L", "UpperArm.R", a => shoulders = a);
            // ADR-391: forward-right is its own clip; the legs turn only onto the nearest clip direction (a wall can swing it a little).
            Assert.That(Mathf.Abs(turn), Is.LessThan(25f), "forward-right: the diagonal clip, not the forward run turned");
            Assert.AreEqual(shouldersForward, shoulders, 12f, "the shoulders still face the aim");

            _input.Frame.Move = new Vector2(-1, 0);
            yield return new WaitForSeconds(0.6f);
            yield return Average("UpperLeg.L", "UpperLeg.R", a => hips = a);
            yield return AverageTurn(a => turn = a);
            yield return Average("UpperArm.L", "UpperArm.R", a => shoulders = a);
            Assert.That(Mathf.Abs(turn), Is.LessThan(25f), "sideways to the left: the sidestep clip, not the forward run turned (#223)");
            Assert.AreEqual(shouldersForward, shoulders, 12f);

            // With the weapon up it must point where the player aims, moving or not.
            float aimStanding = 0f, aimMoving = 0f;
            _input.Frame.Move = Vector2.zero;
            _input.Frame.AimHeld = true;
            yield return new WaitForSeconds(0.8f);
            yield return Average("UpperArm.L", "UpperArm.R", a => aimStanding = a);
            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.6f);
            yield return Average("UpperArm.L", "UpperArm.R", a => aimMoving = a);
            Assert.AreEqual(aimStanding, aimMoving, 8f, "aiming while moving forward: same shoulder line as standing");
            _input.Frame.Move = new Vector2(-1, 0);
            yield return new WaitForSeconds(0.6f);
            yield return Average("UpperArm.L", "UpperArm.R", a => aimMoving = a);
            Assert.AreEqual(aimStanding, aimMoving, 8f, "aiming while sidestepping: same shoulder line as standing");
            _input.Frame.AimHeld = false;

            _input.Frame.Move = new Vector2(0, -1);
            yield return new WaitForSeconds(0.6f);
            Assert.Less(me.GetComponent<CombatantAnimation>().MoveDrive, -2f, "backing off: the back-pedal clip, not a turn-around");
            yield return Average("UpperLeg.L", "UpperLeg.R", a => hips = a);
            Assert.AreEqual(0f, hips, 12f);
        }

        [UnityTest]
        public IEnumerator UpperBodyPose_FollowsTheSituation()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var animation = me.GetComponent<CombatantAnimation>();

            _input.Frame.Move = new Vector2(1, 1);
            yield return WaitUntil(() => animation.MoveDrive > 3f, 2f, "moving");
            Assert.IsNull(animation.UpperState, "empty hands: plain run");

            yield return GiveRifle();
            yield return WaitUntil(() => animation.UpperState == CombatantAnimation.Lowered, 2f, "armed and moving: weapon lowered");

            _input.Frame.AimHeld = true;
            yield return WaitUntil(() => animation.UpperState == CombatantAnimation.Aim, 2f, "aiming: weapon raised");
            _input.Frame.AimHeld = false;
            yield return WaitUntil(() => animation.UpperState == CombatantAnimation.Lowered, 2f, "released: lowered again");

            _input.Frame.Move = Vector2.zero;
            _input.Frame.FirePressed = true;
            _input.Frame.FireHeld = true;
            yield return WaitUntil(() => animation.UpperState == CombatantAnimation.Shoot || animation.UpperState == CombatantAnimation.Aim, 2f, "firing raises the weapon");
            _input.Frame.FireHeld = false;
            yield return WaitUntil(() => animation.UpperState == CombatantAnimation.Lowered, 3f, "and it comes down a moment after the last shot");
        }

        // WEAPON_MOTION_AUDIT #1: swapping guns, the old one lowers in the hand before the new one is drawn (it used to vanish in one frame).
        [UnityTest]
        public IEnumerator SwappingGuns_LowersTheOldOne_ThenDrawsTheNew()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var animation = me.GetComponent<CombatantAnimation>();
            yield return GiveRifle();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 15 });
            yield return new WaitForSeconds(1f);
            int pistolSlot = -1;
            for (int s = 0; s < 5; s++) if (me.WeaponInSlot(s) == 1) pistolSlot = s;
            if (me.ActiveWeaponId == 1) { for (int s = 0; s < 5; s++) if (me.WeaponInSlot(s) == 3) me.ServerSelectSlot(s); yield return new WaitForSeconds(1f); }
            Assert.AreEqual(3, me.ActiveWeaponId, "rifle in hand");
            me.ServerSelectSlot(pistolSlot);
            yield return null;
            Assert.AreEqual(1, me.ActiveWeaponId, "the pistol is the active weapon at once");
            Assert.AreEqual(3, animation.HeldId, "but the rifle is still in the hand");
            Assert.Less(animation.HolsterProgress, 1f, "being lowered");
            yield return WaitUntil(() => animation.HeldId == 1, 1f, "then the pistol is in the hand");
            Assert.Less(animation.DrawProgress, 1f, "being drawn");
            yield return WaitUntil(() => animation.DrawProgress >= 1f, 1f, "drawn within its draw time");
        }

        [UnityTest]
        public IEnumerator Jumping_ShowsTheAirPose_UntilLanding_ThenSoundsTheLanding()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var animation = me.GetComponent<CombatantAnimation>();
            var motion = me.GetComponent<CombatantMotion>();
            var audio = me.GetComponent<CombatantAudio>();
            yield return new WaitForSeconds(0.3f); // settled on the floor
            Assert.IsFalse(motion.Airborne, "standing on the ground");
            Assert.AreEqual(CombatantAnimation.Locomotion, animation.BaseState);
            float ground = pc.transform.position.y;
            int steps = audio.FootstepsPlayed;
            // The knee is where the lower leg starts.
            Transform knee = RigBones.Bone(me, HumanBodyBones.LeftLowerLeg), hips = RigBones.Bone(me, HumanBodyBones.Hips);
            Assert.IsNotNull(knee, "knee bone");
            Assert.IsNotNull(hips, "hips bone");
            float standingKnee = knee.position.y - hips.position.y;

            _input.Frame.Jump = true;
            yield return WaitUntil(() => animation.BaseState == CombatantAnimation.Air, 1f, "air pose after take-off");
            // The top of the jump is where a speed-based check would lose track.
            yield return WaitUntil(() => motion.VerticalSpeed <= 0f, 2f, "top of the jump");
            Assert.Greater(pc.transform.position.y - ground, 0.5f, "actually up in the air");
            Assert.IsTrue(motion.Airborne, "still airborne at the top");
            Assert.AreEqual(CombatantAnimation.Air, animation.BaseState);
            Assert.AreEqual(0, audio.LandingsPlayed);
            // The jump is clips (ADR-537), not the code tuck and dip: the airborne loop at the top, the landing clip after.
            Assert.AreEqual(CombatantAnimation.JumpLoop, animation.BaseShown, "the airborne loop clip at the top");
            Assert.Less(animation.LandDip, 0.05f, "no code dip on top of the clips");

            bool landClip = false;
            yield return WaitUntil(() =>
            {
                landClip |= animation.BaseShown == CombatantAnimation.JumpLand;
                return animation.BaseState == CombatantAnimation.Locomotion;
            }, 2f, "back to normal after landing");
            Assert.Less(pc.transform.position.y - ground, 0.1f);
            Assert.AreEqual(1, audio.LandingsPlayed, "one landing sound");
            Assert.IsTrue(landClip, "the landing clip played");
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(knee.position.y - hips.position.y, standingKnee, 0.05f, "and the knees are back where they were");
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(steps, audio.FootstepsPlayed, "a standing jump makes no footsteps besides the landing");
            Assert.AreEqual(1, audio.LandingsPlayed);

            // A running jump: the legs are not stepping while off the ground. A hit in mid-air must not lose the landing.
            pc.LookAt(pc.transform.position + Vector3.right * 10f + Vector3.up * 1.6f);
            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.5f);
            _input.Frame.Jump = true;
            yield return WaitUntil(() => motion.Airborne, 1f, "off the ground");
            steps = audio.FootstepsPlayed;
            me.ServerTakeDamage(5);
            yield return WaitUntil(() => !motion.Airborne, 2f, "down again");
            Assert.AreEqual(steps, audio.FootstepsPlayed, "no footsteps in the air");
            yield return WaitUntil(() => audio.LandingsPlayed == 2, 1f, "landing sound even though the flinch was playing");
            _input.Frame.Move = Vector2.zero;
            yield return WaitUntil(() => animation.BaseState == CombatantAnimation.Locomotion, 2f, "back to normal");

            // The other combatants use the same parts: a dummy on the floor is not airborne.
            foreach (var other in Combatant.All)
                if (other != me) Assert.IsFalse(other.GetComponent<CombatantMotion>().Airborne, "dummy standing on the floor");
        }

        [UnityTest]
        public IEnumerator LookingDownTheSights_ShowsTheWeaponInFrontOfTheCamera_AndItReturnsToTheHand()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var weapon = me.GetComponent<WeaponVisual>();
            yield return GiveRifle();
            yield return WaitUntil(() => weapon.ShownWeaponId == 3, 2f, "rifle model shown");
            Renderer rifle = null;
            Transform wrist = RigBones.Bone(me, HumanBodyBones.RightHand);
            foreach (var t in me.GetComponentsInChildren<Transform>())
                if (t.name == "Weapon3") rifle = t.GetComponentInChildren<Renderer>();
            Assert.IsNotNull(rifle, "rifle renderer");
            yield return null;
            Assert.IsFalse(weapon.AtEye, "third person: the weapon is in the hand");

            yield return Tap();
            yield return WaitUntil(() => me.AimMode == AimMode.Sights && weapon.AtEye, 2f, "sights on, weapon at the eye");
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(rifle.enabled && rifle.gameObject.activeInHierarchy, "the weapon stays visible while the body is hidden");
            // ADR-154 (3): the sight line sits at the screen centre, so the weapon hangs from the aim point down
            // (its middle can even fall below the screen); what must hold is that its top, the sights, is just under the aim point.
            var onScreen = pc.Camera.WorldToViewportPoint(rifle.bounds.center);
            var inCamera = pc.Camera.transform.InverseTransformPoint(rifle.bounds.center);
            float sightsHeight = inCamera.y + rifle.bounds.extents.y;
            Assert.Greater(onScreen.z, 0.1f, "in front of the camera");
            Assert.AreEqual(0.5f, onScreen.x, 0.05f, "centred under the aim point");
            Assert.That(sightsHeight, Is.InRange(-0.12f, 0.02f), "the sights sit just below the aim point (metres in camera space)");

            yield return Tap();
            yield return WaitUntil(() => me.AimMode == AimMode.Hip && !weapon.AtEye, 2f, "sights off");
            yield return null;
            Assert.Less(Vector3.Distance(rifle.bounds.center, wrist.position), 0.6f, "back in the hand");

            // First person from the hip: visible too, off to the right.
            _input.Frame.ToggleView = true;
            yield return null;
            _input.Frame.ToggleView = false;
            yield return WaitUntil(() => weapon.AtEye, 2f, "first person shows the weapon");
            yield return new WaitForSeconds(0.3f);
            onScreen = pc.Camera.WorldToViewportPoint(rifle.bounds.center);
            Assert.Greater(onScreen.x, 0.55f, "held to the right from the hip");
            Assert.Less(onScreen.y, 0.5f);
        }

        [UnityTest]
        public IEnumerator StartingWhileAnotherProcessHoldsThePort_StillHostsItsOwnFreshMatch()
        {
            // Stand in for a game that is still running: something already bound to the default port.
            using (var occupied = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                const ushort defaultPort = 7770;
                occupied.Bind(new IPEndPoint(IPAddress.Any, defaultPort));

                yield return LoadTestBox();
                var director = MatchDirector.Instance;
                Assert.AreNotEqual(defaultPort, director.Port, "must not reuse a port someone else is serving");
                Assert.IsTrue(Combatant.LocalPlayer.IsServerInitialized, "this process is the server of its own match");
                yield return WaitUntil(() => Combatant.All.Count == 1 + director.DummySpawns.Length, 5f, "a fresh match: one player plus the dummies");
                Assert.AreEqual(System.Linq.Enumerable.Sum(GameData.Loot.Plan(director.LootSpawns.Length, director.Seed), r => Loadout.SpawnEntries(GameData.Weapons, r).Length), LootItem.All.Count, "all loot still on the ground (ADR-136: guns come with ammo)");
            }
        }
    }
}
