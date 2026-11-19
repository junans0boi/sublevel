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
    // ADR-395 (#196): a killing shot turns the victim into a ragdoll pushed along the shot, which then freezes with no
    // physics left; the combatant itself (what the server judges) stays where it died. The capture test leaves review
    // shots of three deaths (open floor, top of a stair, against a wall):
    //   tools/unity.sh test playmode Game.Tests.RagdollPlayTests   (shots in Logs/fx/r5_ragdoll_*.png)
    public class RagdollPlayTests
    {
        const int Rifle = 3;

        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        PlayerController _pc;
        Combatant _me;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            _pc = PlayerController.Local;
            _me = Combatant.LocalPlayer;
            _me.ServerDamageFilter = _ => 0f;
            _me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return new WaitForSeconds(0.8f);
            _me.StanceSpreadFactor = 0f;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static GameObject Box(string name, Vector3 centre, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = centre;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().material.color = color;
            return go;
        }

        Combatant Victim(int skip)
        {
            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsPlayer || c.Dead.Value) continue;
                if (skip-- > 0) continue;
                foreach (var part in c.GetComponents<MonoBehaviour>())
                    if (part.GetType().Name == "TrainingDummy" || part.GetType().Name == "BotController") part.enabled = false;
                return c;
            }
            Assert.Fail("not enough living targets in TestBox");
            return null;
        }

        // Puts the victim at `at` facing the shooter `shooterDistance` m away along -z, and kills it with one rifle shot to the chest.
        IEnumerator Kill(Combatant victim, Vector3 at, float shooterDistance = 5f)
        {
            victim.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Vector3.back));
            _pc.Teleport(at + Vector3.back * shooterDistance + Vector3.up * 0.1f);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.6f);
            var chest = victim.transform.position + Vector3.up * 1.2f;
            _pc.LookAt(chest);
            yield return new WaitForSeconds(0.2f);
            victim.ServerTakeDamage(Mathf.Max(0f, victim.Hp.Value - 1f));
            yield return null;
            Assert.IsTrue(_me.ServerFire(chest - _me.EyePosition), "shot fired");
            float end = Time.realtimeSinceStartup + 3f;
            while (!victim.Dead.Value)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("the shot did not kill");
                yield return null;
            }
        }

        static IEnumerator UntilFrozen(CombatantRagdoll rig)
        {
            float end = Time.realtimeSinceStartup + RagdollRules.Current.MaxSeconds + 3f;
            while (!rig.Frozen)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("ragdoll never froze");
                yield return null;
            }
            yield return null; // deferred Destroy of the bodies
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator KillingShot_RagdollsAlongTheShot_ThenFreezesInPlace()
        {
            var origin = new Vector3(3000f, 0f, 3000f);
            Box("Floor", origin + Vector3.down * 0.5f, new Vector3(40f, 1f, 40f), new Color(0.38f, 0.36f, 0.32f));
            var victim = Victim(0);
            var rig = victim.GetComponentInChildren<CombatantRagdoll>();
            var anim = victim.GetComponent<CombatantAnimation>();
            Assert.IsNotNull(rig, "victim has no ragdoll (TestBoxBuilder.Build)");
            yield return Kill(victim, origin);
            var diedAt = victim.transform.position;
            var hipsAt = rig.Hips.position;
            float end = Time.realtimeSinceStartup + 1f;
            while (!rig.Simulating)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no ragdoll after the kill");
                yield return null;
            }
            Assert.AreEqual(CombatantAnimation.Death, anim.BaseState);
            Assert.Greater(Vector3.Dot(rig.LastImpulse, Vector3.forward), 0f, $"pushed along the shot (+z), impulse {rig.LastImpulse}");
            Assert.LessOrEqual(CombatantRagdoll.SimulatingCount, RagdollRules.Current.MaxActive);
            yield return UntilFrozen(rig);
            var moved = rig.Hips.position - hipsAt;
            Debug.Log($"[Ragdoll] flat: hips moved {moved} (y {rig.Hips.position.y - origin.y:F2} over the floor), impulse {rig.LastImpulse}");
            Assert.Greater(moved.z, 0.05f, "fell away from the shooter");
            Assert.Less(rig.Hips.position.y - origin.y, 0.6f, "lying on the floor");
            Assert.Greater(rig.Hips.position.y - origin.y, 0.0f, "not through the floor");
            Assert.AreEqual(0, victim.GetComponentsInChildren<Rigidbody>().Length, "no physics left once frozen");
            Assert.AreEqual(0, victim.GetComponentsInChildren<Joint>().Length);
            Assert.Less(Vector3.Distance(victim.transform.position, diedAt), 0.01f, "the combatant (server position) did not move");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator CaptureThreeDeaths()
        {
            string folder = Path.GetFullPath("Logs/fx");
            Directory.CreateDirectory(folder);
            var floorColor = new Color(0.4f, 0.38f, 0.34f);

            // 1. Open floor.
            var flat = new Vector3(3000f, 0f, 3000f);
            Box("FlatFloor", flat + Vector3.down * 0.5f, new Vector3(30f, 1f, 30f), floorColor);
            // 2. Top of a stair going down away from the shooter: ten 0.3 x 0.18 m steps.
            var stair = new Vector3(3100f, 0f, 3000f);
            Box("StairTop", stair + new Vector3(0f, -0.5f, -7.5f + 0.35f), new Vector3(6f, 1f, 15f), floorColor);
            for (int i = 0; i < 10; i++)
            {
                float top = -0.18f * (i + 1);
                Box("Step" + i, stair + new Vector3(0f, top - 2f, 0.35f + 0.15f + 0.3f * i), new Vector3(3f, 4f, 0.3f), new Color(0.5f, 0.48f, 0.44f));
            }
            Box("StairBottom", stair + new Vector3(0f, -1.8f - 0.5f, 3.35f + 6f), new Vector3(6f, 1f, 12f), floorColor);
            // 3. Against a wall, 0.45 m behind the victim.
            var wall = new Vector3(3200f, 0f, 3000f);
            Box("WallFloor", wall + Vector3.down * 0.5f, new Vector3(30f, 1f, 30f), floorColor);
            Box("Wall", wall + new Vector3(0f, 1.5f, 0.45f + 0.15f), new Vector3(6f, 3f, 0.3f), new Color(0.55f, 0.55f, 0.58f));
            Physics.SyncTransforms();

            var shotCam = new GameObject("RagdollShotCam").AddComponent<Camera>();
            shotCam.enabled = false;
            shotCam.CopyFrom(_pc.Camera);
            shotCam.fieldOfView = 45f;
            void Save(string name, Vector3 from, Vector3 look)
            {
                shotCam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from));
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
                File.WriteAllBytes(Path.Combine(folder, name), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(rt);
                UnityEngine.Object.Destroy(tex);
            }

            var spots = new[] { ("flat", flat), ("stairs", stair), ("wall", wall) };
            for (int s = 0; s < spots.Length; s++)
            {
                var (name, at) = spots[s];
                var victim = Victim(0);
                var rig = victim.GetComponentInChildren<CombatantRagdoll>();
                yield return Kill(victim, at);
                var side = at + new Vector3(4.2f, 1.6f, name == "wall" ? -1.6f : 1.2f); // in front of the wall, not behind it
                var focus = at + new Vector3(0f, 0.5f, name == "stairs" ? 1.2f : 0.4f);
                yield return new WaitForSeconds(0.25f);
                Save($"r5_ragdoll_{name}_falling.png", side, focus);
                yield return UntilFrozen(rig);
                Save($"r5_ragdoll_{name}.png", side, focus);
                Save($"r5_ragdoll_{name}_front.png", at + new Vector3(0.8f, 1.7f, -3.2f), focus);
                var hips = rig.Hips.position;
                Debug.Log($"[Ragdoll] {name}: hips {hips - at} from the death position, impulse {rig.LastImpulse}, crate marker at {victim.transform.position - at}");
                Assert.Greater(hips.y - at.y, name == "stairs" ? -2.2f : -0.05f, name + ": hips not through the floor");
                if (name == "wall") Assert.Less(hips.z - at.z, 0.45f, "wall: did not pass through the wall");
            }
            UnityEngine.Object.Destroy(shotCam.gameObject);
        }
    }
}
