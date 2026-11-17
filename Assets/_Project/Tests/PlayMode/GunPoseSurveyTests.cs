using System;
using System.Collections;
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
    // Gun pose survey (animation track, ADR-264): every weapon class in every movement and aim state, measured from the posed
    // skeleton and written to Logs/gun_survey.csv (one row per scenario: worst and mean over the window). Run on purpose:
    //   tools/unity.sh test playmode Game.Tests.GunPoseSurveyTests
    [Explicit, Category("Survey")]
    public class GunPoseSurveyTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = Frame.Crouch = Frame.Prone = Frame.Reload = Frame.ThrowPressed = Frame.ThrowReleased = false;
                return f;
            }
        }

        static readonly (int id, string tag)[] Weapons = { (3, "rifle"), (1, "pistol"), (2, "smg"), (4, "shotgun"), (5, "sniper"), (21, "melee") };

        ScriptedInput _input;
        Combatant _me;
        PlayerController _pc;
        CombatantAnimation _anim;
        readonly StringBuilder _csv = new StringBuilder();

        Transform B(HumanBodyBones b) => RigBones.Bone(_me, b);

        IEnumerator Load()
        {
            PlayerAppearanceStore.Save(PlayerAppearance.Default);
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 20f;
            while ((PlayerController.Local == null || Combatant.LocalPlayer == null) && Time.realtimeSinceStartup < end) yield return null;
            _me = Combatant.LocalPlayer; _pc = PlayerController.Local; _anim = _me.GetComponent<CombatantAnimation>();
            _me.ServerDamageFilter = _ => 0f;
            for (int n = 0; SpawnProtection.InputBlocked && n < 60; n++) yield return new WaitForSeconds(0.25f);
            _csv.Clear();
            _csv.AppendLine("view,weapon,scenario,n," +
                "muzzleYawMax,muzzlePitchMin,muzzlePitchMax,aimErrMax,aimErrMean,leftPalmMax,leftPalmMean,leftArmStretchMax,rightArmStretchMax," +
                "shoulderYawMin,shoulderYawMax,hipsYawMean,twistMax,torsoPitchMax,weaponAboveEyeMax,weaponNearBodyMin,fppCoverMax,fppTopMax,speedMean");
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator Equip(int id)
        {
            var w = GameData.Weapons.Find(id);
            int slot = -1;
            for (int s = 0; s < 5; s++) if (_me.WeaponInSlot(s) == id) slot = s;
            if (slot < 0)
            {
                _me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = id, Amount = w.MagSize });
                if (!w.IsMelee) _me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = w.Ammo, Amount = 240 });
                for (int s = 0; s < 5; s++) if (_me.WeaponInSlot(s) == id) slot = s;
            }
            _me.ServerSelectSlot(slot);
            yield return new WaitForSeconds(1.0f);
        }

        // The weapon model's own long axis, pointing away from the right hand (not how the hold was built).
        bool Axis(int id, out Vector3 dir, out Vector3 tip, out Vector3 butt, out Bounds bounds)
        {
            dir = tip = butt = Vector3.zero; bounds = default;
            var gun = _me.GetComponent<WeaponVisual>().ModelFor(id);
            if (gun == null) return false;
            var hand = B(HumanBodyBones.RightHand).position;
            MeshFilter best = null; float longest = 0f;
            foreach (var mf in gun.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.sharedMesh.bounds.size.magnitude > longest) { longest = mf.sharedMesh.bounds.size.magnitude; best = mf; }
            if (best == null) return false;
            var e = best.sharedMesh.bounds.extents;
            var local = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
            dir = best.transform.TransformDirection(local).normalized;
            var center = best.transform.TransformPoint(best.sharedMesh.bounds.center);
            if (Vector3.Dot(dir, center - hand) < 0f) dir = -dir;
            float half = Mathf.Abs(Vector3.Dot(best.transform.TransformVector(Vector3.Scale(local, e)), dir));
            tip = center + dir * half; butt = center - dir * half;
            bounds = new Bounds(center, Vector3.zero);
            foreach (var r in gun.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            return true;
        }

        float Facing(HumanBodyBones left, HumanBodyBones right)
        {
            var across = _pc.transform.InverseTransformDirection(B(right).position - B(left).position);
            return Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg;
        }

        float Stretch(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand)
        {
            float reach = Vector3.Distance(B(upper).position, B(lower).position) + Vector3.Distance(B(lower).position, B(hand).position);
            return Vector3.Distance(B(upper).position, B(hand).position) / Mathf.Max(0.01f, reach);
        }

        // Sample `seconds` of frames while the scenario runs, then one CSV row.
        IEnumerator Sample(string view, int id, string tag, string scenario, float seconds)
        {
            int n = 0;
            float yawMax = 0, pMin = 999, pMax = -999, errMax = 0, errSum = 0, palmMax = 0, palmSum = 0, lStr = 0, rStr = 0;
            float shMin = 999, shMax = -999, hips = 0, twist = 0, torso = 0, above = -999, near = 999, cover = 0, top = -999, speed = 0;
            for (float end = Time.time + seconds; Time.time < end; n++)
            {
                yield return null;
                if (!Axis(id, out var dir, out var tip, out var butt, out var bounds)) continue;
                var local = _pc.transform.InverseTransformDirection(dir);
                float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                float pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
                var look = Quaternion.Euler(-_pc.Pitch, _pc.Yaw, 0f) * Vector3.forward;
                float err = Vector3.Angle(dir, look);
                yawMax = Mathf.Max(yawMax, Mathf.Abs(yaw)); pMin = Mathf.Min(pMin, pitch); pMax = Mathf.Max(pMax, pitch);
                errMax = Mathf.Max(errMax, err); errSum += err;
                var gun = _me.GetComponent<WeaponVisual>().ModelFor(id);
                var grip = gun != null ? gun.transform.Find(CombatantAnimation.LeftHandGripName) : null;
                if (grip != null)
                {
                    var palm = Vector3.Lerp(B(HumanBodyBones.LeftHand).position, B(HumanBodyBones.LeftMiddleProximal).position, 0.8f);
                    float d = Vector3.Distance(palm, grip.position);
                    palmMax = Mathf.Max(palmMax, d); palmSum += d;
                }
                lStr = Mathf.Max(lStr, Stretch(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand));
                rStr = Mathf.Max(rStr, Stretch(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand));
                float sh = Facing(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm), hp = Facing(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
                shMin = Mathf.Min(shMin, sh); shMax = Mathf.Max(shMax, sh); hips += hp; twist = Mathf.Max(twist, Mathf.Abs(Mathf.DeltaAngle(hp, sh)));
                var hipsL = _pc.transform.InverseTransformPoint(B(HumanBodyBones.Hips).position); var chestL = _pc.transform.InverseTransformPoint(B(HumanBodyBones.Chest).position);
                torso = Mathf.Max(torso, Mathf.Abs(Mathf.Atan2(chestL.z - hipsL.z, chestL.y - hipsL.y) * Mathf.Rad2Deg));
                above = Mathf.Max(above, bounds.max.y - _me.EyePosition.y);
                // How close the barrel's tip comes to the chest line from the side (the weapon through the body).
                near = Mathf.Min(near, Vector3.Distance(tip, B(HumanBodyBones.Chest).position));
                if (view == "fpp" && _pc.Camera != null)
                {
                    float x0 = 1, x1 = 0, y0 = 1, y1 = 0; bool any = false;
                    foreach (var r in gun.GetComponentsInChildren<Renderer>())
                    {
                        if (!r.enabled) continue;
                        var b = r.bounds;
                        for (int i = 0; i < 8; i++)
                        {
                            var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                            var v = _pc.Camera.WorldToViewportPoint(c);
                            if (v.z <= 0.01f) continue;
                            any = true; x0 = Mathf.Min(x0, Mathf.Clamp01(v.x)); x1 = Mathf.Max(x1, Mathf.Clamp01(v.x)); y0 = Mathf.Min(y0, Mathf.Clamp01(v.y)); y1 = Mathf.Max(y1, Mathf.Clamp01(v.y));
                        }
                    }
                    if (any) { cover = Mathf.Max(cover, (x1 - x0) * (y1 - y0)); top = Mathf.Max(top, y1); }
                }
                var v2 = _me.GetComponent<CombatantMotion>().LocalVelocity; speed += new Vector2(v2.x, v2.z).magnitude;
            }
            if (n == 0) yield break;
            _csv.AppendLine($"{view},{tag},{scenario},{n},{yawMax:F1},{pMin:F1},{pMax:F1},{errMax:F1},{errSum / n:F1},{palmMax:F3},{palmSum / n:F3},{lStr:F3},{rStr:F3}," +
                $"{shMin:F1},{shMax:F1},{hips / n:F1},{twist:F1},{torso:F1},{above:F3},{near:F3},{cover:F3},{top:F3},{speed / n:F2}");
        }

        IEnumerator Run(string view, int id, string tag, string name, Action begin, float settle, float window, Action end = null)
        {
            begin();
            yield return new WaitForSeconds(settle);
            yield return Sample(view, id, tag, name, window);
            end?.Invoke();
        }

        IEnumerator Pass(string view)
        {
            if ((view == "fpp") != (_pc.Rig.Mode == ViewMode.FirstPerson)) _pc.Rig.ToggleMode();
            yield return new WaitForSeconds(0.8f);
            foreach (var (id, tag) in Weapons)
            {
                yield return Equip(id);
                _input.Frame = new PlayerInputFrame { SelectSlot = -1 };
                _pc.Teleport(Combatant.LocalPlayer.transform.position);
                yield return new WaitForSeconds(0.3f);
                Vector2 f = new Vector2(0, 1);
                yield return Run(view, id, tag, "idle", () => { }, 0.5f, 0.6f);
                yield return Run(view, id, tag, "walk", () => { _input.Frame.Move = f; _input.Frame.SlowWalk = true; }, 0.7f, 0.9f, () => _input.Frame.SlowWalk = false);
                yield return Run(view, id, tag, "run", () => _input.Frame.Move = f, 0.7f, 0.9f);
                yield return Run(view, id, tag, "sprint", () => _input.Frame.Sprint = true, 0.7f, 0.9f, () => _input.Frame.Sprint = false);
                yield return Run(view, id, tag, "back", () => _input.Frame.Move = new Vector2(0, -1), 0.7f, 0.9f);
                yield return Run(view, id, tag, "left", () => _input.Frame.Move = new Vector2(-1, 0), 0.7f, 0.9f);
                yield return Run(view, id, tag, "right", () => _input.Frame.Move = new Vector2(1, 0), 0.7f, 0.9f);
                yield return Run(view, id, tag, "diagFR", () => _input.Frame.Move = new Vector2(1, 1), 0.7f, 0.9f);
                yield return Run(view, id, tag, "diagBL", () => _input.Frame.Move = new Vector2(-1, -1), 0.7f, 0.9f);
                _input.Frame.Move = Vector2.zero;
                yield return new WaitForSeconds(0.3f);
                if (id != 21)
                {
                    yield return Run(view, id, tag, "shoulder", () => _input.Frame.AimHeld = true, 0.9f, 0.5f);
                    yield return Run(view, id, tag, "shoulder_walk", () => _input.Frame.Move = f, 0.6f, 0.8f, () => _input.Frame.Move = Vector2.zero);
                    _input.Frame.AimHeld = false;
                    yield return new WaitForSeconds(0.5f);
                    _input.Frame.AimHeld = true; yield return new WaitForSeconds(0.06f); _input.Frame.AimHeld = false;
                    yield return Run(view, id, tag, "ads", () => { }, 0.9f, 0.5f);
                    yield return Run(view, id, tag, "ads_fire", () => { _input.Frame.FireHeld = true; _input.Frame.FirePressed = true; }, 0.05f, 0.5f, () => _input.Frame.FireHeld = false);
                    _input.Frame.AimHeld = true; yield return new WaitForSeconds(0.06f); _input.Frame.AimHeld = false;
                    yield return new WaitForSeconds(0.6f);
                    yield return Run(view, id, tag, "hip_fire", () => { _input.Frame.FireHeld = true; _input.Frame.FirePressed = true; }, 0.05f, 0.5f, () => _input.Frame.FireHeld = false);
                    yield return new WaitForSeconds(0.4f);
                    yield return Run(view, id, tag, "reload", () => _input.Frame.Reload = true, 0.05f, 1.3f);
                    yield return new WaitForSeconds(2.0f);
                }
                yield return Run(view, id, tag, "jump", () => { _input.Frame.Move = f; _input.Frame.Jump = true; }, 0.02f, 0.7f, () => _input.Frame.Move = Vector2.zero);
                yield return new WaitForSeconds(0.6f);
                yield return Run(view, id, tag, "crouch", () => _input.Frame.Crouch = true, 0.8f, 0.5f);
                yield return Run(view, id, tag, "crouch_walk", () => _input.Frame.Move = f, 0.7f, 0.9f, () => _input.Frame.Move = Vector2.zero);
                yield return Run(view, id, tag, "prone", () => _input.Frame.Prone = true, 1.4f, 0.5f);
                yield return Run(view, id, tag, "prone_crawl", () => _input.Frame.Move = f, 0.7f, 0.9f, () => _input.Frame.Move = Vector2.zero);
                // Back on the feet: Z leaves prone (to standing), then C only if still crouched.
                _input.Frame.Prone = true; yield return new WaitForSeconds(1.2f);
                if (_anim.CrouchPose > 0.5f) { _input.Frame.Crouch = true; yield return new WaitForSeconds(0.8f); }
            }
        }

        [UnityTest, Timeout(1500000)]
        public IEnumerator Survey()
        {
            yield return Load();
            yield return Pass("tpp");
            yield return Pass("fpp");
            PlayerController.InputOverride = null;
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/gun_survey.csv", _csv.ToString());
            Assert.Pass();
        }
    }
}
