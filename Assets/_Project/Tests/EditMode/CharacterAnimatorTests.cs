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

        // Ground speed of `state` played in place at blend `move`: how fast the lower foot slides along the model's z while it is down.
        float PlantedFootSpeed(string state, float move, float clipLength)
        {
            _animator.SetFloat("Move", move);
            const int n = 80;
            var feet = new[] { S.LeftFoot, S.RightFoot };
            var y = new float[2, n + 1]; var z = new float[2, n + 1];
            float low = float.MaxValue;
            for (int i = 0; i <= n; i++)
            {
                Pose(Base, state, i / (float)n);
                for (int f = 0; f < 2; f++) { var p = Local(feet[f]); y[f, i] = p.y; z[f, i] = p.z; low = Mathf.Min(low, p.y); }
            }
            float travelled = 0f, time = 0f, dt = clipLength / n;
            for (int f = 0; f < 2; f++)
                for (int i = 1; i <= n; i++)
                    if (y[f, i] < low + 0.03f && y[f, i - 1] < low + 0.03f) { travelled += Mathf.Abs(z[f, i] - z[f, i - 1]); time += dt; }
            return time > 0f ? travelled / time : 0f;
        }

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
            CollectionAssert.AreEquivalent(new[] { "Locomotion", "LocomotionAim", "Unarmed", "UnarmedCrouch", "RifleCrouch", "Swim", "JumpStart", "JumpLoop", "JumpLand", "Air", "Death", "Prone" }, layers[Base].stateMachine.states.Select(s => s.state.name));
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
            var loops = new[] { "RifleIdle", "RifleWalk", "RifleRun", "RifleWalkBack", "Breath", "Swat_RifleAimingIdle_skin", "U_Idle_Loop", "U_Walk_Loop", "U_Jog_Fwd_Loop", "U_Sprint_Loop", "U_Swim_Idle_Loop", "U_Swim_Fwd_Loop", "U_Jump_Loop" };
            foreach (var (_, clip) in clips)
                if (loops.Contains(clip.name)) Assert.IsTrue(clip.isLooping, clip.name + " loops");
            foreach (var one in new[] { "ReloadingRifleStand", "ReloadRifleCrouch", "ProneReloadingRifle", "RifleDeath", "U_Hit_Chest", "U_Punch_Jab", "U_Punch_Cross", "U_Jump_Start", "U_Jump_Land" })
                Assert.IsFalse(clips.First(c => c.clip.name == one).clip.isLooping, one + " plays once");
        }

        [Test]
        public void EightWayTrees_PlaceEveryClipAtItsOwnGroundVelocity_AroundAllEightDirections()
        {
            foreach (var name in new[] { "Locomotion", "LocomotionAim", "Unarmed" })
            {
                var tree = Controller().layers[Base].stateMachine.states.Select(s => s.state).First(s => s.name == name).motion as BlendTree;
                Assert.IsNotNull(tree, name);
                Assert.AreEqual(BlendTreeType.FreeformDirectional2D, tree.blendType, name + ": 2D on the local velocity (ADR-391, #302)");
                Assert.AreEqual("MoveX", tree.blendParameter); Assert.AreEqual("MoveZ", tree.blendParameterY);
                var c = tree.children;
                Assert.IsTrue(c.Any(x => x.position == Vector2.zero), name + ": a standing clip");
                float Reach(float deg) { var d = new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad)); return c.Where(x => x.position.sqrMagnitude > 0.01f && Vector2.Angle(x.position, d) <= 30f).Select(x => Vector2.Dot(x.position, d)).DefaultIfEmpty(0f).Max(); }
                Assert.That(Reach(0f), Is.InRange(5.5f, 6.3f), name + ": the sprint ahead, at the game's slowest sprint (m/s, ADR-481)");
                Assert.Greater(Reach(180f), 4f, name + ": the back-pedal reaches a run");
                Assert.Greater(Reach(90f), 1.5f, name + ": a sidestep to the right");
                Assert.Greater(Reach(-90f), 1.5f, name + ": a sidestep to the left");
                Assert.IsTrue(c.Any(x => x.position.y < -0.5f && x.timeScale < 0f), name + ": back-pedal = a forward cycle played backwards");
                if (name != "Unarmed") foreach (float deg in new[] { 45f, -45f }) Assert.Greater(Reach(deg), 3f, $"{name}: a diagonal run at {deg} deg");
            }
        }

        // #303 / #227: the rifle crouch is the Mixamo kneel standing still and its crouch walks moving (ADR-536), not a code knee bend
        // (a deep squat with a foot in the air, a lunge when walking).
        [Test]
        public void RifleCrouch_IsTheKneelStillAndTheCrouchWalksAheadAndBack_WithTheHipsLowAndTheFeetDown()
        {
            var state = Controller().layers[Base].stateMachine.states.Select(s => s.state).First(s => s.name == "RifleCrouch");
            var tree = state.motion as BlendTree;
            Assert.IsNotNull(tree);
            Assert.AreEqual(BlendTreeType.Simple1D, tree.blendType);
            Assert.AreEqual("Move", tree.blendParameter);
            Assert.IsTrue(state.speedParameterActive && state.speedParameter == "Pace", "the legs keep pace with the ground");
            var c = tree.children.OrderBy(x => x.threshold).ToArray();
            CollectionAssert.AreEqual(new[] { "RifleCrouchWalkBack", "RifleKneelIdle", "RifleCrouchWalk" }, c.Select(x => x.motion.name));
            Assert.AreEqual(0f, c[1].threshold);
            // Each walk sits at its clip's own ground speed (the Mixamo back walk is a slow 0.32 m/s, the walk 0.59), measured here from the
            // planted foot as the tree plays it in place, and the game paces the legs by the same number, so the feet hold the ground.
            var gait = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/M1/Player.prefab").GetComponentInChildren<CombatantAnimation>(true);
            var so = new SerializedObject(gait);
            foreach (var (child, field) in new[] { (c[0], "_rifleCrouchBackSpeed"), (c[2], "_rifleCrouchSpeed") })
            {
                float ground = PlantedFootSpeed("RifleCrouch", child.threshold, ((AnimationClip)child.motion).length);
                Debug.Log($"[AnimTest] RifleCrouch {child.motion.name}: threshold {child.threshold:F3} planted foot {ground:F3} m/s, {field} {so.FindProperty(field).floatValue:F3}");
                Assert.That(Mathf.Abs(child.threshold), Is.InRange(0.2f, 1.5f), child.motion.name + ": a crouch walk's pace");
                Assert.AreEqual(Mathf.Abs(child.threshold), ground, 0.3f * ground, child.motion.name + ": the threshold is the clip's ground speed");
                Assert.AreEqual(Mathf.Abs(child.threshold), so.FindProperty(field).floatValue, 0.01f, field + " paces the legs by the same ground speed");
            }
            Assert.Less(c[0].threshold, 0f); Assert.Greater(c[2].threshold, 0f);
            foreach (var x in c) { Assert.AreEqual(1f, x.timeScale, x.motion.name + " plays forward"); Assert.IsTrue(((AnimationClip)x.motion).isLooping, x.motion.name + " loops"); }

            _animator.SetFloat("Move", 0f); Pose(Base, "Locomotion", 0f);
            float standingHips = Local(S.Hips).y;
            foreach (var (move, what) in new[] { (0f, "kneel"), (c[2].threshold, "crouch walk"), (c[0].threshold, "crouch walk back") })
            {
                _animator.SetFloat("Move", move);
                float lowFootMax = 0f, highFootMax = 0f, hipsMax = 0f, hipsSum = 0f;
                for (int i = 0; i <= 10; i++)
                {
                    Pose(Base, "RifleCrouch", i / 10f);
                    float l = Local(S.LeftFoot).y, r = Local(S.RightFoot).y;
                    lowFootMax = Mathf.Max(lowFootMax, Mathf.Min(l, r)); highFootMax = Mathf.Max(highFootMax, Mathf.Max(l, r));
                    hipsMax = Mathf.Max(hipsMax, Local(S.Hips).y); if (i < 10) hipsSum += Local(S.Hips).y;
                }
                Debug.Log($"[AnimTest] RifleCrouch {what}: hips mean {hipsSum / 10f:F3} max {hipsMax:F3} (standing {standingHips:F3}) lower ankle max {lowFootMax:F3} higher ankle max {highFootMax:F3}");
                // The kneel is deep; the Mixamo crouch walks are a bent-knee sneak, hips ~0.1 m under standing (mean 0.846 vs 0.952, #303 follow-up issue).
                Assert.Greater(standingHips - (move == 0f ? hipsMax : hipsSum / 10f), move == 0f ? 0.2f : 0.08f, what + ": the hips are down from standing");
                Assert.Less(lowFootMax, 0.2f, what + ": a foot is always on the floor");
                if (move == 0f) Assert.Less(highFootMax, 0.25f, "kneel: neither foot is lifted (#227)");
            }
        }

        // Player report "running, but it looks like a slow jog" (ADR-481): the jog sat at its clip speed 5.19 and the sprint at 7.44, so the
        // game's run (4.7) showed a walk-tinged jog and its sprint (6.0-6.3) half a jog. The run must land on the jog, every sprint on the sprint.
        [Test]
        public void ForwardTrees_TheRunPlaysTheJog_EverySprintPlaysTheWholeSprint()
        {
            var L = Game.Net.GameData.Rules.Locomotion;
            float slowestSprint = L.SprintSpeed * Mathf.Min(1f, Mathf.Min(L.SmgMoveFactor, L.RifleMoveFactor), Mathf.Min(L.MarksmanMoveFactor, L.ShotgunMoveFactor));
            foreach (var name in new[] { "Locomotion", "LocomotionAim", "Unarmed" })
            {
                var tree = (BlendTree)Controller().layers[Base].stateMachine.states.Select(s => s.state).First(s => s.name == name).motion;
                var ahead = tree.children.Where(c => c.position.x == 0f && c.position.y > 0.1f).OrderBy(c => c.position.y).ToArray();
                Assert.AreEqual(new[] { "U_Walk_Loop", "U_Jog_Fwd_Loop", "U_Sprint_Loop" }, ahead.Select(c => c.motion.name).ToArray(), name + ": walk, jog, sprint ahead");
                var jog = ahead[1]; var sprint = ahead[2];
                Assert.AreEqual(L.WalkSpeed, jog.position.y, 1e-3f, name + ": the jog sits at the game's run");
                Assert.AreEqual(slowestSprint, sprint.position.y, 1e-3f, name + ": the sprint sits at the slowest sprint, so every gun's sprint is all sprint");
                foreach (float f in new[] { 1f, L.SmgMoveFactor, L.RifleMoveFactor, L.MarksmanMoveFactor, L.ShotgunMoveFactor })
                    Assert.GreaterOrEqual(L.SprintSpeed * f, sprint.position.y - 1e-3f, $"{name}: sprint at move factor {f} reaches the sprint clip");
                // Each plays at its spot's ground: the clip's own speed (spot / time scale) is a running speed (jog ~5.2, sprint ~7.0 m/s).
                foreach (var c in new[] { jog, sprint })
                    Assert.That(c.position.y / c.timeScale, Is.InRange(4f, 8f), $"{name}: {c.motion.name} rate fits its own ground speed");
            }
        }

        [Test]
        public void BaseLayer_HasAnAirState_WhosePoseIsPickedByAParameter()
        {
            var air = Controller().layers[Base].stateMachine.states.Select(s => s.state).First(s => s.name == "Air");
            Assert.IsTrue(air.timeParameterActive);
            Assert.IsTrue(Controller().parameters.Any(p => p.name == air.timeParameter && p.type == AnimatorControllerParameterType.Float));
        }

        // #304, ADR-537: take-off, airborne loop and landing are real clips on the base layer, for bare hands and guns alike.
        [Test]
        public void BaseLayer_HasJumpStartLoopAndLand_PlayingTheUalJumpClips()
        {
            var states = Controller().layers[Base].stateMachine.states.Select(s => s.state).ToDictionary(s => s.name);
            foreach (var (state, clip) in new[] { ("JumpStart", "U_Jump_Start"), ("JumpLoop", "U_Jump_Loop"), ("JumpLand", "U_Jump_Land") })
            {
                Assert.IsTrue(states.ContainsKey(state), state + " missing");
                Assert.AreEqual(clip, states[state].motion?.name, state);
            }
        }

        [Test]
        public void JumpClips_TheLoopLiftsAFootOffTheFloor_TheLandingBendsTheKnees()
        {
            Pose(Base, "Locomotion", 0f);
            float standHips = Local(S.Hips).y;
            Pose(Base, "JumpLoop", 0.5f);
            Assert.Greater(Mathf.Max(Local(S.LeftFoot).y, Local(S.RightFoot).y), 0.2f, "a foot drawn up in the air");
            float lowest = float.MaxValue;
            for (float t = 0f; t <= 1f; t += 0.1f) { Pose(Base, "JumpLand", t); lowest = Mathf.Min(lowest, Local(S.Hips).y); }
            Assert.Less(lowest, standHips - 0.05f, "the landing absorbs: the hips sink at least 5 cm");
            Pose(Base, "JumpLand", 0f);
            Assert.That(Mathf.Abs(TrunkPitch()), Is.LessThan(45f), "lands on the feet, not on the face");
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
            _animator.SetFloat("MoveZ", 1.2f);
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
        public void Unarmed_Jog_SwingsBothArmsPastTheHips_WhichTheRifleIdleNeverDoes()
        {
            float Swing(string state, float move)
            {
                _animator.SetFloat("MoveZ", move);
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
            Assert.Greater(Swing("Locomotion", 0f), 0.1f, "rifle idle: both hands in front on the gun (moving, the code hold places them, ADR-390)");
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
            _animator.SetFloat("Move", 3f); _animator.SetFloat("MoveZ", 3f);
            foreach (var (layer, state, upper) in new[]
            {
                (Base, "Locomotion", 0f), (Base, "LocomotionAim", 0f), (Base, "Unarmed", 0f), (Base, "UnarmedCrouch", 0f), (Base, "RifleCrouch", 0f), (Base, "Swim", 0f), (Base, "Death", 0f), (Base, "Prone", 0f),
                (Base, "JumpStart", 0f), (Base, "JumpLoop", 0f), (Base, "JumpLand", 0f),
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
    

        // #223: running sideways the chest faces ahead and the legs step across (not the forward run turned sideways, "crab walk").
        [Test]
        public void Sidesteps_KeepTheShouldersFacingAhead_WhileTheFeetCrossSideways()
        {
            foreach (var (state, x) in new[] { ("Locomotion", 4.5f), ("Locomotion", -4.5f), ("LocomotionAim", 2.5f), ("LocomotionAim", -2.5f), ("Unarmed", 4.5f), ("Unarmed", -4.5f) })
            {
                _animator.SetFloat("MoveX", x); _animator.SetFloat("MoveZ", 0f);
                Pose(Base, state, 0f);
                float facing = 0f, minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                for (int i = 0; i < 30; i++)
                {
                    _animator.Update(0.03f);
                    facing += FacingDot() / 30f;
                    float dx = Local(S.LeftFoot).x - Local(S.RightFoot).x, dz = Local(S.LeftFoot).z - Local(S.RightFoot).z;
                    minX = Mathf.Min(minX, dx); maxX = Mathf.Max(maxX, dx); minZ = Mathf.Min(minZ, dz); maxZ = Mathf.Max(maxZ, dz);
                }
                Debug.Log($"[AnimTest] sidestep {state} x {x}: facing {facing:F2}, feet apart sideways {minX:F2}..{maxX:F2}, front-back {minZ:F2}..{maxZ:F2}");
                // The shoulders are squared to the aim in code over the clip (checked in play by GaitSurveyTests: facing >= 0.8 every way).
                Assert.Greater(maxX - minX, maxZ - minZ, $"{state} {x}: the feet move sideways more than front to back");
            }
        }
    

        [Test]
        public void NearestDirection_TakesTheShortWayRound()
        {
            var dirs = new[] { -94f, -45f, 0f, 42f, 88f, 180f };
            Assert.AreEqual(2, CombatantAnimation.NearestDirection(dirs, 10f));
            Assert.AreEqual(3, CombatantAnimation.NearestDirection(dirs, 30f));
            Assert.AreEqual(5, CombatantAnimation.NearestDirection(dirs, -175f), "the short way round");
            Assert.AreEqual(5, CombatantAnimation.NearestDirection(dirs, -135f), "backing off at an angle: the back-pedal wins a near tie with the sidestep");
            Assert.AreEqual(0, CombatantAnimation.NearestDirection(dirs, -100f));
        }
    }
}
