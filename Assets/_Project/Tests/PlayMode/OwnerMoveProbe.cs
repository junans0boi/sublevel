using System;
using System.Collections;
using Game.Gameplay;
using Game.Net;
using UnityEngine;

namespace Game.Tests
{
    // #485 "움직이면 1~2초 뒤에 움직여짐": the owner's own body is its prediction (ADR-029), so W must move it on the very
    // frame it is read. Holds W (then W+Shift) on the local player and measures the frames from the press to the first
    // movement, every server correction it receives (CombatantStance.Corrected), jumps of the body, the longest time it stood
    // still with W held, and the worst frame.
    public sealed class OwnerMoveProbe
    {
        public int FramesToMove = -1;
        public float SecondsToMove = -1f; // from the press to the first movement (a frame or a few: CharacterController skips moves under 1 mm)
        public int Corrections;
        public int SnapsBack;
        public float MaxFrameMs;
        public float Meters;
        public float LongestStuck, LongestBlocked; // seconds: W held but the body still / SpawnProtection.InputBlocked on
        public string Start = "", End = "";

        public override string ToString() =>
            $"framesToMove={FramesToMove} secondsToMove={SecondsToMove:F3} corrections={Corrections} snapsBack={SnapsBack} meters={Meters:F1} maxFrameMs={MaxFrameMs:F0} stuckS={LongestStuck:F2} blockedS={LongestBlocked:F2} | at start: {Start} | at end: {End}";

        // What could hold the body: printed with the numbers so a failure says why.
        public static string State()
        {
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            if (me == null || pc == null) return "no local player";
            var cc = pc.GetComponent<CharacterController>();
            var p = me.transform.position;
            bool wall = Physics.Raycast(p + Vector3.up, me.transform.forward, out var hit, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(me.transform);
            return $"pos={p} yaw={me.transform.eulerAngles.y:F0} external={pc.ExternalControl} parkour={pc.InParkour} swim={pc.Swimming} blocked={SpawnProtection.InputBlocked} " +
                   $"riding={ElevatorPanel.Riding} island={WaitingHud.OnIsland} ui={UiFocus.Current} result={ResultScreen.BlocksGameplay} dead={me.Dead.Value} " +
                   $"cc={(cc != null && cc.enabled)} grounded={(cc != null && cc.isGrounded)} lastMove={pc.LastInput.Move} wallAhead={(wall ? hit.collider.name + "@" + hit.distance.ToString("F2") : "none")}";
        }

        public static IEnumerator Run(HarnessInput input, OwnerMoveProbe result, float seconds = 4f)
        {
            var me = Combatant.LocalPlayer;
            var stance = me.GetComponent<CombatantStance>();
            Action<Vector3> onCorrected = _ => result.Corrections++;
            stance.Corrected += onCorrected;
            try
            {
                var start = me.transform.position;
                result.Start = State();
                input.Frame.Move = new Vector2(0f, 1f);
                int frames = 0;
                float pressed = Time.realtimeSinceStartup;
                // Accelerate gives about 0.5 m/s in the first frame: ~1 cm at 60 fps.
                while (Flat(me.transform.position - start) < 0.001f && frames < 600) { yield return null; frames++; }
                result.FramesToMove = frames;
                result.SecondsToMove = Time.realtimeSinceStartup - pressed;
                input.Frame.Sprint = true;
                var last = me.transform.position;
                float t0 = Time.realtimeSinceStartup, end = t0 + seconds, stuck = 0f, blocked = 0f;
                while (Time.realtimeSinceStartup < end)
                {
                    // Back and forth every 1.5 s: a long run stays where it started (island, field) instead of meeting a wall.
                    input.Frame.Move = new Vector2(0f, ((int)((Time.realtimeSinceStartup - t0) / 1.5f) & 1) == 0 ? 1f : -1f);
                    yield return null;
                    float dt = Time.unscaledDeltaTime;
                    result.MaxFrameMs = Mathf.Max(result.MaxFrameMs, dt * 1000f);
                    var p = me.transform.position;
                    float step = Flat(p - last);
                    result.Meters += step; // walked in all, there and back
                    // Faster than any run: the server (or anything else) put the body somewhere.
                    if (step > 10f * dt + 0.5f) result.SnapsBack++;
                    stuck = step < 0.0005f ? stuck + dt : 0f;
                    blocked = SpawnProtection.InputBlocked ? blocked + dt : 0f;
                    result.LongestStuck = Mathf.Max(result.LongestStuck, stuck);
                    result.LongestBlocked = Mathf.Max(result.LongestBlocked, blocked);
                    last = p;
                }
                result.End = State();
            }
            finally
            {
                input.Frame.Move = Vector2.zero;
                input.Frame.Sprint = false;
                stance.Corrected -= onCorrected;
            }
        }

        static float Flat(Vector3 d) => new Vector2(d.x, d.z).magnitude;
    }
}
