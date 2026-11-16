using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The main entry screen (adopted lobby ②, "승강기 조작반", ADR-245): a top bar of tabs, the party standing on the
    // platform in the upper two thirds, and a control panel in the lower third (mode switch, LED, perspective, map, one
    // status button). The state is PartyLobby; this only draws it. Squad matches need the team flow of the pollution track,
    // so for now 스쿼드 can be chosen but starting says why it cannot.
    public class LobbyPanel
    {
        public const float SafeMargin = 64f; // 21:9: UI stays inside this, only the background widens
        const float BarHeight = 72f, PanelHeight = 340f;
        public const int SelfId = 0;

        public PartyLobby Party { get; private set; }
        public RectTransform Root { get; private set; }
        public string LedText => _led != null ? _led.text : "";
        public string StatusLabel => _statusLabel != null ? _statusLabel.text : "";
        public string ReasonText => _reason != null ? _reason.text : "";

        MainMenu _menu;
        Text _led, _statusLabel, _reason;
        Button _status, _solo, _squad, _tpp, _fpp;
        readonly Text[] _slotName = new Text[PartyLobby.SquadSize];
        readonly Text[] _slotMark = new Text[PartyLobby.SquadSize];
        readonly Image[] _slotBox = new Image[PartyLobby.SquadSize];

        public static LobbyPanel Build(MainMenu menu, Transform parent, System.Action training, System.Action custom, System.Action settings, System.Action credits, System.Action quit)
        {
            var p = new LobbyPanel { _menu = menu };
            p.Party = new PartyLobby(SelfId, "나");
            p.Party.SetView(SelfId, GameSettings.DefaultViewFirstPerson ? Perspective.First : Perspective.Third);
            p.Root = UiKit.Fill("Lobby", parent);
            p.BuildBar(training, custom, settings, credits, quit);
            p.BuildSlots();
            p.BuildControl();
            p.Refresh();
            return p;
        }

        void BuildBar(System.Action training, System.Action custom, System.Action settings, System.Action credits, System.Action quit)
        {
            var bar = UiKit.Rect("TopBar", Root, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, BarHeight));
            bar.anchorMax = new Vector2(1f, 1f);
            UiKit.Box(bar, UiKit.PanelDeep);
            var title = UiKit.Rect("Title", bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(SafeMargin, 0f), new Vector2(300f, 48f));
            UiKit.Label(title, "SUBLEVEL", 34, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            string[] names = { "플레이", "훈련장", "커스텀 모드", "설정", "크레딧", "종료" };
            System.Action[] acts = { null, training, custom, settings, credits, quit };
            const float w = 168f, gap = 8f;
            float x = -SafeMargin;
            for (int i = names.Length - 1; i >= 0; i--)
            {
                var r = UiKit.Rect("Tab" + i, bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(x, 0f), new Vector2(w, 48f));
                var act = acts[i];
                var b = UiKit.Button(r, names[i], 22, act == null ? null : new UnityEngine.Events.UnityAction(act));
                if (i == 0) { b.GetComponent<Image>().color = UiKit.Selected; b.interactable = false; b.GetComponentInChildren<Text>().color = UiKit.Text; }
                x -= w + gap;
            }
            // The tabs sit on the right, so the title keeps the left.
        }

        void BuildSlots()
        {
            var row = UiKit.Rect("Slots", Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(BarHeight + 80f)), new Vector2(4f * 300f + 3f * 24f, 360f));
            for (int i = 0; i < PartyLobby.SquadSize; i++)
            {
                var slot = UiKit.Rect("Slot" + i, row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * 324f, 0f), new Vector2(300f, 360f));
                _slotBox[i] = UiKit.Box(slot, UiKit.Panel);
                UiKit.Frame(slot, 1f, UiKit.SteelDim);
                _slotMark[i] = UiKit.Label(UiKit.Rect("Mark", slot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(280f, 32f)), "", 22, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                _slotName[i] = UiKit.Label(UiKit.Rect("Name", slot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(280f, 36f)), "", 26, UiKit.Text, TextAnchor.MiddleCenter);
                // Silhouette stand-in: a head and a body block (the real character model comes from the character track).
                UiKit.Box(UiKit.Rect("Head", slot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(56f, 56f)), new Color(1f, 1f, 1f, 0.12f));
                UiKit.Box(UiKit.Rect("Body", slot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(120f, 140f)), new Color(1f, 1f, 1f, 0.12f));
            }
        }

        void BuildControl()
        {
            var panel = UiKit.Rect("Control", Root, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(0f, PanelHeight));
            panel.anchorMax = new Vector2(1f, 0f);
            UiKit.Box(panel, UiKit.PanelDeep);
            // Left: the mode switch.
            var cap = UiKit.Rect("ModeCap", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(SafeMargin, -24f), new Vector2(300f, 30f));
            UiKit.Label(cap, "모드", 20, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _solo = UiKit.Button(UiKit.Rect("Solo", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(SafeMargin, -62f), new Vector2(280f, 84f)), "솔로 · 1인", 28, () => { Party.SetMode(SelfId, TeamMode.Solo); Refresh(); });
            _squad = UiKit.Button(UiKit.Rect("Squad", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(SafeMargin, -158f), new Vector2(280f, 84f)), "스쿼드 · 4인", 28, () => { Party.SetMode(SelfId, TeamMode.Squad); Refresh(); });
            // Centre: LED, perspective, map.
            var led = UiKit.Rect("Led", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(640f, 96f));
            UiKit.Box(led, new Color(0.02f, 0.03f, 0.035f, 1f));
            UiKit.Frame(led, 1f, UiKit.SteelDim);
            _led = UiKit.Label(UiKit.Fill("Text", led), "", 44, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            _tpp = UiKit.Button(UiKit.Rect("Tpp", panel, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-6f, -160f), new Vector2(200f, 56f)), "3인칭 TPP", 22, () => { Party.SetView(SelfId, Perspective.Third); Refresh(); });
            _fpp = UiKit.Button(UiKit.Rect("Fpp", panel, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(6f, -160f), new Vector2(200f, 56f)), "1인칭 FPP", 22, () => { Party.SetView(SelfId, Perspective.First); Refresh(); });
            var map = UiKit.Rect("Map", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -232f), new Vector2(640f, 84f));
            UiKit.Box(map, UiKit.Panel);
            UiKit.Frame(map, 1f, UiKit.SteelDim);
            UiKit.Label(UiKit.Fill("Text", map, 12f), "청람도 · 6개 지역 · 50명 · 약 20~25분", 22, UiKit.Text, TextAnchor.MiddleCenter);
            // Right: the one status button and why it may be off.
            _status = UiKit.Button(UiKit.Rect("Status", panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-SafeMargin, -62f), new Vector2(460f, 120f)), "", 34, OnStatus);
            _statusLabel = _status.GetComponentInChildren<Text>();
            var reason = UiKit.Rect("Reason", panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-SafeMargin, -196f), new Vector2(460f, 56f));
            _reason = UiKit.Label(reason, "", 20, UiKit.TextDim, TextAnchor.UpperRight);
            _reason.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        // The state button's one action. Squad matches wait for the team flow, so only solo starts today.
        void OnStatus()
        {
            var b = Party.ButtonFor(SelfId);
            if (!b.Enabled || b.Action != LobbyAction.Start) return;
            if (Party.Mode != TeamMode.Solo) return;
            _menu.SetMode(TeamMode.Solo);
            GameSettings.DefaultViewFirstPerson = Party.View == Perspective.First;
            _menu.StartGame();
        }

        public void Refresh()
        {
            bool squad = Party.Mode == TeamMode.Squad;
            _led.text = (squad ? "스쿼드 · " + PartyLobby.SquadSize + "인" : "솔로 · 1인") + "  |  " + (Party.View == Perspective.First ? "FPP" : "TPP");
            Mark(_solo, !squad); Mark(_squad, squad);
            Mark(_tpp, Party.View == Perspective.Third); Mark(_fpp, Party.View == Perspective.First);
            int n = squad ? PartyLobby.SquadSize : Party.Count; // slots shown, centred as a group
            float x0 = (PartyLobby.SquadSize * 324f - 24f - (n * 324f - 24f)) * 0.5f;
            for (int i = 0; i < PartyLobby.SquadSize; i++)
            {
                ((RectTransform)_slotBox[i].transform).anchoredPosition = new Vector2(x0 + i * 324f, 0f);
                bool used = i < Party.Count, shown = used || squad;
                _slotBox[i].gameObject.SetActive(shown);
                if (!shown) continue;
                _slotName[i].text = used ? Party.Members[i].Name : "+ 초대 (준비 중)";
                _slotName[i].color = used ? UiKit.Text : UiKit.TextDim;
                _slotMark[i].text = !used ? "" : Party.IsLeader(Party.Members[i].Id) ? "★ 파티장" : Party.Members[i].Ready ? "✓ 준비 완료" : "… 준비 중";
            }
            var b = Party.ButtonFor(SelfId);
            bool squadBlocked = squad && b.Action == LobbyAction.Start;
            _statusLabel.text = squadBlocked ? "스쿼드 시작" : b.Label;
            _status.interactable = b.Enabled && !squadBlocked;
            _statusLabel.color = _status.interactable ? UiKit.Text : UiKit.TextDim;
            _reason.text = squadBlocked ? "스쿼드 매치는 팀 규칙 병합 뒤에 열립니다." : b.Reason;
        }

        // Selected = a thick accent tint plus a check (not colour alone); the others stay dim.
        static void Mark(Button b, bool selected)
        {
            b.GetComponent<Image>().color = selected ? UiKit.Selected : UiKit.PanelLight;
            var t = b.GetComponentInChildren<Text>();
            string s = t.text.TrimStart('✓', ' ');
            t.text = selected ? "✓ " + s : s;
            t.color = selected ? UiKit.Text : UiKit.TextDim;
        }
    }
}
