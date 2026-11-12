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
                Frame.FirePressed = false;
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

            _input.Frame.LeanLeft = true;
            yield return WaitUntil(() => me.Lean.Value == -1, 2f, "lean left on the server");
            yield return WaitUntil(() => EyeSide() < -lean.OffsetMeters + 0.02f, 2f, "eyes moved left");
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(pc.Camera.transform.eulerAngles.z, 0f)), lean.CameraRollDeg - 1f, "camera rolled");
            // The body leans with the view: the head ends up well to the left of the hips.
            Transform head = null, hips = null;
            foreach (var t in me.GetComponentsInChildren<Transform>())
            {
                if (t.name == "Head") head = t;
                else if (t.name == "Hips") hips = t;
            }
            Assert.IsNotNull(head, "Head bone");
            Assert.IsNotNull(hips, "Hips bone");
            yield return null; // a frame for LateUpdate to pose the torso
            float headSide = pc.transform.InverseTransformPoint(head.position).x - pc.transform.InverseTransformPoint(hips.position).x;
            Assert.Less(headSide, -0.08f, "torso tilted toward the lean");

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
        }

        [UnityTest]
        public IEnumerator Leaning_DoesNotPutTheEyesThroughAWall()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            // Stand with the west wall (inner face at x = -29.5) 0.45 m to the left, facing north.
            pc.Teleport(new Vector3(-29.05f, 0.1f, -24f));
            pc.LookAt(new Vector3(-29.05f, 1.6f, 0f));
            yield return null;
            _input.Frame.LeanLeft = true;
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(-1, me.Lean.Value);
            Assert.Greater(me.EyePosition.x, -29.5f + 0.1f, "eyes stay on this side of the wall");
        }

        [UnityTest]
        public IEnumerator DiagonalMovement_BlendsBothDirections_AndTheUpperBodyFollowsTheSituation()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var animation = me.GetComponent<CombatantAnimation>();

            _input.Frame.Move = new Vector2(1, 1);
            yield return WaitUntil(() => animation.MoveBlend.x > 1.5f && animation.MoveBlend.y > 1.5f, 2f, "both blend axes driven when moving diagonally");
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
                Assert.AreEqual(director.LootSpawns.Length, LootItem.All.Count, "all loot still on the ground");
            }
        }
    }
}
