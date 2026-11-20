using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The team's pings on the HUD (ADR-130): for every ping its owner's team put down and the local player was told,
    // an icon in the world (on the HUD canvas, so walls do not hide it) with its kind, the pinger's number and the
    // distance, a mark on the compass, the minimap and the full map. They fade over the last second and then go.
    // The local player's own "go" ping is not drawn here: it is the map marker, drawn by the compass/minimap/world marker.
    public sealed class TeamPingView
    {
        const float WorldSize = 24f;
        // One world marker's footprint (name over the diamond over the distance), for keeping close pings apart (#173).
        const float LabelWidth = 110f, LabelHeight = 80f;
        const float MapSize = 24f; // was 17: too small to find on the full map (#173)
        static readonly Color Rim = new Color(0.04f, 0.05f, 0.06f, 0.95f);

        sealed class Marker
        {
            public RectTransform World, Compass, Minimap, Map;
            public Text Glyph, Name, Distance;
            public Image[] Fills;
            public CanvasGroup[] Groups;
            public int ShownId = -1, ShownMetres = -1;
            public float ShownAlpha = -1f;
        }

        readonly List<Marker> _pool = new List<Marker>();
        readonly Transform _worldRoot;
        RectTransform _compass, _minimap, _map;
        int _used;
        float[] _xs = new float[8], _ys = new float[8];

        // Read-only state for tests.
        public int Visible => _used;
        public int WorldShown { get { int n = 0; for (int i = 0; i < _used; i++) if (_pool[i].World.gameObject.activeSelf) n++; return n; } }
        public int CompassShown { get { int n = 0; for (int i = 0; i < _used; i++) if (_pool[i].Compass != null && _pool[i].Compass.gameObject.activeSelf) n++; return n; } }
        public int MinimapShown { get { int n = 0; for (int i = 0; i < _used; i++) if (_pool[i].Minimap != null && _pool[i].Minimap.gameObject.activeSelf) n++; return n; } }
        public int MapShown { get { int n = 0; for (int i = 0; i < _used; i++) if (_pool[i].Map != null && _pool[i].Map.gameObject.activeSelf) n++; return n; } }
        public string WorldText(int i) => i >= 0 && i < _used && _pool[i].World.gameObject.activeSelf ? _pool[i].Name.text + " " + _pool[i].Distance.text : "";
        public Vector2 WorldPosition(int i) => i >= 0 && i < _used ? _pool[i].World.anchoredPosition : Vector2.zero;

        public TeamPingView(Transform worldRoot) => _worldRoot = worldRoot;

        public static Color ColourOf(PingKind kind)
        {
            switch (kind)
            {
                case PingKind.Go: return new Color(0.30f, 0.85f, 0.95f, 1f);
                case PingKind.Here: return new Color(0.96f, 0.96f, 0.82f, 1f);
                case PingKind.Enemy: return new Color(0.95f, 0.25f, 0.22f, 1f);
                case PingKind.Loot: return new Color(0.98f, 0.80f, 0.22f, 1f);
                case PingKind.Danger: return new Color(0.98f, 0.55f, 0.12f, 1f);
                default: return new Color(0.40f, 0.85f, 0.45f, 1f);
            }
        }

        static string GlyphOf(PingKind kind)
        {
            switch (kind)
            {
                case PingKind.Go: return "이";
                case PingKind.Here: return "여";
                case PingKind.Enemy: return "적";
                case PingKind.Loot: return "물";
                case PingKind.Danger: return "!";
                default: return "방";
            }
        }

        // Hangs the marks on the compass, minimap and map once they exist.
        public void SetHosts(RectTransform compass, RectTransform minimap, RectTransform map)
        {
            _compass = compass;
            _minimap = minimap;
            _map = map;
            for (int i = 0; i < _pool.Count; i++) AttachHosted(_pool[i]);
        }

        public void Tick(Combatant me, Camera cam, float canvasScale, float heading, Vector3 from, bool hud, float now)
        {
            var rules = TeamRules.Current;
            var board = me.PingsHeard;
            board.Prune(now, rules);
            int k = 0;
            for (int i = 0; i < board.Count; i++)
            {
                var e = board.Active[i];
                if (e.Owner == me.ObjectId && e.Kind == PingKind.Go) continue;
                while (_pool.Count <= k) _pool.Add(Build(_pool.Count));
                Update(_pool[k], e, cam, canvasScale, heading, from, hud, PingBoard.Alpha(e, now, rules));
                k++;
            }
            _used = k;
            for (int i = k; i < _pool.Count; i++) Hide(_pool[i]);
            SpreadWorldLabels();
        }

        void SpreadWorldLabels()
        {
            if (_xs.Length < _used) { _xs = new float[_used * 2]; _ys = new float[_used * 2]; }
            int n = 0;
            for (int i = 0; i < _used; i++)
                if (_pool[i].World.gameObject.activeSelf) { var a = _pool[i].World.anchoredPosition; _xs[n] = a.x; _ys[n] = a.y; n++; }
            if (n < 2) return;
            Pings.Spread(_xs, _ys, n, LabelWidth, LabelHeight);
            n = 0;
            for (int i = 0; i < _used; i++)
                if (_pool[i].World.gameObject.activeSelf) { _pool[i].World.anchoredPosition = new Vector2(_xs[n], _ys[n]); n++; }
        }

        public void HideAll()
        {
            _used = 0;
            for (int i = 0; i < _pool.Count; i++) Hide(_pool[i]);
        }

        void Update(Marker m, in PingEntry e, Camera cam, float scale, float heading, Vector3 from, bool hud, float alpha)
        {
            var colour = ColourOf(e.Kind);
            if (m.ShownId != e.Id)
            {
                m.ShownId = e.Id;
                m.ShownMetres = -1;
                m.Glyph.text = GlyphOf(e.Kind);
                m.Name.text = HudDigits.IntText(e.Slot) + " " + Pings.Name(e.Kind);
                m.Name.color = colour;
                m.Distance.color = colour;
                foreach (var fill in m.Fills) if (fill != null) fill.color = colour;
            }
            if (Mathf.Abs(alpha - m.ShownAlpha) > 0.01f)
            {
                m.ShownAlpha = alpha;
                foreach (var g in m.Groups) if (g != null) g.alpha = alpha;
            }
            var at = new Vector3(e.X, e.Y, e.Z);
            int metres = Mathf.RoundToInt(MapProjection.Distance(from.x, from.z, at.x, at.z));
            if (metres != m.ShownMetres)
            {
                m.ShownMetres = metres;
                m.Distance.text = MapMarkerText.Metres(metres);
            }

            Vector2 screen = default;
            bool world = hud && cam != null && TeamHud.OnScreen(cam, at + Vector3.up * WorldMarkerView.LiftMeters, scale, out screen);
            Show(m.World, world);
            if (world) m.World.anchoredPosition = screen;
            if (m.Compass != null)
            {
                Show(m.Compass, true);
                m.Compass.anchoredPosition = new Vector2(CompassStrip.MarkerOffset(heading, Minimap.Bearing(from, at), out _), 12f);
            }
            if (m.Minimap != null)
            {
                Show(m.Minimap, true);
                m.Minimap.anchoredPosition = TeamHud.ClampToMinimap(Minimap.ToView(at, from));
            }
            if (m.Map != null)
            {
                Show(m.Map, true);
                m.Map.anchoredPosition = TeamHud.MapPixels(at);
            }
        }

        static void Hide(Marker m)
        {
            m.ShownId = -1;
            Show(m.World, false);
            Show(m.Compass, false);
            Show(m.Minimap, false);
            Show(m.Map, false);
        }

        static void Show(RectTransform r, bool on)
        {
            if (r != null && r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
        }

        Marker Build(int index)
        {
            var m = new Marker { Fills = new Image[4], Groups = new CanvasGroup[4] };
            var centre = new Vector2(0.5f, 0.5f);
            // In the world: a lit diamond holding the kind's letter, the pinger and kind above it, the distance under it.
            m.World = UiKit.Rect("TeamPing" + index, _worldRoot, centre, centre, Vector2.zero, new Vector2(WorldSize, WorldSize));
            var diamond = UiKit.Rect("Diamond", m.World, centre, centre, Vector2.zero, new Vector2(WorldSize, WorldSize));
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            UiKit.Box(UiKit.Fill("Rim", diamond, -2f), Rim);
            m.Fills[0] = UiKit.Box(UiKit.Fill("Fill", diamond), Color.white);
            m.Glyph = UiKit.Label(UiKit.Rect("Glyph", m.World, centre, centre, Vector2.zero, new Vector2(WorldSize, WorldSize)), "", 16, Rim, TextAnchor.MiddleCenter, FontStyle.Bold);
            m.Name = UiKit.Label(UiKit.Rect("Name", m.World, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(140f, 22f)), "", 17, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            m.Distance = UiKit.Label(UiKit.Rect("Distance", m.World, centre, new Vector2(0.5f, 1f), new Vector2(0f, -WorldSize * 0.5f - 6f), new Vector2(100f, 22f)), "", 17, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            TeamHud.Shadowed(m.Name);
            TeamHud.Shadowed(m.Distance);
            m.Groups[0] = m.World.gameObject.AddComponent<CanvasGroup>();
            m.World.gameObject.SetActive(false);
            AttachHosted(m);
            return m;
        }

        void AttachHosted(Marker m)
        {
            if (_compass != null && m.Compass == null) m.Compass = HostMark("TeamPing", _compass, 10f, m, 1, null);
            if (_minimap != null && m.Minimap == null) m.Minimap = HostMark("TeamPing", _minimap, 9f, m, 2, null);
            if (_map != null && m.Map == null) m.Map = HostMark("TeamPing", _map, MapSize, m, 3, Vector2.zero);
            m.ShownAlpha = -1f;
            m.ShownId = -1;
        }

        static RectTransform HostMark(string name, Transform parent, float size, Marker m, int slot, Vector2? anchor)
        {
            var a = anchor ?? new Vector2(0.5f, 0.5f);
            var rect = UiKit.Rect(name, parent, a, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var diamond = UiKit.Rect("Diamond", rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            UiKit.Box(UiKit.Fill("Rim", diamond, -1.5f), Rim);
            m.Fills[slot] = UiKit.Box(UiKit.Fill("Fill", diamond), Color.white);
            m.Groups[slot] = rect.gameObject.AddComponent<CanvasGroup>();
            rect.gameObject.SetActive(false);
            return rect;
        }

        public void Destroy()
        {
            for (int i = 0; i < _pool.Count; i++)
                foreach (var r in new[] { _pool[i].World, _pool[i].Compass, _pool[i].Minimap, _pool[i].Map })
                    if (r != null) Object.Destroy(r.gameObject);
            _pool.Clear();
            _used = 0;
        }
    }
}
