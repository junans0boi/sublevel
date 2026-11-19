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
    // Review shots and checks of the r5 hit effects (#197 blood, #198 holes and debris, #201 camera shake). Run on purpose with
    //   FX_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.FxImpactCaptureTests
    [Explicit, Category("Capture")]
    public class FxImpactCaptureTests
    {
        const int Rifle = 3;

        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        // Where the main camera really is after every LateUpdate (the shake is on only between LateUpdate and the next Update).
        [DefaultExecutionOrder(32000)]
        sealed class CameraProbe : MonoBehaviour
        {
            public Vector3 Position;
            public Quaternion Rotation;
            void LateUpdate()
            {
                var cam = Camera.main;
                if (cam == null) return;
                Position = cam.transform.position;
                Rotation = cam.transform.rotation;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            GameSettings.Revert();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static GameObject Box(string name, Vector3 at, Vector3 scale, Vector3 faceTo, Color color, BulletSurface? surface)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = at;
            go.transform.localScale = scale;
            var face = new Vector3(faceTo.x - at.x, 0f, faceTo.z - at.z);
            if (face.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(face);
            go.GetComponent<Renderer>().material.color = color;
            if (surface.HasValue) go.AddComponent<BulletSurfaceTag>().Surface = surface.Value;
            return go;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureHitEffects()
        {
            string folder = Environment.GetEnvironmentVariable("FX_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/fx";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            string P(string n) => Path.Combine(folder, n);
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
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 90 });
            yield return new WaitForSeconds(0.8f);
            me.StanceSpreadFactor = 0f;
            Combatant dummy = null;
            foreach (var c in Combatant.All) if (!c.IsPlayer) { dummy = c; break; }
            Assert.NotNull(dummy, "TestBox has a dummy");
            foreach (var part in dummy.GetComponents<MonoBehaviour>())
                if (part.GetType().Name == "TrainingDummy") part.enabled = false;
            dummy.ServerDamageFilter = _ => 0f;
            GameSettings.Preview(SettingId.Blood, (float)BloodLevel.Normal);
            GameSettings.Preview(SettingId.CameraShake, 1f);

            // The arena: a concrete floor far from everything, a wall behind the dummy, four material slabs, a pond.
            var o = new Vector3(3000f, 0f, 3000f);
            Box("Floor", o + new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), Vector3.zero, new Color(0.42f, 0.41f, 0.38f), BulletSurface.Concrete);
            Box("BackWall", o + new Vector3(-6f, 1.5f, 7.2f), new Vector3(4f, 3f, 0.3f), o + new Vector3(-6f, 1.5f, 0f), new Color(0.72f, 0.71f, 0.68f), BulletSurface.Concrete);
            var slabs = new (string name, float x, BulletSurface s, Color c)[]
            {
                ("Concrete", 1.6f, BulletSurface.Concrete, new Color(0.62f, 0.62f, 0.6f)),
                ("Wood", 3.2f, BulletSurface.Wood, new Color(0.5f, 0.34f, 0.2f)),
                ("Metal", 4.8f, BulletSurface.Metal, new Color(0.45f, 0.5f, 0.55f)),
                ("Glass", 6.4f, BulletSurface.Glass, new Color(0.25f, 0.32f, 0.38f)),
            };
            foreach (var s in slabs)
                Box(s.name, o + new Vector3(s.x, 1.1f, 5f), new Vector3(1.4f, 2.2f, 0.4f), o + new Vector3(s.x, 1.1f, -10f), s.c, s.s);
            WaterVolume.Create(null, new Vector2(o.x + 3f, o.z - 8f), new Vector2(8f, 6f), 0.35f, true);
            dummy.transform.position = o + new Vector3(-6f, 0.05f, 5.8f);
            Physics.SyncTransforms();
            pc.Teleport(o + new Vector3(-6f, 0.1f, -1f));
            yield return new WaitForSeconds(0.8f);

            var shotCam = new GameObject("FxShotCam").AddComponent<Camera>();
            shotCam.enabled = false;
            shotCam.CopyFrom(pc.Camera);
            void Save(string name, Vector3 at, Vector3 lookAt, float fov)
            {
                shotCam.fieldOfView = fov;
                shotCam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(lookAt - at));
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
            IEnumerator Fire(Vector3 at)
            {
                pc.LookAt(at);
                yield return new WaitForSeconds(0.22f);
                Assert.IsTrue(me.ServerFire(at - me.EyePosition), "fired at " + at);
            }

            // ---- #197 blood: spray, stains on the wall behind and the floor, a drip trail, a pool where it went down ----
            var chest = dummy.transform.position + Vector3.up * 1.15f;
            int sprays = ImpactFx.BloodSprays, stains = ImpactFx.BloodStains;
            yield return Fire(chest);
            yield return new WaitForSeconds(0.07f); // batchmode frames are short: let the drops fly apart
            var sideCam = dummy.transform.position + new Vector3(2.6f, 1.4f, -1.2f);
            Save("r5_01_blood_spray.png", sideCam, chest, 45f);
            Assert.Greater(ImpactFx.BloodSprays, sprays, "a body hit sprays blood");
            for (int i = 0; i < 5; i++) yield return Fire(chest + new Vector3(UnityEngine.Random.Range(-0.12f, 0.12f), UnityEngine.Random.Range(-0.3f, 0.35f), 0f));
            yield return Fire(dummy.transform.position + Vector3.up * 1.62f); // the head
            yield return new WaitForSeconds(1.2f);
            Assert.Greater(ImpactFx.BloodStains, stains + 2, "the wall behind and the floor catch blood");
            Save("r5_02_blood_stains.png", dummy.transform.position + new Vector3(2.2f, 1.6f, -2.4f), dummy.transform.position + new Vector3(0f, 0.8f, 1.2f), 55f);

            // The hit body walks off: drops every few steps.
            int beforeTrail = ImpactFx.BloodStains;
            for (int i = 0; i < 8; i++)
            {
                dummy.transform.position += new Vector3(0.55f, 0f, -0.35f);
                Physics.SyncTransforms();
                yield return new WaitForSeconds(0.75f);
            }
            Assert.Greater(ImpactFx.BloodStains, beforeTrail + 2, "a hit body leaves drops as it moves");
            // Then it goes down where it stands: a pool.
            dummy.ServerDamageFilter = null;
            int beforePool = ImpactFx.BloodStains;
            for (int i = 0; i < 12 && !dummy.Dead.Value; i++) yield return Fire(dummy.transform.position + Vector3.up * 1.62f);
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(dummy.Dead.Value, "the dummy went down");
            Assert.Greater(ImpactFx.BloodStains, beforePool, "a pool where it went down");
            var trailMid = o + new Vector3(-3.8f, 0f, 3.2f);
            Save("r5_03_blood_trail_pool.png", trailMid + new Vector3(-1.2f, 3.4f, -3.6f), trailMid, 55f);

            // 피 효과 끔: no spray and no stains.
            GameSettings.Preview(SettingId.Blood, (float)BloodLevel.Off);
            int offSprays = ImpactFx.BloodSprays, offStains = ImpactFx.BloodStains;
            ImpactFx.Blood(chest, Vector3.forward, false, null);
            Assert.AreEqual(offSprays, ImpactFx.BloodSprays, "off shows no blood");
            Assert.AreEqual(offStains, ImpactFx.BloodStains, "off leaves no stains");
            GameSettings.Preview(SettingId.Blood, (float)BloodLevel.Normal);

            // ---- #198 holes and debris per surface ----
            pc.Teleport(o + new Vector3(4f, 0.1f, 0f));
            yield return new WaitForSeconds(0.8f);
            int n = 5;
            foreach (var s in slabs)
            {
                var hitAt = o + new Vector3(s.x, 1.25f, 4.8f);
                int bursts = ImpactFx.DebrisBursts;
                yield return Fire(hitAt);
                yield return new WaitForSeconds(0.09f);
                Save($"r5_{n++:00}_debris_{s.name.ToLowerInvariant()}.png", hitAt + new Vector3(1.1f, 0.35f, -1.3f), hitAt, 50f);
                Assert.Greater(ImpactFx.DebrisBursts, bursts, s.name + " throws debris");
            }
            var groundAt = o + new Vector3(4f, 0f, 3f);
            yield return Fire(groundAt);
            yield return new WaitForSeconds(0.09f);
            Save($"r5_{n++:00}_debris_ground.png", groundAt + new Vector3(1.2f, 0.8f, -1.2f), groundAt, 50f);
            foreach (var s in slabs)
                for (int i = 0; i < 4; i++)
                    yield return Fire(o + new Vector3(s.x + UnityEngine.Random.Range(-0.45f, 0.45f), UnityEngine.Random.Range(0.4f, 2f), 4.8f));
            for (int i = 0; i < 4; i++) yield return Fire(o + new Vector3(4f + UnityEngine.Random.Range(-1.5f, 1.5f), 0f, UnityEngine.Random.Range(2f, 3.5f)));
            yield return new WaitForSeconds(1.2f);
            Assert.LessOrEqual(ImpactFx.LiveMarks, ImpactFx.MarkCapacity);
            Save($"r5_{n++:00}_marks_close.png", o + new Vector3(4f, 1.5f, 1.4f), o + new Vector3(4f, 1.0f, 5f), 60f);

            // Water: a splash instead of a hole.
            int splashes = ImpactFx.Splashes;
            pc.Teleport(o + new Vector3(3f, 0.1f, -3.5f));
            yield return new WaitForSeconds(0.8f);
            var waterAt = o + new Vector3(3f, 0f, -7.5f);
            yield return Fire(waterAt);
            yield return new WaitForSeconds(0.18f);
            Save($"r5_{n++:00}_water_splash.png", waterAt + new Vector3(2.4f, 1.2f, 1.8f), waterAt + Vector3.up * 0.6f, 50f);
            Assert.Greater(ImpactFx.Splashes, splashes, "a shot into the pond splashes");

            // ---- #201 camera shake: a blast nearby, not far; the aim is untouched; the setting at 0% stops it ----
            var probe = new GameObject("CameraProbe").AddComponent<CameraProbe>();
            pc.Teleport(o + new Vector3(-2f, 0.1f, -4f));
            pc.LookAt(o + new Vector3(-2f, 1.4f, 6f));
            yield return new WaitForSeconds(2.5f); // earlier shots' trauma drains
            Assert.AreEqual(0f, pc.ShakeTrauma, 1e-4f);
            var calm = probe.Rotation;
            Save($"r5_{n++:00}_shake_calm.png", probe.Position, probe.Position + calm * Vector3.forward, pc.Camera.fieldOfView);
            PlayerController.ShakeFromBlast(o + new Vector3(-2f, 0f, 60f), 1f);
            Assert.AreEqual(0f, pc.ShakeTrauma, 1e-4f, "a blast 60 m away does not shake");
            PlayerController.ShakeFromBlast(o + new Vector3(0f, 0f, 0f), 1f);
            Assert.Greater(pc.ShakeTrauma, 0.6f, "a blast 4.5 m away shakes hard");
            float worst = 0f;
            Quaternion worstRot = calm;
            for (int i = 0; i < 12; i++)
            {
                yield return null;
                float a = Quaternion.Angle(calm, probe.Rotation);
                if (a > worst) { worst = a; worstRot = probe.Rotation; }
            }
            Debug.Log($"[FxCapture] blast shake worst {worst:F2} deg");
            Assert.Greater(worst, 0.5f, "the view moves");
            Assert.Less(Quaternion.Angle(calm, pc.Camera.transform.rotation), 0.01f, "between LateUpdate and Update the camera (aim) is unshaken");
            Save($"r5_{n++:00}_shake_blast.png", probe.Position, probe.Position + worstRot * Vector3.forward, pc.Camera.fieldOfView);
            yield return new WaitForSeconds(1.5f);

            // A hit taken (from the dummy's replacement: the player shoots itself through the server path is not possible,
            // so the hit path is the same call CombatantView makes) and a landing from 5 m.
            PlayerController.Shake(CameraShake.Hit(false));
            Assert.Greater(pc.ShakeTrauma, 0.2f, "a hit shakes");
            yield return new WaitForSeconds(1.5f);
            pc.Teleport(o + new Vector3(-2f, 5.5f, -4f));
            float landBy = Time.time + 4f;
            float landed = 0f;
            while (Time.time < landBy && landed <= 0f)
            {
                yield return null;
                landed = pc.ShakeTrauma;
            }
            Assert.Greater(landed, 0.1f, "a 5 m drop shakes on landing");
            yield return new WaitForSeconds(1.5f);
            GameSettings.Preview(SettingId.CameraShake, 0f);
            PlayerController.ShakeFromBlast(o + new Vector3(-2f, 0f, -3f), 1f);
            float off = 0f;
            var still = probe.Rotation;
            for (int i = 0; i < 8; i++) { yield return null; off = Mathf.Max(off, Quaternion.Angle(still, probe.Rotation)); }
            Assert.Less(off, 0.05f, "0% = no shake");
            Debug.Log("[FxCapture] shots in " + folder);
        }
    }
}
