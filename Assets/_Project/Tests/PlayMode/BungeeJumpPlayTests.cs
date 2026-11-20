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
    // The bungee jump as the jumper sees it (#419, 준환 r5.2 "박진감이 필요함"): a frame sequence through the player's own camera and a
    // side view, with a telemetry line per frame. Run on purpose with
    //   BUNGEE_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.BungeeJumpPlayTests.CaptureJump
    public class BungeeJumpPlayTests
    {
        // F is held down while `Press` is set (the jumper asks to come down).
        sealed class Keys : IPlayerInput { public bool Press; public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1, Interact = Press }; }
        Keys _keys;

        bool _oldFlag;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _oldFlag = MatchRules.Current.UseWaitingIsland;
            MatchRules.Current.UseWaitingIsland = true;
            WaitingRoom.ForceEnabled = true;
            DedicatedServer.StartSecondsOverride = 180f;
            PlayerController.InputOverride = _keys = new Keys();
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 90f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null || WaitingRoom.Current == null)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "world loaded and the waiting room open");
                yield return null;
            }
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MatchRules.Current.UseWaitingIsland = _oldFlag;
            WaitingRoom.ForceEnabled = false;
            DedicatedServer.StartSecondsOverride = -1f;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static float DeckY => GameObject.Find("WaitingIsland").transform.position.y;
        static Vector3 Local(float lx, float y, float lz) => new Vector3(WaitingIsland.WorldX(lx), y, WaitingIsland.WorldZ(lz));

        [UnityTest, Explicit, Category("Capture"), Timeout(300000)]
        public IEnumerator CaptureJump()
        {
            string folder = System.Environment.GetEnvironmentVariable("BUNGEE_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = Path.Combine(Application.dataPath, "../Logs/bungee-shots");
            Directory.CreateDirectory(folder);
            float deck = DeckY;
            var pad = WaitingIsland.BungeePad;
            var pc = PlayerController.Local;
            var ride = pc.GetComponent<BungeeRide>();
            pc.Teleport(Local(pad.x, deck + BungeeCurve.Top + 0.1f, pad.z));
            pc.LookAt(Local(pad.x - 30f, deck + BungeeCurve.Top - 10f, pad.z));
            float end = Time.realtimeSinceStartup + 10f;
            while (!ride.Riding) { Assert.Less(Time.realtimeSinceStartup, end, "the jump began"); yield return null; }
            float began = Time.time;
            var side = new GameObject("BungeeSideCamera").AddComponent<Camera>();
            side.enabled = false;
            side.CopyFrom(pc.Camera);
            side.fieldOfView = 50f;
            var grab = new GameObject("BungeeFrameGrabber").AddComponent<FrameGrabber>();
            grab.Folder = folder; grab.Pov = pc.Camera; grab.Side = side; grab.Deck = deck; grab.Began = began; grab.Ride = ride;
            // Hanging: wait a moment on the prompt, then F.
            while (ride.Riding && Time.time - began < 60f)
            {
                _keys.Press = ride.Phase == BungeePhase.Hang && ride.JumpSeconds > BungeeCurve.ReadyAt + 1.5f;
                yield return null;
            }
            _keys.Press = false;
            int frame = grab.Frame;
            var log = grab.Log;
            Object.Destroy(grab.gameObject);
            File.WriteAllText(Path.Combine(folder, "telemetry.csv"), log.ToString());
            Object.Destroy(side.gameObject);
            Debug.Log($"[BungeeJumpPlayTests] {frame} frames in {folder}");
        }

        // ADR-498: hanging, the prompt offers F; F lowers the jumper onto the pad at once (well before the automatic lowering), the cord
        // is drawn while they are on it and gone once they stand on the pad, the server's guard never steps in, and nothing hurts.
        [UnityTest, Timeout(300000)]
        public IEnumerator Hang_PressF_LowersOntoThePad_CordOnThenOff_NoGuardCorrection_Unhurt()
        {
            var me = Combatant.LocalPlayer;
            float hp = me.Hp.Value, deck = DeckY;
            var pad = WaitingIsland.BungeePad;
            var pc = PlayerController.Local;
            var ride = pc.GetComponent<BungeeRide>();
            pc.Teleport(Local(pad.x, deck + BungeeCurve.Top + 0.1f, pad.z));
            yield return null;
            if (!me.TryGetComponent<MoveGuard>(out _)) me.gameObject.AddComponent<MoveGuard>();
            int corrections = MoveGuard.Corrections;
            float end = Time.realtimeSinceStartup + 10f;
            while (!ride.Jumping) { Assert.Less(Time.realtimeSinceStartup, end, "the jump began"); yield return null; }
            float began = Time.time;
            bool cordInFall = false, headDown = false, sawPrompt = false;
            float pressedAt = -1f;
            end = Time.realtimeSinceStartup + BungeeCurve.Duration + 10f;
            while (ride.Jumping)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "the jump ended");
                var cord = Object.FindAnyObjectByType<BungeeCordView>();
                if (ride.Phase == BungeePhase.Fall && cord != null && cord.Shown && cord.Jumper == me) cordInFall = true;
                if (ride.Phase == BungeePhase.Bounce && me.GetComponent<CombatantAnimation>().BungeeFlip > 0.99f) headDown = true;
                if (ride.Phase == BungeePhase.Hang && !ride.AskedDown)
                {
                    StringAssert.Contains("F", ride.Prompt); StringAssert.Contains("내려가기", ride.Prompt);
                    sawPrompt = true;
                    if (ride.JumpSeconds > BungeeCurve.ReadyAt + 0.5f && pressedAt < 0f) { _keys.Press = true; pressedAt = Time.time - began; }
                }
                yield return null;
                _keys.Press = false;
            }
            float took = Time.time - began;
            Assert.IsTrue(cordInFall, "the cord is drawn from the board to the jumper while falling");
            Assert.IsTrue(headDown, "head-down through the bounce");
            Assert.IsTrue(sawPrompt, "the hang offers F");
            Assert.Greater(pressedAt, 0f);
            Assert.Less(took, BungeeCurve.AutoLowerAt, "F brought them down before the automatic lowering would have started");
            Assert.AreEqual(pressedAt + BungeeCurve.LowerSeconds, took, 0.5f, "lowered right after F");
            yield return new WaitForSeconds(1.5f);   // the server's copy ends a moment later (its F arrived later)
            var p = me.transform.position;
            Assert.Less(p.y - deck, 0.6f, "standing on the deck");
            Assert.Less(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(WaitingIsland.WorldX(pad.x), WaitingIsland.WorldZ(pad.z))), 1.5f, "on the landing pad");
            var after = Object.FindAnyObjectByType<BungeeCordView>();
            Assert.IsFalse(after != null && after.Shown, "the cord is off once they stand on the pad");
            Assert.IsFalse(pc.ExternalControl, "walking again");
            Assert.AreEqual(BungeeCurve.HangSeconds, BungeeCurve.AutoLowerAt - BungeeCurve.ReadyAt, 1e-3f);
            Assert.AreEqual(hp, me.Hp.Value, 1e-3f, "no damage");
            Assert.AreEqual(corrections, MoveGuard.Corrections, "the guard did not take the jump, the hang or the lowering for a cheat");
        }

        // Grabs after every LateUpdate (the ride's camera and shake are on by then); batchmode has no WaitForEndOfFrame.
        [DefaultExecutionOrder(10000)]
        sealed class FrameGrabber : MonoBehaviour
        {
            public string Folder; public Camera Pov, Side; public float Deck, Began; public BungeeRide Ride;
            public readonly StringBuilder Log = new StringBuilder("t,phase,h,speed,fov,camPitch,camRoll,camY,trauma,cord\n");
            public int Frame;
            float _next, _last = -1f, _lastH;

            void LateUpdate()
            {
                if (Ride == null || !Ride.Riding) return;
                float t = Time.time - Began, h = Combatant.LocalPlayer.transform.position.y - Deck;
                float speed = _last < 0f ? 0f : Mathf.Abs(h - _lastH) / Mathf.Max(Time.time - _last, 1e-3f);
                _lastH = h; _last = Time.time;
                var cam = Pov.transform;
                var cord = Object.FindAnyObjectByType<BungeeCordView>();
                Log.AppendLine($"{t:F2},{Ride.Phase},{h:F2},{speed:F1},{Pov.fieldOfView:F1},{-Mathf.DeltaAngle(0f, cam.eulerAngles.x):F0},{Mathf.DeltaAngle(0f, cam.eulerAngles.z):F0},{cam.position.y - Deck:F2},{Ride.Trauma:F2},{(cord != null && cord.Shown ? 1 : 0)}");
                if (t < _next) return;
                _next += 0.25f;
                Save(Pov, Path.Combine(Folder, $"pov_{Frame:000}_{t:00.00}.png"));
                var body = Combatant.LocalPlayer.transform.position + Vector3.up * 1f;
                Side.transform.position = new Vector3(body.x - 7f, Mathf.Max(Deck + 1.7f, body.y - 1f), body.z - 5f);
                Side.transform.rotation = Quaternion.LookRotation(body + Vector3.up * 1.5f - Side.transform.position);
                Save(Side, Path.Combine(Folder, $"side_{Frame:000}_{t:00.00}.png"));
                Frame++;
            }
        }

        static void Save(Camera cam, string path)
        {
            const int w = 960, h = 540;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var old = cam.targetTexture;
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
            var active = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
