using System;
using System.Collections;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Pose review shots (#111, ADR-083): a rifle in hand standing, slow walking, running, sprinting, crouch-walking,
    // shouldered standing and walking, the raise from a sprint, and a strafe; each from the right side, the front-left
    // quarter, the left side and the game camera, several frames apart. Run with the play mode capture test
    //   POSE_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.PoseCaptureTests
    public partial class FeelCapture
    {
        bool _poseShots;

        public static bool PoseShotsDone { get; private set; }

        // Call before loading the scene.
        public static FeelCapture StartPoseShots(string folder)
        {
            PoseShotsDone = false;
            // Before the scene loads: the player reads its input source once, when it is created.
            var go = new GameObject("FeelCapture");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<FeelCapture>();
            capture._folder = System.IO.Path.GetFullPath(folder);
            capture._poseShots = true;
            PlayerController.InputOverride = capture;
            return capture;
        }

        IEnumerator PoseShots(Combatant me)
        {
            var pc = PlayerController.Local;
            me.ServerDamageFilter = _ => 0f;
            for (int n = 0; SpawnProtection.InputBlocked && n < 60; n++) yield return new WaitForSeconds(0.25f);
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = GameData.Weapons.Find(3).Ammo, Amount = 90 });
            yield return new WaitForSeconds(1f);
            var anim = me.GetComponent<CombatantAnimation>();
            // The parked vehicles stand in the way of the outside views.
            foreach (var v in Vehicle.All)
                if (v != null) foreach (var r in v.GetComponentsInChildren<Renderer>()) r.enabled = false;
            var home = me.transform.position;
            var run = LongestFreeDirection(home + Vector3.up * 1.2f, out float room);
            Debug.Log($"[FeelCapture] pose shots: {room:F1} m free behind the start along {run}");

            yield return PoseCase(pc, home, run, room, "a_idle", Vector2.zero);
            yield return PoseCase(pc, home, run, room, "b_walk", new Vector2(0f, 1f), slowWalk: true);
            yield return PoseCase(pc, home, run, room, "c_run", new Vector2(0f, 1f));
            yield return PoseCase(pc, home, run, room, "d_sprint", new Vector2(0f, 1f), sprint: true);
            _frame.Crouch = true;
            yield return new WaitForSeconds(0.6f);
            yield return PoseCase(pc, home, run, room, "e_crouchwalk", new Vector2(0f, 1f));
            _frame.Crouch = true;
            yield return new WaitForSeconds(0.6f);
            yield return PoseCase(pc, home, run, room, "f_aim_idle", Vector2.zero, aim: true);
            yield return PoseCase(pc, home, run, room, "g_aim_walk", new Vector2(0f, 1f), aim: true);
            yield return PoseCase(pc, home, run, room, "h_strafe_right", new Vector2(1f, 0f));

            // Sprint, then fire: the raise, frame by frame from the side.
            _watch = Watch.Side;
            yield return StartRun(pc, home, run, room, new Vector2(0f, 1f), true);
            yield return new WaitForSeconds(0.8f);
            _frame.Sprint = false;
            _frame.Move = Vector2.zero;
            // Hip fire raises it at once (holding aim only counts as aiming after the tap window, AimRules.TapSeconds).
            _frame.FirePressed = _frame.FireHeld = true;
            float start = Time.time;
            foreach (float at in new[] { 0f, 0.05f, 0.1f, 0.15f, 0.25f })
            {
                while (Time.time - start < at) yield return null;
                Debug.Log($"[FeelCapture] raise {at:F2}: ready {anim.RifleReady:F2} hold {anim.RifleHoldShown:F2}");
                yield return VShot($"i_raise_{Mathf.RoundToInt(at * 100):000}");
            }
            _frame.FireHeld = false;
            _watch = Watch.Game;
            PoseShotsDone = true;
            Debug.Log("[FeelCapture] pose shots done: " + _folder);
        }

        IEnumerator StartRun(PlayerController pc, Vector3 home, Vector3 run, float room, Vector2 move, bool sprint)
        {
            _frame.Move = Vector2.zero;
            _frame.Sprint = false;
            // Moving: start at the near end of the free line so there is room to get up to speed.
            var from = move == Vector2.zero ? home : home - run * Mathf.Max(0f, room - 1.5f);
            pc.Teleport(from);
            yield return null;
            var facing = move.x != 0f ? Quaternion.Euler(0f, -90f, 0f) * run : run;
            pc.LookAt(from + facing * 10f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.4f);
            _frame.Move = move;
            _frame.Sprint = sprint;
        }

        IEnumerator PoseCase(PlayerController pc, Vector3 home, Vector3 run, float room, string name, Vector2 move,
            bool sprint = false, bool slowWalk = false, bool aim = false)
        {
            _frame.SlowWalk = slowWalk;
            _frame.AimHeld = aim;
            yield return StartRun(pc, home, run, room, move, sprint);
            yield return new WaitForSeconds(0.7f);
            var anim = Combatant.LocalPlayer.GetComponent<CombatantAnimation>();
            for (int i = 0; i < 3; i++)
            {
                Debug.Log($"[FeelCapture] {name} {i}: speed {Combatant.LocalPlayer.GetComponent<CombatantMotion>().LocalVelocity.magnitude:F1} hold {anim.RifleHoldShown:F2} ready {anim.RifleReady:F2} pitch {anim.RiflePose.Pitch:F0} upper {anim.UpperState}");
                foreach (var (view, tag) in new[] { (Watch.Side, "side"), (Watch.Quarter, "quarter"), (Watch.LeftSide, "left"), (Watch.Game, "tpp") })
                {
                    _watch = view;
                    yield return VShot($"{name}_{tag}_{i}");
                }
                yield return new WaitForSeconds(0.12f);
            }
            _watch = Watch.Game;
            _frame.Move = Vector2.zero;
            _frame.Sprint = _frame.SlowWalk = _frame.AimHeld = false;
            yield return new WaitForSeconds(0.3f);
        }

        // The level direction with the most open floor through `at` (eight directions); `room` = free distance behind `at`.
        static Vector3 LongestFreeDirection(Vector3 at, out float room)
        {
            var best = Vector3.forward;
            room = 0f;
            float bestTotal = 0f;
            for (int i = 0; i < 8; i++)
            {
                var dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                float ahead = Physics.Raycast(at, dir, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 60f;
                float behind = Physics.Raycast(at, -dir, out var back, 60f, ~0, QueryTriggerInteraction.Ignore) ? back.distance : 60f;
                if (ahead + behind > bestTotal)
                {
                    bestTotal = ahead + behind;
                    room = behind;
                    best = dir;
                }
            }
            return best;
        }
    }
}
