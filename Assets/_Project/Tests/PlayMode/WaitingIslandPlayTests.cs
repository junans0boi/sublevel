using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #415 #416 #419 (ADR-382/383/386): the waiting phase in the real World scene, as a host with its own client: the person starts on the
    // island, cannot leave it, takes no damage, can bungee, and is sent to the elevator when the countdown ends.
    public class WaitingIslandPlayTests
    {
        sealed class NoPlayerInput : IPlayerInput
        {
            public bool PressF;   // one F press on the next frame
            public Vector2 Walk;  // held movement (x right, y forward)
            public PlayerInputFrame Read() { var f = new PlayerInputFrame { SelectSlot = -1, Interact = PressF, Move = Walk }; PressF = false; return f; }
        }

        bool _oldFlag;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _oldFlag = MatchRules.Current.UseWaitingIsland;
            MatchRules.Current.UseWaitingIsland = true;
            WaitingRoom.ForceEnabled = true;
            // A short countdown for the deployment test; the lift and the jump need the room open longer.
            DedicatedServer.StartSecondsOverride = TestContext.CurrentContext.Test.Name.Contains("Deploys") ? 12f : 120f;
            PlayerController.InputOverride = new NoPlayerInput();
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

        static bool OnIsland(Vector3 p) => WaitingIsland.Inside(p.x, p.z);

        [UnityTest, Timeout(300000)]
        public IEnumerator StartsOnTheIsland_NotBlocked_AndDeploysWhenTheCountdownEnds()
        {
            var me = Combatant.LocalPlayer;
            Assert.IsTrue(OnIsland(me.transform.position), "spawned on the island: " + me.transform.position);
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(SpawnProtection.InputBlocked, "the shut elevator doors do not hold a person on the island");
            float end = Time.realtimeSinceStartup + 60f;
            while (WaitingRoom.Current != null) { Assert.Less(Time.realtimeSinceStartup, end, "the countdown ended"); yield return null; }
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(OnIsland(me.transform.position), "sent to the elevator: " + me.transform.position);
            Assert.IsTrue(SpawnProtection.InputBlocked, "in the elevator with the doors shut");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CannotLeaveTheIsland_ServerPutsThemBack()
        {
            var me = Combatant.LocalPlayer;
            // The dedicated server guards every person (all remote); the host's own body is unguarded, so the test adds the guard.
            if (!me.TryGetComponent<MoveGuard>(out _)) me.gameObject.AddComponent<MoveGuard>();
            yield return new WaitForSeconds(MoveGuard.Interval * 2f);
            var p = me.transform.position;
            PlayerController.Local.Teleport(new Vector3(WaitingIsland.CentreX + 70f, p.y, WaitingIsland.CentreZ));
            yield return new WaitForSeconds(1.2f);
            var q = me.transform.position;
            // ADR-508: back to the nearest walkable place, the island's walk circle or the race pit (east of the island that is the pit).
            Assert.IsTrue(RaceTrack.Walkable(q.x - WaitingIsland.CentreX, q.z - WaitingIsland.CentreZ), $"pulled back onto the island or into the pit, now {q.x - WaitingIsland.CentreX:0.0}, {q.z - WaitingIsland.CentreZ:0.0}");
            PlayerController.Local.Teleport(new Vector3(WaitingIsland.CentreX - 70f, p.y, WaitingIsland.CentreZ));
            yield return new WaitForSeconds(1.2f);
            q = me.transform.position;
            float d = Vector2.Distance(new Vector2(q.x, q.z), new Vector2(WaitingIsland.CentreX, WaitingIsland.CentreZ));
            Assert.LessOrEqual(d, WaitingIsland.WalkRadius + 1f, "west of the island: pulled back inside the walk circle, now " + d + " m from the centre");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator NothingHurts_OnTheIsland()
        {
            var me = Combatant.LocalPlayer;
            float before = me.Hp.Value;
            me.ServerTakeDamage(40f);
            yield return null;
            Assert.AreEqual(before, me.Hp.Value, 1e-3f, "a hit does nothing while waiting");
        }

        static float DeckY => GameObject.Find("WaitingIsland").transform.position.y;
        static Vector3 Local(float lx, float y, float lz) => new Vector3(WaitingIsland.WorldX(lx), y, WaitingIsland.WorldZ(lz));

        // The r5 report: the cage's floor sat inside the ground sill (and the landing pad floated over the deck), so it looked sunk below ground.
        [UnityTest, Timeout(300000)]
        public IEnumerator LiftCarFloor_RestsOnTheDeck_AboveTheSill_UnderTheFeet()
        {
            float deck = DeckY;
            var car = GameObject.Find("WaitingIsland").transform.Find("BungeeLiftCar");
            Assert.IsNotNull(car, "the World scene has the lift car");
            float end = Time.realtimeSinceStartup + BungeeLift.Cycle + 2f;
            while (WaitingRoom.Current.Lift == null || WaitingRoom.Current.Lift.Phase != LiftPhase.OpenBottom)
            { Assert.Less(Time.realtimeSinceStartup, end, "the car came to the bottom"); yield return null; }
            var lift = WaitingIsland.Lift;
            PlayerController.Local.Teleport(Local(lift.x, deck + 0.1f, lift.z));
            yield return new WaitForSeconds(0.5f);
            var floor = car.Find("Floor").GetComponent<MeshRenderer>().bounds;
            float top = floor.max.y - deck, feet = Combatant.LocalPlayer.transform.position.y - deck;
            Debug.Log($"[WaitingIslandPlayTests] car floor {floor.min.y - deck:0.000}..{top:0.000} m over the deck, sill top {WaitingIsland.LiftSillTop:0.000}, feet {feet:0.000}");
            Assert.Greater(top, WaitingIsland.LiftSillTop + 0.03f, "the car floor shows above the sill");
            Assert.Less(top, 0.15f, "the car floor is a low step, not a box");
            Assert.GreaterOrEqual(floor.min.y - deck, -0.05f, "nothing of the car floor is below the deck");
            Assert.Less(Mathf.Abs(feet - top), 0.15f, "the rider stands on the car floor, not sunk in it");
        }

        // ADR-496 (준환 r5.2: "승강기 통과된다"): the cage's sides and the fence of the shaft stop a walker.
        [UnityTest, Timeout(300000)]
        public IEnumerator Lift_CageSides_AndShaftFence_StopAWalker()
        {
            float deck = DeckY; var lift = WaitingIsland.Lift;
            float end = Time.realtimeSinceStartup + BungeeLift.Cycle + 2f;
            while (WaitingRoom.Current.Lift == null || WaitingRoom.Current.Lift.Phase != LiftPhase.OpenBottom)
            { Assert.Less(Time.realtimeSinceStartup, end, "the car came to the bottom"); yield return null; }
            var input = (NoPlayerInput)PlayerController.InputOverride;
            var me = Combatant.LocalPlayer;
            foreach (var (startX, limit, what) in new[] { (0f, BungeeLift.HalfSize, "the cage's east side"), (-WaitingIsland.LiftShaftHalf - 1.2f, -WaitingIsland.LiftShaftHalf, "the shaft's west fence") })
            {
                PlayerController.Local.Teleport(Local(lift.x + startX, deck + 0.1f, lift.z + 0.4f));
                yield return new WaitForSeconds(0.3f);
                PlayerController.Local.LookAt(Local(lift.x + 30f, deck + 1.5f, lift.z + 0.4f));
                yield return null;
                input.Walk = Vector2.up;
                yield return new WaitForSeconds(1.5f);
                input.Walk = Vector2.zero;
                float x = me.transform.position.x - WaitingIsland.WorldX(lift.x);
                Debug.Log($"[WaitingIslandPlayTests] walked east from {startX:0.0} to {x:0.00} (limit {limit:0.00})");
                Assert.Greater(x, startX + 0.2f, "the walker moved");
                Assert.Less(x, limit, what + " holds");
            }
        }

        // Review shots (third person, the feet, the car floor and the deck together). Run on purpose with
        //   ISLAND_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.WaitingIslandPlayTests.CaptureLiftAndPad
        [UnityTest, Explicit, Category("Capture"), Timeout(300000)]
        public IEnumerator CaptureLiftAndPad()
        {
            string folder = System.Environment.GetEnvironmentVariable("ISLAND_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = System.IO.Path.Combine(Application.dataPath, "../Logs/island-shots");
            System.IO.Directory.CreateDirectory(folder);
            float deck = DeckY;
            float end = Time.realtimeSinceStartup + BungeeLift.Cycle + 2f;
            while (WaitingRoom.Current.Lift == null || WaitingRoom.Current.Lift.Phase != LiftPhase.OpenBottom)
            { Assert.Less(Time.realtimeSinceStartup, end, "the car came to the bottom"); yield return null; }
            var cam = new GameObject("IslandShotCamera").AddComponent<Camera>();
            cam.enabled = false;
            if (PlayerController.Local.Camera != null) cam.CopyFrom(PlayerController.Local.Camera);
            cam.fieldOfView = 55f;
            var lift = WaitingIsland.Lift; var pad = WaitingIsland.BungeePad;
            var shots = new (string name, Vector3 at, Vector3 eye)[]
            {
                ("lift_car_bottom", Local(lift.x, deck + 0.1f, lift.z), Local(lift.x - 3.2f, deck + 1.9f, lift.z + 4.2f)),
                ("lift_car_low", Local(lift.x, deck + 0.1f, lift.z), Local(lift.x - 2.6f, deck + 0.6f, lift.z + 3.0f)),
                ("bungee_pad", Local(pad.x, deck + 0.1f, pad.z), Local(pad.x - 3.5f, deck + 1.8f, pad.z + 3.5f)),
            };
            foreach (var s in shots)
            {
                PlayerController.Local.Teleport(s.at);
                yield return new WaitForSeconds(0.8f);
                var look = Combatant.LocalPlayer.transform.position + Vector3.up * 0.5f;
                cam.transform.SetPositionAndRotation(s.eye, Quaternion.LookRotation(look - s.eye));
                const int w = 1280, h = 720;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                var active = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, s.name + ".png"), tex.EncodeToPNG());
            }
            Object.Destroy(cam.gameObject);
            Debug.Log("[WaitingIslandPlayTests] shots in " + folder);
        }

        static Vector3 AtCallButton(bool top, float deck, float back)
        {
            var b = top ? WaitingIsland.CallTop : WaitingIsland.CallBottom;
            return Local(b.x, deck + (top ? BungeeCurve.Top : 0f) + 0.1f, b.z + (top ? -back : back));   // the bottom button faces north, the top one south
        }

        static IEnumerator SendTheCarUp()
        {
            float end = Time.realtimeSinceStartup + 10f;
            while (WaitingRoom.Current.Lift == null) { Assert.Less(Time.realtimeSinceStartup, end, "the lift runs"); yield return null; }
            WaitingRoom.Current.Lift.Call(true, Time.unscaledTimeAsDouble);
            end = Time.realtimeSinceStartup + BungeeLift.Cycle + 4f;
            while (WaitingRoom.Current.Lift.Phase != LiftPhase.OpenTop) { Assert.Less(Time.realtimeSinceStartup, end, "the car went up"); yield return null; }
        }

        // ADR-490 (준환 r5.2: "패드 밟고 엘리베이터까지 걸어가는 게 귀찮다"): F at the call button beside the gate brings the car, which opens.
        [UnityTest, Timeout(300000)]
        public IEnumerator CallButton_F_BringsTheCarDown_AndItOpens()
        {
            float deck = DeckY;
            yield return SendTheCarUp();
            PlayerController.Local.Teleport(AtCallButton(false, deck, 0.6f));
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(PlayerController.Local.LiftCallInReach, "the button is in reach: the prompt shows");
            Assert.AreEqual("[F]  엘리베이터 호출", Hud.Instance != null ? Hud.Instance.PromptText : "[F]  엘리베이터 호출");
            ((NoPlayerInput)PlayerController.InputOverride).PressF = true;
            yield return null; yield return null;
            Assert.IsTrue(WaitingRoom.Current.Lift.CalledBottom || WaitingRoom.Current.Lift.Moving, "the call was taken");
            float end = Time.realtimeSinceStartup + LiftCall.CallDepartSeconds + BungeeLift.TravelSeconds + 3f;
            while (WaitingRoom.Current.Lift.Phase != LiftPhase.OpenBottom) { Assert.Less(Time.realtimeSinceStartup, end, "the car came down"); yield return null; }
            yield return new WaitForSeconds(0.6f);   // the broadcast reaches the client view
            var car = GameObject.Find("WaitingIsland").transform.Find("BungeeLiftCar");
            Assert.AreEqual(0f, car.localPosition.y, 0.05f, "the car stands at the deck");
            Assert.IsFalse(car.Find("Doors").gameObject.activeSelf, "and its gates are open");
            Assert.IsFalse(WaitingRoom.Current.Lift.CalledBottom, "the light went out on arrival");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CallButton_PressFromFarAway_IsRefused_ThroughTheNetworkPath()
        {
            var me = Combatant.LocalPlayer;
            float deck = DeckY;
            yield return SendTheCarUp();
            Assert.IsFalse(WaitingRoom.Current.ServerCall(me, false), "the island's middle is far from the button");
            PlayerController.Local.Teleport(AtCallButton(false, deck, 8f));
            yield return new WaitForSeconds(0.3f);
            me.NetworkManager.ClientManager.Broadcast(new LiftCallRequest { Top = false });   // a client claiming a press from 8 m
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(LiftPhase.OpenTop, WaitingRoom.Current.Lift.Phase);
            Assert.IsFalse(WaitingRoom.Current.Lift.CalledBottom, "refused: out of reach");
            PlayerController.Local.Teleport(AtCallButton(false, deck, 1f));
            yield return new WaitForSeconds(0.3f);
            me.NetworkManager.ClientManager.Broadcast(new LiftCallRequest { Top = false });
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(WaitingRoom.Current.Lift.CalledBottom || WaitingRoom.Current.Lift.Moving, "the same request at the button is taken");
        }

        // Review shot of the button with its prompt (UI included). Run on purpose with
        //   ISLAND_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.WaitingIslandPlayTests.CaptureCallButton
        [UnityTest, Explicit, Category("Capture"), Timeout(300000)]
        public IEnumerator CaptureCallButton()
        {
            string folder = System.Environment.GetEnvironmentVariable("ISLAND_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = System.IO.Path.Combine(Application.dataPath, "../Logs/island-shots");
            System.IO.Directory.CreateDirectory(folder);
            float deck = DeckY;
            yield return SendTheCarUp();
            var input = (NoPlayerInput)PlayerController.InputOverride;
            for (int i = 0; i < 2; i++)
            {
                bool top = i == 1;
                PlayerController.Local.Teleport(AtCallButton(top, deck, 1.6f) + (top ? Vector3.right * 0.7f : Vector3.zero));   // the top plate hides behind the body otherwise
                yield return new WaitForSeconds(0.5f);
                var b = top ? WaitingIsland.CallTop : WaitingIsland.CallBottom;
                PlayerController.Local.LookAt(Local(b.x, deck + b.y - 0.3f, b.z));
                yield return new WaitForSeconds(0.3f);
                Debug.Log($"[WaitingIslandPlayTests] {(top ? "top" : "bottom")} button: in reach {PlayerController.Local.LiftCallInReach}, prompt '{(Hud.Instance != null ? Hud.Instance.PromptText : "")}'");
                yield return Shot(folder, top ? "call_button_top" : "call_button_prompt");
                yield return CloseShot(folder, top ? "call_button_top_close" : "call_button_close", top, deck);
                if (!top)
                {
                    input.PressF = true;
                    yield return new WaitForSeconds(0.4f);
                    yield return Shot(folder, "call_button_lit");
                    yield return CloseShot(folder, "call_button_lit_close", false, deck);
                    float end = Time.realtimeSinceStartup + 15f;
                    while (WaitingRoom.Current.Lift.Phase != LiftPhase.OpenBottom) { Assert.Less(Time.realtimeSinceStartup, end); yield return null; }
                    yield return new WaitForSeconds(0.6f);
                    yield return Shot(folder, "call_button_arrived");
                    yield return SendTheCarUp();
                }
            }
            Debug.Log("[WaitingIslandPlayTests] shots in " + folder);
        }

        // The player's camera with the HUD canvases drawn into it (an overlay canvas is not in a camera render).
        // Review shots of the hoist (ADR-496): the car halfway down with its ropes and counterweight, the drive on the shaft head, and the
        // fence round the shaft on the deck. Run on purpose with
        //   ISLAND_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.WaitingIslandPlayTests.CaptureHoist
        [UnityTest, Explicit, Category("Capture"), Timeout(300000)]
        public IEnumerator CaptureHoist()
        {
            string folder = System.Environment.GetEnvironmentVariable("ISLAND_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = System.IO.Path.Combine(Application.dataPath, "../Logs/island-shots");
            System.IO.Directory.CreateDirectory(folder);
            float deck = DeckY, top = BungeeCurve.Top; var lp = WaitingIsland.Lift;
            yield return SendTheCarUp();
            yield return new WaitForSeconds(0.6f);
            yield return FreeShot(folder, "hoist_fence", Local(lp.x + 6f, deck + 1.8f, lp.z + 5f), Local(lp.x, deck + 1.6f, lp.z), 60f);
            yield return FreeShot(folder, "hoist_head", Local(lp.x - 3f, deck + top + 1.7f, lp.z - 4.5f), Local(lp.x + 0.8f, deck + top + 3.1f, lp.z), 55f);
            WaitingRoom.Current.Lift.Call(false, Time.unscaledTimeAsDouble);
            float end = Time.realtimeSinceStartup + 10f;
            while (!WaitingRoom.Current.Lift.Moving || WaitingRoom.Current.Lift.Into(Time.unscaledTimeAsDouble) < BungeeLift.TravelSeconds * 0.45f)
            { Assert.Less(Time.realtimeSinceStartup, end); yield return null; }
            yield return FreeShot(folder, "hoist_ride", Local(lp.x + 7f, deck + top * 0.5f - 4f, lp.z + 6f), Local(lp.x, deck + top * 0.5f, lp.z), 60f);
            Debug.Log("[WaitingIslandPlayTests] shots in " + folder);
        }

        static IEnumerator FreeShot(string folder, string name, Vector3 from, Vector3 at, float fov)
        {
            var cam = new GameObject("FreeShotCamera").AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = fov; cam.transform.position = from; cam.transform.LookAt(at);
            yield return Shot(folder, name, cam);
            Object.Destroy(cam.gameObject);
        }

        // The panel up close, from a little right of its face (no HUD).
        static IEnumerator CloseShot(string folder, string name, bool top, float deck)
        {
            var b = top ? WaitingIsland.CallTop : WaitingIsland.CallBottom;
            var face = Local(b.x, deck + b.y, b.z);
            var cam = new GameObject("CloseShotCamera").AddComponent<Camera>();
            cam.enabled = false; cam.nearClipPlane = 0.02f; cam.fieldOfView = 45f;
            cam.transform.position = face + new Vector3(0.3f, 0.12f, top ? -0.95f : 0.95f);
            cam.transform.LookAt(face + Vector3.down * 0.05f);
            yield return Shot(folder, name, cam);
            Object.Destroy(cam.gameObject);
        }

        static IEnumerator Shot(string folder, string name, Camera only = null)
        {
            yield return null;
            var cam = only != null ? only : PlayerController.Local.Camera != null ? PlayerController.Local.Camera : Camera.main;
            var canvases = only != null ? new Canvas[0] : Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var modes = new RenderMode[canvases.Length];
            for (int i = 0; i < canvases.Length; i++)
            {
                modes[i] = canvases[i].renderMode;
                if (canvases[i].isRootCanvas && modes[i] == RenderMode.ScreenSpaceOverlay)
                { canvases[i].renderMode = RenderMode.ScreenSpaceCamera; canvases[i].worldCamera = cam; canvases[i].planeDistance = cam.nearClipPlane + 0.02f; }
            }
            const int w = 1280, h = 720;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var before = cam.targetTexture;
            cam.targetTexture = rt; Canvas.ForceUpdateCanvases(); cam.Render(); cam.targetTexture = before;
            for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null && canvases[i].isRootCanvas) canvases[i].renderMode = modes[i];
            var active = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Lift_GoesUpFast_AndBackDown_WithoutAGuardCorrection()
        {
            var me = Combatant.LocalPlayer;
            float hp = me.Hp.Value, deck = DeckY;
            var lift = WaitingIsland.Lift;
            PlayerController.Local.Teleport(Local(lift.x, deck + 0.1f, lift.z));
            yield return null;
            // The guard as on the dedicated server, added after the test's own teleport (a host does not announce it).
            if (!me.TryGetComponent<MoveGuard>(out _)) me.gameObject.AddComponent<MoveGuard>();
            int corrections = MoveGuard.Corrections;
            float end = Time.realtimeSinceStartup + BungeeLift.Cycle + 6f, left = -1f, arrived = -1f;
            while (arrived < 0f)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "the lift took them up within one cycle, now at +" + (me.transform.position.y - deck) + " m");
                float h = me.transform.position.y - deck;
                if (left < 0f && h > 0.8f) left = Time.realtimeSinceStartup;
                if (h > BungeeCurve.Top - 0.5f) arrived = Time.realtimeSinceStartup;
                yield return null;
            }
            Assert.Less(arrived - left, BungeeLift.TravelSeconds + 0.6f, "an express: 60 m in about five seconds");
            yield return new WaitForSeconds(1f);
            Assert.Greater(me.transform.position.y - deck, BungeeCurve.Top - 0.5f, "stands on the top landing after the ride");
            // Staying in the car: the next trip takes them down again.
            end = Time.realtimeSinceStartup + BungeeLift.Cycle + 6f;
            while (me.transform.position.y - deck > 1.5f) { Assert.Less(Time.realtimeSinceStartup, end, "the lift took them down"); yield return null; }
            Assert.AreEqual(corrections, MoveGuard.Corrections, "the guard did not take the 15 m/s ride for a cheat");
            Assert.AreEqual(hp, me.Hp.Value, 1e-3f);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Jump_FromTheBoard_FallsBouncesAndLandsOnThePad_Unhurt()
        {
            var me = Combatant.LocalPlayer;
            float hp = me.Hp.Value, deck = DeckY;
            var pad = WaitingIsland.BungeePad;
            PlayerController.Local.Teleport(Local(pad.x, deck + BungeeCurve.Top + 0.1f, pad.z));
            yield return null;
            if (!me.TryGetComponent<MoveGuard>(out _)) me.gameObject.AddComponent<MoveGuard>();
            int corrections = MoveGuard.Corrections;
            float low = float.MaxValue, end = Time.realtimeSinceStartup + 2f + BungeeCurve.Duration + 3f, fellAt = -1f;
            bool fell = false;
            while (Time.realtimeSinceStartup < end)
            {
                float h = me.transform.position.y - deck;
                if (h < BungeeCurve.Top - 10f && !fell) { fell = true; fellAt = Time.realtimeSinceStartup; }
                if (fell && Time.realtimeSinceStartup - fellAt < BungeeCurve.BouncePeriod) low = Mathf.Min(low, h);   // the first dip, before the lowering
                yield return null;
            }
            Assert.IsTrue(fell, "jumped after standing on the board");
            Assert.That(low, Is.InRange(1.5f, 6f), "the lowest point of the bounce is a few metres over the pad");
            var p = me.transform.position;
            Assert.Less(p.y - deck, 1.5f, "back on the deck");
            Assert.Less(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(WaitingIsland.WorldX(pad.x), WaitingIsland.WorldZ(pad.z))), 2f, "on the landing pad");
            Assert.AreEqual(hp, me.Hp.Value, 1e-3f, "no damage from the jump");
            Assert.AreEqual(corrections, MoveGuard.Corrections, "the guard did not take the fall for a cheat");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FallingFromTheTower_IsPutBackOnTheDeck_Unhurt()
        {
            var me = Combatant.LocalPlayer;
            float hp = me.Hp.Value, deck = DeckY;
            var tw = WaitingIsland.Tower;
            PlayerController.Local.Teleport(Local(tw.x - 8f, deck + 30f, tw.z - 8f));   // in the air beside the tower, not on its top floor
            yield return new WaitForSeconds(0.6f);
            Assert.Less(me.transform.position.y - deck, 2.5f, "put back on the deck at once, now +" + (me.transform.position.y - deck) + " m");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(hp, me.Hp.Value, 1e-3f);
        }
    }
}
