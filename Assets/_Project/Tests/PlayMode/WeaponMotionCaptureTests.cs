using System;
using System.Collections;
using System.Globalization;
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
    // Weapon motion audit (docs/WEAPON_MOTION_AUDIT.md): every weapon class through movement, aim, fire, reload, swap and the
    // transitions between them, at a fixed 60 fps (Time.captureFramerate) so frame-to-frame numbers are comparable. Every frame
    // goes to <folder>/frames.csv (aim error, left palm off the handguard, muzzle and hand speeds, gun-to-head/chest distance,
    // planted-foot slide, first-person weapon jerk); every few frames a JPG of the game view (and a front-quarter view in
    // third person). Run on purpose:
    //   WM_SHOTS=Logs/wm_before WM_ONLY=rifle,pistol tools/unity.sh test playmode Game.Tests.WeaponMotionCaptureTests
    [Explicit, Category("Capture")]
    public class WeaponMotionCaptureTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = Frame.Crouch = Frame.Prone = Frame.Reload = Frame.ThrowPressed = Frame.ThrowReleased = Frame.ToggleView = false;
                Frame.FireReleased = false; Frame.SelectSlot = -1; Frame.Holster = false;
                Frame.Look = Vector2.zero;
                return f;
            }
        }

        static readonly (int id, string tag)[] Weapons = { (3, "rifle"), (2, "smg"), (1, "pistol"), (4, "shotgun"), (5, "sniper") };
        static readonly (string name, Vector2 move)[] Dirs =
        {
            ("F", new Vector2(0, 1)), ("FR", new Vector2(1, 1)), ("R", new Vector2(1, 0)), ("BR", new Vector2(1, -1)),
            ("B", new Vector2(0, -1)), ("BL", new Vector2(-1, -1)), ("L", new Vector2(-1, 0)), ("FL", new Vector2(-1, 1)),
        };

        ScriptedInput _input;
        Combatant _me;
        PlayerController _pc;
        CombatantAnimation _anim;
        Probe _probe;
        Vector3 _home, _run;
        string _folder;
        int _id;
        string _tag;
        bool _shots;

        // Reads the posed skeleton after every LateUpdate that poses it (animation 0, weapon at the eye 100).
        [DefaultExecutionOrder(900)]
        sealed class Probe : MonoBehaviour
        {
            public WeaponMotionCaptureTests T;
            public string Seg;
            public int ShotEvery; // 0 = no pictures
            public float SegStart;
            Camera _outside;
            Vector3 _lastDir, _lastHand, _lastFoot0, _lastFoot1, _fp1, _fp2;
            bool _have;
            int _frame;
            public readonly StringBuilder Csv = new StringBuilder();

            public void Begin(string seg, int shotEvery) { Seg = seg; ShotEvery = shotEvery; SegStart = Time.time; _have = false; _frame = 0; }

            void LateUpdate()
            {
                if (Seg == null || T._me == null) return;
                var me = T._me; var pc = T._pc; var anim = T._anim;
                float dt = Mathf.Max(1e-4f, Time.deltaTime);
                int id = anim.HeldId;
                var body = pc.transform;
                Transform B(HumanBodyBones b) => RigBones.Bone(me, b);
                bool fpp = pc.Rig.Mode == ViewMode.FirstPerson;
                float aimErr = -1, palm = -1, headGap = -1, chestGap = -1, muzzleSpeed = 0, fpJerk = 0;
                var dir = Vector3.zero;
                if (id != 0 && T.Axis(id, out dir, out var tip, out var butt))
                {
                    var look = Quaternion.Euler(-pc.Pitch, pc.Yaw, 0f) * Vector3.forward;
                    aimErr = Vector3.Angle(dir, look);
                    var gun = me.GetComponent<WeaponVisual>().ModelFor(id);
                    var grip = gun != null ? gun.transform.Find(CombatantAnimation.LeftHandGripName) : null;
                    if (grip != null && !fpp)
                        palm = Vector3.Distance(Vector3.Lerp(B(HumanBodyBones.LeftHand).position, B(HumanBodyBones.LeftMiddleProximal).position, 0.8f), grip.position);
                    if (!fpp)
                    {
                        headGap = SegmentDistance(B(HumanBodyBones.Head).position + Vector3.up * 0.08f, butt, tip) - 0.11f;
                        var chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
                        chestGap = SegmentDistance(chest.position, butt, tip) - 0.12f;
                    }
                    var localDir = body.InverseTransformDirection(dir);
                    if (_have) muzzleSpeed = Vector3.Angle(localDir, _lastDir) / dt;
                    _lastDir = localDir;
                    if (fpp && pc.Camera != null && gun != null)
                    {
                        var p = pc.Camera.transform.InverseTransformPoint(gun.transform.position);
                        if (_frame >= 2) fpJerk = (p - 2f * _fp1 + _fp2).magnitude / (dt * dt);
                        _fp2 = _fp1; _fp1 = p;
                    }
                }
                var hand = body.InverseTransformPoint(B(HumanBodyBones.RightHand).position);
                float handSpeed = _have ? (hand - _lastHand).magnitude / dt : 0f;
                _lastHand = hand;
                // The lower foot, while it is down, should not move over the ground.
                var f0 = B(HumanBodyBones.LeftFoot).position; var f1 = B(HumanBodyBones.RightFoot).position;
                float slide = 0f;
                if (_have && !fpp)
                {
                    bool leftLow = f0.y <= f1.y;
                    var now = leftLow ? f0 : f1; var was = leftLow ? _lastFoot0 : _lastFoot1;
                    if (now.y - body.position.y < 0.14f) slide = new Vector2(now.x - was.x, now.z - was.z).magnitude / dt;
                }
                _lastFoot0 = f0; _lastFoot1 = f1;
                float sh = T.Facing(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm);
                float hips = T.Facing(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
                var v = me.GetComponent<CombatantMotion>().LocalVelocity;
                float moveDeg = new Vector2(v.x, v.z).magnitude > 0.3f ? Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg : 0f;
                var inv = CultureInfo.InvariantCulture;
                Csv.AppendLine(string.Format(inv,
                    "{0},{1},{2:F3},{3},{4},{5},{6:F2},{7:F2},{8:F2},{9:F2},{10:F2},{11:F2},{12:F1},{13:F3},{14:F3},{15:F3},{16:F0},{17:F2},{18:F2},{19:F1},{20:F1},{21:F0},{22:F0},{23:F2},{24:F2},{25:F2},{26:F3},{27:F3}",
                    Seg, _frame, Time.time - SegStart, id, anim.BaseShown, anim.UpperState ?? "-", new Vector2(v.x, v.z).magnitude,
                    anim.RifleReady, anim.RifleHoldShown, anim.ActionLayerWeight, anim.CrouchPose, anim.PronePose, aimErr, palm, headGap, chestGap,
                    muzzleSpeed, handSpeed, slide, sh, hips, moveDeg, fpJerk, anim.DrawProgress, anim.ReloadProgress, pc.Pitch, body.InverseTransformPoint(B(HumanBodyBones.Hips).position).y, anim.PlantDrop));
                if (ShotEvery > 0 && _frame % ShotEvery == 0) Shoot(fpp);
                _have = true;
                _frame++;
            }

            static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
            {
                var ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                return Vector3.Distance(p, a + ab * t);
            }

            void Shoot(bool fpp)
            {
                var pc = T._pc;
                string name = $"{Seg}_{_frame:000}";
                Render(pc.Camera, name + (fpp ? "_fpp" : "_tpp"));
                if (fpp) return;
                if (_outside == null)
                {
                    _outside = new GameObject("WM_Outside").AddComponent<Camera>();
                    _outside.enabled = false; _outside.fieldOfView = 40f; _outside.nearClipPlane = 0.05f;
                }
                var p = pc.transform;
                _outside.transform.position = p.position + Vector3.up * 1.25f + p.forward * 2.3f + p.right * 2.0f;
                _outside.transform.rotation = Quaternion.LookRotation(p.position + Vector3.up * 1.05f - _outside.transform.position);
                Render(_outside, name + "_out");
            }

            void Render(Camera cam, string name)
            {
                if (cam == null) return;
                const int w = 800, h = 450;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                var before = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = before;
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path.Combine(T._folder, name + ".jpg"), tex.EncodeToJPG(80));
                UnityEngine.Object.Destroy(tex);
            }
        }

        Transform Bone(HumanBodyBones b) => RigBones.Bone(_me, b);

        float Facing(HumanBodyBones left, HumanBodyBones right)
        {
            var across = _pc.transform.InverseTransformDirection(Bone(right).position - Bone(left).position);
            return Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg;
        }

        // The weapon model's own long axis away from the right hand (same as GunPoseSurveyTests).
        bool Axis(int id, out Vector3 dir, out Vector3 tip, out Vector3 butt)
        {
            dir = tip = butt = Vector3.zero;
            var gun = _me.GetComponent<WeaponVisual>().ModelFor(id);
            if (gun == null || !gun.activeInHierarchy) return false;
            var hand = Bone(HumanBodyBones.RightHand).position;
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
            return true;
        }

        static bool Wanted(string tag)
        {
            string only = Environment.GetEnvironmentVariable("WM_ONLY");
            if (string.IsNullOrEmpty(only)) return true;
            foreach (var k in only.Split(',')) if (k == tag) return true;
            return false;
        }

        IEnumerator Equip(int id)
        {
            var w = GameData.Weapons.Find(id);
            int slot = -1;
            for (int s = 0; s < 5; s++) if (_me.WeaponInSlot(s) == id) slot = s;
            if (slot < 0)
            {
                _me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = id, Amount = w.MagSize });
                if (!w.IsMelee) _me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = w.Ammo, Amount = 400 });
                for (int s = 0; s < 5; s++) if (_me.WeaponInSlot(s) == id) slot = s;
            }
            _me.ServerSelectSlot(slot);
            yield return new WaitForSeconds(1.0f);
        }

        int SlotOf(int id) { for (int s = 0; s < 5; s++) if (_me.WeaponInSlot(s) == id) return s; return -1; }

        // Back home facing so that `move` (local) runs along the free direction.
        IEnumerator Home(Vector2 move)
        {
            _input.Frame.Move = Vector2.zero;
            _pc.Teleport(_home);
            yield return null;
            float yaw = Mathf.Atan2(_run.x, _run.z) * Mathf.Rad2Deg - (move == Vector2.zero ? 0f : Mathf.Atan2(move.x, move.y) * Mathf.Rad2Deg);
            _pc.LookAt(_home + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.25f);
        }

        // One segment: `setup` before logging starts, then `body` runs while every frame is logged.
        IEnumerator Seg(string view, string name, Vector2 move, IEnumerator body, int shotEvery = 6)
        {
            yield return Home(move);
            _probe.Begin($"{_tag}_{view}_{name}", _shots ? shotEvery : 0);
            yield return body;
            _probe.Seg = null;
            _input.Frame.Move = Vector2.zero;
            _input.Frame.Sprint = _input.Frame.AimHeld = _input.Frame.FireHeld = _input.Frame.SlowWalk = false;
            yield return new WaitForSeconds(0.3f);
        }

        // Turning in place: `degPerFrame` of yaw every frame (180 deg/s at 60 fps for 3).
        IEnumerator Turn(float seconds, float degPerFrame)
        {
            float t0 = Time.time;
            while (Time.time - t0 < seconds) { _input.Frame.Look = new Vector2(degPerFrame, 0f); yield return null; }
            yield return new WaitForSeconds(0.4f);
        }

        IEnumerator Hold(float seconds) { yield return new WaitForSeconds(seconds); }

        IEnumerator Steps(params (float at, Action act)[] steps)
        {
            float t0 = Time.time;
            foreach (var (at, act) in steps)
            {
                while (Time.time - t0 < at) yield return null;
                act?.Invoke();
            }
        }

        IEnumerator FireFor(float seconds, bool auto)
        {
            float t0 = Time.time, next = 0f;
            while (Time.time - t0 < seconds)
            {
                if (auto) { _input.Frame.FireHeld = true; if (Time.time - t0 < 0.02f) _input.Frame.FirePressed = true; }
                else if (Time.time - t0 >= next) { _input.Frame.FirePressed = true; _input.Frame.FireHeld = true; next += 0.3f; }
                else _input.Frame.FireHeld = false;
                yield return null;
            }
            _input.Frame.FireHeld = false;
        }

        IEnumerator Sights(bool on)
        {
            bool now = _pc.GetComponent<Combatant>().AimMode == AimMode.Sights;
            if (now == on) yield break;
            _input.Frame.AimHeld = true; yield return new WaitForSeconds(0.06f); _input.Frame.AimHeld = false;
            yield return new WaitForSeconds(0.05f);
        }

        IEnumerator ToView(ViewMode mode)
        {
            if (_pc.Rig.Mode != mode) _pc.Rig.ToggleMode();
            yield return new WaitForSeconds(0.8f);
        }

        IEnumerator ThirdPersonPass()
        {
            const string v = "tpp";
            var f = new Vector2(0, 1);
            yield return Seg(v, "idle", Vector2.zero, Hold(1.0f));
            foreach (var (dn, d) in Dirs)
                yield return Seg(v, "run_" + dn, d, Steps((0f, () => _input.Frame.Move = d), (1.2f, null)), _tag == "rifle" ? 4 : 12);
            foreach (var (dn, d) in Dirs)
                yield return Seg(v, "aimwalk_" + dn, d, Steps((0f, () => { _input.Frame.AimHeld = true; }), (0.3f, () => _input.Frame.Move = d), (1.5f, null)), _tag == "rifle" ? 4 : 12);
            yield return Seg(v, "walk_F", f, Steps((0f, () => { _input.Frame.Move = f; _input.Frame.SlowWalk = true; }), (1.2f, null)), 6);
            yield return Seg(v, "sprint_F", f, Steps((0f, () => { _input.Frame.Move = f; _input.Frame.Sprint = true; }), (1.4f, null)), 4);
            // Transitions.
            yield return Seg(v, "x_sprint_to_aim", f, Steps((0f, () => { _input.Frame.Move = f; _input.Frame.Sprint = true; }), (1.0f, () => { _input.Frame.Sprint = false; _input.Frame.AimHeld = true; }), (1.8f, null)), 2);
            yield return Seg(v, "x_aim_to_sprint", f, Steps((0f, () => { _input.Frame.Move = f; _input.Frame.AimHeld = true; }), (0.9f, () => { _input.Frame.AimHeld = false; _input.Frame.Sprint = true; }), (1.8f, null)), 2);
            yield return Seg(v, "x_hip_to_aim_idle", Vector2.zero, Steps((0.3f, () => _input.Frame.AimHeld = true), (0.9f, () => _input.Frame.AimHeld = false), (1.6f, null)), 2);
            yield return Seg(v, "x_crouch_aim", Vector2.zero, Steps((0f, () => _input.Frame.AimHeld = true), (0.4f, () => _input.Frame.Crouch = true), (1.4f, () => _input.Frame.Crouch = true), (2.4f, null)), 3);
            yield return Seg(v, "crouch_walk_F", f, Steps((0f, () => _input.Frame.Crouch = true), (0.6f, () => _input.Frame.Move = f), (1.8f, () => _input.Frame.Move = Vector2.zero), (1.9f, () => _input.Frame.Crouch = true), (2.5f, null)), 6);
            yield return Seg(v, "crouch_walk_R", new Vector2(1, 0), Steps((0f, () => _input.Frame.Crouch = true), (0.6f, () => _input.Frame.Move = new Vector2(1, 0)), (1.8f, () => _input.Frame.Move = Vector2.zero), (1.9f, () => _input.Frame.Crouch = true), (2.5f, null)), 6);
            yield return Seg(v, "turn_hip", Vector2.zero, Turn(1.2f, 3f), 4);
            yield return Seg(v, "turn_aim", Vector2.zero, Chain(Steps((0f, () => _input.Frame.AimHeld = true), (0.3f, null)), Turn(1.0f, 3f)), 4);
            yield return Seg(v, "fire_hip", Vector2.zero, FireFor(1.0f, auto: _tag == "rifle" || _tag == "smg"), 2);
            yield return Seg(v, "fire_ads", Vector2.zero, Chain(Sights(true), Hold(0.4f), FireFor(1.0f, auto: _tag == "rifle" || _tag == "smg"), Sights(false)), 2);
            yield return FireFor(0.4f, auto: true); yield return new WaitForSeconds(0.5f);
            yield return Seg(v, "reload_stand", Vector2.zero, Steps((0.1f, () => _input.Frame.Reload = true), (3.2f, null)), 3);
            yield return FireFor(0.4f, auto: true); yield return new WaitForSeconds(0.5f);
            yield return Seg(v, "reload_walk", f, Steps((0f, () => _input.Frame.Move = f), (0.4f, () => _input.Frame.Reload = true), (3.4f, null)), 3);
            yield return Seg(v, "jump_stand", Vector2.zero, Steps((0.1f, () => _input.Frame.Jump = true), (1.3f, null)), 3);
            yield return Seg(v, "jump_run", f, Steps((0f, () => _input.Frame.Move = f), (0.6f, () => _input.Frame.Jump = true), (1.8f, null)), 3);
            int other = _id == 1 ? SlotOf(3) : SlotOf(1);
            int mine = SlotOf(_id);
            yield return Seg(v, "x_swap_out", Vector2.zero, Steps((0.1f, () => _me.ServerSelectSlot(other)), (1.2f, null)), 2);
            yield return Seg(v, "x_swap_back", Vector2.zero, Steps((0.1f, () => _me.ServerSelectSlot(mine)), (1.2f, null)), 2);
            if (_tag == "rifle" || _tag == "pistol")
            {
                yield return Seg(v, "prone_crawl", f, Steps((0f, () => _input.Frame.Prone = true), (1.5f, () => _input.Frame.Move = f), (2.7f, () => _input.Frame.Move = Vector2.zero), (2.8f, () => _input.Frame.Prone = true), (4.2f, null)), 6);
                if (_anim.CrouchPose > 0.5f) { _input.Frame.Crouch = true; yield return new WaitForSeconds(0.8f); }
            }
        }

        IEnumerator FirstPersonPass()
        {
            const string v = "fpp";
            var f = new Vector2(0, 1);
            yield return ToView(ViewMode.FirstPerson);
            yield return Seg(v, "idle", Vector2.zero, Hold(1.0f), 10);
            yield return Seg(v, "run_F", f, Steps((0f, () => _input.Frame.Move = f), (1.2f, null)), 6);
            yield return Seg(v, "run_R", new Vector2(1, 0), Steps((0f, () => _input.Frame.Move = new Vector2(1, 0)), (1.2f, null)), 6);
            yield return Seg(v, "sprint_F", f, Steps((0f, () => { _input.Frame.Move = f; _input.Frame.Sprint = true; }), (1.4f, null)), 6);
            yield return Seg(v, "x_sprint_to_aim", f, Steps((0f, () => { _input.Frame.Move = f; _input.Frame.Sprint = true; }), (1.0f, () => { _input.Frame.Sprint = false; _input.Frame.AimHeld = true; }), (1.8f, null)), 3);
            yield return Seg(v, "aimwalk_F", f, Steps((0f, () => _input.Frame.AimHeld = true), (0.3f, () => _input.Frame.Move = f), (1.5f, null)), 6);
            yield return Seg(v, "fire_hip", Vector2.zero, FireFor(1.0f, auto: _tag == "rifle" || _tag == "smg"), 3);
            yield return Seg(v, "fire_ads", Vector2.zero, Chain(Sights(true), Hold(0.4f), FireFor(1.0f, auto: _tag == "rifle" || _tag == "smg"), Sights(false)), 3);
            yield return FireFor(0.4f, auto: true); yield return new WaitForSeconds(0.5f);
            yield return Seg(v, "reload_stand", Vector2.zero, Steps((0.1f, () => _input.Frame.Reload = true), (3.2f, null)), 6);
            yield return Seg(v, "jump_run", f, Steps((0f, () => _input.Frame.Move = f), (0.6f, () => _input.Frame.Jump = true), (1.8f, null)), 4);
            int other = _id == 1 ? SlotOf(3) : SlotOf(1);
            int mine = SlotOf(_id);
            yield return Seg(v, "x_swap_out", Vector2.zero, Steps((0.1f, () => _me.ServerSelectSlot(other)), (1.2f, null)), 3);
            yield return Seg(v, "x_swap_back", Vector2.zero, Steps((0.1f, () => _me.ServerSelectSlot(mine)), (1.2f, null)), 3);
            yield return ToView(ViewMode.ThirdPerson);
        }

        static IEnumerator Chain(params IEnumerator[] parts) { foreach (var p in parts) yield return p; }

        // Melee, throw and heal: one look each (their poses are code, ADR-073 / ADR-144).
        IEnumerator Others()
        {
            _tag = "other"; _id = 0;
            const string v = "tpp";
            yield return Equip(21);
            yield return Seg(v, "melee_swing", Vector2.zero, Steps((0.2f, () => { _input.Frame.FirePressed = true; _input.Frame.FireHeld = true; }), (0.3f, () => _input.Frame.FireHeld = false), (1.3f, null)), 2);
            yield return Seg(v, "melee_run", new Vector2(0, 1), Steps((0f, () => _input.Frame.Move = new Vector2(0, 1)), (1.2f, null)), 4);
            var user = _me.GetComponent<ThrowableUser>();
            if (user != null) user.ServerAdd(3);
            _me.ServerSelectSlot(Loadout.ThrowableSlot);
            yield return new WaitForSeconds(1.0f);
            yield return Seg(v, "throw", Vector2.zero, Steps((0.2f, () => { _input.Frame.FireHeld = _input.Frame.FirePressed = true; if (user != null) user.ServerBeginCook(); }),
                (0.9f, () => { _input.Frame.FireHeld = false; _input.Frame.FireReleased = true; }), (2.0f, null)), 2);
            yield return Equip(3);
            _me.ServerDamageFilter = d => d.Amount; _me.ServerTakeDamage(50f); _me.ServerDamageFilter = _ => 0f;
            _me.ServerGive(new LootEntry { Kind = LootKind.Bandage, Amount = 2 });
            var healing = _me.GetComponent<CombatantHealing>();
            yield return Seg(v, "heal_bandage", Vector2.zero, Steps((0.1f, () => healing.ServerBegin(HealKind.Bandage)), (3.0f, null)), 6);
            yield return new WaitForSeconds(2f);
        }

        Vector3 FreeDirection(Vector3 from)
        {
            Vector3 best = Vector3.forward; float far = 0f;
            for (int i = 0; i < 16; i++)
            {
                var d = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                float room = Physics.SphereCast(from + Vector3.up * 1.2f, 0.4f, d, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 60f;
                if (room > far) { far = room; best = d; }
            }
            Debug.Log($"[WeaponMotion] free run {far:F1} m along {best}");
            return best;
        }

        [UnityTest, Timeout(3000000)]
        public IEnumerator Capture()
        {
            _folder = Path.GetFullPath(Environment.GetEnvironmentVariable("WM_SHOTS") is string s && s.Length > 0 ? s : "Logs/wm_shots");
            Directory.CreateDirectory(_folder);
            PlayerAppearanceStore.Save(PlayerAppearance.Default);
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 30f;
            while ((PlayerController.Local == null || Combatant.LocalPlayer == null) && Time.realtimeSinceStartup < end) yield return null;
            _me = Combatant.LocalPlayer; _pc = PlayerController.Local; _anim = _me.GetComponent<CombatantAnimation>();
            _me.ServerDamageFilter = _ => 0f;
            for (int n = 0; SpawnProtection.InputBlocked && n < 60; n++) yield return new WaitForSeconds(0.25f);
            foreach (var veh in Vehicle.All) if (veh != null) foreach (var r in veh.GetComponentsInChildren<Renderer>()) r.enabled = false;
            yield return new WaitForSeconds(0.5f);
            Time.captureFramerate = 60;
            _home = _me.transform.position;
            _run = FreeDirection(_home);
            _probe = new GameObject("WM_Probe").AddComponent<Probe>();
            _probe.T = this;
            _probe.Csv.AppendLine("seg,frame,t,weapon,base,upper,speed,ready,rifle,action,crouch,prone,aimErr,palm,headGap,chestGap,muzzleDegS,handMS,footSlide,shoulderYaw,hipsYaw,moveDeg,fpJerk,draw,reload,pitch,hipsY,plantDrop");
            bool shotsAll = Environment.GetEnvironmentVariable("WM_NO_SHOTS") != "1";
            // Both primaries the swap goes between are always carried.
            yield return Equip(1);
            yield return Equip(3);
            foreach (var (id, tag) in Weapons)
            {
                if (!Wanted(tag)) continue;
                _id = id; _tag = tag;
                _shots = shotsAll && (tag == "rifle" || tag == "pistol" || Environment.GetEnvironmentVariable("WM_SHOTS_ALL") == "1");
                yield return Equip(id);
                yield return ThirdPersonPass();
                yield return FirstPersonPass();
                File.WriteAllText(Path.Combine(_folder, "frames.csv"), _probe.Csv.ToString());
            }
            if (Wanted("other")) { _shots = shotsAll; yield return Others(); }
            File.WriteAllText(Path.Combine(_folder, "frames.csv"), _probe.Csv.ToString());
            Time.captureFramerate = 0;
            UnityEngine.Object.Destroy(_probe.gameObject);
            PlayerController.InputOverride = null;
            Assert.Pass();
        }
    }
}
