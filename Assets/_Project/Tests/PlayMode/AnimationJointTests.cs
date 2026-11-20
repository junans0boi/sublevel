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

        string _savedLook;

        IEnumerator Load()
        {
            // The saved look is editor-wide (PlayerPrefs): the default model, whatever an earlier run left there; put back in TearDown.
            _savedLook = PlayerPrefs.GetString(PlayerAppearanceStore.Key, "");
            PlayerAppearanceStore.Save(PlayerAppearance.Default); // explicit: Load() also reads the old ui.char.index key, which another run may have left
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
            if (_savedLook != null) { if (_savedLook == "") PlayerAppearanceStore.Clear(); else PlayerPrefs.SetString(PlayerAppearanceStore.Key, _savedLook); }
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
        void Shot(string name, float side = 3f, float height = 0.9f, float aimHeight = 0.5f, float front = 0f)
        {
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 35; cam.clearFlags = CameraClearFlags.Skybox;
            var target = _pc.transform.position + Vector3.up * aimHeight;
            go.transform.position = _pc.transform.position + _pc.transform.right * side + _pc.transform.forward * (front == 0f ? 0f : -front) + Vector3.up * height;
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
            Shot("shouldered_side"); Shot("shouldered_front", 0f, 1.3f, 1.1f, -3f);
            yield return Record("shouldered", 0.5f, rows);
            float worstPalm = 0f, worstPitch = 0f;
            foreach (var r in rows) { worstPalm = Mathf.Max(worstPalm, r[8]); worstPitch = Mathf.Max(worstPitch, Mathf.Abs(r[7])); }
            Debug.Log($"[AnimMetrics] shouldered: left palm to grip max {worstPalm:F3} m, barrel pitch error max {worstPitch:F1} deg");
            Assert.LessOrEqual(worstPalm, 0.05f, "left palm on the LeftHandGrip (m)");
            Assert.LessOrEqual(worstPitch, 5f, "barrel along the look (deg)");
        }

        // Roster characters are saved in a T-pose: the hold must still point each weapon where the pose says (it pointed 90 degrees off to the right), and class the guns by length.
        [UnityTest]
        public IEnumerator LowReady_PointsTheMuzzleAheadAndDown_AndGunsAreClassedByLength()
        {
            yield return Load();
            yield return GiveRifle();
            var hand = B(HumanBodyBones.RightHand);
            var model = _me.GetComponent<WeaponVisual>().ModelFor(3);
            MeshFilter best = null; float longest = 0f;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.sharedMesh.bounds.size.magnitude > longest) { longest = mf.sharedMesh.bounds.size.magnitude; best = mf; }
            var e = best.sharedMesh.bounds.extents;
            var local = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
            var dir = best.transform.TransformDirection(local).normalized;
            if (Vector3.Dot(dir, best.transform.TransformPoint(best.sharedMesh.bounds.center) - hand.position) < 0f) dir = -dir;
            var d = _pc.transform.InverseTransformDirection(dir);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pitch = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
            Debug.Log($"[AnimMetrics] low ready rifle muzzle yaw {yaw:F1} pitch {pitch:F1}; rifle pistol-grip {_anim.IsPistolGrip(3)}, pistol pistol-grip {_anim.IsPistolGrip(1)}");
            Assert.That(Mathf.Abs(yaw), Is.LessThan(25f), "muzzle ahead (not across the body)");
            Assert.That(pitch, Is.InRange(-40f, 0f), "muzzle low, down");
            Assert.IsFalse(_anim.IsPistolGrip(3), "a rifle is not held like a pistol");
            Assert.IsTrue(_anim.IsPistolGrip(1), "a pistol is");
        }

        [UnityTest]
        public IEnumerator RunningForward_LiftsEachFoot_AndKeepsBothHandsOnTheGun()
        {
            yield return Load();
            yield return GiveRifle();
            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.8f);
            var rows = new List<float[]>();
            for (int i = 0; i < 4; i++) { Shot($"run_{i}", 3f, 0.9f, 0.9f); yield return new WaitForSeconds(0.12f); }
            yield return Record("run", 1.5f, rows);
            float lo = float.MaxValue, hi = float.MinValue, worstPalm = 0f, speed = 0f;
            foreach (var r in rows) { lo = Mathf.Min(lo, r[4]); hi = Mathf.Max(hi, r[4]); worstPalm = Mathf.Max(worstPalm, r[8]); speed += r[10] / rows.Count; }
            Debug.Log($"[AnimMetrics] run position {_pc.transform.position} input {_input.Frame.Move} sprinting {_pc.Sprinting}");
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
            // ADR-391: the diagonal is its own clip; the legs only turn the few degrees onto it, and the shoulders hold the aim.
            Assert.That(Mathf.Abs(legTurn), Is.LessThan(25f), "the legs turn only onto the nearest clip direction");
            Assert.AreEqual(0f, shoulderFacing, 15f, "the shoulders hold the aim");
        }

        // Which way a left-right pair of joints faces, degrees from the player's facing (right positive).
        float Facing(HumanBodyBones left, HumanBodyBones right)
        {
            var across = _pc.transform.InverseTransformDirection(B(right).position - B(left).position);
            return Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg;
        }

        Animator BodyAnimator => _me.GetComponentInChildren<Animator>();
        float HandAhead(HumanBodyBones hand) => L(B(hand)).z - L(B(HumanBodyBones.Chest)).z;

        [UnityTest]
        public IEnumerator BareHands_MoveWithTheBareHandClips_AndTheRifleHoldComesOnlyWithAGun()
        {
            yield return Load();
            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.8f);
            float behind = float.MaxValue; bool unarmedState = true; float upperMax = 0f, aimedMax = 0f;
            for (float end = Time.time + 1.2f; Time.time < end;)
            {
                yield return null;
                float z = Mathf.Min(L(B(HumanBodyBones.LeftHand)).z, L(B(HumanBodyBones.RightHand)).z) - L(B(HumanBodyBones.Hips)).z;
                behind = Mathf.Min(behind, z);
                unarmedState &= BodyAnimator.GetCurrentAnimatorStateInfo(0).IsName("Unarmed") || BodyAnimator.GetNextAnimatorStateInfo(0).IsName("Unarmed");
                upperMax = Mathf.Max(upperMax, BodyAnimator.GetLayerWeight(1));
                aimedMax = Mathf.Max(aimedMax, _anim.RifleHoldShown);
            }
            Debug.Log($"[AnimMetrics] bare hands run: back-most hand {behind:F3} m behind the hips, upper layer weight max {upperMax:F2}, rifle hold max {aimedMax:F2}, unarmed state {unarmedState}, base {_anim.BaseState}");
            Assert.IsTrue(unarmedState, "the animator plays the Unarmed tree");
            Assert.AreEqual(CombatantAnimation.Locomotion, _anim.BaseState, "logically still locomotion");
            Assert.AreEqual(0f, upperMax, 1e-3f, "no upper-layer weapon pose without a weapon");
            Assert.AreEqual(0f, aimedMax, 1e-3f, "no rifle hold without a weapon");
            Assert.Less(behind, -0.05f, "an arm swings back past the hips: bare-hand jog, not a rifle pose");
            Shot("bare_run", 3f, 0.9f, 0.9f);

            yield return GiveRifle();
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(BodyAnimator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), "with a gun the rifle tree plays");
            Assert.Greater(_anim.RifleHoldShown, 0.9f, "the rifle hold shows with a gun");
        }

        // The body faces the way the controller faces (shoulder-line facing, 1 = forward) and a punch turns the shoulders the right way.
        [UnityTest]
        public IEnumerator BareHands_FaceForward_Standing_Walking_AndAPunchTwistsTheShouldersTheRightWay()
        {
            yield return Load();
            float Dot() { var across = (B(HumanBodyBones.RightUpperArm).position - B(HumanBodyBones.LeftUpperArm).position).normalized; return Vector3.Dot(Vector3.Cross(across, Vector3.up), _pc.transform.forward); }
            float Yaw() => Facing(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm); // degrees, right positive
            float stand = 0f; int n = 0;
            for (float end = Time.time + 0.6f; Time.time < end; n++) { yield return null; stand += Dot(); }
            stand /= n;
            Shot("bare_front", 0f, 1.0f, 1.0f, -3.5f); Shot("bare_side", 3f, 1.0f, 1.0f);
            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.6f);
            float walk = 0f, wMin = 999f, wMax = -999f; n = 0;
            for (float end = Time.time + 0.8f; Time.time < end; n++) { yield return null; walk += Dot(); wMin = Mathf.Min(wMin, Yaw()); wMax = Mathf.Max(wMax, Yaw()); }
            walk /= n;
            Debug.Log($"[AnimMetrics] walking shoulder yaw {wMin:F1}..{wMax:F1}");
            _input.Frame.Move = Vector2.zero;
            yield return new WaitForSeconds(0.6f);
            float rest = 0f; n = 0;
            for (float end = Time.time + 0.3f; Time.time < end; n++) { yield return null; rest += Yaw(); }
            rest /= n;
            bool right = false; _me.Punched += (in Combatant.PunchResult p) => right = p.Right;
            _input.Frame.FirePressed = true;
            float minYaw = 999f, maxYaw = -999f;
            for (float end = Time.time + 0.5f; Time.time < end;) { yield return null; minYaw = Mathf.Min(minYaw, Yaw()); maxYaw = Mathf.Max(maxYaw, Yaw()); }
            Debug.Log($"[AnimMetrics] bare hands facing: standing {stand:F2}, walking {walk:F2}; shoulder yaw at rest {rest:F1}, during a {(right ? "right" : "left")} punch {minYaw:F1}..{maxYaw:F1}");
            Assert.Greater(stand, 0.85f, "standing bare-handed, the body faces forward");
            Assert.Greater(walk, 0.8f, "walking bare-handed, the body faces forward");
            if (right) Assert.Less(minYaw, rest - 5f, "a right punch brings the right shoulder forward (shoulder line turns left)");
            else Assert.Greater(maxYaw, rest + 5f, "a left punch brings the left shoulder forward (shoulder line turns right)");
            Shot("bare_punch", 2.5f, 1.3f, 1.2f);
        }

        [UnityTest]
        public IEnumerator BareHands_RunningForward_ShouldersAndHipsFaceTheMotion()
        {
            yield return Load();
            _input.Frame.Move = new Vector2(0, 1); _input.Frame.Sprint = true;
            yield return new WaitForSeconds(1.0f);
            float sMin = 999f, sMax = -999f, sSum = 0f, hSum = 0f, hMin = 999f, hMax = -999f, dir = 0f; int n = 0; var last = _pc.transform.position;
            for (float end = Time.time + 1.5f; Time.time < end; n++)
            {
                yield return null;
                float sh = Facing(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm), hp = Facing(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
                sSum += sh; hSum += hp; sMin = Mathf.Min(sMin, sh); sMax = Mathf.Max(sMax, sh); hMin = Mathf.Min(hMin, hp); hMax = Mathf.Max(hMax, hp);
                var d = _pc.transform.position - last; last = _pc.transform.position;
                if (d.sqrMagnitude > 1e-8f) dir += Vector3.SignedAngle(_pc.transform.forward, d, Vector3.up);
            }
            Debug.Log($"[AnimMetrics] bare run: sprinting {_pc.Sprinting} shoulders mean {sSum / n:F1} ({sMin:F1}..{sMax:F1}) hips mean {hSum / n:F1} ({hMin:F1}..{hMax:F1}) travel vs facing {dir / n:F1}");
            Shot("bare_run", 3f, 1.0f, 1.0f);
            Assert.That(sSum / n, Is.InRange(-8f, 8f), "running bare-handed, the shoulders face the way we go");
            Assert.That(hSum / n, Is.InRange(-8f, 8f), "running bare-handed, the hips face the way we go");
        }

        [UnityTest]
        public IEnumerator Punch_TheFistIsOutWithin50MsOfTheServersJudgement_AndTheVictimFlinches()
        {
            yield return Load();
            float eventAt = -1f; bool right = false;
            _me.Punched += (in Combatant.PunchResult p) => { eventAt = Time.time; right = p.Right; };
            _input.Frame.FirePressed = true;
            for (int i = 0; i < 600 && eventAt < 0f; i++) yield return null;
            Assert.GreaterOrEqual(eventAt, 0f, "a punch was thrown with bare hands");
            var hand = right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;
            float bestAt = 0f, best = float.MinValue, rest = HandAhead(hand);
            for (float end = eventAt + 0.5f; Time.time < end;)
            {
                yield return null;
                float a = HandAhead(hand);
                if (a > best) { best = a; bestAt = Time.time; }
            }
            float plan = _anim.PunchImpactAt - eventAt;
            Debug.Log($"[AnimMetrics] punch ({(right ? "right" : "left")}): planned fist-out {plan * 1000f:F0} ms after the event, measured peak {(bestAt - eventAt) * 1000f:F0} ms, reach {best - rest:F2} m");
            Assert.LessOrEqual(plan, 0.05f, "planned: the clip's fist-out frame follows the judgement within 50 ms");
            Assert.LessOrEqual(Mathf.Abs(bestAt - eventAt), 0.05f + 0.034f, "measured: the fist peaks within 50 ms plus two frames (the pose is read a frame late)");
            Assert.Greater(best - rest, 0.3f, "the fist reaches out");
            Shot("punch_peak", 2.5f, 1.3f, 1.2f);
        }

        [UnityTest]
        public IEnumerator Hit_TheBodyFlinchesOnTheAdditiveLayer_WithoutLosingItsPose()
        {
            yield return Load();
            _me.ServerDamageFilter = a => a.Amount;
            yield return GiveRifle();
            yield return new WaitForSeconds(0.5f);
            float Chest() { var f = _pc.transform.InverseTransformDirection(B(HumanBodyBones.Chest).up); return Mathf.Atan2(f.z, f.y) * Mathf.Rad2Deg; }
            float rest = Chest(), peak = 0f; bool flinchState = false;
            _me.ServerTakeDamage(5f);
            for (float end = Time.time + 0.6f; Time.time < end;)
            {
                yield return null;
                peak = Mathf.Max(peak, Mathf.Abs(Chest() - rest));
                flinchState |= BodyAnimator.GetCurrentAnimatorStateInfo(3).IsName("Flinch") || BodyAnimator.GetNextAnimatorStateInfo(3).IsName("Flinch");
            }
            Debug.Log($"[AnimMetrics] hit flinch: chest moved up to {peak:F1} deg, additive state reached {flinchState}, upper state {_anim.UpperState}, rifle hold {_anim.RifleHoldShown:F2}");
            Assert.IsTrue(flinchState, "the Flinch state on the Additive layer");
            Assert.Greater(peak, 1f, "the chest tips by at least a degree");
            Assert.Greater(_anim.RifleHoldShown, 0.9f, "the rifle hold is not interrupted by the hit");
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
            for (int i = 0; i < 6; i++) { yield return new WaitForSeconds(0.4f); Shot($"reload_{i}", 2.5f, 1.2f, 1.1f); }
            yield return Record("reload", 2.5f, rows);
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
            Shot("prone_side");
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
        [UnityTest] public IEnumerator On35DegreeSlope_NoFootSinks_AndNoneFloatsOver15Cm() { yield return OnSlope(35f, 0.15f); }

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
            Shot($"slope{SlopeDeg:0}_side", 3f, 0.8f, 0.7f);
            var err = SoleErrors(col, flatRest / Mathf.Cos(SlopeDeg * Mathf.Deg2Rad)); // vertical offset of an ankle over a sloped sole
            Debug.Log($"[AnimMetrics] slope {SlopeDeg} deg: sole errors left {err[0]:F3} right {err[1]:F3} m, plant {_anim.FootPlantLeft:F3}/{_anim.FootPlantRight:F3} pelvis drop {_anim.PlantDrop:F3} | {_anim.PlantDebug[0]} | {_anim.PlantDebug[1]} | capsule y {_pc.transform.position.y:F3} ground at origin {top.point.y:F3}");
            // Sunk is the visible fault (the body in the ground), floating less so: the pelvis comes down no more than 12 cm below the nearer ground.
            Assert.GreaterOrEqual(Mathf.Min(err[0], err[1]), -0.03f, "no foot sinks into the slope (m)");
            Assert.LessOrEqual(Mathf.Max(err[0], err[1]), tolerance, "no foot floats over the slope (m)");
            UnityEngine.Object.Destroy(slope);
        }

        // Ankle height over the ground really under it (all layers, characters excluded).
        float AnkleOverGround(HumanBodyBones bone)
        {
            var p = B(bone).position;
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 1f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<Combatant>() == null && h.normal.y > 0.5f) best = Mathf.Max(best, h.point.y);
            Assert.IsFalse(float.IsNegativeInfinity(best), "ground under the foot");
            return p.y - best;
        }

        // Player report "walks buried in the ground": standing and moving on flat ground, an ankle is `rest` over the ground under it, never sunk.
        [UnityTest]
        public IEnumerator FlatGround_SolesOnTheGround_StandingAndMoving_NeverSunk()
        {
            yield return Load();
            float rest = _anim.FootRest, standing = Mathf.Abs(AnkleOverGround(HumanBodyBones.LeftFoot) - rest);
            float lowest = 9f, drop = 0f;
            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.6f);
            for (float end = Time.time + 1.5f; Time.time < end;)
            {
                yield return null;
                lowest = Mathf.Min(lowest, AnkleOverGround(HumanBodyBones.LeftFoot), AnkleOverGround(HumanBodyBones.RightFoot));
                drop = Mathf.Max(drop, _anim.PlantDrop);
            }
            Debug.Log($"[AnimMetrics] flat ground: standing ankle error {standing:F3} m, lowest ankle while moving {lowest:F3} (rest {rest:F3}), max pelvis drop {drop:F3}");
            Assert.LessOrEqual(standing, 0.025f, "standing: an ankle is `rest` above the ground (m)");
            Assert.GreaterOrEqual(lowest, rest - 0.03f, "moving: no ankle is more than 3 cm below its resting height over the ground");
            Assert.LessOrEqual(drop, 0.15f, "the pelvis is lowered only by the capsule's hover, not further");
        }

        // A floor on a layer the default ray casts skip (Ignore Raycast) must still be stood on: the feet must not sink to the floor below it.
        [UnityTest]
        public IEnumerator FloorOnIgnoreRaycastLayer_TheBodyIsNotSunkToTheFloorBelow()
        {
            yield return Load();
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.layer = 2; // Ignore Raycast
            slab.transform.position = new Vector3(-14f, -0.9f, -26f);
            slab.transform.localScale = new Vector3(8f, 2f, 8f); // top at y = 0.1
            Physics.SyncTransforms();
            _pc.Teleport(new Vector3(-14f, 0.3f, -26f));
            yield return new WaitForSeconds(1.0f);
            float onTop = AnkleOverGround(HumanBodyBones.LeftFoot);
            float pelvis = _anim.PlantDrop;
            Debug.Log($"[AnimMetrics] slab on Ignore Raycast: ankle over the slab top {onTop:F3} (rest {_anim.FootRest:F3}), pelvis drop {pelvis:F3}, origin y {_pc.transform.position.y:F3}");
            Assert.LessOrEqual(pelvis, 0.15f, "no sinking through the slab");
            UnityEngine.Object.Destroy(slab);
        }

        [UnityTest]
        public IEnumerator Crouch_BareHands_HeadComesDownToTheCapsule()
        {
            yield return Load();
            _input.Frame.Crouch = true;
            yield return WaitUntil(() => _me.GetComponent<CombatantStance>().CrouchAmount >= 0.999f, 2f, "crouched");
            yield return new WaitForSeconds(0.8f);
            var rows = new List<float[]>();
            Shot("crouch_bare_side", 3f, 1.0f, 0.8f);
            yield return Record("crouch_bare", 0.4f, rows);
            var r = rows[rows.Count - 1];
            Debug.Log($"[AnimMetrics] crouch bare hands: hips {r[1]:F3} head {r[3]:F3} feet {r[4]:F3}/{r[5]:F3} drop {_anim.ModelDrop:F3}");
            Assert.That(r[3], Is.InRange(0.8f, 1.2f), "head joint height crouched, the UAL crouch (m)");
            Assert.Less(Mathf.Abs(_anim.ModelDrop), 0.03f, "the clip is the crouch: no code knee bend");
            Assert.Less(Mathf.Min(r[4], r[5]), 0.2f, "a foot on the floor");
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
            Shot("crouch_side", 3f, 1.0f, 0.8f);
            yield return Record("crouch", 0.4f, rows);
            var r = rows[rows.Count - 1];
            Physics.Raycast(_pc.transform.position + Vector3.up, Vector3.down, out var floorHit, 3f);
            Debug.Log($"[AnimMetrics] crouch origin over floor {_pc.transform.position.y - floorHit.point.y:F3} plant {_anim.FootPlantLeft:F3}/{_anim.FootPlantRight:F3} drop {_anim.PlantDrop:F3} lod {_anim.Lod} | {_anim.PlantDebug[0]} | {_anim.PlantDebug[1]}");
            Debug.Log($"[AnimMetrics] crouch: hips {r[1]:F3} head {r[3]:F3} feet {r[4]:F3}/{r[5]:F3} palm {r[8]:F3}");
            Assert.That(r[3], Is.InRange(0.9f, 1.3f), "head height crouched (m)");
            Assert.Less(Mathf.Min(r[4], r[5]), 0.2f, "a foot on the floor");
            Assert.AreEqual("RifleCrouch", _anim.BaseShown, "the rifle crouch is the Mixamo kneel clip (ADR-536, #303)");
            Assert.Less(Mathf.Abs(_anim.ModelDrop), 0.03f, "the clip is the crouch: no code knee bend");
        }
    }
}
