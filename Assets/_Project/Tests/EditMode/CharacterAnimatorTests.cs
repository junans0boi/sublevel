using System.Linq;
using Game.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Tests
{
    // Animation track (ADR-188, ADR-191): the generated controller on the Humanoid roster, judged by joint heights and angles
    // read from the posed skeleton (Animator.Update in edit mode), not by clip names or still images.
    // Needs CharacterImport.SyncStaging, CharacterRoster.Build and TestBoxBuilder.Build.
    public class CharacterAnimatorTests
    {
        const string ControllerPath = "Assets/_Project/Prefabs/M1_5/Character.controller";
        const int Base = 0, Upper = 1, Action = 2, Additive = 3;
        GameObject _go;
        CharacterDef _def;
        Animator _animator;

        static AnimatorController Controller()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(controller, "run TestBoxBuilder.Build to generate " + ControllerPath);
            return controller;
        }

        [SetUp]
        public void Spawn()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Roster/Roster_MixamoSwat.prefab");
            Assert.IsNotNull(prefab, "run CharacterImport.SyncStaging and CharacterRoster.Build");
            _go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _def = _go.GetComponent<CharacterDef>();
            _animator = _def.Animator;
            _animator.runtimeAnimatorController = Controller();
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.Rebind();
        }

        [TearDown]
        public void Despawn() => Object.DestroyImmediate(_go);

        // Pose the animator: play `state` on `layer` at normalized time t, with the given layer weights, and evaluate.
        void Pose(int layer, string state, float t, float upper = 0f, float action = 0f, float additive = 1f)
        {
            _animator.SetLayerWeight(Upper, upper);
            _animator.SetLayerWeight(Action, action);
            _animator.SetLayerWeight(Additive, additive);
            _animator.Play(state, layer, t);
            _animator.Update(0f);
        }

        Vector3 Local(Transform bone) => _go.transform.InverseTransformPoint(bone.position);
        HumanoidSockets S => _def.Sockets;

        // Trunk lean from vertical in the vertical-forward plane, degrees: 0 upright, 90 lying head-forward.
        float TrunkPitch() { var d = Local(S.Chest) - Local(S.Hips); return Mathf.Atan2(d.z, d.y) * Mathf.Rad2Deg; }

        [Test]
        public void Controller_HasFourLayers_BaseUpperActionAdditive()
        {
            var layers = Controller().layers;
            CollectionAssert.AreEqual(new[] { "Base", "Upper", "Action", "Additive" }, layers.Select(l => l.name));
            Assert.AreEqual(AnimatorLayerBlendingMode.Additive, layers[Additive].blendingMode);
            Assert.AreEqual(0f, layers[Upper].defaultWeight);
            Assert.AreEqual(0f, layers[Action].defaultWeight);
            CollectionAssert.AreEquivalent(new[] { "Lowered", "Aim", "Shoot", "PunchLeft", "PunchRight" }, layers[Upper].stateMachine.states.Select(s => s.state.name));
            CollectionAssert.AreEquivalent(new[] { "None", "ReloadStand", "ReloadCrouch", "ReloadProne" }, layers[Action].stateMachine.states.Select(s => s.state.name));
            CollectionAssert.AreEquivalent(new[] { "Breath", "Flinch" }, layers[Additive].stateMachine.states.Select(s => s.state.name));
            CollectionAssert.AreEquivalent(new[] { "Locomotion", "Unarmed", "UnarmedCrouch", "Swim", "Air", "Death", "Prone" }, layers[Base].stateMachine.states.Select(s => s.state.name));
        }

        [Test]
        public void UpperAndActionLayers_AreMaskedToArmsHeadAndTorso_LegsAndRootStayWithTheBase()
        {
            foreach (var i in new[] { Upper, Action })
            {
                var mask = Controller().layers[i].avatarMask;
                Assert.IsNotNull(mask, "a layer without a mask would freeze the legs");
                foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm })
                    Assert.IsTrue(mask.GetHumanoidBodyPartActive(part), $"layer {i}: {part} on");
                foreach (var part in new[] { AvatarMaskBodyPart.Root, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg })
                    Assert.IsFalse(mask.GetHumanoidBodyPartActive(part), $"layer {i}: {part} off");
            }
        }

        [Test]
        public void EveryClip_IsHumanoidMotion_AndLoopsOnlyWhenItIsAHoldOrACycle()
        {
            var clips = new System.Collections.Generic.List<(string state, AnimationClip clip)>();
            foreach (var layer in Controller().layers)
                foreach (var st in layer.stateMachine.states)
                {
                    if (st.state.motion is BlendTree tree) foreach (var c in tree.children) clips.Add((st.state.name, (AnimationClip)c.motion));
                    else if (st.state.motion is AnimationClip clip) clips.Add((st.state.name, clip));
                }
            Assert.GreaterOrEqual(clips.Count, 15);
            foreach (var (state, clip) in clips) Assert.IsTrue(clip.isHumanMotion, $"{state}/{clip.name} must be a Humanoid clip so every roster model plays it");
            var loops = new[] { "RifleIdle", "RifleWalk", "RifleRun", "RifleWalkBack", "Breath", "Swat_RifleAimingIdle_skin", "U_Idle_Loop", "U_Walk_Loop", "U_Jog_Fwd_Loop", "U_Swim_Idle_Loop", "U_Swim_Fwd_Loop" };
            foreach (var (_, clip) in clips)
                if (loops.Contains(clip.name)) Assert.IsTrue(clip.isLooping, clip.name + " loops");
            foreach (var one in new[] { "ReloadingRifleStand", "ReloadRifleCrouch", "ProneReloadingRifle", "RifleDeath", "U_Hit_Chest", "U_Punch_Jab", "U_Punch_Cross" })
                Assert.IsFalse(clips.First(c => c.clip.name == one).clip.isLooping, one + " plays once");
        }

        [Test]
        public void Locomotion_BlendsBackPedal_Idle_Walk_AndRun_BySpeed()
        {
            var tree = Controller().layers[Base].stateMachine.defaultState.motion as BlendTree;
            Assert.IsNotNull(tree);
            Assert.AreEqual(BlendTreeType.Simple1D, tree.blendType, "direction comes from turning the legs (ADR-190 fallback until the 2D blend is verified)");
            var c = tree.children;
            Assert.IsTrue(c.Any(x => x.threshold == 0f) && c.Any(x => x.threshold < -0.5f));
            Assert.AreEqual(2, c.Count(x => x.threshold > 0.5f), "walk and run");
            Assert.AreEqual(c.Length, c.Select(x => x.motion).Distinct().Count());
            float walk = c.Where(x => x.threshold > 0.01f).Min(x => x.threshold), run = c.Max(x => x.threshold);
            Assert.That(walk, Is.InRange(0.8f, 2.5f), "walk clip ground speed (m/s)");
            Assert.That(run, Is.InRange(2.5f, 8f), "run clip ground speed (m/s)");
            Assert.Greater(run, walk * 1.5f);
        }

        [Test]
        public void BaseLayer_HasAnAirState_WhosePoseIsPickedByAParameter()
        {
            var air = Controller().layers[Base].stateMachine.states.Select(s => s.state).First(s => s.name == "Air");
            Assert.IsTrue(air.timeParameterActive);
            Assert.IsTrue(Controller().parameters.Any(p => p.name == air.timeParameter && p.type == AnimatorControllerParameterType.Float));
        }

        [Test]
        public void Standing_Idle_PutsHipsHeadAndFeetWhereAHumanOf1p8MetresHasThem()
        {
            Pose(Base, "Locomotion", 0f);
            Assert.That(Local(S.Hips).y, Is.InRange(0.85f, 1.05f), "hips height");
            Assert.That(Local(S.Head).y, Is.InRange(1.45f, 1.7f), "head joint height");
            Assert.That(Local(S.LeftFoot).y, Is.InRange(0.05f, 0.2f), "left ankle over the floor");
            Assert.That(Local(S.RightFoot).y, Is.InRange(0.05f, 0.2f), "right ankle over the floor");
            Assert.That(Mathf.Abs(TrunkPitch()), Is.LessThan(12f), "upright trunk");
        }

        [Test]
        public void WalkCycle_LiftsEachFoot_AndSwingsTheLegsAtLeastAThirdOfAMetreApart()
        {
            _animator.SetFloat("Move", 1.2f);
            float minZ = float.MaxValue, maxZ = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            Pose(Base, "Locomotion", 0f);
            for (int i = 0; i < 40; i++)
            {
                _animator.Update(0.05f);
                float dz = Local(S.LeftFoot).z - Local(S.RightFoot).z, y = Local(S.LeftFoot).y;
                minZ = Mathf.Min(minZ, dz); maxZ = Mathf.Max(maxZ, dz);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            Assert.Greater(maxZ - minZ, 0.33f, "feet pass each other front to back");
            Assert.Greater(maxY - minY, 0.06f, "a foot actually lifts");
        }

        [Test]
        public void UpperLayer_AimPose_MovesArmsAndTorsoButNotLegs()
        {
            Pose(Base, "Locomotion", 0f);
            var legs0 = new[] { S.LeftUpperLeg, S.RightUpperLeg, S.LeftLowerLeg, S.RightLowerLeg }.Select(b => b.rotation).ToArray();
            var rightHand0 = Local(S.RightHand);
            Pose(Upper, "Aim", 0f, upper: 1f);
            var legs1 = new[] { S.LeftUpperLeg, S.RightUpperLeg, S.LeftLowerLeg, S.RightLowerLeg }.Select(b => b.rotation).ToArray();
            for (int i = 0; i < legs0.Length; i++) Assert.Less(Quaternion.Angle(legs0[i], legs1[i]), 0.5f, "leg bone " + i + " must stay with the base layer");
            Assert.Greater(Vector3.Distance(rightHand0, Local(S.RightHand)), 0.03f, "the aim pose moved the right hand");
        }

        [Test]
        public void ActionLayer_ReloadStand_TakesTheLeftHandOffTheGunToTheMagazineWell()
        {
            Pose(Upper, "Aim", 0f, upper: 1f);
            var aimLeft = Local(S.LeftHand); var aimRight = Local(S.RightHand);
            float far = 0f;
            for (int i = 1; i <= 9; i++)
            {
                Pose(Action, "ReloadStand", i / 10f, upper: 1f, action: 1f);
                far = Mathf.Max(far, Vector3.Distance(aimLeft, Local(S.LeftHand)));
            }
            Assert.Greater(far, 0.2f, "the left hand leaves the aim position during the reload");
            Pose(Action, "ReloadStand", 0.5f, upper: 1f, action: 1f);
            Assert.Less(Vector3.Distance(aimRight, Local(S.RightHand)), 0.35f, "the right hand stays near the grip");
        }

        [Test]
        public void AdditiveFlinch_FirstPart_TipsTheChestByAFewDegrees()
        {
            Pose(Base, "Locomotion", 0f);
            float rest = TrunkAngleOfChest(), peak = 0f;
            // The game plays the first 40% of the hit clip (CombatantAnimation.FlinchShare) and fades out; the rest of the clip staggers on.
            for (int i = 1; i <= 8; i++) { Pose(Additive, "Flinch", i * 0.05f); peak = Mathf.Max(peak, Mathf.Abs(TrunkAngleOfChest() - rest)); }
            Assert.That(peak, Is.InRange(4f, 20f), "flinch size in degrees (the hit-chest pose added on top)");
            Pose(Additive, "Flinch", 0f);
            Assert.Less(Mathf.Abs(TrunkAngleOfChest() - rest), 1.5f, "the additive reference frame leaves the pose alone");
        }

        // The chest's pitch in the character frame (rotation of its forward about the right axis), degrees.
        float TrunkAngleOfChest() { var f = _go.transform.InverseTransformDirection(S.Chest.up); return Mathf.Atan2(f.z, f.y) * Mathf.Rad2Deg; }

        [Test]
        public void Breath_MovesTheChestLessThanThreeDegrees()
        {
            Pose(Base, "Locomotion", 0f, additive: 0f);
            float rest = TrunkAngleOfChest();
            Pose(Additive, "Breath", 0.5f, additive: 1f);
            Assert.Less(Mathf.Abs(TrunkAngleOfChest() - rest), 3f);
        }

        [Test]
        public void Death_EndsLyingDown_HeadNearTheFloor()
        {
            Pose(Base, "Death", 1f);
            Assert.Less(Local(S.Head).y, 0.35f, "head height at the end of the death clip");
            Assert.Greater(Mathf.Abs(TrunkPitch()), 60f, "trunk is horizontal");
        }

        float HandAhead(Transform hand) => Local(hand).z - Local(S.Chest).z;

        [Test]
        public void Punches_ExtendTheirOwnFistForwardAtLeast30Cm_AndComeBack()
        {
            foreach (var (state, hand) in new[] { ("PunchRight", S.RightHand), ("PunchLeft", S.LeftHand) })
            {
                Pose(Base, "Locomotion", 0f);
                float rest = HandAhead(hand), peak = float.MinValue;
                var series = new System.Text.StringBuilder();
                for (int i = 0; i <= 20; i++) { Pose(Upper, state, i / 20f, upper: 1f); series.Append(HandAhead(hand).ToString("F2") + " "); peak = Mathf.Max(peak, HandAhead(hand)); }
                Debug.Log($"[AnimTest] {state} hand ahead of chest by twentieths (rest {rest:F2}): {series}");
                Assert.Greater(peak - rest, 0.3f, state + ": fist reach over the rest pose (m)");
                Pose(Upper, state, 1f, upper: 1f);
                Assert.Less(Mathf.Abs(HandAhead(hand) - rest), 0.25f, state + ": the hand is back near its rest at the end");
            }
        }

        [Test]
        public void Unarmed_Jog_SwingsBothArmsPastTheHips_WhichTheRifleRunNeverDoes()
        {
            float Swing(string state, float move)
            {
                _animator.SetFloat("Move", move);
                Pose(Base, state, 0f);
                float behind = float.MaxValue;
                for (int i = 0; i < 40; i++)
                {
                    _animator.Update(0.03f);
                    behind = Mathf.Min(behind, Mathf.Min(Local(S.LeftHand).z, Local(S.RightHand).z) - Local(S.Hips).z);
                }
                return behind;
            }
            Assert.Less(Swing("Unarmed", 3.5f), -0.05f, "bare hands: an arm swings back past the hips");
            Assert.Greater(Swing("Locomotion", 3f), 0.1f, "rifle run: both hands stay in front on the gun");
        }

        // Which way the body faces, read from the shoulder line (right of the root = +x): 1 = the way the model faces, -1 = turned round.
        float FacingDot()
        {
            var across = (S.RightUpperArm.position - S.LeftUpperArm.position).normalized;
            return Vector3.Dot(Vector3.Cross(across, Vector3.up), _go.transform.forward);
        }

        [Test]
        public void EveryState_FacesTheWayTheModelFaces_NoClipTurnsTheBodyRound()
        {
            // Player report: standing and walking bare-handed the body faced backwards, and a punch twisted the waist the other way. The clips of
            // other files (UAL) carry their own forward axis, which the import must not keep.
            _animator.SetFloat("Move", 3f);
            foreach (var (layer, state, upper) in new[]
            {
                (Base, "Locomotion", 0f), (Base, "Unarmed", 0f), (Base, "UnarmedCrouch", 0f), (Base, "Swim", 0f), (Base, "Death", 0f), (Base, "Prone", 0f),
                (Upper, "Aim", 1f), (Upper, "PunchRight", 1f), (Upper, "PunchLeft", 1f),
            })
            {
                float worst = 1f, mean = 0f;
                for (int i = 0; i <= 10; i++)
                {
                    Pose(layer, state, i / 10f, upper: upper);
                    worst = Mathf.Min(worst, FacingDot()); mean += FacingDot() / 11f;
                }
                Debug.Log($"[AnimTest] facing {state}: worst {worst:F2} mean {mean:F2} (yaw off by up to {Mathf.Acos(Mathf.Clamp(worst, -1f, 1f)) * Mathf.Rad2Deg:F0} deg)");
                if (state == "Death" || state == "Prone") continue; // lying down: the shoulder line is not a facing
                // A jog twists the shoulders against the hips by up to ~45 degrees, so the cycle's average decides, and no frame may be near sideways.
                Assert.Greater(mean, 0.85f, $"{state}: on average the body faces the way the model faces (shoulder-line facing, 1 = forward)");
                Assert.Greater(worst, 0.5f, $"{state}: no frame is turned more than 60 degrees from the model's facing");
            }
        }
    }
}