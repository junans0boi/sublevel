using FishNet.Managing;
using UnityEngine;

namespace Game.Net
{
    // Client: the countdown of the waiting island, as one line on the screen and as text on the big board of the island
    // (ADR-385, #418). The board's numbers are the server's broadcast; nothing here decides anything.
    public sealed class WaitingHud : MonoBehaviour
    {
        WaitingBroadcast _last;
        float _at = -10f;
        NetworkManager _nm;
        static GUIStyle _style;
        TextMesh _title, _time, _people, _bungee;

        public static void Attach(NetworkManager nm)
        {
            if (nm == null || nm.GetComponent<WaitingHud>() != null) return;
            var hud = nm.gameObject.AddComponent<WaitingHud>();
            hud._nm = nm;
            nm.ClientManager.RegisterBroadcast<WaitingBroadcast>(hud.OnBroadcast);
        }

        void OnDestroy() { OnIsland = false; if (_nm != null) _nm.ClientManager.UnregisterBroadcast<WaitingBroadcast>(OnBroadcast); }

        void OnBroadcast(WaitingBroadcast message, FishNet.Transporting.Channel channel) { _last = message; _at = Time.unscaledTime; }

        // True while the server says the room is open: the player stands on the island, not in an elevator.
        public static bool OnIsland { get; private set; }

        bool Live => _last.Active && Time.unscaledTime - _at <= 3f;

        void Update()
        {
            bool live = Live;
            OnIsland = live;
            if (_title == null && !TryBuildBoard()) return;
            _time.gameObject.SetActive(live); _people.gameObject.SetActive(live); _bungee.gameObject.SetActive(live && _last.BungeeBest > 0f);
            if (!live) { _title.text = "SUBLEVEL"; return; }
            int s = Mathf.CeilToInt(_last.SecondsLeft);
            _title.text = "DEPARTURE";
            _time.text = $"{s / 60}:{s % 60:00}";
            _people.text = $"{_last.Total} / {_last.Target} ON BOARD";
            _bungee.text = $"BUNGEE BEST  {_last.BungeeBest:0.0} m";
        }

        // The island is part of the World scene; its board spot is WaitingIsland.Board (Core). Built once the island object exists.
        bool TryBuildBoard()
        {
            var island = GameObject.Find("WaitingIsland");
            if (island == null) return false;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("WaitingBoardText").transform;
            root.SetParent(island.transform, false);
            var b = Game.Core.WaitingIsland.Board;
            root.localPosition = new Vector3(b.x, 7f, b.z + 0.1f);
            root.localRotation = Quaternion.Euler(0f, 180f, 0f);          // the text faces the middle of the island (+Z)
            _title = Line(root, font, "Title", 0f, 3.2f, 1.1f, new Color(0.85f, 0.9f, 1f));
            _time = Line(root, font, "Time", 0f, 0.6f, 2.6f, new Color(1f, 0.82f, 0.25f));
            _people = Line(root, font, "People", 0f, -2.2f, 1.0f, new Color(0.8f, 1f, 0.85f));
            _bungee = Line(root, font, "Bungee", 0f, -3.6f, 0.8f, new Color(1f, 0.6f, 0.4f));
            return true;
        }

        static TextMesh Line(Transform parent, Font font, string name, float x, float y, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, y, 0f);
            var text = go.AddComponent<TextMesh>();
            text.font = font; text.fontSize = 64; text.characterSize = size * 0.35f; text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
            text.color = color; text.richText = false;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return text;
        }

        // The same time on the screen, for a person who is not looking at the board.
        void OnGUI()
        {
            if (!Live) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            int s = Mathf.CeilToInt(_last.SecondsLeft);
            GUI.Label(new Rect(0, 20, Screen.width, 70), $"Departure in {s / 60}:{s % 60:00}    {_last.Total} / {_last.Target}", _style);
        }
    }
}
