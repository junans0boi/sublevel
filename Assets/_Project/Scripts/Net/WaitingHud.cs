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
                Dress(island.transform);
            }
            var phase = live ? (Game.Core.LiftPhase)_last.LiftPhase : Game.Core.LiftPhase.OpenBottom;
            float into = live ? _last.LiftInto + (_preview ? 0f : Time.unscaledTime - _at) : 0f;
            float h = Game.Core.BungeeLift.HeightOf(phase, into);
            var p = _car.localPosition; p.y = h; _car.localPosition = p;
            if (_carDoors != null) _carDoors.gameObject.SetActive(!Game.Core.BungeeLift.DoorsOpenOf(phase, into));
            if (_bottom == null) { _bottom = new CallButton(_car.parent, false); _top = new CallButton(_car.parent, true); }
            bool moving = !Game.Core.BungeeLift.DoorsOpenOf(phase, into);
            string floor = (h > Game.Core.BungeeLift.Height * 0.5f ? "2" : "1") + (!moving ? "" : phase == Game.Core.LiftPhase.Rising ? "▲" : "▼");
            _bottom.Show(live && (_last.LiftCalls & 1) != 0, floor);
            _top.Show(live && (_last.LiftCalls & 2) != 0, floor);
        }

        static Material Look(string name) => Resources.Load<Material>("WaitingLook/" + name);

        // The island's own materials (ADR-491) on the hall, the tower and the lift car. shortcut: the World scene baked before ADR-491
        // still carries the town's materials, so they are swapped here by name; drop this once World.unity is rebuilt (the builder uses them).
        static void Dress(Transform island)
        {
            Material steel = Look("WaitingSteel"), panel = Look("WaitingPanel"), dome = Look("WaitingDome"), stainless = Look("WaitingStainless"), tread = Look("WaitingTread"), accent = Look("WaitingAccent");
            if (steel == null) return;
            foreach (var r in island.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name; bool inCar = r.transform.parent != island;
                var m = !inCar && (n.StartsWith("Frame") || n == "Trim" || n == "BoardBack") ? steel : !inCar && n.StartsWith("Wall") ? panel : !inCar && n.StartsWith("Dome") ? dome : !inCar && n == "Band" ? accent
                    : inCar && (n == "Bars" || n == "Gates") ? stainless : inCar && n == "Floor" ? tread : null;
                if (m != null) r.sharedMaterial = m;
            }
        }

        // A call panel (ADR-490/491), built at runtime from WaitingIsland's numbers so the World scene need not be rebuilt: a brushed
        // steel faceplate (on a slim graphite pillar at the bottom, on the rail at the top) with a small dark window showing the car's
        // floor and one round push button with its arrow (▲ at the bottom, ▼ at the top) that glows while the call stands.
        sealed class CallButton
        {
            readonly Renderer _button; readonly Light _glow; readonly TextMesh _floor, _arrow;
            static readonly Color Off = new Color(0.78f, 0.80f, 0.82f), On = new Color(1f, 0.78f, 0.35f);
            static readonly Color ArrowOff = new Color(0.25f, 0.27f, 0.30f), ArrowOn = new Color(0.45f, 0.22f, 0.02f);

            public CallButton(Transform island, bool top)
            {
                var b = top ? Game.Core.WaitingIsland.CallTop : Game.Core.WaitingIsland.CallBottom;
                var root = new GameObject(top ? "LiftCallTop" : "LiftCallBottom").transform;
                root.SetParent(island, false);
                root.localPosition = new Vector3(b.x, b.y, b.z);
                root.localRotation = Quaternion.Euler(0f, top ? 180f : 0f, 0f);   // the face (+Z local) towards whoever presses it
                Material steel = Look("WaitingSteel"), stainless = Look("WaitingStainless");
                if (!top) Part(root, PrimitiveType.Cube, "Pillar", new Vector3(0f, -b.y * 0.5f + 0.3f, -0.05f), new Vector3(0.28f, b.y + 0.6f, 0.08f), steel);
                Part(root, PrimitiveType.Cube, "Plate", Vector3.zero, new Vector3(0.22f, 0.40f, 0.02f), stainless);
                Part(root, PrimitiveType.Cube, "Window", new Vector3(0f, 0.12f, 0.012f), new Vector3(0.15f, 0.08f, 0.006f), null).sharedMaterial.color = new Color(0.03f, 0.03f, 0.035f);
                Part(root, PrimitiveType.Cylinder, "Bezel", new Vector3(0f, -0.05f, 0.012f), new Vector3(0.13f, 0.004f, 0.13f), null).sharedMaterial.color = new Color(0.08f, 0.08f, 0.09f);
                _button = Part(root, PrimitiveType.Cylinder, "Button", new Vector3(0f, -0.05f, 0.02f), new Vector3(0.105f, 0.008f, 0.105f), null);
                _button.sharedMaterial.color = Off; _button.sharedMaterial.SetFloat("_Smoothness", 0.7f);
                var g = new GameObject("Glow"); g.transform.SetParent(root, false); g.transform.localPosition = new Vector3(0f, -0.05f, 0.2f);
                _glow = g.AddComponent<Light>(); _glow.type = LightType.Point; _glow.range = 1.2f; _glow.intensity = 1.5f; _glow.color = On; _glow.shadows = LightShadows.None; _glow.enabled = false;
                _floor = Label(root, new Vector3(0f, 0.12f, 0.016f), 0.016f, new Color(1f, 0.6f, 0.2f));
                _arrow = Label(root, new Vector3(0f, -0.05f, 0.029f), 0.014f, ArrowOff);
                _arrow.text = top ? "▼" : "▲";
            }

            static Renderer Part(Transform parent, PrimitiveType shape, string name, Vector3 at, Vector3 size, Material m)
            {
                var go = GameObject.CreatePrimitive(shape);
                go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());   // looks only: the server judges the reach
                go.transform.SetParent(parent, false); go.transform.localPosition = at; go.transform.localScale = size;
                if (shape == PrimitiveType.Cylinder) go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // its axis out of the plate
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = m != null ? m : new Material(r.sharedMaterial);   // a shared look, or its own copy to colour
                return r;
            }

            static TextMesh Label(Transform parent, Vector3 at, float size, Color c)
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var t = new GameObject("Label"); t.transform.SetParent(parent, false);
                t.transform.localPosition = at; t.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var text = t.AddComponent<TextMesh>();
                text.font = font; text.fontSize = 64; text.characterSize = size; text.anchor = TextAnchor.MiddleCenter; text.color = c;
                t.GetComponent<MeshRenderer>().sharedMaterial = BoardMaterial(font);
                return text;
            }

            bool? _lit;
            public void Show(bool lit, string floor)
            {
                if (_floor.text != floor) _floor.text = floor;
                if (_lit == lit) return;
                _lit = lit;
                var m = _button.sharedMaterial;
                m.color = lit ? On : Off;
                if (lit) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");   // URP Lit: the button glows, not just tints
                m.SetColor("_EmissionColor", lit ? On * 1.6f : Color.black);
                _arrow.color = lit ? ArrowOn : ArrowOff;
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
