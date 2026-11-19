using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    // Third-person gait survey (player report "the run looks stupid", #223 #302): bare hands and rifle, 8 directions plus sprint.
    // Per scenario: planted-foot slide (m/s the foot on the ground moves over the floor; 0 = the stride matches the ground speed),
    // frames with both feet in the air, cadence, hip bob and chest roll -> Logs/gaitsurvey/<tag>.csv, and a picture strip
    // (top: side view across the travel, bottom: the player's own camera) -> Logs/gaitsurvey/<tag>_<scenario>.png.
    // GAIT_TAG env var names the run (before / after).
    public class GaitSurveyTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = Frame.Crouch = Frame.Prone = Frame.Reload = false;
                Frame.Look = Vector2.zero;
                return f;
            }
        }

        // Toe slide while planted at a run or sprint, m/s. A clip whose stride does not match the ground covered drags the planted foot
        // over the floor ("skating"). The toe roll at push-off reads ~0.8 even on a matched stride, and one scenario varies by ~0.5 run to
        // run. Before ADR-390: mean 2.4, back-pedal and pure sidesteps 2.5-3.5. After: mean ~1.2, every scenario 0.8-2.4.
        public const float MaxMeanSlide = 1.6f, MaxSlide = 3.0f;

        ScriptedInput _input;
        Combatant _me;
        PlayerController _pc;
        CombatantAnimation _anim;
        string _savedLook;
        static string Tag => Environment.GetEnvironmentVariable("GAIT_TAG") ?? "survey";

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
            _savedLook = PlayerPrefs.GetString(PlayerAppearanceStore.Key, "");
            PlayerAppearanceStore.Save(PlayerAppearance.Default);
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            _me = Combatant.LocalPlayer; _pc = PlayerController.Local; _anim = _me.GetComponent<CombatantAnimation>();
            _me.ServerDamageFilter = _ => 0f;
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            if (_savedLook != null) { if (_savedLook == "") PlayerAppearanceStore.Clear(); else PlayerPrefs.SetString(PlayerAppearanceStore.Key, _savedLook); }
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static readonly (string name, Vector2 move, bool sprint)[] Scenarios =
        {
            ("F", new Vector2(0, 1), false), ("FR", new Vector2(1, 1), false), ("R", new Vector2(1, 0), false), ("BR", new Vector2(1, -1), false),
            ("B", new Vector2(0, -1), false), ("BL", new Vector2(-1, -1), false), ("L", new Vector2(-1, 0), false), ("FL", new Vector2(-1, 1), false),
            ("SprintF", new Vector2(0, 1), true), ("SprintFR", new Vector2(1, 1), true),
        };
        // Rifle only, aiming over the shoulder (the aimed sidesteps, ADR-391).
        static readonly (string name, Vector2 move, bool sprint)[] AimScenarios = { ("AimR", new Vector2(1, 0), false), ("AimL", new Vector2(-1, 0), false), ("AimF", new Vector2(0, 1), false) };

        [UnityTest]
        public IEnumerator BareHands_EightWaysAndSprint_PlantedFootDoesNotSkate() { yield return Survey("bare", false); }

        [UnityTest]
        public IEnumerator Rifle_EightWaysAndSprint_PlantedFootDoesNotSkate() { yield return Survey("rifle", true); }

        IEnumerator Survey(string loadout, bool rifle)
        {
            yield return Load();
            if (rifle)
            {
                _me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
                yield return WaitUntil(() => _me.ActiveWeaponId == 3, 2f, "rifle in hand");
                yield return new WaitForSeconds(0.6f);
            }
            Directory.CreateDirectory("Logs/gaitsurvey");
            var csv = new StringBuilder();
            string path = $"Logs/gaitsurvey/{Tag}.csv";
            if (!File.Exists(path)) csv.AppendLine("loadout,scenario,facing,speed,stance,slideMean,slideP90,flightShare,stepsPerSec,hipBob,chestRollRange,legTurn,pace,move");
            var failures = new List<string>();
            float slideSum = 0f; int slideCount = 0;
            var frameLog = new StringBuilder();
            foreach (var (name, move, sprint) in rifle ? Scenarios.Concat(AimScenarios) : Scenarios)
            {
                _input.Frame.Move = Vector2.zero; _input.Frame.Sprint = false;
                _input.Frame.AimHeld = name.StartsWith("Aim");
                // Travel always runs along +x through the open strip south of the spawn wall: face so the input's direction points there.
                float inputDeg = Mathf.Atan2(move.x, move.y) * Mathf.Rad2Deg;
                _input.Frame.Look = new Vector2(Mathf.DeltaAngle(_pc.Yaw, 90f - inputDeg), 0f);
                yield return null;
                _pc.Teleport(new Vector3(-12f, 0.05f, -21.5f));
                yield return new WaitForSeconds(0.5f);
                _input.Frame.Move = move; _input.Frame.Sprint = sprint;
                yield return new WaitForSeconds(0.8f);

                var slides = new List<float>();
                var footTrack = new[] { new List<(float h, float v)>(), new List<(float h, float v)>() };
                var ankleTrack = new[] { new List<(float h, float v)>(), new List<(float h, float v)>() };
                var ankles = new[] { RigBones.Bone(_me, HumanBodyBones.LeftFoot), RigBones.Bone(_me, HumanBodyBones.RightFoot) };
                var lastAnkle = new[] { ankles[0].position, ankles[1].position };
                int frames = 0, flight = 0, steps = 0; float facing = 0f, hipLo = 999f, hipHi = -999f, rollLo = 999f, rollHi = -999f, speed = 0f, legTurn = 0f, pace = 0f, mv = 0f;
                // The toes: a running foot rolls onto them, so the ankle moves on while the toes stay planted.
                var feet = new[] { RigBones.Bone(_me, HumanBodyBones.LeftToes) ?? RigBones.Bone(_me, HumanBodyBones.LeftFoot), RigBones.Bone(_me, HumanBodyBones.RightToes) ?? RigBones.Bone(_me, HumanBodyBones.RightFoot) };
                var last = new[] { feet[0].position, feet[1].position };
                var wasDown = new bool[2];
                var shots = new List<(Texture2D side, Texture2D game)>();
                float start = Time.time, nextShot = start;
                var anim = _me.GetComponentInChildren<Animator>();
                while (Time.time < start + 1.4f)
                {
                    yield return null;
                    float dt = Time.deltaTime;
                    if (dt <= 0f) continue;
                    float ground = _pc.transform.position.y, rest = _anim.FootRest;
                    bool[] down = new bool[2];
                    for (int s = 0; s < 2; s++)
                    {
                        float h = feet[s].position.y - ground;
                        down[s] = h < rest + 0.035f;
                        var d = feet[s].position - last[s]; d.y = 0f;
                        if (down[s] && wasDown[s]) slides.Add(d.magnitude / dt);
                        footTrack[s].Add((h, d.magnitude / dt));
                        var da = ankles[s].position - lastAnkle[s]; da.y = 0f;
                        ankleTrack[s].Add((ankles[s].position.y - ground, da.magnitude / dt));
                        lastAnkle[s] = ankles[s].position;
                        frameLog.AppendLine($"{loadout},{name},{Time.time - start:F3},{s},{h:F3},{d.magnitude / dt:F2},{d.x / dt:F2}");
                        if (down[s] && !wasDown[s]) steps++;
                        last[s] = feet[s].position; wasDown[s] = down[s];
                    }
                    if (!down[0] && !down[1] && Mathf.Min(feet[0].position.y, feet[1].position.y) - ground > rest + 0.06f) flight++;
                    frames++;
                    float hip = RigBones.Bone(_me, HumanBodyBones.Hips).position.y - ground;
                    hipLo = Mathf.Min(hipLo, hip); hipHi = Mathf.Max(hipHi, hip);
                    var across = _pc.transform.InverseTransformDirection(RigBones.Bone(_me, HumanBodyBones.RightUpperArm).position - RigBones.Bone(_me, HumanBodyBones.LeftUpperArm).position);
                    float roll = Mathf.Atan2(across.y, new Vector2(across.x, across.z).magnitude) * Mathf.Rad2Deg;
                    facing += Vector3.Dot(Vector3.Cross(new Vector3(across.x, 0f, across.z).normalized, Vector3.up), Vector3.forward);
                    rollLo = Mathf.Min(rollLo, roll); rollHi = Mathf.Max(rollHi, roll);
                    var v = _me.GetComponent<CombatantMotion>().LocalVelocity;
                    speed += new Vector2(v.x, v.z).magnitude; legTurn += _anim.LegTurnDeg; pace += anim.GetFloat(CombatantAnimation.Pace); mv += anim.GetFloat(CombatantAnimation.Move);
                    if (shots.Count < 4 && Time.time >= nextShot) { shots.Add((Render(SideCamera()), Render(_pc.Camera))); nextShot += 0.11f; }
                }
                slides.Sort();
                // Stance speed: how fast each foot moves over the floor while it is down (within 2 cm of the lowest it got in this run, so
                // clips that plant the sole at different heights compare fairly); a stride that matches the ground speed holds it still (0).
                // A foot pivoting on its toes or its heel has one end still: while either end is down, the slower end's speed counts.
                float stance = 0f;
                for (int s = 0; s < 2; s++)
                {
                    var toe = footTrack[s]; var ank = ankleTrack[s];
                    float lowT = float.MaxValue, lowA = float.MaxValue;
                    foreach (var f in toe) lowT = Mathf.Min(lowT, f.h);
                    foreach (var f in ank) lowA = Mathf.Min(lowA, f.h);
                    float sum = 0f; int n = 0;
                    for (int i = 1; i < toe.Count; i++)
                    {
                        bool toeDown = toe[i].h < lowT + 0.02f && toe[i - 1].h < lowT + 0.02f, heelDown = ank[i].h < lowA + 0.02f && ank[i - 1].h < lowA + 0.02f;
                        if (toeDown || heelDown) { sum += Mathf.Min(toeDown ? toe[i].v : float.MaxValue, heelDown ? ank[i].v : float.MaxValue); n++; }
                    }
                    stance += (n > 0 ? sum / n : 0f) / 2f;
                }
                float slideMean = 0f; foreach (var s in slides) slideMean += s; slideMean = slides.Count > 0 ? slideMean / slides.Count : 0f;
                float p90 = slides.Count > 0 ? slides[(int)(slides.Count * 0.9f)] : 0f;
                float elapsed = Time.time - start;
                csv.AppendLine(string.Join(",", loadout, name, (facing / frames).ToString("F2"), (speed / frames).ToString("F2"), stance.ToString("F2"), slideMean.ToString("F2"), p90.ToString("F2"), ((float)flight / frames).ToString("F2"),
                    (steps / elapsed).ToString("F2"), (hipHi - hipLo).ToString("F3"), (rollHi - rollLo).ToString("F1"), (legTurn / frames).ToString("F0"), (pace / frames).ToString("F2"), (mv / frames).ToString("F2")));
                Debug.Log($"[GaitSurvey] {loadout} {name}: speed {speed / frames:F2} stance {stance:F2} slide mean {slideMean:F2} p90 {p90:F2} flight {(float)flight / frames:F2} steps/s {steps / elapsed:F2} hip bob {hipHi - hipLo:F3} chest roll {rollHi - rollLo:F1} legTurn {legTurn / frames:F0} pace {pace / frames:F2}");
                Strip(shots, $"Logs/gaitsurvey/{Tag}_{loadout}_{name}.png");
                // #223: whichever way it goes, the chest faces the aim (shoulder line; 1 = straight ahead).
                if (facing / frames < 0.8f) failures.Add($"{loadout} {name} shoulders turned off the aim (facing {facing / frames:F2})");
                if (speed / frames > 2.5f) { slideSum += stance; slideCount++; if (stance > MaxSlide) failures.Add($"{loadout} {name} stance foot slides {stance:F2} m/s"); }
            }
            File.AppendAllText(path, csv.ToString());
            File.AppendAllText($"Logs/gaitsurvey/{Tag}_frames.csv", frameLog.ToString());
            _input.Frame.Move = Vector2.zero; _input.Frame.AimHeld = false;
            Debug.Log($"[GaitSurvey] {loadout}: mean stance slide {slideSum / Mathf.Max(1, slideCount):F2} m/s over {slideCount} running scenarios");
            Assert.IsEmpty(failures, "planted foot skates more than " + MaxSlide + " m/s");
            Assert.LessOrEqual(slideSum / Mathf.Max(1, slideCount), MaxMeanSlide, "mean planted-foot slide over the 8 ways and sprint (m/s)");
        }

        // Across the travel (+x), from the south, following the player.
        Camera SideCamera()
        {
            var go = new GameObject("GaitSideCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 30f; cam.clearFlags = CameraClearFlags.Skybox;
            var p = _pc.transform.position;
            go.transform.position = p + new Vector3(0.6f, 1.0f, -5.5f);
            go.transform.LookAt(p + new Vector3(0.6f, 0.85f, 0f));
            return cam;
        }

        static Texture2D Render(Camera cam)
        {
            const int w = 480, h = 360;
            var rt = new RenderTexture(w, h, 24);
            var old = cam.targetTexture;
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
            UnityEngine.Object.Destroy(rt);
            if (cam.name == "GaitSideCam") UnityEngine.Object.Destroy(cam.gameObject);
            return tex;
        }

        static void Strip(List<(Texture2D side, Texture2D game)> shots, string file)
        {
            if (shots.Count == 0) return;
            int w = shots[0].side.width, h = shots[0].side.height;
            var sheet = new Texture2D(w * shots.Count, h * 2, TextureFormat.RGB24, false);
            for (int i = 0; i < shots.Count; i++)
            {
                sheet.SetPixels(i * w, h, w, h, shots[i].side.GetPixels());
                sheet.SetPixels(i * w, 0, w, h, shots[i].game.GetPixels());
                UnityEngine.Object.Destroy(shots[i].side); UnityEngine.Object.Destroy(shots[i].game);
            }
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            UnityEngine.Object.Destroy(sheet);
        }
    }
}
