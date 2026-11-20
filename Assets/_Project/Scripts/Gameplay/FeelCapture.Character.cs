using System;
using System.Collections;
using System.Linq;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Character-centric review shots (CHARACTER_AUDIT): every weapon class idle, locomotion with rifle and pistol,
    // stances, aim, lean, reload, draw/holster/swap, throw, heal, jump, turn, hit and death, each frame by frame from
    // outside (side/quarter/front/third person) and through the player's own camera (first person). Run with
    //   CHAR_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.CharacterCaptureTests
    public partial class FeelCapture
    {
        bool _charShots;

        // CHAR_ONLY=B_pistol,C_ : only the shots whose name starts with one of these (empty = all).
        static bool Want(string name)
        {
            string only = Environment.GetEnvironmentVariable("CHAR_ONLY");
            if (string.IsNullOrEmpty(only)) return true;
            foreach (var key in only.Split(',')) if (key.Length > 0 && name.StartsWith(key, StringComparison.Ordinal)) return true;
            return false;
        }

        public static FeelCapture StartCharShots(string folder)
        {
            var capture = StartPoseShots(folder);
            capture._charShots = true;
            return capture;
        }

        // Outside views in third person, then the player's own eyes.
        IEnumerator Views(string name, bool fp = true, params Watch[] outside)
        {
            if (!Want(name)) yield break;
            var pc = PlayerController.Local;
            if (pc.Rig.Mode != ViewMode.ThirdPerson) pc.Rig.ToggleMode();
            foreach (var v in outside)
            {
                _watch = v;
                yield return VShot($"{name}_{v.ToString().ToLower()}");
            }
            if (fp)
            {
                pc.Rig.ToggleMode();
                _watch = Watch.Game;
                // The camera glides in from behind the character (DistanceChangePerSecond) and the weapon follows it:
                // wait until it sits at the eyes (a fixed frame count shot the gun mid-glide), at most a second.
                yield return null;
                var eyeCam = pc.Camera.transform;
                var self = pc.GetComponent<Combatant>();
                for (float waited = 0f; waited < 1.2f && (eyeCam.position - self.EyePosition).magnitude > 0.1f; waited += Time.deltaTime) yield return null;
                for (int k = 0; k < 4; k++) yield return null;
                yield return VShot($"{name}_fpp");
                pc.Rig.ToggleMode();
                for (int k = 0; k < 3; k++) yield return null;
            }
            _watch = Watch.Game;
        }

        static readonly Watch[] Out3 = { Watch.Side, Watch.Quarter, Watch.Front };

        int SlotOf(Combatant me, int id)
        {
            for (int s = 0; s < 5; s++) if (me.WeaponInSlot(s) == id) return s;
            return -1;
        }

        IEnumerator Equip(Combatant me, int id)
        {
            var w = GameData.Weapons.Find(id);
            if (SlotOf(me, id) < 0)
            {
                me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = id, Amount = w.MagSize });
                if (!w.IsMelee) me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = w.Ammo, Amount = 90 });
            }
            int slot = SlotOf(me, id);
            if (slot < 0) { Debug.LogWarning("[FeelCapture] weapon " + id + " not in a slot"); yield break; }
            me.ServerSelectSlot(slot);
            yield return new WaitForSeconds(1.0f);
        }

        IEnumerator Place(PlayerController pc, Vector3 home, Vector3 run, float room, Vector2 move, bool backwards = false)
        {
            _frame.Move = Vector2.zero;
            _frame.Sprint = false;
            var from = move == Vector2.zero ? home : home - run * Mathf.Max(0f, room - 1.5f);
            pc.Teleport(from);
            yield return null;
            var facing = move.x != 0f ? Quaternion.Euler(0f, -90f, 0f) * run : backwards ? -run : run;
            pc.LookAt(from + facing * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.4f);
            _frame.Move = move;
        }

        // A motion state: settle, then `n` rounds of (side, quarter, tpp, fpp) with a gap, so strides can be compared.
        IEnumerator Motion(PlayerController pc, Vector3 home, Vector3 run, float room, string name, Vector2 move,
            bool sprint = false, bool slow = false, bool aim = false, bool backwards = false, int n = 3, float gap = 0.13f)
        {
            if (!Want(name)) yield break;
            _frame.SlowWalk = slow;
            _frame.AimHeld = aim;
            _frame.Sprint = sprint;
            yield return Place(pc, home, run, room, move, backwards);
            _frame.Sprint = sprint;
            yield return new WaitForSeconds(0.8f);
            for (int i = 0; i < n; i++)
            {
                yield return Views($"{name}_{i}", true, Watch.Side, Watch.Quarter);
                yield return new WaitForSeconds(gap);
            }
            _frame.Move = Vector2.zero;
            _frame.Sprint = _frame.SlowWalk = _frame.AimHeld = false;
            yield return new WaitForSeconds(0.3f);
        }

        IEnumerator Burst(string name, float[] times, Action start, bool fp = true, params Watch[] views)
        {
            if (views == null || views.Length == 0) views = new[] { Watch.Side };
            if (!Want(name)) yield break;
            float t0 = Time.time;
            start?.Invoke();
            foreach (var at in times)
            {
                while (Time.time - t0 < at) yield return null;
                var animLog = PlayerController.Local.GetComponent<CombatantAnimation>();
                Debug.Log($"[FeelCapture] {name} want {at:F2} real {Time.time - t0:F3} holster {animLog.HolsterProgress:F2} draw {animLog.DrawProgress:F2} held {animLog.HeldId} reload {animLog.ReloadProgress:F2} base {animLog.BaseShown} airborne {animLog.GetComponent<CombatantMotion>().Airborne} y {animLog.transform.position.y:F2}");
                yield return Views($"{name}_t{Mathf.RoundToInt(at * 100):0000}", fp, views);
            }
        }

        // Fire once (held for automatic fire) and leave the trigger after `seconds` of game time.
        IEnumerator Shoot(float seconds)
        {
            _frame.FirePressed = _frame.FireHeld = true;
            float t0 = Time.time;
            while (Time.time - t0 < seconds) yield return null;
            _frame.FirePressed = _frame.FireHeld = false;
        }

        IEnumerator ToView(PlayerController pc, ViewMode mode)
        {
            if (pc.Rig.Mode == mode) yield break;
            pc.Rig.ToggleMode();
            float end = Time.realtimeSinceStartup + 1.5f;
            var self = pc.GetComponent<Combatant>();
            while (Time.realtimeSinceStartup < end && mode == ViewMode.FirstPerson && (pc.Camera.transform.position - self.EyePosition).magnitude > 0.1f) yield return null;
            for (int k = 0; k < 4; k++) yield return null;
        }

        IEnumerator CaseShots(Combatant me, PlayerController pc, Vector3 home, Vector3 run, int id, string tag)
        {
            string name = "r5_weapon_casing_" + tag;
            yield return Equip(me, id);
            pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.5f);
            var fx = me.GetComponent<CasingFx>();
            int before = fx.Ejected;
            // Third person, close on the port from the right: the case leaving and flying.
            yield return ToView(pc, ViewMode.ThirdPerson);
            Time.timeScale = 0.1f;
            float t0 = Time.time;
            StartCoroutine(Shoot(0.02f));
            int n = 0;
            foreach (var at in new[] { 0.03f, 0.08f, 0.13f, 0.18f })
            {
                while (Time.time - t0 < at) yield return null;
                _watch = Watch.Port;
                yield return VShot($"{name}_tp_{n++}_t{Mathf.RoundToInt(at * 100):000}");
            }
            _watch = Watch.Game;
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.6f);
            // First person: one frame just after the shot.
            yield return ToView(pc, ViewMode.FirstPerson);
            Time.timeScale = 0.1f;
            t0 = Time.time;
            StartCoroutine(Shoot(0.02f));
            while (Time.time - t0 < 0.05f) yield return null;
            yield return VShot(name + "_fpp");
            Time.timeScale = 1f;
            yield return ToView(pc, ViewMode.ThirdPerson);
            // On the ground: a few more shots, then close on where they lie, first concrete, then a dirt patch.
            foreach (var ground in new[] { "concrete", "dirt" })
            {
                GameObject patch = null;
                if (ground == "dirt")
                {
                    patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    patch.transform.position = home + Vector3.up * 0.01f;
                    patch.transform.localScale = new Vector3(6f, 0.02f, 6f);
                    patch.AddComponent<SurfaceIdTag>().Id = SurfaceId.Dirt;
                    var r = patch.GetComponent<Renderer>();
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_BaseColor", new Color(0.33f, 0.25f, 0.17f));
                    r.SetPropertyBlock(block);
                    yield return null;
                }
                if (me.ActiveMag < 4)
                {
                    // A full magazine first (the Breacher holds five).
                    _frame.Reload = true;
                    float by = Time.realtimeSinceStartup + 2f;
                    while (!me.Reloading.Value && Time.realtimeSinceStartup < by) yield return null;
                    _frame.Reload = false;
                    by = Time.realtimeSinceStartup + 6f;
                    while (me.Reloading.Value && Time.realtimeSinceStartup < by) yield return null;
                    yield return new WaitForSeconds(0.3f);
                }
                for (int k = 0; k < 4; k++) { yield return Shoot(0.05f); yield return new WaitForSeconds(id == 4 ? 0.6f : 0.25f); }
                yield return new WaitForSeconds(0.5f);
                var right = Vector3.Cross(Vector3.up, run).normalized;
                if (!CasingPool.TryRestingCenter(out var spot)) spot = home + right * 0.9f;
                _fixFrom = spot + right * 0.5f + Vector3.up * 0.55f - run * 0.3f;
                _fixAt = spot;
                _watch = Watch.Fixed;
                yield return VShot($"{name}_ground_{ground}");
                _watch = Watch.Game;
                if (patch != null) Destroy(patch);
                yield return new WaitForSeconds(CasingRules.LifeSeconds);
            }
            Debug.Log($"[FeelCapture] {name}: ejected {fx.Ejected - before} live {CasingPool.Live} sounds {CasingPool.SoundsPlayed}");
        }

        IEnumerator ReloadShots(Combatant me, PlayerController pc, Vector3 home, Vector3 run, int id, string tag)
        {
            string name = "r5_weapon_reload_" + tag;
            yield return Equip(me, id);
            var def = GameData.Weapons.Find(id);
            var anim = me.GetComponent<CombatantAnimation>();
            // Out of the well, gone (hand at the belt), going in, seated.
            var at = new[] { ReloadMotion.MagOutAt + 0.04f, 0.4f, ReloadMotion.MagInAt - 0.04f, 0.92f };
            var label = new[] { "0out", "1gone", "2in", "3done" };
            foreach (var mode in new[] { ViewMode.ThirdPerson, ViewMode.FirstPerson })
            {
                pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
                yield return ToView(pc, mode);
                yield return new WaitForSeconds(1.5f); // past a bolt action's cycle
                yield return Shoot(0.3f);
                yield return new WaitForSeconds(0.6f);
                Time.timeScale = 0.25f;
                _frame.Reload = true;
                float startBy = Time.realtimeSinceStartup + 3f;
                while (anim.ReloadProgress < 0f && Time.realtimeSinceStartup < startBy) yield return null;
                _frame.Reload = false;
                if (anim.ReloadProgress < 0f) Debug.LogWarning($"[FeelCapture] {name}: the reload did not start");
                for (int k = 0; k < at.Length; k++)
                {
                    if (mode == ViewMode.ThirdPerson && anim.ActionLayerWeight > 0.3f)
                    {
                        // The reload clip has its own timing: follow the magazine (out at the well, away, coming back, seated).
                        float by = Time.realtimeSinceStartup + 20f;
                        float awayAt = -1f;
                        while (anim.ReloadProgress >= 0f && Time.realtimeSinceStartup < by)
                        {
                            int ph = anim.ClipMagPhase;
                            if (k == 0 && ph >= 1) break;
                            if (k == 1) { if (ph >= 2 && awayAt < 0f) awayAt = Time.time; if (awayAt >= 0f && Time.time - awayAt > 0.12f) break; if (ph >= 3) break; }
                            if (k == 2 && ph >= 3) break;
                            if (k == 3 && anim.ReloadProgress >= at[k]) break;
                            yield return null;
                        }
                    }
                    else
                        while (anim.ReloadProgress >= 0f && anim.ReloadProgress < at[k]) yield return null;
                    var mag = me.GetComponent<WeaponVisual>().MagazineOf(id);
                    Debug.Log($"[FeelCapture] {name} {mode} {label[k]}: progress {anim.ReloadProgress:F2} magInHand {anim.ActionShown.MagInHand} clip {anim.ClipMagPhase}/{anim.ClipMagToWell:F2} onGun {(mag != null ? mag.OnGun.activeInHierarchy.ToString() : "none")} mesh {(mag != null ? mag.Mesh.name + " " + mag.Mesh.bounds.size.ToString("F3") + " x" + mag.Scale.ToString("F2") : "")}");
                    var held = anim.ReloadMagModel;
                    if (held != null) Debug.Log($"[FeelCapture]   held mag active {held.activeInHierarchy} at cam {pc.Camera.transform.InverseTransformPoint(held.transform.position).ToString("F2")} bounds {held.GetComponent<Renderer>().bounds.size.ToString("F2")} layer {held.layer} mesh {held.GetComponent<MeshFilter>().sharedMesh.name}");
                    _watch = mode == ViewMode.ThirdPerson ? Watch.Port : Watch.Game;
                    yield return VShot($"{name}_{(mode == ViewMode.ThirdPerson ? "tp" : "fpp")}_{label[k]}");
                    _watch = Watch.Game;
                }
                Time.timeScale = 1f;
                yield return new WaitForSeconds(def.ReloadSeconds(0) + 0.3f);
            }
            yield return ToView(pc, ViewMode.ThirdPerson);
        }

        IEnumerator CharShots(Combatant me)
        {
            var pc = PlayerController.Local;
            me.ServerDamageFilter = _ => 0f;
            for (int n = 0; SpawnProtection.InputBlocked && n < 60; n++) yield return new WaitForSeconds(0.25f);
            foreach (var v in Vehicle.All)
                if (v != null) foreach (var r in v.GetComponentsInChildren<Renderer>()) r.enabled = false;
            var user = me.GetComponent<ThrowableUser>();
            if (user != null) user.ServerAdd(3);
            yield return new WaitForSeconds(0.5f);
            var home = me.transform.position;
            var run = LongestFreeDirection(home + Vector3.up * 1.2f, out float room);
            Debug.Log($"[FeelCapture] char shots: {room:F1} m behind along {run}");
            pc.Teleport(home);
            pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.3f);

            bool fpOnly = Environment.GetEnvironmentVariable("CHAR_FP_ONLY") == "1";
            if (Environment.GetEnvironmentVariable("CHAR_ADS_ONLY") == "1")
            {
                yield return AdsAudit(me, pc, home, run);
                PoseShotsDone = true;
                yield break;
            }
            if (!fpOnly) yield return AllThirdPerson(me, pc, home, run, room, user);
            if (Want("K")) yield return FirstPersonOnly(me, pc, home, run, room);
            _watch = Watch.Game;
            PoseShotsDone = true;
            Debug.Log("[FeelCapture] char shots done: " + _folder);
        }

        // #226: third-person ADS per class, standing and crouched, with a helmet on (CHAR_ADS_ONLY=1).
        IEnumerator AdsAudit(Combatant me, PlayerController pc, Vector3 home, Vector3 run)
        {
            me.ServerGive(new LootEntry { Kind = LootKind.Helmet, Tier = 2 });
            me.ServerGive(new LootEntry { Kind = LootKind.Vest, Tier = 2 });
            foreach (var (id, tag) in new[] { (3, "rifle"), (2, "smg"), (4, "shotgun"), (5, "sniper"), (1, "pistol") })
            {
                yield return Equip(me, id);
                foreach (bool crouch in new[] { false, true })
                {
                    _frame.Crouch = crouch; // a press toggles; FeelCapture clears it next frame
                    _frame.AimHeld = true;
                    pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
                    yield return new WaitForSeconds(1.0f);
                    yield return Views($"T_ads_{tag}_{(crouch ? "crouch" : "stand")}", false, Watch.Side, Watch.Front, Watch.Quarter);
                    _frame.AimHeld = false;
                    _frame.Crouch = crouch;
                    yield return new WaitForSeconds(0.5f);
                }
                _frame.AimHeld = false;
                yield return Views($"T_idle_{tag}", false, Watch.Side);
            }
        }

        // K. first person for good (no view toggling): ADS per weapon, run, reload, swap, heal, throw.
        IEnumerator FirstPersonOnly(Combatant me, PlayerController pc, Vector3 home, Vector3 run, float room)
        {
            if (pc.Rig.Mode != ViewMode.FirstPerson) pc.Rig.ToggleMode();
            _watch = Watch.Game;
            pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
            foreach (var (id, tag) in new[] { (3, "rifle"), (1, "pistol"), (4, "shotgun"), (5, "sniper"), (2, "smg") })
            {
                yield return Equip(me, id);
                for (int k = 0; k < 20; k++) yield return null;
                yield return VShot($"K_fp_hip_{tag}");
                // Holding the aim key shoulders the weapon; a tap toggles the sights (AimRules.TapSeconds).
                _frame.AimHeld = true;
                yield return new WaitForSeconds(1.0f);
                yield return VShot($"K_fp_shoulder_{tag}");
                _frame.AimHeld = false;
                yield return new WaitForSeconds(0.5f);
                _frame.AimHeld = true; yield return new WaitForSeconds(0.06f); _frame.AimHeld = false;
                yield return new WaitForSeconds(1.0f);
                yield return VShot($"K_fp_ads_{tag}");
                _frame.AimHeld = true; yield return new WaitForSeconds(0.06f); _frame.AimHeld = false;
                yield return new WaitForSeconds(0.6f);
            }
            yield return Equip(me, 3);
            pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.8f);
            // Idle breathing over time, the aim transition frame by frame, a shot's kick (#217).
            foreach (var i in new[] { 0, 1, 2 }) { yield return VShot($"K_fp_breath_{i}"); yield return new WaitForSeconds(0.32f); }
            _frame.AimHeld = true; yield return new WaitForSeconds(0.06f); _frame.AimHeld = false;
            foreach (var i in new[] { 0, 1, 2, 3 }) { yield return VShot($"K_fp_trans_{i}"); yield return new WaitForSeconds(0.045f); }
            yield return new WaitForSeconds(0.8f);
            _frame.AimHeld = true; yield return new WaitForSeconds(0.06f); _frame.AimHeld = false;
            yield return new WaitForSeconds(0.8f);
            _frame.FirePressed = _frame.FireHeld = true; yield return null; _frame.FirePressed = false;
            yield return VShot("K_fp_fire_0"); _frame.FireHeld = false;
            yield return new WaitForSeconds(0.04f); yield return VShot("K_fp_fire_1");
            yield return new WaitForSeconds(0.3f); yield return VShot("K_fp_fire_2");
            foreach (var t in new[] { 0.2f, 0.6f, 1.2f })
            {
                if (t == 0.2f) { _frame.FirePressed = _frame.FireHeld = true; yield return new WaitForSeconds(0.2f); _frame.FireHeld = false; yield return new WaitForSeconds(0.8f); _frame.Reload = true; yield return null; _frame.Reload = false; }
                yield return new WaitForSeconds(t == 0.2f ? 0.2f : 0.5f);
                yield return VShot($"K_fp_reload_{t:0.0}");
            }
            yield return new WaitForSeconds(2.5f);
            _frame.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.8f);
            foreach (var i in new[] { 0, 1, 2 }) { yield return VShot($"K_fp_run_{i}"); yield return new WaitForSeconds(0.15f); }
            _frame.Sprint = true;
            yield return new WaitForSeconds(0.8f);
            foreach (var i in new[] { 0, 1, 2 }) { yield return VShot($"K_fp_sprint_{i}"); yield return new WaitForSeconds(0.15f); }
            _frame.Move = Vector2.zero; _frame.Sprint = false;
            pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.5f);
            _frame.Holster = true; yield return new WaitForSeconds(0.1f);
            yield return VShot("K_fp_holstered");
            _frame.Holster = true; yield return new WaitForSeconds(0.1f);
            yield return VShot("K_fp_drawn_0");
            _frame.Jump = true; yield return new WaitForSeconds(0.25f);
            yield return VShot("K_fp_jump");
            yield return new WaitForSeconds(0.45f); yield return VShot("K_fp_jump_fall");
            yield return new WaitForSeconds(0.12f); yield return VShot("K_fp_land_0");
            yield return new WaitForSeconds(0.08f); yield return VShot("K_fp_land_1");
            yield return new WaitForSeconds(1f);
            me.ServerDamageFilter = d => d.Amount;
            me.ServerTakeDamage(new DamageInfo { Amount = 10f, Zone = HitZone.Body });
            yield return new WaitForSeconds(0.08f);
            yield return VShot("K_fp_hit");
            me.ServerDamageFilter = d => d.Amount; me.ServerTakeDamage(45f); me.ServerDamageFilter = _ => 0f;
            var healing = me.GetComponent<CombatantHealing>();
            healing.Bandages.Value = 1; // the bag is full of ammo by now: set the count directly
            Debug.Log($"[FeelCapture] fp heal prep count {healing.CountOf(HealKind.Bandage)} reloading {me.Reloading.Value} hp {me.Hp.Value} team {me.TeamBlocksAction}");
            Debug.Log("[FeelCapture] fp heal started " + healing.ServerBegin(HealKind.Bandage));
            Time.timeScale = 0.3f; // the shots take longer than the motion
            foreach (var t in new[] { 0.3f, 0.9f, 1.4f }) { yield return new WaitForSeconds(t == 0.3f ? 0.3f : 0.5f); yield return VShot($"K_fp_heal_{Mathf.RoundToInt(t * 10)}"); }
            Time.timeScale = 1f;
            yield return new WaitForSeconds(4f);
            me.ServerDamageFilter = d => d.Amount; me.ServerTakeDamage(20f); me.ServerDamageFilter = _ => 0f;
            healing.Drinks.Value = 2;
            Debug.Log("[FeelCapture] fp drink started " + healing.ServerBegin(HealKind.Drink));
            Time.timeScale = 0.3f;
            foreach (var t in new[] { 0.3f, 1.0f }) { yield return new WaitForSeconds(t == 0.3f ? 0.3f : 0.7f); yield return VShot($"K_fp_drink_{Mathf.RoundToInt(t * 10)}"); }
            Time.timeScale = 1f;
            yield return new WaitForSeconds(4f);
            var thrower = me.GetComponent<ThrowableUser>();
            if (thrower != null) thrower.ServerAdd(3);
            me.ServerSelectSlot(Loadout.ThrowableSlot);
            yield return new WaitForSeconds(1f);
            yield return VShot("K_fp_throwable");
            { var ia = me.GetComponent<CombatantAnimation>(); Debug.Log($"[FeelCapture] throwable slot {me.ActiveSlot.Value} holstered {me.Holstered.Value} total {thrower.TotalCount} active {ia.ItemActive} w {ia.ItemWeight:F2} prop {ia.ItemPose.Prop} cam {PlayerController.Local.Rig.Mode}"); }
            Time.timeScale = 0.3f;
            if (thrower != null) thrower.ServerBeginCook();
            yield return new WaitForSeconds(0.5f); yield return VShot("K_fp_throw_pin");
            _frame.FirePressed = _frame.FireHeld = true; yield return null; _frame.FirePressed = false;
            yield return new WaitForSeconds(0.6f); yield return VShot("K_fp_throw_windup");
            _frame.FireHeld = false; _frame.FireReleased = true; yield return null; _frame.FireReleased = false;
            foreach (var t in new[] { 0.1f, 0.2f, 0.4f }) { yield return new WaitForSeconds(t == 0.1f ? 0.1f : 0.15f); yield return VShot($"K_fp_throw_rel_{Mathf.RoundToInt(t * 10)}"); }
            Time.timeScale = 1f;
            yield return new WaitForSeconds(1f);
            // Reach for loot and a door, bare hands (#212).
            me.ServerSetHolstered(true);
            yield return new WaitForSeconds(0.8f);
            Time.timeScale = 0.3f;
            me.GetComponent<CombatantAnimation>().StartGesture(HandGesture.Pickup);
            foreach (var t in new[] { 0.1f, 0.2f }) { yield return new WaitForSeconds(0.12f); yield return VShot($"K_fp_pickup_{Mathf.RoundToInt(t * 10)}"); }
            yield return new WaitForSeconds(0.6f);
            me.GetComponent<CombatantAnimation>().StartGesture(HandGesture.Door);
            yield return new WaitForSeconds(0.12f); yield return VShot("K_fp_door_0");
            Time.timeScale = 1f;
            me.ServerSetHolstered(false);
            if (pc.Rig.Mode != ViewMode.ThirdPerson) pc.Rig.ToggleMode();
        }

        IEnumerator AllThirdPerson(Combatant me, PlayerController pc, Vector3 home, Vector3 run, float room, ThrowableUser user)
        {
            // A. idle per weapon class.
            foreach (var (id, tag) in new[] { (3, "rifle"), (1, "pistol"), (2, "smg"), (4, "shotgun"), (5, "sniper"), (21, "melee") })
            {
                yield return Equip(me, id);
                yield return Views("A_idle_" + tag, true, Out3);
            }
            me.ServerSetHolstered(true);
            yield return new WaitForSeconds(0.8f);
            yield return Views("A_idle_unarmed", true, Out3);
            me.ServerSetHolstered(false);
            me.ServerSelectSlot(Loadout.ThrowableSlot);
            yield return new WaitForSeconds(1.0f);
            yield return Views("A_idle_throwable", true, Out3);

            // B. locomotion, rifle then pistol.
            foreach (var (id, tag) in new[] { (3, "rifle"), (1, "pistol") })
            {
                yield return Equip(me, id);
                yield return Motion(pc, home, run, room, $"B_{tag}_walk", new Vector2(0f, 1f), slow: true);
                yield return Motion(pc, home, run, room, $"B_{tag}_run", new Vector2(0f, 1f));
                yield return Motion(pc, home, run, room, $"B_{tag}_sprint", new Vector2(0f, 1f), sprint: true);
                yield return Motion(pc, home, run, room, $"B_{tag}_back", new Vector2(0f, -1f), backwards: true);
                yield return Motion(pc, home, run, room, $"B_{tag}_strafeR", new Vector2(1f, 0f));
                yield return Motion(pc, home, run, room, $"B_{tag}_strafeL", new Vector2(-1f, 0f));
                yield return Motion(pc, home, run, room, $"B_{tag}_diag", new Vector2(1f, 1f));
            }
            // SMG and shotgun and melee run, to see each class's carry.
            foreach (var (id, tag) in new[] { (2, "smg"), (4, "shotgun"), (5, "sniper"), (21, "melee") })
            {
                yield return Equip(me, id);
                yield return Motion(pc, home, run, room, $"B_{tag}_run", new Vector2(0f, 1f), n: 2);
            }
            me.ServerSetHolstered(true);
            yield return new WaitForSeconds(0.6f);
            yield return Motion(pc, home, run, room, "B_unarmed_run", new Vector2(0f, 1f), n: 3);
            yield return Motion(pc, home, run, room, "B_unarmed_sprint", new Vector2(0f, 1f), sprint: true, n: 3);
            me.ServerSetHolstered(false);

            // C. crouch / prone.
            yield return Equip(me, 3);
            _frame.Crouch = true; yield return new WaitForSeconds(0.7f);
            yield return Views("C_crouch_idle", true, Out3);
            yield return Motion(pc, home, run, room, "C_crouch_walk", new Vector2(0f, 1f));
            yield return Motion(pc, home, run, room, "C_crouch_back", new Vector2(0f, -1f), backwards: true); // #303: the Mixamo back walk
            _frame.Crouch = true; yield return new WaitForSeconds(0.2f);
            _frame.Prone = true; yield return new WaitForSeconds(1.2f);
            yield return Views("C_prone_idle", true, Out3);
            yield return Motion(pc, home, run, room, "C_prone_crawl", new Vector2(0f, 1f));
            _frame.Prone = true; yield return new WaitForSeconds(1.0f);

            // D. aim: ADS idle per class, hip fire, lean.
            foreach (var (id, tag) in new[] { (3, "rifle"), (1, "pistol"), (4, "shotgun"), (5, "sniper"), (2, "smg") })
            {
                yield return Equip(me, id);
                _frame.AimHeld = true;
                pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
                yield return new WaitForSeconds(0.9f);
                yield return Views("D_ads_" + tag, true, Watch.Side, Watch.Quarter);
                _frame.AimHeld = false;
                yield return new WaitForSeconds(0.4f);
            }
            yield return Equip(me, 3);
            pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.4f);
            yield return Burst("D_hipfire", new[] { 0.05f, 0.15f, 0.3f }, () => _frame.FirePressed = _frame.FireHeld = true);
            _frame.FireHeld = false;
            yield return Views("D_lean_none_hip", false, Watch.Behind, Watch.Front);
            foreach (var left in new[] { true, false })
                foreach (var aim in new[] { false, true })
                {
                    _frame.LeanLeft = left; _frame.LeanRight = !left; _frame.AimHeld = aim;
                    yield return new WaitForSeconds(0.7f);
                    var anim = me.GetComponent<CombatantAnimation>();
                    Debug.Log($"[FeelCapture] lean left={left} plantL {anim.FootPlantLeft:F3} plantR {anim.FootPlantRight:F3} footLow {anim.FootLow:F3} rest {anim.FootRest:F3} | {anim.PlantDebug[0]} | {anim.PlantDebug[1]}");
                    yield return Views($"D_lean_{(left ? "Q" : "E")}_{(aim ? "ads" : "hip")}", true, Watch.Front, Watch.Behind);
                }
            _frame.LeanLeft = _frame.LeanRight = _frame.AimHeld = false;
            yield return new WaitForSeconds(0.5f);

            // r5 weapon effects (#199 #200): cases leaving the port (third person close from the right side, 4 frames
            // 0.05 s apart, and one first person frame), cases lying on concrete and on dirt, and the magazine leaving,
            // gone, going in and seated in third and first person. Slowed so each frame lands inside the motion.
            if (Want("r5_weapon_casing_rifle") || Want("r5_weapon_casing_pistol") || Want("r5_weapon_casing_shotgun") || Want("r5_weapon_reload_mesh_rifle") || Want("r5_weapon_reload_block_sniper") || Want("r5_weapon_inspect"))
            {
                foreach (var (id, tag) in new[] { (9, "rifle"), (1, "pistol"), (4, "shotgun") })
                    if (Want("r5_weapon_casing_" + tag)) yield return CaseShots(me, pc, home, run, id, tag);
                foreach (var (id, tag) in new[] { (9, "mesh_rifle"), (5, "block_sniper") })
                    if (Want("r5_weapon_reload_" + tag)) yield return ReloadShots(me, pc, home, run, id, tag);
                if (Want("r5_weapon_inspect")) yield return Equip(me, 9);
                pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
                yield return new WaitForSeconds(0.5f);
                var inspectAnim = me.GetComponent<CombatantAnimation>();
                float inspectSecs = ReloadMotion.InspectSeconds;
                yield return Burst("r5_weapon_inspect", new[] { 0.1f, 0.3f, 0.55f, 0.7f }.Select(k => k * inspectSecs).ToArray(), () => inspectAnim.StartInspect(), false, Watch.Side);
                yield return new WaitForSeconds(1f);
            }

            // E. reload per weapon.            // E. reload per weapon.
            foreach (var (id, tag) in new[] { (3, "rifle"), (1, "pistol"), (4, "shotgun"), (2, "smg"), (5, "sniper") })
            {
                yield return Equip(me, id);
                pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
                _frame.FirePressed = _frame.FireHeld = true; yield return new WaitForSeconds(0.2f);
                _frame.FireHeld = false; yield return new WaitForSeconds(0.8f);
                yield return Burst("E_reload_" + tag, new[] { 0.1f, 0.4f, 0.8f, 1.3f, 1.9f }, () => _frame.Reload = true);
                _frame.Reload = false;
                yield return new WaitForSeconds(2.5f);
            }

            // F. draw / holster / swap.
            yield return Equip(me, 3);
            Time.timeScale = 0.15f; // the shots take longer than the motion: slow the game so each frame lands inside it
            yield return Burst("F_holster", new[] { 0.04f, 0.1f, 0.16f, 0.22f, 0.3f }, () => _frame.Holster = true, false);
            yield return Burst("F_draw", new[] { 0.04f, 0.1f, 0.18f, 0.26f, 0.4f }, () => _frame.Holster = true, false);
            Time.timeScale = 1f;
            yield return new WaitForSeconds(1f);
            int other = SlotOf(me, 1);
            yield return Burst("F_swap_rifle_to_pistol", new[] { 0.05f, 0.15f, 0.3f, 0.5f, 0.8f }, () => me.ServerSelectSlot(other));
            yield return new WaitForSeconds(0.8f);
            yield return Burst("F_swap_to_melee", new[] { 0.05f, 0.2f, 0.4f, 0.8f }, () => me.ServerSelectSlot(SlotOf(me, 21)));
            yield return new WaitForSeconds(0.4f);
            // melee swing
            yield return Burst("F_melee_swing", new[] { 0.05f, 0.15f, 0.25f, 0.4f }, () => _frame.FirePressed = _frame.FireHeld = true);
            _frame.FireHeld = false;
            yield return new WaitForSeconds(0.8f);
            yield return Equip(me, 3);

            // G. throw.
            me.ServerSelectSlot(Loadout.ThrowableSlot);
            yield return new WaitForSeconds(1.0f);
            Time.timeScale = 0.25f;
            yield return Burst("G_throw_hold", new[] { 0.05f }, null, false);
            if (user != null) user.ServerBeginCook();
            yield return Burst("G_throw_cook", new[] { 0.05f, 0.4f }, () => _frame.FireHeld = _frame.FirePressed = true, false);
            yield return Burst("G_throw_release", new[] { 0.04f, 0.1f, 0.17f, 0.24f, 0.32f, 0.45f }, () => { _frame.FireHeld = false; _frame.FireReleased = true; }, false);
            _frame.FireReleased = false;
            Time.timeScale = 1f;
            yield return new WaitForSeconds(2f);

            // H. heal / eat / drink.
            me.ServerDamageFilter = d => d.Amount;
            me.ServerTakeDamage(50f);
            me.ServerDamageFilter = _ => 0f;
            me.ServerGive(new LootEntry { Kind = LootKind.Bandage, Amount = 2 });
            me.ServerGive(new LootEntry { Kind = LootKind.FirstAidKit, Amount = 1 });
            me.ServerGive(new LootEntry { Kind = LootKind.Drink, Amount = 2 });
            var healing = me.GetComponent<CombatantHealing>();
            foreach (var (kind, tag) in new[] { (HealKind.Bandage, "bandage"), (HealKind.FirstAid, "firstaid"), (HealKind.Drink, "drink") })
            {
                bool ok = healing.ServerBegin(kind);
                Debug.Log($"[FeelCapture] heal {tag} started {ok}");
                Time.timeScale = 0.3f;
                yield return Burst("H_heal_" + tag, new[] { 0.3f, 0.9f, 1.5f }, null, false);
                Time.timeScale = 1f;
                yield return new WaitForSeconds(4f);
            }

            // L. reach for loot and a door.
            me.ServerSetHolstered(true);
            yield return new WaitForSeconds(0.8f);
            var reach = me.GetComponent<CombatantAnimation>();
            Time.timeScale = 0.25f;
            yield return Burst("L_pickup", new[] { 0.05f, 0.15f, 0.27f, 0.4f }, () => reach.StartGesture(HandGesture.Pickup), false);
            yield return new WaitForSeconds(0.5f);
            yield return Burst("L_door", new[] { 0.05f, 0.12f, 0.22f, 0.35f }, () => reach.StartGesture(HandGesture.Door), false);
            Time.timeScale = 1f;
            me.ServerSetHolstered(false);

            // I. jump, turn in place.
            yield return Equip(me, 3);
            pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.5f);
            yield return Burst("I_jump_stand", new[] { 0.08f, 0.2f, 0.35f, 0.5f, 0.7f }, () => _frame.Jump = true);
            yield return new WaitForSeconds(1f);
            yield return Place(pc, home, run, room, new Vector2(0f, 1f));
            _frame.Sprint = true;
            yield return new WaitForSeconds(0.7f);
            yield return Burst("I_jump_run", new[] { 0.08f, 0.2f, 0.35f, 0.5f, 0.7f }, () => _frame.Jump = true);
            _frame.Move = Vector2.zero; _frame.Sprint = false;
            yield return new WaitForSeconds(1f);
            // A 6 m drop (#304): no push-off, the airborne loop, then the landing (falls ~0.93 s at the game's gravity).
            pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.5f);
            yield return Burst("I_drop_6m", new[] { 0.1f, 0.5f, 0.85f, 0.95f, 1.05f, 1.2f, 1.4f }, () => pc.Teleport(home + Vector3.up * 6f));
            yield return new WaitForSeconds(1f);
            pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.5f);
            for (int step = 0; step < 4; step++)
            {
                for (int k = 0; k < 6; k++) { _frame.Look = new Vector2(15f, 0f); yield return null; }
                yield return Views($"I_turn_{step}", true, Watch.Side);
            }

            // S. ground planting on a slope, breathing over time.
            if (Want("S_"))
            {
                var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ramp.name = "CharRamp";
                ramp.transform.localScale = new Vector3(4f, 0.3f, 6f);
                ramp.transform.position = home + run * 3f + Vector3.up * 0.45f;
                ramp.transform.rotation = Quaternion.LookRotation(run, Vector3.up) * Quaternion.Euler(-18f, 0f, 0f);
                pc.Teleport(home + run * 3f + Vector3.up * 1.2f);
                pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
                yield return new WaitForSeconds(1.2f);
                yield return Views("S_slope_rifle", false, Watch.Side, Watch.Quarter);
                _frame.Crouch = true; yield return new WaitForSeconds(0.8f);
                yield return Views("S_slope_crouch", false, Watch.Side, Watch.Front);
                _frame.Crouch = true; yield return new WaitForSeconds(0.5f);
                Destroy(ramp);
                pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
                yield return new WaitForSeconds(0.8f);
                foreach (var t in new[] { 0f, 0.9f, 1.9f, 2.8f }) { yield return new WaitForSeconds(t == 0f ? 0f : 0.9f); yield return Views($"S_breath_{t:0.0}", false, Watch.Side); }
            }

            // J. hit reaction and death.
            me.ServerDamageFilter = d => d.Amount;
            yield return Burst("J_hit", new[] { 0.05f, 0.15f, 0.3f, 0.6f }, () => me.ServerTakeDamage(new DamageInfo { Amount = 10f, Zone = HitZone.Body }));
            yield return new WaitForSeconds(1.5f);
            yield return Burst("J_death", new[] { 0.1f, 0.4f, 0.8f, 1.5f, 2.5f }, () => me.ServerTakeDamage(new DamageInfo { Amount = 999f, Zone = HitZone.Body }));
        }
    }
}
