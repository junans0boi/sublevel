using FishNet;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The network status line (#171, ADR-128), top right under the kill feed: ping, packet loss and the server tick, coloured by how bad
    // they are. Off by default; the setting 네트워크 상태 표시 or its key (default F3) turns it on. Installs itself next
    // to every match, like the kill feed. The text is rewritten four times a second, never per frame.
    public class NetStatusView : MonoBehaviour
    {
        const int SortOrder = 1;
        const float Margin = 36f, Width = 440f, Height = 34f;
        // Under the kill feed's reserved block (it starts 78 below the top, KillFeed.AreaHeight tall).
        const float Top = 78f + KillFeed.AreaHeight + 10f;
        const float RefreshSeconds = 0.25f;

        public static NetStatusView Instance { get; private set; }

        Canvas _canvas;
        Text _label;
        Image _edge;
        NetStatusClient _client;
        float _nextRefresh;

        // ---- Read-only state for tests ----

        public bool Visible => _canvas != null && _canvas.enabled;
        public string Text => _label != null ? _label.text : "";
        public NetGrade Grade { get; private set; }
        public RectTransform Panel { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            MatchState.Started -= Install;
            MatchState.Started += Install;
        }

        static void Install(MatchState state)
        {
            if (Instance != null || state == null) return;
            var go = new GameObject("NetStatus");
            go.transform.SetParent(state.transform, false); // goes away with the match scene
            go.AddComponent<NetStatusView>();
        }

        void Awake()
        {
            Instance = this;
            var canvas = UiKit.Canvas("NetStatusCanvas", SortOrder, false);
            canvas.transform.SetParent(transform, false);
            _canvas = canvas;
            var topRight = Vector2.one;
            Panel = UiKit.Rect("Panel", canvas.transform, topRight, topRight, new Vector2(-Margin, -Top), new Vector2(Width, Height));
            UiKit.Box(Panel, UiKit.Panel);
            _edge = UiKit.Box(UiKit.Rect("Edge", Panel, new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(3f, Height)), UiKit.Steel);
            var text = UiKit.Fill("Text", Panel);
            text.offsetMin = new Vector2(10f, 0f);
            text.offsetMax = new Vector2(-14f, 0f);
            _label = UiKit.Label(text, "", 20, UiKit.Text, TextAnchor.MiddleRight);
            _canvas.enabled = false;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            // The key toggles the setting (saved), so the settings screen and the key agree.
            if (UiFocus.Current == UiScreen.None && KeyBinds.Pressed(BindAction.NetStats))
                GameSettings.SetAndSave(SettingId.NetStats, GameSettings.ShowNetStats ? 0f : 1f);

            if (_client == null) _client = NetStatusClient.Attach(InstanceFinder.NetworkManager); // counting pulses before it is shown
            bool wanted = GameSettings.ShowNetStats && UiFocus.Current != UiScreen.Inventory && UiFocus.Current != UiScreen.Map;
            if (_canvas.enabled != wanted) { _canvas.enabled = wanted; _nextRefresh = 0f; }
            if (!wanted || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh();
        }

        void Refresh()
        {
            if (_client == null) { _label.text = "네트워크 없음"; return; }
            Apply(_client.PingMs, _client.Loss, _client.HasPulses, _client.ServerTick, _client.TickRate);
            // The server's count of refused client requests (#170); only a host or dedicated server has any.
            if (ServerGuard.Total > 0) _label.text += "  거절 " + ServerGuard.Total;
        }

        // Shows these numbers (what Refresh does with the live ones; tests and captures call it with chosen ones).
        public void Apply(int pingMs, float loss, bool hasPulses, uint serverTick, int tickRate)
        {
            Grade = NetStatusRules.Grade(pingMs, loss);
            _label.text = NetStatusRules.Format(pingMs, loss, hasPulses, serverTick, tickRate);
            var color = Grade == NetGrade.Good ? UiKit.Steel : Grade == NetGrade.Fair ? UiKit.Accent : UiKit.Danger;
            _edge.color = color;
            _label.color = Grade == NetGrade.Good ? UiKit.Text : color;
        }
    }
}
