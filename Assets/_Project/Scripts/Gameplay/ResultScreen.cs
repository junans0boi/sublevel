using Game.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // What the local player sees after dying or winning (ADR-094). On death: a few seconds of death cam on the killer
    // (name, weapon, distance), then the panel: "탈락" with the placement once the player (or their team) is out,
    // "사망" while the team plays on, "승리" for the last one (team) standing; kills, damage dealt, survival time and
    // who did it, with spectate / restart / main menu buttons. Spectating hands the camera to SpectateCamera.
    // It reads MatchState only. It does not go through UiFocus: while the panel is up, BlocksGameplay is true and
    // PlayerController frees the cursor and ignores gameplay input. Esc still opens the pause menu on top.
    public class ResultScreen : MonoBehaviour
    {
        public enum Phase { None, DeathCam, Panel, Spectate }

        public const float DeathCamSeconds = 4f;
        const int SortOrder = 90; // under the pause menu (100), over the HUD
        const float Width = 860f, Padding = 28f;
        const float ButtonHeight = 56f, ButtonGap = 12f;
        const float StatHeight = 44f, TileHeight = 100f;

        static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);
        static readonly string[] SmallInts = new string[128];

        public static ResultScreen Instance { get; private set; }

        // True while the panel is on screen. Gameplay input (move, look, fire, inventory) should be ignored and the
        // mouse left free for the buttons while this is set.
        public static bool BlocksGameplay { get; private set; }

        // True from the local player's death on: the HUD (their own empty health, minimap) stands down.
        public static bool HidesHud => !MatchAutoplay.Active && Instance != null && Instance.Current != Phase.None && Combatant.LocalPlayer != null && Combatant.LocalPlayer.Dead.Value;

        // ---- Read-only state for tests ----

        public bool IsShown => _root != null && _root.activeSelf;
        public Phase Current { get; private set; } = Phase.None;
        public MatchOutcome Outcome { get; private set; } = MatchOutcome.Playing;
        public SpectateCamera Spectate { get; private set; }
        public string TitleText => _title != null ? _title.text : "";
        public string PlacementText => _placement != null ? _rank.text + " " + _placement.text : "";
        public string DistanceText => _distance != null ? _distance.text : "";
        public string KillsText => _kills != null ? _kills.text : "";
        public string DamageText => _damage != null ? _damage.text : "";
        public string TimeText => _time != null ? _time.text : "";
        public string KilledByText => _killedBy != null ? _killedBy.text : "";
        public string DeathCamText => _camName != null && _camRoot.activeSelf ? _camName.text + " / " + _camDetail.text : "";

        GameObject _root, _camRoot, _spectateButton;
        Text _title, _subtitle, _rank, _distance, _placement, _kills, _damage, _time, _killedBy, _camName, _camDetail;
        int _shownPlacement = int.MinValue, _shownTotal = int.MinValue, _shownKills = int.MinValue, _shownDamage = int.MinValue, _shownTime = int.MinValue, _shownMeters = int.MinValue;
        bool _wasDead, _killerKnown;
        float _camUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => BlocksGameplay = false;

        // Installs itself next to every match; nothing in the scene or the player prefab refers to it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            MatchState.Started -= Install;
            MatchState.Started += Install;
        }

        static void Install(MatchState state)
        {
            if (Instance != null || state == null) return;
            var go = new GameObject("ResultScreen");
            // Under the match object so it goes away with the scene.
            go.transform.SetParent(state.transform, false);
            go.AddComponent<ResultScreen>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Spectate = gameObject.AddComponent<SpectateCamera>();
            Build();
            BuildDeathCam();
            _root.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            BlocksGameplay = false;
        }

        // 다시 시작: a fresh match (the host reloads the scene, a remote client asks the server).
        public void Restart()
        {
            if (MatchState.Instance != null) MatchState.Instance.RequestRestart();
            else Debug.LogWarning("ResultScreen: restart ignored, there is no match.");
        }

        // 메인 메뉴: leaves the match like the pause menu does.
        public void ToMainMenu() => MainMenu.LeaveMatch();

        // 관전: follows a teammate, the killer or anyone alive; stays on the panel when nobody is left.
        public void StartSpectating()
        {
            if (Current != Phase.Panel || !Spectate.BeginFollow()) return;
            Enter(Phase.Spectate);
        }

        // Click / Space during the death cam.
        public void SkipDeathCam()
        {
            if (Current == Phase.DeathCam) Enter(Phase.Panel);
        }

        void Update()
        {
            if (_root == null) return;
            var state = MatchState.Instance;
            var me = Combatant.LocalPlayer;
            bool dead = me != null && me.Dead.Value;
            var outcome = state != null ? state.LocalOutcome : MatchOutcome.Playing;

            if (dead && !_wasDead) StartDeathCam(me);
            _wasDead = dead;
            if (Current == Phase.DeathCam)
            {
                // The kill can arrive a moment after the death itself.
                if (!_killerKnown) FindKiller(me);
                if (Time.unscaledTime >= _camUntil || SkipPressed()) Enter(Phase.Panel);
            }
            if (outcome != Outcome)
            {
                Outcome = outcome;
                // Out, or the match decided while watching: the panel (after the death cam).
                if (outcome != MatchOutcome.Playing && Current != Phase.DeathCam) Enter(Phase.Panel);
                else if (outcome == MatchOutcome.Playing && !dead) Enter(Phase.None);
            }
            // Nobody left to watch.
            if (Current == Phase.Spectate && Spectate.Current != SpectateCamera.Mode.Follow) Enter(Phase.Panel);
            if (Current != Phase.Panel) return;

            TickNumbers(state, me);
            if (_spectateButton.activeSelf != dead) _spectateButton.SetActive(dead);

            // A dead player's Enter already reaches the server through PlayerController; the winner is alive, so
            // the same key is read here. Scripted input (tests, captures) never touches the device.
            if (!dead && PlayerController.InputOverride == null && UiFocus.Current == UiScreen.None)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.enterKey.wasPressedThisFrame) Restart();
            }
        }

        void Enter(Phase phase)
        {
            Current = phase;
            bool panel = phase == Phase.Panel;
            BlocksGameplay = panel;
            _root.SetActive(panel);
            _camRoot.SetActive(phase == Phase.DeathCam);
            if (phase == Phase.None) Spectate.Stop(false);
            else if (phase != Phase.Spectate && Spectate.Current == SpectateCamera.Mode.Follow) Spectate.Stop(true);
            if (!panel) return;
            _shownPlacement = _shownTotal = _shownKills = _shownDamage = _shownTime = _shownMeters = int.MinValue;
            var me = Combatant.LocalPlayer;
            bool team = me != null && me.HasTeam;
            switch (Outcome)
            {
                case MatchOutcome.Won:
                    _title.text = "승리";
                    _title.color = UiKit.Accent;
                    _subtitle.text = team ? "최후의 생존 팀" : "최후의 생존자";
                    break;
                case MatchOutcome.Eliminated:
                    _title.text = "탈락";
                    _title.color = UiKit.Danger;
                    _subtitle.text = team ? "팀이 탈락했습니다" : "이번 매치에서 탈락했습니다";
                    break;
                default:
                    _title.text = "사망";
                    _title.color = UiKit.Danger;
                    _subtitle.text = "팀원이 아직 싸우고 있습니다";
                    break;
            }
        }

        void StartDeathCam(Combatant me)
        {
            _killerKnown = false;
            _camName.text = "";
            _camDetail.text = "";
            Spectate.BeginDeathCam(me, null);
            FindKiller(me);
            _camUntil = Time.unscaledTime + DeathCamSeconds;
            Enter(Phase.DeathCam);
        }

        void FindKiller(Combatant me)
        {
            var state = MatchState.Instance;
            if (state == null || me == null || !state.TryGetDeath(me.ObjectId, out var entry)) return;
            _killerKnown = true;
            Combatant killer = null;
            foreach (var c in Combatant.All)
                if (c != null && c.ObjectId == entry.KillerId) killer = c;
            bool hasKiller = killer != null && !string.IsNullOrEmpty(entry.KillerName);
            string tag = KillFeed.Tag(in entry, hasKiller);
            if (hasKiller)
            {
                int metres = Mathf.RoundToInt(Vector3.Distance(killer.transform.position, me.transform.position));
                _camName.text = entry.KillerName;
                _camDetail.text = tag + "  ·  " + IntText(metres) + "m";
                _killedBy.text = entry.KillerName + "  ·  " + tag;
            }
            else
            {
                _camName.text = "사망";
                _camDetail.text = tag;
                _killedBy.text = tag;
            }
            Spectate.BeginDeathCam(me, hasKiller ? killer : null);
        }

        bool SkipPressed()
        {
            if (PlayerController.InputOverride != null || UiFocus.Current != UiScreen.None) return false;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            return (mouse != null && mouse.leftButton.wasPressedThisFrame) || (keyboard != null && keyboard.spaceKey.wasPressedThisFrame);
        }

        void TickNumbers(MatchState state, Combatant me)
        {
            if (state == null) return;
            // Team matches (ADR-072) place teams: "#2 / 전체 4팀".
            bool team = me != null && me.HasTeam;
            int placement = state.LocalPlacement, total = team ? state.TeamsTotal : state.TotalCount, kills = state.LocalKills;
            if (placement != _shownPlacement || total != _shownTotal)
            {
                _shownPlacement = placement;
                _shownTotal = total;
                _rank.text = placement == 0 ? "—" : "#" + IntText(placement);
                _placement.text = "/ 전체 " + IntText(total) + (team ? "팀" : "");
            }
            if (kills != _shownKills)
            {
                _shownKills = kills;
                _kills.text = IntText(kills);
            }
            int damage = Mathf.RoundToInt(state.DamageOf(me));
            if (damage != _shownDamage)
            {
                _shownDamage = damage;
                _damage.text = damage.ToString();
            }
            int meters = Mathf.RoundToInt(state.MetersOf(me));
            if (meters != _shownMeters)
            {
                _shownMeters = meters;
                _distance.text = DistanceOf(meters);
            }
            int seconds = Mathf.FloorToInt(state.SecondsOf(me));
            if (seconds != _shownTime)
            {
                _shownTime = seconds;
                _time.text = TimeOf(seconds);
            }
            if (Outcome == MatchOutcome.Won && (me == null || !me.Dead.Value)) _killedBy.text = "—";
        }

        // 850 m, then 1.2 km from a thousand.
        public static string DistanceOf(int meters) => meters < 1000 ? meters + " m" : (meters / 1000f).ToString("0.0") + " km";

        public static string TimeOf(int seconds) => (seconds / 60) + ":" + (seconds % 60).ToString("00");

        void Build()
        {
            var canvas = UiKit.Canvas("ResultScreenCanvas", SortOrder, true);
            _root = canvas.gameObject;
            _root.transform.SetParent(transform, false);
            UiKit.Box(UiKit.Fill("Dim", _root.transform), Dim, true);

            var centre = new Vector2(0.5f, 0.5f);
            var panel = UiKit.Rect("Panel", _root.transform, centre, centre, Vector2.zero, new Vector2(Width, 100f));
            UiKit.Box(panel, UiKit.Panel, true);
            float inner = Width - Padding * 2f;
            float y = Padding;

            // PUBG-like: the result on the left, the placement big on the right, then one row of numbers.
            UiKit.Box(Row("Rule", panel, inner, 3f, y), UiKit.Accent);
            y += 3f + 14f;
            var head = Row("Head", panel, inner, 130f, y);
            _title = UiKit.Label(Cell("Title", head, 0f, 0.4f, 0.55f, 1f), "", 60, UiKit.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            _subtitle = UiKit.Label(Cell("Subtitle", head, 0f, 0f, 0.55f, 0.4f), "", 22, UiKit.TextDim, TextAnchor.UpperLeft);
            _rank = UiKit.Label(Cell("Rank", head, 0.45f, 0.32f, 1f, 1f), "", 96, UiKit.Accent, TextAnchor.LowerRight, FontStyle.Bold);
            _placement = UiKit.Label(Cell("Total", head, 0.45f, 0f, 1f, 0.32f), "", 24, UiKit.TextDim, TextAnchor.UpperRight);
            y += 130f + 18f;

            const float gap = 10f;
            float tile = (inner - gap * 3f) / 4f;
            _time = Tile(panel, "생존 시간", inner, tile, 0, gap, y);
            _kills = Tile(panel, "처치", inner, tile, 1, gap, y);
            _damage = Tile(panel, "피해량", inner, tile, 2, gap, y);
            _distance = Tile(panel, "이동 거리", inner, tile, 3, gap, y);
            y += TileHeight + 12f;

            var by = Row("처치자", panel, inner, StatHeight, y);
            UiKit.Box(by, UiKit.PanelLight);
            var byCaption = UiKit.Fill("Caption", by);
            byCaption.offsetMin = new Vector2(16f, 0f);
            UiKit.Label(byCaption, "처치자", 22, UiKit.TextDim);
            var byValue = UiKit.Fill("Value", by);
            byValue.offsetMax = new Vector2(-16f, 0f);
            _killedBy = UiKit.Label(byValue, "—", 24, UiKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            y += StatHeight + 22f;

            // Buttons side by side; 관전 only for the dead.
            var buttons = Row("Buttons", panel, inner, ButtonHeight, y);
            _spectateButton = UiKit.Button(Cell("관전", buttons, 0f, 0f, 0.32f, 1f), "관전", 24, StartSpectating).gameObject;
            UiKit.Button(Cell("다시 시작", buttons, 0.34f, 0f, 0.66f, 1f), "다시 시작", 24, Restart);
            UiKit.Button(Cell("메인 메뉴", buttons, 0.68f, 0f, 1f, 1f), "메인 메뉴", 24, ToMainMenu);
            y += ButtonHeight + 10f;
            UiKit.Label(Row("Hint", panel, inner, 26f, y), "Enter — 다시 시작", 18, UiKit.TextDim, TextAnchor.MiddleCenter);
            y += 26f;
            panel.sizeDelta = new Vector2(Width, y + Padding);
        }

        // A box inside `parent` between two anchor corners (fractions of the parent).
        static RectTransform Cell(string name, RectTransform parent, float x0, float y0, float x1, float y1)
        {
            var rect = UiKit.Fill(name, parent);
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            return rect;
        }

        // One number under its caption.
        static Text Tile(Transform panel, string caption, float inner, float width, int index, float gap, float y)
        {
            float x = -inner * 0.5f + width * 0.5f + index * (width + gap);
            var rect = UiKit.Rect(caption, panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, -y), new Vector2(width, TileHeight));
            UiKit.Box(rect, UiKit.PanelLight);
            var value = UiKit.Rect("Value", rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(width - 8f, 56f));
            var label = UiKit.Label(value, "", 44, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            var cap = UiKit.Rect("Caption", rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(width - 8f, 26f));
            UiKit.Label(cap, caption, 20, UiKit.TextDim, TextAnchor.MiddleCenter);
            return label;
        }

        // The killer's name over the death cam, in the upper third where the eye already is.
        void BuildDeathCam()
        {
            var canvas = UiKit.Canvas("DeathCamCanvas", SortOrder, false);
            _camRoot = canvas.gameObject;
            _camRoot.transform.SetParent(transform, false);
            var at = new Vector2(0.5f, 0.78f);
            var band = UiKit.Rect("Band", _camRoot.transform, at, at, Vector2.zero, new Vector2(640f, 150f));
            UiKit.Box(band, UiKit.Panel);
            var top = new Vector2(0.5f, 1f);
            UiKit.Label(UiKit.Rect("Caption", band, top, top, new Vector2(0f, -12f), new Vector2(600f, 24f)), "나를 처치한 상대", 18, UiKit.TextDim, TextAnchor.MiddleCenter);
            _camName = UiKit.Label(UiKit.Rect("Name", band, top, top, new Vector2(0f, -38f), new Vector2(600f, 52f)), "", 40, UiKit.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            _camDetail = UiKit.Label(UiKit.Rect("Detail", band, top, top, new Vector2(0f, -92f), new Vector2(600f, 28f)), "", 22, UiKit.Text, TextAnchor.MiddleCenter);
            UiKit.Label(UiKit.Rect("Hint", band, top, top, new Vector2(0f, -122f), new Vector2(600f, 22f)), "클릭 / Space — 건너뛰기", 16, UiKit.TextDim, TextAnchor.MiddleCenter);
            _camRoot.SetActive(false);
        }

        // A row hung from the top of the panel, `y` reference pixels down.
        static RectTransform Row(string name, Transform panel, float width, float height, float y) =>
            UiKit.Rect(name, panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(width, height));

        static string IntText(int value)
        {
            if (value < 0 || value >= SmallInts.Length) return value.ToString();
            return SmallInts[value] ?? (SmallInts[value] = value.ToString());
        }
    }
}
