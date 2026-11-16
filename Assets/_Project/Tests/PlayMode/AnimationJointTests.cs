using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    // Animation track (ADR-191): joint heights, angles and distances of the real roster character in the TestBox scene,
    // read from the posed skeleton every frame and written to Logs/anim_metrics.csv (the frame sequence). No sound is
    // played by these checks, but run them with the audio engine off (the Unity project's Disable Audio) anyway.
    public class AnimationJointTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = Frame.Crouch = Frame.Prone = Frame.Reload = false;
                return f;
            }
        }

        ScriptedInput _input;
        Combatant _me;
        PlayerController _pc;
        CombatantAnimation _anim;
        readonly StringBuilder _csv = new StringBuilder();
        string _scenario;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator Load()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            _me = Combatant.LocalPlayer; _pc = PlayerController.Local; _anim = _me.GetComponent<CombatantAnimation>();
            _me.ServerDamageFilter = _ => 0f;
            _csv.Clear();
            _csv.AppendLine("scenario,t,hipsY,chestY,headY,footLY,footRY,trunkPitch,weaponPitchErr,leftPalmToGrip,reloadW,speed,legTurn");
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            Directory.CreateDirectory("Logs");
            if (_csv.Length > 0) File.AppendAllText("Logs/anim_metrics.csv", _csv.ToString());
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        IEnumerator GiveRifle()
        {
            _me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            _me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = GameData.Weapons.Find(3).Ammo, Amount = 90 });
            yield return WaitUntil(() => _me.ActiveWeaponId == 3, 2f, "rifle in hand");
            yield return new WaitForSeconds(0.6f);
        }

        Transform B(HumanBodyBones b) => RigBones.Bone(_me, b);
        float Up(Transform t) => t.position.y - _pc.transform.position.y;
        Vector3 L(Transform t) => _pc.transform.InverseTransformPoint(t.position);
        // Degrees the barrel points above the player's look: the weapon mesh's own long axis (so it does not depend on how the hold was built),
        // pointing away from the right hand.
        float WeaponPitchError()
        {
            var gun = _me.GetComponent<WeaponVisual>().ModelFor(3);
            var hand = B(HumanBodyBones.RightHand);
            MeshFilter best = null; float longest = 0f;
            foreach (var mf in gun.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.sharedMesh.bounds.size.magnitude > longest) { longest = mf.sharedMesh.bounds.size.magnitude; best = mf; }
            var e = best.sharedMesh.bounds.extents;
            var axis = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
            var dir = best.transform.TransformDirection(axis).normalized;
            if (Vector3.Dot(dir, best.transform.TransformPoint(best.sharedMesh.bounds.center) - hand.position) < 0f) dir = -dir;
            return Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg - _pc.Pitch;
        }
        float LeftPalmToGrip()
        {
            var gun = _me.GetComponent<WeaponVisual>().ModelFor(3);
            var grip = gun != null ? gun.transform.Find(CombatantAnimation.LeftHandGripName) : null;
            if (grip == null) return float.NaN;
            var palm = Vector3.Lerp(B(HumanBodyBones.LeftHand).position, B(HumanBodyBones.LeftMiddleProximal).position, 0.8f);
            return Vector3.Distance(palm, grip.position);
        }

        // A review picture from the player's own camera, rendered off screen (WaitForEndOfFrame does not run in batch mode).
        void Shot(string name, float side = 3f, float height = 0.9f, float aimHeight = 0.5f)
        {
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 35; cam.clearFlags = CameraClearFlags.Skybox;
            var target = _pc.transform.position + Vector3.up * aimHeight;
            go.transform.position = _pc.transform.position + _pc.transform.right * side + Vector3.up * height;
            go.transform.LookAt(target);
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); RenderTexture.active = null;
            Directory.CreateDirectory("Logs/animshots");
            File.WriteAllBytes($"Logs/animshots/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(go);
        }

        // One CSV row per frame for `seconds`; returns the rows' values for the caller's assertions.
        IEnumerator Record(string scenario, float seconds, List<float[]> rows)
        {
            for (float end = Time.time + seconds; Time.time < end;)
            {
                yield return null;
                var v = _me.GetComponent<CombatantMotion>().LocalVelocity;
                var row = new[]
                {
                    Time.time, Up(B(HumanBodyBones.Hips)), Up(B(HumanBodyBones.Chest)), Up(B(HumanBodyBones.Head)), Up(B(HumanBodyBones.LeftFoot)), Up(B(HumanBodyBones.RightFoot)),
                    Mathf.Atan2(L(B(HumanBodyBones.Chest)).z - L(B(HumanBodyBones.Hips)).z, L(B(HumanBodyBones.Chest)).y - L(B(HumanBodyBones.Hips)).y) * Mathf.Rad2Deg,
                    WeaponPitchError(), LeftPalmToGrip(), _anim.ActionLayerWeight, new Vector2(v.x, v.z).magnitude, _anim.LegTurnDeg,
                };
                rows.Add(row);
                _csv.AppendLine(scenario + "," + string.Join(",", Array.ConvertAll(row, f => f.ToString("F3"))));
            }
        }

        [UnityTest]
        public IEnumerator Shouldered_BothHandsOnTheGun_AndTheBarrelPointsWhereThePlayerLooks()
        {
            yield return Load();
            yield return GiveRifle();
            _input.Frame.AimHeld = true;
            yield return WaitUntil(() => _me.AimMode == AimMode.Shoulder, 2f, "shouldered");
            yield return new WaitForSeconds(0.8f);
            var rows = new List<float[]>();
            yield return Record("shouldered", 0.5f, rows);
            float worstPalm = 0f, worstPitch = 0f;
            foreach (var r in rows) { worstPalm = Mathf.Max(worstPalm, r[8]); worstPitch = Mathf.Max(worstPitch, Mathf.Abs(r[7])); }
            Debug.Log($"[AnimMetrics] shouldered: left palm to grip max {worstPalm:F3} m, barrel pitch error max {worstPitch:F1} deg");
            Assert.LessOrEqual(worstPalm, 0.05f, "left palm on the LeftHandGrip (m)");
            Assert.LessOrEqual(worstPitch, 5f, "barrel along the look (deg)");
        }

        [UnityTest]
        public IEnumerator RunningForward_LiftsEachFoot_AndKeepsBothHandsOnTheGun()
        {
            yield return Load();
            yield return GiveRifle();
            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.8f);
            var rows = new List<float[]>();
            yield return Record("run", 1.5f, rows);
            float lo = float.MaxValue, hi = float.MinValue, worstPalm = 0f, speed = 0f;
            foreach (var r in rows) { lo = Mathf.Min(lo, r[4]); hi = Mathf.Max(hi, r[4]); worstPalm = Mathf.Max(worstPalm, r[8]); speed += r[10] / rows.Count; }
            Debug.Log($"[AnimMetrics] run: left ankle {lo:F3}..{hi:F3} m, palm to grip max {worstPalm:F3} m, mean speed {speed:F2}");
            Assert.Greater(speed, 2f, "actually running (mean m/s)");
            Assert.Greater(hi - lo, 0.1f, "the foot lifts");
            Assert.LessOrEqual(worstPalm, 0.06f, "left palm stays on the grip while running (m)");
        }

        [UnityTest]
        public IEnumerator MovingDiagonally_TurnsTheLegsTowardsTheMotion_WhileTheShouldersKeepTheAim()
        {
            yield return Load();
            yield return GiveRifle();
            float hipsFacing = 0f, shoulderFacing = 0f, legTurn = 0f; int n = 0;
            _input.Frame.Move = new Vector2(1, 1);
            yield return new WaitForSeconds(0.8f);
            for (float end = Time.time + 1.0f; Time.time < end; n++)
            {
                yield return null;
                hipsFacing += Facing(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
                shoulderFacing += Facing(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm);
                legTurn += _anim.LegTurnDeg;
            }
            hipsFacing /= n; shoulderFacing /= n; legTurn /= n;
            Debug.Log($"[AnimMetrics] diagonal: leg turn {legTurn:F1} hips line {hipsFacing:F1} shoulder line {shoulderFacing:F1} deg from the facing");
            Assert.That(legTurn, Is.InRange(30f, 65f), "the legs turn towards the motion (a wall in the room can swing the direction, so a range)");
            Assert.AreEqual(legTurn, hipsFacing, 18f, "the hips line follows the leg turn");
            Assert.AreEqual(legTurn, hipsFacing - shoulderFacing, 18f, "the shoulders hold the aim while the hips turn");
        }

        // Which way a left-right pair of joints faces, degrees from the player's facing (right positive).
        float Facing(HumanBodyBones left, HumanBodyBones right)
        {
            var across = _pc.transform.InverseTransformDirection(B(right).position - B(left).position);
            return Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg;
        }

        [UnityTest]
        public IEnumerator Reload_TheActionLayerCarriesBothHands_AndTheGripIsBackAfterwards()
        {
            yield return Load();
            yield return GiveRifle();
            _input.Frame.FirePressed = _input.Frame.FireHeld = true;
            yield return WaitUntil(() => _me.Mag0.Value < 30, 2f, "a shot fired");
            _input.Frame.FireHeld = false;
            yield return new WaitForSeconds(0.8f);
            var rows = new List<float[]>();
            _input.Frame.Reload = true;
            yield return Record("reload", 0.2f, rows);
            yield return WaitUntil(() => _me.Reloading.Value, 2f, "reloading");
            yield return Record("reload", 4.5f, rows);
            float maxW = 0f, maxPalm = 0f;
            foreach (var r in rows) { maxW = Mathf.Max(maxW, r[9]); if (r[9] > 0.9f) maxPalm = Mathf.Max(maxPalm, r[8]); }
            Debug.Log($"[AnimMetrics] reload: action layer weight max {maxW:F2}, left palm to grip max {maxPalm:F3} m while the clip runs");
            Assert.Greater(maxW, 0.95f, "the Action layer takes over");
            Assert.Greater(maxPalm, 0.15f, "the left hand leaves the gun for the magazine");
            yield return new WaitForSeconds(0.8f);
            var after = new List<float[]>();
            yield return Record("reload_after", 0.3f, after);
            Assert.Less(_anim.ActionLayerWeight, 0.01f, "the layer is released");
            foreach (var r in after) Assert.LessOrEqual(r[8], 0.06f, "left palm back on the grip");
        }

        [UnityTest]
        public IEnumerator Prone_BodyLiesLow_WeaponLevel_FeetOnTheFloor()
        {
            yield return Load();
            yield return GiveRifle();
            _input.Frame.Prone = true;
            yield return WaitUntil(() => _me.GetComponent<CombatantStance>().ProneAmount >= 0.999f, 2f, "prone");
            _input.Frame.AimHeld = true;
            yield return new WaitForSeconds(1.0f);
            var rows = new List<float[]>();
            yield return Record("prone", 0.5f, rows);
            var r0 = rows[rows.Count - 1];
            Shot("prone_tpp");
            Debug.Log($"[AnimMetrics] prone: hips {r0[1]:F3} chest {r0[2]:F3} head {r0[3]:F3} feet {r0[4]:F3}/{r0[5]:F3} trunk {r0[6]:F1} deg weapon pitch error {r0[7]:F1}");
            float eye = Stances.EyeHeight(1.6f, Stance.Prone, StanceRules.Current);
            Assert.Less(r0[3], eye + 0.15f, $"head joint no higher than the prone eye height {eye:F2} m + 0.15");
            Assert.Less(r0[1], 0.35f, "hips near the ground (m)");
            Assert.Greater(r0[6], 45f, "the trunk lies along the ground (deg from upright, 90 = flat; the chest is lifted to look ahead)");
            Assert.Less(Mathf.Max(r0[4], r0[5]), 0.4f, "both ankles close to the floor (m)");
            Assert.LessOrEqual(Mathf.Abs(r0[7]), 8f, "barrel along the look");
        }

        // Both ankles' height over the ground under each (the ramp's top), minus the rest height of an ankle on flat ground.
        float[] SoleErrors(Collider ground, float rest)
        {
            var errors = new float[2];
            int i = 0;
            foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
            {
                var p = B(bone).position;
                Assert.IsTrue(ground.Raycast(new Ray(p + Vector3.up * 1f, Vector3.down), out var hit, 3f), "ground under the foot");
                errors[i++] = p.y - hit.point.y - rest;
            }
            return errors;
        }

        [UnityTest] public IEnumerator On25DegreeSlope_BothFeetStayOnTheGround() { yield return OnSlope(25f, 0.03f); }
        [UnityTest] public IEnumerator On35DegreeSlope_BothFeetStayWithin5Cm() { yield return OnSlope(35f, 0.05f); }

        IEnumerator OnSlope(float SlopeDeg, float tolerance)
        {
            yield return Load();
            var slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slope.name = "TestSlope";
            slope.transform.position = new Vector3(-12f, -0.9f, -26f);
            slope.transform.rotation = Quaternion.Euler(-SlopeDeg, 0f, 0f);       // rises towards +z
            slope.transform.localScale = new Vector3(8f, 2f, 14f);
            Physics.SyncTransforms();
            var col = slope.GetComponent<Collider>();
            Assert.IsTrue(col.Raycast(new Ray(new Vector3(-12f, 5f, -26f), Vector3.down), out var top, 10f));
            float flatRest = _anim.FootRest; // ankle height over the floor standing on flat ground, measured in the saved pose
            _pc.Teleport(top.point + Vector3.up * 0.05f);
            _pc.LookAt(top.point + new Vector3(0f, 1.6f, 10f)); // up the slope: one foot is uphill of the other when the stance is staggered
            yield return new WaitForSeconds(1.2f);
            var err = SoleErrors(col, flatRest / Mathf.Cos(SlopeDeg * Mathf.Deg2Rad)); // vertical offset of an ankle over a sloped sole
            Debug.Log($"[AnimMetrics] slope {SlopeDeg} deg: sole errors left {err[0]:F3} right {err[1]:F3} m, plant {_anim.FootPlantLeft:F3}/{_anim.FootPlantRight:F3} pelvis drop {_anim.PlantDrop:F3} | {_anim.PlantDebug[0]} | {_anim.PlantDebug[1]} | capsule y {_pc.transform.position.y:F3} ground at origin {top.point.y:F3}");
            Assert.LessOrEqual(Mathf.Abs(err[0]), tolerance, "left foot on the slope (m)");
            Assert.LessOrEqual(Mathf.Abs(err[1]), tolerance, "right foot on the slope (m)");
            UnityEngine.Object.Destroy(slope);
        }

        [UnityTest]
        public IEnumerator Crouch_HeadComesDownToTheCapsule()
        {
            yield return Load();
            yield return GiveRifle();
            _input.Frame.Crouch = true;
            yield return WaitUntil(() => _me.GetComponent<CombatantStance>().CrouchAmount >= 0.999f, 2f, "crouched");
            yield return new WaitForSeconds(0.6f);
            var rows = new List<float[]>();
            yield return Record("crouch", 0.4f, rows);
            var r = rows[rows.Count - 1];
            Debug.Log($"[AnimMetrics] crouch: hips {r[1]:F3} head {r[3]:F3} feet {r[4]:F3}/{r[5]:F3} palm {r[8]:F3}");
            Assert.That(r[3], Is.InRange(0.9f, 1.3f), "head height crouched (m)");
            Assert.Less(Mathf.Min(r[4], r[5]), 0.2f, "a foot on the floor");
        }
    }
}
