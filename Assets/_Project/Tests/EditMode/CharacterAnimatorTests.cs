using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Tests
{
    // M1.6a: the generated animator must blend movement in every direction and keep weapon poses on the upper body only.
    public class CharacterAnimatorTests
    {
        const string ControllerPath = "Assets/_Project/Prefabs/M1_5/Character.controller";

        static AnimatorController Controller()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(controller, "run TestBoxBuilder.Build to generate " + ControllerPath);
            return controller;
        }

        [Test]
        public void Locomotion_IsATwoAxisBlend_CoveringAllFourSidesWithDistinctClips()
        {
            var tree = Controller().layers[0].stateMachine.defaultState.motion as BlendTree;
            Assert.IsNotNull(tree, "the base layer's default state should be a blend tree");
            Assert.AreEqual(BlendTreeType.FreeformCartesian2D, tree.blendType, "diagonals come from blending two axes");
            var children = tree.children;
            Assert.IsTrue(children.Any(c => c.position == Vector2.zero), "standing still");
            Assert.IsTrue(children.Any(c => c.position.y > 0.5f && Mathf.Abs(c.position.x) < 0.01f), "forward");
            Assert.IsTrue(children.Any(c => c.position.y < -0.5f && Mathf.Abs(c.position.x) < 0.01f), "backward");
            Assert.IsTrue(children.Any(c => c.position.x < -0.5f && Mathf.Abs(c.position.y) < 0.01f), "left");
            Assert.IsTrue(children.Any(c => c.position.x > 0.5f && Mathf.Abs(c.position.y) < 0.01f), "right");
            Assert.AreEqual(children.Length, children.Select(c => c.motion).Distinct().Count(), "every direction has its own clip");
            Assert.IsTrue(children.All(c => c.motion != null && ((AnimationClip)c.motion).isLooping), "movement clips loop");
        }

        [Test]
        public void Locomotion_ClipSpeedsAreBelievableForAHuman()
        {
            var tree = (BlendTree)Controller().layers[0].stateMachine.defaultState.motion;
            float walk = tree.children.Where(c => c.position.y > 0.01f).Min(c => c.position.y);
            float run = tree.children.Max(c => c.position.y);
            Assert.That(walk, Is.InRange(0.8f, 2.5f), "walk clip ground speed (m/s)");
            Assert.That(run, Is.InRange(2.5f, 8f), "run clip ground speed (m/s)");
            Assert.Greater(run, walk * 1.5f);
        }

        [Test]
        public void UpperLayer_IsMaskedToTheUpperBody_AndHoldsTheWeaponPoses()
        {
            var layers = Controller().layers;
            Assert.AreEqual(2, layers.Length);
            var mask = layers[1].avatarMask;
            Assert.IsNotNull(mask, "upper layer needs a mask or it would freeze the legs");
            bool legsOff = true, armsOn = false;
            for (int i = 0; i < mask.transformCount; i++)
            {
                var path = mask.GetTransformPath(i);
                if (path.Contains("UpperLeg") && mask.GetTransformActive(i)) legsOff = false;
                if (path.Contains("UpperArm") && mask.GetTransformActive(i)) armsOn = true;
            }
            Assert.IsTrue(legsOff, "legs must stay with the base layer");
            Assert.IsTrue(armsOn, "arms belong to the upper layer");
            CollectionAssert.AreEquivalent(new[] { "Lowered", "Aim", "Shoot" }, layers[1].stateMachine.states.Select(s => s.state.name));
            Assert.AreEqual("Lowered", layers[1].stateMachine.defaultState.name, "armed and idle means weapon down");
        }
    }
}
