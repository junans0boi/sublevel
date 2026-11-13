using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of combat feedback (#152, ADR-118): impacts per surface, the marks they leave, hit markers by part,
    // the kill marker and the hit direction ring. Run on purpose with
    //   IMPACT_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.ImpactCaptureTests
    [Explicit, Category("Capture")]
    public class ImpactCaptureTests
    {
        const int Rifle = 3;

        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static GameObject Slab(string name, Vector3 at, Vector3 faceTo, BulletSurface surface, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = at;
            go.transform.localScale = new Vector3(1.1f, 2.2f, 0.4f);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(faceTo.x - at.x, 0f, faceTo.z - at.z));
            go.GetComponent<Renderer>().material.color = color;
            go.AddComponent<BulletSurfaceTag>().Surface = surface;
            return go;
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator CaptureCombatFeedback()
        {
            string folder = Environment.GetEnvironmentVariable("IMPACT_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/impactshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return new WaitForSeconds(0.8f);
            me.StanceSpreadFactor = 0f;
            Combatant dummy = null;
            foreach (var c in Combatant.All) if (!c.IsPlayer) { dummy = c; break; }
            foreach (var part in dummy.GetComponents<MonoBehaviour>())
                if (part.GetType().Name == "TrainingDummy") part.enabled = false;
            dummy.ServerDamageFilter = _ => 0f;

            string P(string n) => Path.Combine(folder, n);
            IEnumerator Shot(string name) => UiShot.Save(P(name), 1280, 720);

            // Hit markers on the dummy: body, legs, head, then a kill.
            pc.Teleport(dummy.transform.position + dummy.transform.forward * 5f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.6f);
            pc.LookAt(dummy.transform.position + Vector3.up * 1.1f);
            yield return new WaitForSeconds(0.3f);
            var cam = pc.Camera;
            IEnumerator Marker(string name, float height, string what)
            {
                pc.LookAt(dummy.transform.position + Vector3.up * height);
                yield return null;
                Assert.IsTrue(me.ServerFire(dummy.transform.position + Vector3.up * height - me.EyePosition), what);
                yield return Shot(name);
                yield return new WaitForSeconds(0.4f);
            }
            yield return Marker("fx_01_marker_body.png", 1.1f, "body");
            yield return Marker("fx_02_marker_legs.png", 0.35f, "legs");
            yield return Marker("fx_03_marker_head.png", 1.65f, "head");
            Hud.HitMarker(true, false);
            yield return Shot("fx_04_marker_kill.png");
            yield return new WaitForSeconds(0.3f);
            Hud.DamageFrom(me.transform.position + me.transform.right * 6f + me.transform.forward * -2f);
            yield return Shot("fx_05_damage_from_right_rear.png");
            yield return new WaitForSeconds(1.2f);

            // Impacts per surface on open ground: three slabs 5 m ahead, a camera off to the side (no HUD).
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(3000f, -0.5f, 3000f);
            floor.transform.localScale = new Vector3(60f, 1f, 60f);
            floor.GetComponent<Renderer>().material.color = new Color(0.38f, 0.36f, 0.32f);
            pc.Teleport(new Vector3(3000f, 0.1f, 3000f));
            yield return new WaitForSeconds(0.8f);
            Physics.SyncTransforms();
            Slab("Concrete", new Vector3(2998.4f, 1.1f, 3005f), new Vector3(2998.4f, 1.1f, 2990f), BulletSurface.Concrete, new Color(0.6f, 0.6f, 0.6f));
            Slab("Wood", new Vector3(3000f, 1.1f, 3005f), new Vector3(3000f, 1.1f, 2990f), BulletSurface.Wood, new Color(0.5f, 0.34f, 0.2f));
            Slab("Metal", new Vector3(3001.6f, 1.1f, 3005f), new Vector3(3001.6f, 1.1f, 2990f), BulletSurface.Metal, new Color(0.45f, 0.5f, 0.55f));
            Physics.SyncTransforms();
            var shotCam = new GameObject("ImpactShotCam").AddComponent<Camera>();
            shotCam.enabled = false;
            shotCam.CopyFrom(pc.Camera);
            shotCam.fieldOfView = 50f;
            shotCam.transform.SetPositionAndRotation(new Vector3(3004.5f, 1.5f, 3001.5f), Quaternion.LookRotation(new Vector3(3000f, 1.1f, 3005f) - new Vector3(3004.5f, 1.5f, 3001.5f)));
            void Save(string name)
            {
                var rt = new RenderTexture(1280, 720, 24);
                shotCam.targetTexture = rt;
                shotCam.Render();
                shotCam.targetTexture = null;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(P(name), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(rt);
                UnityEngine.Object.Destroy(tex);
            }
            IEnumerator Impact(string name, Vector3 at)
            {
                pc.LookAt(at);
                yield return new WaitForSeconds(0.3f);
                Assert.IsTrue(me.ServerFire(at - me.EyePosition), name);
                yield return null;
                Save(name);
                yield return new WaitForSeconds(0.15f);
                Save(name.Replace(".png", "_dust.png")); // the dust a moment later
                yield return new WaitForSeconds(0.3f);
            }
            yield return Impact("fx_06_impact_concrete.png", new Vector3(2998.4f, 1.2f, 3004.8f));
            yield return Impact("fx_07_impact_wood.png", new Vector3(3000f, 1.2f, 3004.8f));
            yield return Impact("fx_08_impact_metal.png", new Vector3(3001.6f, 1.2f, 3004.8f));
            yield return Impact("fx_09_impact_ground.png", new Vector3(3000.8f, 0f, 3003.5f));
            // Several shots into each so the marks read, then a close look from the front.
            foreach (float x in new[] { 2998.4f, 3000f, 3001.6f })
                for (int i = 0; i < 3; i++)
                {
                    var at = new Vector3(x + UnityEngine.Random.Range(-0.35f, 0.35f), UnityEngine.Random.Range(0.5f, 1.8f), 3004.8f);
                    pc.LookAt(at);
                    yield return new WaitForSeconds(0.25f);
                    me.ServerFire(at - me.EyePosition);
                }
            yield return new WaitForSeconds(1f);
            shotCam.fieldOfView = 55f;
            shotCam.transform.SetPositionAndRotation(new Vector3(3000.8f, 1.4f, 3001.2f), Quaternion.LookRotation(new Vector3(3000f, 1.2f, 3005f) - new Vector3(3000.8f, 1.4f, 3001.2f)));
            Save("fx_10_marks_left_on_walls.png");
            Debug.Log("[ImpactCapture] shots in " + folder);
        }
    }
}
