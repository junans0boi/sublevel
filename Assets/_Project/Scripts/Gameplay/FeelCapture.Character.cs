using System;
using System.Collections;
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

        public static FeelCapture StartCharShots(string folder)
        {
            var capture = StartPoseShots(folder);
            capture._charShots = true;
            return capture;
        }

        // Outside views in third person, then the player's own eyes.
        IEnumerator Views(string name, bool fp = true, params Watch[] outside)
        {
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
                // The weapon is moved to the eye by the view's own update: give it frames, or the shot shows no gun.
                for (int k = 0; k < 6; k++) yield return null;
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

        IEnumerator Burst(string name, float[] times, Action start, bool fp = true)
        {
            float t0 = Time.time;
            start?.Invoke();
            foreach (var at in times)
            {
                while (Time.time - t0 < at) yield return null;
                yield return Views($"{name}_t{Mathf.RoundToInt(at * 100):0000}", fp, Watch.Side);
            }
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
            if (!fpOnly) yield return AllThirdPerson(me, pc, home, run, room, user);
            yield return FirstPersonOnly(me, pc, home, run, room);
            _watch = Watch.Game;
            PoseShotsDone = true;
            Debug.Log("[FeelCapture] char shots done: " + _folder);
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
            yield return new WaitForSeconds(1f);
            me.ServerDamageFilter = d => d.Amount;
            me.ServerTakeDamage(new DamageInfo { Amount = 10f, Zone = HitZone.Body });
            yield return new WaitForSeconds(0.08f);
            yield return VShot("K_fp_hit");
            me.ServerDamageFilter = _ => 0f;
            var healing = me.GetComponent<CombatantHealing>();
            me.ServerGive(new LootEntry { Kind = LootKind.Bandage, Amount = 1 });
            Debug.Log("[FeelCapture] fp heal started " + healing.ServerBegin(HealKind.Bandage));
            yield return new WaitForSeconds(1f);
            yield return VShot("K_fp_heal");
            yield return new WaitForSeconds(4f);
            me.ServerSelectSlot(Loadout.ThrowableSlot);
            yield return new WaitForSeconds(1f);
            yield return VShot("K_fp_throwable");
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
            foreach (var left in new[] { true, false })
                foreach (var aim in new[] { false, true })
                {
                    _frame.LeanLeft = left; _frame.LeanRight = !left; _frame.AimHeld = aim;
                    yield return new WaitForSeconds(0.7f);
                    yield return Views($"D_lean_{(left ? "Q" : "E")}_{(aim ? "ads" : "hip")}", true, Watch.Front, Watch.Behind);
                }
            _frame.LeanLeft = _frame.LeanRight = _frame.AimHeld = false;
            yield return new WaitForSeconds(0.5f);

            // E. reload per weapon.
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
            yield return Burst("F_holster", new[] { 0.05f, 0.2f, 0.4f, 0.7f }, () => _frame.Holster = true);
            yield return Burst("F_draw", new[] { 0.05f, 0.2f, 0.4f, 0.7f }, () => _frame.Holster = true);
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
            if (user != null) user.ServerBeginCook();
            yield return Burst("G_throw_cook", new[] { 0.05f, 0.4f }, () => _frame.FireHeld = _frame.FirePressed = true);
            yield return Burst("G_throw_release", new[] { 0.05f, 0.15f, 0.3f, 0.6f }, () => { _frame.FireHeld = false; _frame.FireReleased = true; });
            _frame.FireReleased = false;
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
                yield return Burst("H_heal_" + tag, new[] { 0.3f, 1.0f, 2.0f }, null);
                yield return new WaitForSeconds(4f);
            }

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
            pc.Teleport(home); pc.LookAt(home + run * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.5f);
            for (int step = 0; step < 4; step++)
            {
                for (int k = 0; k < 6; k++) { _frame.Look = new Vector2(15f, 0f); yield return null; }
                yield return Views($"I_turn_{step}", true, Watch.Side);
            }

            // J. hit reaction and death.
            me.ServerDamageFilter = d => d.Amount;
            yield return Burst("J_hit", new[] { 0.05f, 0.15f, 0.3f, 0.6f }, () => me.ServerTakeDamage(new DamageInfo { Amount = 10f, Zone = HitZone.Body }));
            yield return new WaitForSeconds(1.5f);
            yield return Burst("J_death", new[] { 0.1f, 0.4f, 0.8f, 1.5f, 2.5f }, () => me.ServerTakeDamage(new DamageInfo { Amount = 999f, Zone = HitZone.Body }));
        }
    }
}
