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
        const float Width = 520f, Padding = 28f;
        const float ButtonHeight = 56f, ButtonGap = 12f;
        const float StatHeight = 40f;

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
        public string PlacementText => _placement != null ? _placement.text : "";
        public string KillsText => _kills != null ? _kills.text : "";
        public string DamageText => _damage != null ? _damage.text : "";
        public string TimeText => _time != null ? _time.text : "";
        public string KilledByText => _killedBy != null ? _killedBy.text : "";
        public string DeathCamText => _camName != null && _camRoot.activeSelf ? _camName.text + " / " + _camDetail.text : "";

        GameObject _root, _camRoot, _spectateButton;
        Text _title, _subtitle, _placement, _kills, _damage, _time, _killedBy, _camName, _camDetail;
        int _shownPlacement = int.MinValue, _shownTotal = int.MinValue, _shownKills = int.MinValue, _shownDamage = int.MinValue, _shownTime = int.MinValue;
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
            _shownPlacement = _shownTotal = _shownKills = _shownDamage = _shownTime = int.MinValue;
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
                _placement.text = placement == 0 ? "—" : "#" + IntText(placement) + " / 전체 " + IntText(total) + (team ? "팀" : "");
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
            int seconds = Mathf.FloorToInt(state.SecondsOf(me));
            if (seconds != _shownTime)
            {
                _shownTime = seconds;
                _time.text = TimeOf(seconds);
            }
            if (Outcome == MatchOutcome.Won && (me == null || !me.Dead.Value)) _killedBy.text = "—";
        }

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

            // A thin accent line over the title instead of a loud banner.
            UiKit.Box(Row("Rule", panel, 64f, 3f, y), UiKit.Accent);
            y += 3f + 18f;
            _title = UiKit.Label(Row("Title", panel, inner, 72f, y), "", 60, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            y += 72f + 6f;
            _subtitle = UiKit.Label(Row("Subtitle", panel, inner, 30f, y), "", 22, UiKit.TextDim, TextAnchor.MiddleCenter);
            y += 30f + 26f;

            _placement = Stat(panel, "순위", inner, ref y);
            _kills = Stat(panel, "처치", inner, ref y);
            _damage = Stat(panel, "피해량", inner, ref y);
            _time = Stat(panel, "생존 시간", inner, ref y);
            _killedBy = Stat(panel, "처치자", inner, ref y);
            _killedBy.text = "—";
            y += 22f;

            _spectateButton = UiKit.Button(Row("관전", panel, inner, ButtonHeight, y), "관전", 24, StartSpectating).gameObject;
            y += ButtonHeight + ButtonGap;
            y = MenuButton(panel, "다시 시작", inner, y, Restart);
            y = MenuButton(panel, "메인 메뉴", inner, y, ToMainMenu);
            UiKit.Label(Row("Hint", panel, inner, 26f, y), "Enter — 다시 시작", 18, UiKit.TextDim, TextAnchor.MiddleCenter);
            y += 26f;
            panel.sizeDelta = new Vector2(Width, y + Padding);
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

        // Caption on the left, value on the right.
        static Text Stat(Transform panel, string caption, float width, ref float y)
        {
            var row = Row(caption, panel, width, StatHeight, y);
            UiKit.Box(row, UiKit.PanelLight);
            var name = UiKit.Fill("Caption", row);
            name.offsetMin = new Vector2(16f, 0f);
            UiKit.Label(name, caption, 22, UiKit.TextDim);
            var value = UiKit.Fill("Value", row);
            value.offsetMax = new Vector2(-16f, 0f);
            var label = UiKit.Label(value, "", 26, UiKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            y += StatHeight + 8f;
            return label;
        }

        static float MenuButton(Transform panel, string text, float width, float y, UnityEngine.Events.UnityAction onClick)
        {
            UiKit.Button(Row(text, panel, width, ButtonHeight, y), text, 24, onClick);
            return y + ButtonHeight + ButtonGap;
        }

        static string IntText(int value)
        {
            if (value < 0 || value >= SmallInts.Length) return value.ToString();
            return SmallInts[value] ?? (SmallInts[value] = value.ToString());
        }
    }
}
