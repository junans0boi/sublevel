using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Gameplay
{
    // Review builds only (hidden, removed before release with -feelcapture/-fastzones, #77): `-autoplay <folder>`
    // plays the quick match of the match simulation test in the World scene with nobody at the keyboard. The local
    // player is out from the start and the camera follows the current leader (most kills) as a spectator; six
    // screenshots are saved into <folder> (deployment, first fight, region warning, lockdown fog, final region,
    // winner) together with the match log, then the build quits (exit code 0 when the match ended, 1 otherwise).
    // The lockdown runs PollutionRules.QuickMatch (PollutionDirector reads the same argument).
    [DefaultExecutionOrder(300)]
    public class MatchAutoplay : MonoBehaviour, IPlayerInput
    {
        const string MatchScene = "World";
        const float TimeScale = 2f;
        const float StartTimeoutSeconds = 60f;     // real seconds
        const float MatchTimeoutSeconds = 15f * 60f;
        const float LeaderEverySeconds = 3f;
        const float CameraBack = 4.5f;
        const float CameraUp = 2.2f;
        const float LookHeight = 1.3f;
        const float CameraRadius = 0.25f;

        enum View { Behind, Front, Overview }

        string _folder;
        Combatant _follow;
        View _view;
        Vector3 _overviewAt;
        float _nextLeader;
        int _noiseSeen;
        int _warned = -1, _locked = -1;
        GameObject _winnerCanvas;

        public static bool Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Active) return;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autoplay");
            if (i < 0 || i + 1 >= args.Length) return;
            Active = true;
            var go = new GameObject("MatchAutoplay");
            DontDestroyOnLoad(go);
            var autoplay = go.AddComponent<MatchAutoplay>();
            autoplay._folder = Path.GetFullPath(args[i + 1]);
            PlayerController.InputOverride = autoplay;
            if (SceneManager.GetActiveScene().name != MatchScene)
            {
                if (Application.CanStreamedLevelBeLoaded(MatchScene)) SceneManager.LoadScene(MatchScene);
                else Debug.LogError("[Autoplay] scene is not in the build: " + MatchScene);
            }
        }

        // Nobody at the keyboard.
        public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };

        void OnEnable()
        {
            PollutionDirector.ServerRegionStateChanged += OnRegion;
        }

        void OnDisable()
        {
            PollutionDirector.ServerRegionStateChanged -= OnRegion;
            ZoneBanner.SpectatorFeet = null;
            Time.timeScale = 1f;
        }

        void OnRegion(int region, ZoneState state)
        {
            if (state == ZoneState.Warning && _warned < 0) _warned = region;
            if (state == ZoneState.Closed && _locked < 0) _locked = region;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            float giveUp = Time.realtimeSinceStartup + StartTimeoutSeconds;
            while (Combatant.LocalPlayer == null || MatchState.Instance == null || PollutionDirector.Instance == null || !PollutionDirector.Instance.ServerReady)
            {
                if (Time.realtimeSinceStartup > giveUp)
                {
                    Debug.LogError("[Autoplay] the match did not start");
                    Application.Quit(1);
                    yield break;
                }
                yield return null;
            }
            var me = Combatant.LocalPlayer;
            if (!me.IsServerInitialized)
            {
                Debug.LogError("[Autoplay] only works when this process hosts the match");
                Application.Quit(1);
                yield break;
            }
            var recorder = MatchRecorder.Attach();
            var state = MatchState.Instance;
            var zones = PollutionDirector.Instance;
            _noiseSeen = BotNoise.Count;

            // A spectator: the player is the first one out (zone damage passes the spawn protection).
            me.ServerTakeDamage(new DamageInfo { Amount = 10000f, Cause = DamageCause.Zone, Zone = HitZone.Body });
            Time.timeScale = TimeScale;

            // 1. Deployment: a bot in its station as the door goes down.
            Follow(Leader(), View.Front);
            while (ElevatorDoors.Instance != null && ElevatorDoors.Instance.ServerDoorsClosed) yield return null;
            yield return new WaitForSeconds(0.9f);
            yield return Shot("01_deployment");
            Follow(Leader(), View.Behind);

            bool fight = false, warning = false, fog = false, final = false;
            float end = Time.realtimeSinceStartup + MatchTimeoutSeconds;
            while (!state.IsOver && Time.realtimeSinceStartup < end)
            {
                if (!fight && TryGunshot(out var shooter))
                {
                    fight = true;
                    Follow(shooter, View.Behind);
                    yield return new WaitForSeconds(0.4f);
                    yield return Shot("02_first_fight");
                }
                else if (!warning && _warned >= 0)
                {
                    warning = true;
                    FollowIn(zones, _warned);
                    yield return new WaitForSeconds(1.5f);
                    yield return Shot("03_region_warning");
                }
                else if (!fog && _locked >= 0)
                {
                    fog = true;
                    FollowIn(zones, _locked);
                    // The fog eases in over a couple of seconds.
                    yield return new WaitForSeconds(3f);
                    yield return Shot("04_lockdown_fog");
                }
                else if (!final && zones.OnlyFinalRegionLeft)
                {
                    final = true;
                    FollowIn(zones, zones.FinalRegion);
                    yield return new WaitForSeconds(1.5f);
                    yield return Shot("05_final_region");
                }
                else if (Time.time >= _nextLeader || _follow == null || _follow.Dead.Value)
                {
                    _nextLeader = Time.time + LeaderEverySeconds;
                    Follow(Leader(), View.Behind);
                }
                yield return null;
            }

            // 6. The winner.
            Time.timeScale = 1f;
            var winner = FindById(state.WinnerId);
            if (winner != null) Follow(winner, View.Front);
            ShowWinner(state, zones, winner);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("06_winner");

            var log = recorder.Finish();
            recorder.Write(_folder);
            Debug.Log($"[Autoplay] over {log.over}: winner {log.winner} after {log.durationSeconds:0} s, {log.kills.Count} kills, final region {log.finalRegion}; shots in {_folder}");
            Application.Quit(state.IsOver ? 0 : 1);
        }

        // ---- Who to watch ----

        // The living combatant with the most kills (the earliest spawned on a tie).
        static Combatant Leader()
        {
            var state = MatchState.Instance;
            Combatant best = null;
            int bestKills = -1;
            foreach (var c in Combatant.All)
            {
                if (c == null || c.Dead.Value) continue;
                int kills = state != null ? state.KillsOf(c) : 0;
                if (kills > bestKills || (kills == bestKills && best != null && c.ObjectId < best.ObjectId))
                {
                    best = c;
                    bestKills = kills;
                }
            }
            return best;
        }

        static Combatant FindById(int objectId)
        {
            if (objectId < 0) return null;
            foreach (var c in Combatant.All)
                if (c != null && c.ObjectId == objectId) return c;
            return null;
        }

        // Someone fired since the last look.
        bool TryGunshot(out Combatant shooter)
        {
            shooter = null;
            int count = BotNoise.Count;
            for (int i = BotNoise.FirstUnread(_noiseSeen); i < count && shooter == null; i++)
            {
                var noise = BotNoise.At(i);
                if (noise.Kind != NoiseKind.Gunshot) continue;
                var c = FindById(noise.SourceId);
                if (c != null && !c.Dead.Value) shooter = c;
            }
            _noiseSeen = count;
            return shooter != null;
        }

        void Follow(Combatant c, View view)
        {
            _follow = c;
            _view = c != null ? view : View.Overview;
        }

        // Someone alive in that region, else a view over the region from above.
        void FollowIn(PollutionDirector zones, int region)
        {
            Combatant pick = null;
            foreach (var c in Combatant.All)
                if (c != null && !c.Dead.Value && zones.RegionIndexAt(c.transform.position) == region) { pick = c; break; }
            if (pick != null)
            {
                Follow(pick, View.Behind);
                return;
            }
            _follow = null;
            _view = View.Overview;
            _overviewAt = zones.RegionCentre(region);
        }

        // ---- Camera ----

        void LateUpdate()
        {
            // The elimination card would cover the view: the spectator looks past it.
            var result = ResultScreen.Instance;
            if (result != null)
                foreach (Transform child in result.transform)
                    if (child.gameObject.activeSelf) child.gameObject.SetActive(false);

            var cam = Camera.main;
            if (cam == null) return;
            if (_follow != null && _view != View.Overview)
            {
                var body = _follow.transform;
                var look = body.position + Vector3.up * LookHeight;
                var flat = body.forward;
                flat.y = 0f;
                flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
                var wanted = body.position + (_view == View.Front ? flat : -flat) * CameraBack + Vector3.up * CameraUp;
                var to = wanted - look;
                float distance = to.magnitude;
                if (distance > 1e-3f && Physics.SphereCast(look, CameraRadius, to / distance, out var hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(body))
                    wanted = look + to / distance * Mathf.Max(0.5f, hit.distance - 0.1f);
                cam.transform.SetPositionAndRotation(wanted, Quaternion.LookRotation(look - wanted, Vector3.up));
                ZoneBanner.SpectatorFeet = body.position;
                return;
            }
            var centre = _overviewAt;
            var from = centre + new Vector3(0f, 70f, -70f);
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(centre - from, Vector3.up));
            ZoneBanner.SpectatorFeet = centre;
        }

        void ShowWinner(MatchState state, PollutionDirector zones, Combatant winner)
        {
            if (_winnerCanvas != null) return;
            var canvas = UiKit.Canvas("AutoplayWinner", 900, false);
            _winnerCanvas = canvas.gameObject;
            var strip = UiKit.Rect("Strip", canvas.transform, new Vector2(0.5f, 0.75f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 170f));
            UiKit.Box(strip, new Color(0f, 0f, 0f, 0.6f));
            string name = state != null && !string.IsNullOrEmpty(state.WinnerName) ? state.WinnerName : "없음";
            var title = UiKit.Rect("Title", strip, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(1060f, 80f));
            UiKit.Label(title, "승리 — " + name, 56, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            int kills = state != null && winner != null ? state.KillsOf(winner) : 0;
            int total = state != null ? state.TotalCount : 0;
            string region = zones != null && winner != null ? zones.RegionNameOf(zones.RegionIndexAt(winner.transform.position)) : "";
            var line = UiKit.Rect("Line", strip, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(1060f, 50f));
            UiKit.Label(line, $"처치 {kills} · 참가 {total} · {region}", 26, UiKit.Text, TextAnchor.MiddleCenter);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_folder, name + ".png"), texture.EncodeToPNG());
            Destroy(texture);
            Debug.Log("[Autoplay] shot " + name);
        }
    }
}
