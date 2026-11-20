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

        void OnBroadcast(WaitingBroadcast message, FishNet.Transporting.Channel channel)
        {
            int lit = message.LiftCalls & ~_last.LiftCalls;   // a button that just lit: its ding (the presser hears it, and who stands near)
            if (message.Active && lit != 0) { if ((lit & 1) != 0) Ding(false); if ((lit & 2) != 0) Ding(true); }
            _last = message; _at = Time.unscaledTime;
        }

        // True while the server says the room is open: the player stands on the island, not in an elevator.
        public static bool OnIsland { get; private set; }

        bool _preview;
        bool Live => _preview || (_last.Active && Time.unscaledTime - _at <= 3f);

        // Editor captures: the board as it reads with this broadcast, with no network behind it.
        public static void Preview(WaitingBroadcast message)
        {
            var hud = new GameObject("WaitingHudPreview").AddComponent<WaitingHud>();
            hud._last = message; hud._preview = true;
            hud.Update();
        }

        void Update()
        {
            Refresh();
        }

        void Refresh()
        {
            bool live = Live;
            OnIsland = live;
            MoveLiftCar(live);
            if (_title == null && !TryBuildBoard()) return;
            _time.gameObject.SetActive(live); _people.gameObject.SetActive(live); _bungee.gameObject.SetActive(live && _last.BungeeBest > 0f);
            if (!live) { _title.text = "SUBLEVEL"; return; }
            int s = Mathf.CeilToInt(_last.SecondsLeft);
            _title.text = "DEPARTURE";
            _time.text = $"{s / 60}:{s % 60:00}";
            _people.text = $"{_last.Total} / {_last.Target} ON BOARD";
            _bungee.text = $"BUNGEE BEST  {_last.BungeeBest:0.0} m";
        }

        // The bungee lift's cage (ADR-387/490) follows the server's lift state (phase + seconds into it): the car, its shut doors and the
        // call buttons are drawn here, the riders' bodies are moved by their own BungeeRide. Before the first broadcast (or after the room
        // closed) the car waits at the bottom, doors open.
        Transform _car, _carDoors;
        CallButton _bottom, _top;
        void MoveLiftCar(bool live)
        {
            if (_car == null)
            {
                var island = GameObject.Find("WaitingIsland");
                var car = island != null ? island.transform.Find("BungeeLiftCar") : null;
                if (car == null) return;
                _car = car; _carDoors = car.Find("Doors");
            }
            var phase = live ? (Game.Core.LiftPhase)_last.LiftPhase : Game.Core.LiftPhase.OpenBottom;
            float into = live ? _last.LiftInto + (_preview ? 0f : Time.unscaledTime - _at) : 0f;
            float h = Game.Core.BungeeLift.HeightOf(phase, into);
            var p = _car.localPosition; p.y = h; _car.localPosition = p;
            if (_carDoors != null) _carDoors.gameObject.SetActive(!Game.Core.BungeeLift.DoorsOpenOf(phase, into));
            if (_bottom == null) { _bottom = new CallButton(_car.parent, false); _top = new CallButton(_car.parent, true); }
            bool moving = !Game.Core.BungeeLift.DoorsOpenOf(phase, into);
            string floor = (h > Game.Core.BungeeLift.Height * 0.5f ? "2" : "1") + (!moving ? "" : phase == Game.Core.LiftPhase.Rising ? " ▲" : " ▼");
            _bottom.Show(live && (_last.LiftCalls & 1) != 0, floor);
            _top.Show(live && (_last.LiftCalls & 2) != 0, floor);
        }

        // A call button (ADR-490), built at runtime from WaitingIsland's numbers so the World scene need not be rebuilt: a post (bottom)
        // or a plate on the rail (top), the lamp that lights while the call stands, and the floor the car is at above it.
        sealed class CallButton
        {
            readonly Renderer _lamp; readonly Light _glow; readonly TextMesh _floor;
            static readonly Color Off = new Color(0.25f, 0.22f, 0.2f), On = new Color(1f, 0.72f, 0.2f);

            public CallButton(Transform island, bool top)
            {
                var b = top ? Game.Core.WaitingIsland.CallTop : Game.Core.WaitingIsland.CallBottom;
                var root = new GameObject(top ? "LiftCallTop" : "LiftCallBottom").transform;
                root.SetParent(island, false);
                root.localPosition = new Vector3(b.x, b.y, b.z);
                root.localRotation = Quaternion.Euler(0f, top ? 180f : 0f, 0f);   // the face (+Z local) towards whoever presses it
                var trim = island.Find("Trim")?.GetComponent<Renderer>()?.sharedMaterial;
                if (!top) Box(root, "Post", new Vector3(0f, -b.y * 0.5f + 0.25f, -0.05f), new Vector3(0.22f, b.y + 0.5f, 0.12f), trim, Color.white);
                Box(root, "Plate", Vector3.zero, new Vector3(0.18f, 0.28f, 0.04f), trim, new Color(0.15f, 0.15f, 0.16f));
                _lamp = Box(root, "Lamp", new Vector3(0f, 0f, 0.03f), new Vector3(0.12f, 0.12f, 0.03f), trim, Off);
                var g = new GameObject("Glow"); g.transform.SetParent(root, false); g.transform.localPosition = new Vector3(0f, 0f, 0.25f);
                _glow = g.AddComponent<Light>(); _glow.type = LightType.Point; _glow.range = 1.8f; _glow.intensity = 3f; _glow.color = On; _glow.shadows = LightShadows.None; _glow.enabled = false;
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var t = new GameObject("Floor"); t.transform.SetParent(root, false);
                t.transform.localPosition = new Vector3(0f, 0.3f, 0.03f); t.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                _floor = t.AddComponent<TextMesh>();
                _floor.font = font; _floor.fontSize = 64; _floor.characterSize = 0.022f; _floor.anchor = TextAnchor.MiddleCenter; _floor.color = new Color(1f, 0.55f, 0.25f);
                t.GetComponent<MeshRenderer>().sharedMaterial = BoardMaterial(font);
            }

            static Renderer Box(Transform parent, string name, Vector3 at, Vector3 size, Material m, Color c)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());   // looks only: the server judges the reach
                go.transform.SetParent(parent, false); go.transform.localPosition = at; go.transform.localScale = size;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = new Material(m != null ? m : r.sharedMaterial) { color = c };   // its own copy: the lamp changes colour
                return r;
            }

            bool? _lit;
            public void Show(bool lit, string floor)
            {
                if (_floor.text != floor) _floor.text = floor;
                if (_lit == lit) return;
                _lit = lit;
                _lamp.sharedMaterial.color = lit ? On : Off;
                if (lit) _lamp.sharedMaterial.EnableKeyword("_EMISSION"); else _lamp.sharedMaterial.DisableKeyword("_EMISSION");   // URP Lit: the lamp glows, not just tints
                _lamp.sharedMaterial.SetColor("_EmissionColor", lit ? On * 2f : Color.black);
                _glow.enabled = lit;
            }
        }

        static AudioClip _chime;
        static void Ding(bool top)
        {
            if (_chime == null) _chime = Resources.Load<AudioClip>("Sfx/elev_arrive_chime");   // the elevators' arrival chime (ADR-387 reuses it too)
            if (_chime == null) return;
            var island = GameObject.Find("WaitingIsland");
            if (island == null) return;
            var b = top ? Game.Core.WaitingIsland.CallTop : Game.Core.WaitingIsland.CallBottom;
            AudioSource.PlayClipAtPoint(_chime, island.transform.TransformPoint(new Vector3(b.x, b.y, b.z)), 0.7f);   // shortcut: Net cannot read GameSettings' effects volume (Gameplay); move to a Gameplay view if it matters
        }

        // Client: the call button F would press from where `me` stands looking along `look` (the pickups' reach and view cone), on the island only.
        public static bool CallInReach(Combatant me, Vector3 look, out bool top)
        {
            top = false;
            if (!OnIsland || me == null || me.Dead.Value) return false;
            var island = GameObject.Find("WaitingIsland");
            if (island == null) return false;
            var feet = me.transform.position; var eye = me.EyePosition; var rules = Game.Core.InteractRules.Current;
            float best = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                var b = i == 1 ? Game.Core.WaitingIsland.CallTop : Game.Core.WaitingIsland.CallBottom;
                var at = island.transform.TransformPoint(new Vector3(b.x, b.y, b.z));
                if (!Game.Core.Interact.Candidate(feet.x, feet.y, feet.z, eye.x, eye.z, look.x, look.z, at.x, at.y, at.z, rules, out float bearing)) continue;
                if (bearing < best) { best = bearing; top = i == 1; }
            }
            return best < float.MaxValue;
        }

        // Client: F at a call button. The host calls straight into its own server; a client asks it (the server checks the reach).
        public static bool Call(Combatant me, bool top)
        {
            if (me == null) return false;
            if (me.IsServerInitialized) { var room = WaitingRoom.Current; return room != null && room.ServerCall(me, top); }
            if (me.NetworkManager == null) return false;
            me.NetworkManager.ClientManager.Broadcast(new LiftCallRequest { Top = top });
            return true;
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
            _title = Line(root, font, "Title", 0f, 3.5f, 0.8f, new Color(0.85f, 0.9f, 1f));
            _time = Line(root, font, "Time", 0f, 0.7f, 2.0f, new Color(1f, 0.82f, 0.25f));
            _people = Line(root, font, "People", 0f, -2.0f, 0.75f, new Color(0.8f, 1f, 0.85f));
            _bungee = Line(root, font, "Bungee", 0f, -3.6f, 0.6f, new Color(1f, 0.6f, 0.4f));
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
            go.GetComponent<MeshRenderer>().sharedMaterial = BoardMaterial(font);
            return text;
        }

        // The font's own material (GUI/Text Shader) draws with ZTest Always, so the board text showed through the dome, the tower and the
        // cage; a material property does not change that, so the text gets a small depth-tested shader of its own (Resources/Shaders).
        static Material _boardMaterial;
        static Material BoardMaterial(Font font)
        {
            if (_boardMaterial == null)
            {
                var shader = Resources.Load<Shader>("Shaders/WaitingBoardText");
                _boardMaterial = shader != null ? new Material(shader) { mainTexture = font.material.mainTexture } : new Material(font.material);
                _boardMaterial.name = "WaitingBoardText";
            }
            return _boardMaterial;
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
