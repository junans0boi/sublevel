using Game.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // End-of-match screen for the local player: "탈락" with the placement when they die, "승리" when they are the
    // last one standing; kills and restart/quit buttons on both. It reads MatchState only.
    // It does not go through UiFocus (there is no screen id for it yet): while it is up, BlocksGameplay is true and
    // PlayerController is expected to free the cursor and ignore gameplay input. Esc still opens the pause menu on top.
    public class ResultScreen : MonoBehaviour
    {
        const int SortOrder = 90; // under the pause menu (100), over the HUD
        const float Width = 520f, Padding = 28f;
        const float ButtonHeight = 56f, ButtonGap = 12f;
        const float StatHeight = 40f;

        static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);
        static readonly string[] SmallInts = new string[128];

        public static ResultScreen Instance { get; private set; }

        // True while the result is on screen. Gameplay input (move, look, fire, inventory) should be ignored and the
        // mouse left free for the buttons while this is set.
        public static bool BlocksGameplay { get; private set; }

        // ---- Read-only state for tests ----

        public bool IsShown => _root != null && _root.activeSelf;
        public MatchOutcome Outcome { get; private set; } = MatchOutcome.Playing;
        public string TitleText => _title != null ? _title.text : "";
        public string PlacementText => _placement != null ? _placement.text : "";
        public string KillsText => _kills != null ? _kills.text : "";

        GameObject _root;
        Text _title, _subtitle, _placement, _kills;
        int _shownPlacement = int.MinValue, _shownTotal = int.MinValue, _shownKills = int.MinValue;

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
            Build();
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

        // 게임 종료: straight out, no confirmation.
        public void Quit() => Application.Quit();

        void Update()
        {
            if (_root == null) return;
            var state = MatchState.Instance;
            var outcome = state != null ? state.LocalOutcome : MatchOutcome.Playing;
            if (outcome != Outcome) Show(outcome);
            if (outcome == MatchOutcome.Playing) return;

            // Team matches (ADR-072) place teams: "#2 / 전체 4팀".
            bool team = Combatant.LocalPlayer != null && Combatant.LocalPlayer.HasTeam;
            int placement = state.LocalPlacement, total = team ? state.TeamsTotal : state.TotalCount, kills = state.LocalKills;
            if (placement != _shownPlacement || total != _shownTotal)
            {
                _shownPlacement = placement;
                _shownTotal = total;
                _placement.text = "#" + IntText(placement) + " / 전체 " + IntText(total) + (team ? "팀" : "");
            }
            if (kills != _shownKills)
            {
                _shownKills = kills;
                _kills.text = IntText(kills);
            }

            // A dead player's Enter already reaches the server through PlayerController; the winner is alive, so
            // the same key is read here. Scripted input (tests, captures) never touches the device.
            if (outcome == MatchOutcome.Won && PlayerController.InputOverride == null && UiFocus.Current == UiScreen.None)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.enterKey.wasPressedThisFrame) Restart();
            }
        }

        void Show(MatchOutcome outcome)
        {
            Outcome = outcome;
            bool shown = outcome != MatchOutcome.Playing;
            BlocksGameplay = shown;
            _root.SetActive(shown);
            if (!shown) return;
            bool won = outcome == MatchOutcome.Won;
            _title.text = won ? "승리" : "탈락";
            _title.color = won ? UiKit.Accent : UiKit.Danger;
            bool team = Combatant.LocalPlayer != null && Combatant.LocalPlayer.HasTeam;
            _subtitle.text = won ? (team ? "최후의 생존 팀" : "최후의 생존자") : (team ? "팀이 탈락했습니다" : "이번 매치에서 탈락했습니다");
        }

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
            y += 22f;

            y = MenuButton(panel, "다시 시작", inner, y, Restart);
            y = MenuButton(panel, "게임 종료", inner, y, Quit);
            UiKit.Label(Row("Hint", panel, inner, 26f, y), "Enter — 다시 시작", 18, UiKit.TextDim, TextAnchor.MiddleCenter);
            y += 26f;
            panel.sizeDelta = new Vector2(Width, y + Padding);
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
