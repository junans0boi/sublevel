using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Foot plant survey (player report "walks buried in the ground"): per roster model, walking over flat ground, a 15 degree ramp and stairs
    // built in the TestBox: the pelvis drop, the model drop and how far each ankle is over the ground really under it, minus the model's own
    // resting ankle height (negative = sunk). SURVEY_MODELS=MixamoSwat,UbcMale limits the models. Writes Logs/anim_plant_survey.csv.
    public class FootPlantSurveyTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() { var f = Frame; Frame.Jump = Frame.Crouch = Frame.Prone = false; return f; }
        }

        string _saved;

        [UnitySetUp] public IEnumerator Remember() { _saved = PlayerPrefs.GetString(PlayerAppearanceStore.Key, ""); yield break; }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            PlayerController.InputOverride = null;
            if (string.IsNullOrEmpty(_saved)) PlayerAppearanceStore.Clear(); else PlayerPrefs.SetString(PlayerAppearanceStore.Key, _saved);
            yield break;
        }

        static float GroundAt(Vector3 p)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 1f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<Combatant>() == null && h.normal.y > 0.5f) best = Mathf.Max(best, h.point.y);
            return float.IsNegativeInfinity(best) ? p.y - 4f : best;
        }

        // Surfaces along the south lane (z = -26), each 14 m long, walked towards +x: flat floor, a 15 degree ramp, stairs (0.18 m rise, 0.3 m run).
        static void BuildSurfaces()
        {
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "SurveyRamp"; ramp.transform.localScale = new Vector3(14f, 2f, 6f);
            ramp.transform.rotation = Quaternion.Euler(0f, 0f, 15f);
            ramp.transform.position = new Vector3(-2f, -1f * Mathf.Cos(15f * Mathf.Deg2Rad) - 0.1f, -26f);
            for (int i = 0; i < 30; i++)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = "SurveyStep"; step.transform.localScale = new Vector3(0.3f, (i + 1) * 0.18f, 6f);
                step.transform.position = new Vector3(13f + i * 0.3f, (i + 1) * 0.09f, -26f);
            }
            Physics.SyncTransforms();
        }

        // Side pictures of the player at foot height on each surface (same composition before and after a change): PLANT_SHOTS=<tag>
        static void Shot(string name)
        {
            var pc = PlayerController.Local;
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 22; cam.clearFlags = CameraClearFlags.Skybox;
            go.transform.position = pc.transform.position + Vector3.back * 4.5f + Vector3.up * 0.45f; // the lane runs east: the camera looks at the left side
            go.transform.LookAt(pc.transform.position + Vector3.up * 0.45f);
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); RenderTexture.active = null;
            Directory.CreateDirectory("Logs/plantshots");
            File.WriteAllBytes($"Logs/plantshots/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(go);
        }

        // The player as spawned in the World scene (elevator), seen from the front and the side, with the bone scales and the facing in numbers.
        [UnityTest, Timeout(300000)]
        public IEnumerator SpawnPoseInWorld()
        {
            var input = new ScriptedInput();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("World");
            yield return null; yield return null;
            float end = Time.realtimeSinceStartup + 60f;
            while ((PlayerController.Local == null || Combatant.LocalPlayer == null) && Time.realtimeSinceStartup < end) yield return null;
            var me = Combatant.LocalPlayer; var pc = PlayerController.Local;
            Assert.IsNotNull(me);
            yield return new WaitForSeconds(2f);
            var animator = me.GetComponentInChildren<Animator>();
            var def = me.GetComponentInChildren<CharacterDef>();
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            for (int i = 0; i < 3; i++)
            {
                var across = (B(HumanBodyBones.RightUpperArm).position - B(HumanBodyBones.LeftUpperArm).position).normalized;
                var face = Vector3.Cross(across, Vector3.up);
                Debug.Log($"[Spawn] model {def.Id} root fwd {pc.transform.forward} body faces {face} (dot {Vector3.Dot(face, pc.transform.forward):F2}) hips scale {B(HumanBodyBones.Hips).lossyScale} spine {B(HumanBodyBones.Spine).lossyScale} chest {B(HumanBodyBones.Chest).lossyScale} visual rot {animator.transform.localEulerAngles} root pos {pc.transform.position} legTurn {me.GetComponent<CombatantAnimation>().LegTurnDeg:F1} model fwd {animator.transform.forward} wrapper fwd {def.transform.forward} wrapper euler {def.transform.eulerAngles} hips fwd {B(HumanBodyBones.Hips).forward} chest up {B(HumanBodyBones.Chest).up} parent chain {string.Join("/", animator.transform.GetComponentsInParent<Transform>().Select(t => t.name + ":" + t.localEulerAngles.y.ToString("F0")).Take(4))} plantDrop {me.GetComponent<CombatantAnimation>().PlantDrop:F3} base {me.GetComponent<CombatantAnimation>().BaseState}");
                ShotFront($"spawn_front_{i}"); Shot($"spawn_side_{i}");
                yield return new WaitForSeconds(0.5f);
            }
            PlayerController.InputOverride = null;
        }

        static void ShotFront(string name)
        {
            var pc = PlayerController.Local;
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 30; cam.clearFlags = CameraClearFlags.Skybox;
            go.transform.position = pc.transform.position + pc.transform.forward * 3.5f + Vector3.up * 1.0f;
            go.transform.LookAt(pc.transform.position + Vector3.up * 1.0f);
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); RenderTexture.active = null;
            Directory.CreateDirectory("Logs/plantshots");
            File.WriteAllBytes($"Logs/plantshots/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(go);
        }

        [UnityTest, Timeout(1700000)]
        public IEnumerator Survey()
        {
            var models = (Environment.GetEnvironmentVariable("SURVEY_MODELS") ?? string.Join(",", PlayerAppearance.ModelIds)).Split(',');
            var csv = new StringBuilder("model,surface,footRest,plantDropMax,plantDropMean,modelDropMax,footFixMax,sunkMax,floatMax,speed\n");
            Directory.CreateDirectory("Logs");
            foreach (var id in models)
            {
                PlayerAppearanceStore.Save(new PlayerAppearance { ModelId = id });
                var input = new ScriptedInput();
                PlayerController.InputOverride = input;
                SceneManager.LoadScene("TestBox");
                yield return null; yield return null; // the old scene (and its player) is still there until the load goes through
                float end = Time.realtimeSinceStartup + 20f;
                while ((PlayerController.Local == null || Combatant.LocalPlayer == null) && Time.realtimeSinceStartup < end) yield return null;
                var me = Combatant.LocalPlayer; var pc = PlayerController.Local;
                Assert.IsNotNull(me, id);
                me.ServerDamageFilter = _ => 0f;
                BuildSurfaces();
                yield return new WaitForSeconds(0.6f);
                var anim = me.GetComponent<CombatantAnimation>();
                var animator = me.GetComponentInChildren<Animator>();
                var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
                foreach (var (surface, start) in new[] { ("flat", new Vector3(-24f, 0.1f, -26f)), ("ramp", new Vector3(-8f, 0.1f, -26f)), ("stairs", new Vector3(11.5f, 0.1f, -26f)) })
                {
                    pc.Teleport(start);
                    pc.LookAt(start + Vector3.right * 10f + Vector3.up * 1.6f);
                    input.Frame.Move = Vector2.zero;
                    yield return new WaitForSeconds(0.5f);
                    string tag = Environment.GetEnvironmentVariable("PLANT_SHOTS");
                    if (!string.IsNullOrEmpty(tag)) Shot($"{tag}_{id}_{surface}_stand");
                    input.Frame.Move = new Vector2(0, 1);
                    yield return new WaitForSeconds(0.4f);
                    if (!string.IsNullOrEmpty(tag)) for (int i = 0; i < 3; i++) { Shot($"{tag}_{id}_{surface}_walk{i}"); yield return new WaitForSeconds(0.13f); }
                    float dropMax = 0f, dropSum = 0f, mdMax = 0f, ffMax = 0f, sunk = 0f, floating = 0f, speed = 0f; int n = 0;
                    for (float t = Time.time + 1.6f; Time.time < t; n++)
                    {
                        yield return null;
                        dropMax = Mathf.Max(dropMax, anim.PlantDrop); dropSum += anim.PlantDrop;
                        mdMax = Mathf.Max(mdMax, anim.ModelDrop); ffMax = Mathf.Max(ffMax, Mathf.Abs(anim.FootFix));
                        // Per ankle: over its ground minus the resting height. Only the lower ankle counts (the other one is in the air in a stride).
                        float low = feet.Min(f => f.position.y - GroundAt(f.position)) - anim.FootRest;
                        sunk = Mathf.Max(sunk, -low); floating = Mathf.Max(floating, low);
                        speed += me.GetComponent<CombatantMotion>().LocalVelocity.magnitude;
                    }
                    csv.AppendLine($"{id},{surface},{anim.FootRest:F3},{dropMax:F3},{dropSum / n:F3},{mdMax:F3},{ffMax:F3},{sunk:F3},{floating:F3},{speed / n:F2}");
                    input.Frame.Move = Vector2.zero;
                    File.WriteAllText("Logs/anim_plant_survey.csv", csv.ToString());
                }
                PlayerController.InputOverride = null;
            }
            File.WriteAllText("Logs/anim_plant_survey.csv", csv.ToString());
        }
    }
}
