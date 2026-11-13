using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Full-screen map (key M): the baked picture, region names, elevators, passes, the local player's arrow and one
    // marker the player places by clicking. Exists only in scenes with a WorldInfo; while it exists UiFocus.MapAvailable
    // is true, so the map key does nothing in scenes without it. M or Esc closes it (PlayerController toggles the focus).
    public class MapScreen : MonoBehaviour
    {
        public const float MapPx = 960f;        // the square picture, in canvas reference pixels
        const int SortingOrder = 101;
        const int GridCells = 8;

        static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.78f);
        static readonly Color Frame = new Color(0.30f, 0.34f, 0.38f, 1f);
        static readonly Color Empty = new Color(0.09f, 0.11f, 0.13f, 1f);
        static readonly Color GridColor = new Color(1f, 1f, 1f, 0.07f);
        static readonly Color PassColor = new Color(0.92f, 0.92f, 0.86f, 1f);
        static readonly Color MarkerColor = new Color(0.40f, 0.85f, 0.95f, 1f);
        static readonly Color Dark = new Color(0.04f, 0.05f, 0.06f, 0.95f);
        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 BottomLeft = Vector2.zero;

        static Sprite _arrowSprite;

        public static MapScreen Instance { get; private set; }

        public bool IsOpen => _canvas != null && _canvas.gameObject.activeSelf;
        public float MapPixels => MapPx;
        // Positions inside the map picture, in canvas reference pixels from its south-west corner.
        public Vector2 PlayerArrowPixels => _arrow != null ? _arrow.anchoredPosition : Vector2.zero;
        public Vector2 MarkerPixels => _markerRect != null ? _markerRect.anchoredPosition : Vector2.zero;
        public bool MarkerVisible => _markerRect != null && _markerRect.gameObject.activeSelf;
        public string MarkerReadout => _markerReadout != null ? _markerReadout.text : "";

        bool _built;
        Canvas _canvas;
        RectTransform _map;
        RectTransform _arrow, _arrowLabel, _markerRect, _markerLabelRect;
        Text _markerLabel, _markerReadout, _positionReadout;
        int _lastDistance = -1, _lastCompass = -1, _lastCell = -1;

        void Awake()
        {
            var world = WorldInfo.Current;
            if (world == null) { Destroy(this); return; }   // no generated map in this scene: nothing to show
            Instance = this;
            Build(world);
            _canvas.gameObject.SetActive(false);
            _built = true;
        }

        void OnEnable()
        {
            if (!_built) return;
            UiFocus.MapAvailable = true;
            UiFocus.Changed += OnFocusChanged;
            OnFocusChanged();
        }

        void OnDisable()
        {
            if (!_built) return;
            UiFocus.MapAvailable = false;
            UiFocus.Changed -= OnFocusChanged;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            UiFocus.MapAvailable = false;
            MapMarker.Clear();
        }

        void OnFocusChanged()
        {
            if (_canvas == null) return;
            bool open = UiFocus.Current == UiScreen.Map;
            if (_canvas.gameObject.activeSelf == open) return;
            _canvas.gameObject.SetActive(open);
            if (open)
            {
                _lastDistance = _lastCompass = _lastCell = -1;
                Refresh();
            }
        }

        void Update()
        {
            if (!IsOpen) return;
            var me = Combatant.LocalPlayer;
            if (me != null && me.Dead.Value)
            {
                UiFocus.Close();
                return;
            }
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) Click(mouse.position.ReadValue(), false);
                else if (mouse.rightButton.wasPressedThisFrame) Click(mouse.position.ReadValue(), true);
            }
            Refresh();
        }

        // A click at a screen position: left places (or moves) the marker if the point is on the map, right clears it.
        // Returns true when the marker changed.
        public bool Click(Vector2 screenPosition, bool clear)
        {
            if (!IsOpen) return false;
            if (clear)
            {
                bool had = MapMarker.Has;
                MapMarker.Clear();
                Refresh();
                return had;
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_map, screenPosition, null, out var local)) return false;
            var uv = new Vector2(local.x / MapPx + 0.5f, local.y / MapPx + 0.5f);
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return false;
            var world = MapImage.UvToWorld(uv);
            var player = PlayerController.Local;
            world.y = player != null ? player.transform.position.y : 0f;
            MapMarker.Set(world);
            Refresh();
            return true;
        }

        // Screen position of a world point on the open map (what a click there would send to Click).
        public Vector2 WorldToScreen(Vector3 world)
        {
            var uv = MapImage.WorldToUv(world);
            var local = new Vector3((uv.x - 0.5f) * MapPx, (uv.y - 0.5f) * MapPx, 0f);
            return RectTransformUtility.WorldToScreenPoint(null, _map.TransformPoint(local));
        }

        // Moves the arrow and marker and updates the readouts; only text that changed is rebuilt.
        void Refresh()
        {
            var player = PlayerController.Local;
            if (player != null)
            {
                var position = player.transform.position;
                var at = PixelsOf(position);
                _arrow.anchoredPosition = at;
                _arrow.localRotation = Quaternion.Euler(0f, 0f, -player.ViewYaw);
                _arrowLabel.anchoredPosition = at + new Vector2(0f, -30f);
                MapProjection.GridCell(MapImage.WorldSize, GridCells, position.x, position.z, out int column, out int row);
                int cell = row * GridCells + column;
                if (cell != _lastCell)
                {
                    _lastCell = cell;
                    _positionReadout.text = "내 위치  " + MapProjection.ColumnLetter(column) + (row + 1);
                }
            }

            bool has = MapMarker.Has;
            if (_markerRect.gameObject.activeSelf != has)
            {
                _markerRect.gameObject.SetActive(has);
                _markerLabelRect.gameObject.SetActive(has);
                _lastDistance = _lastCompass = -1;
            }
            if (!has)
            {
                if (_markerReadout.text != "마커 없음") _markerReadout.text = "마커 없음";
                return;
            }
            var marker = MapMarker.Position;
            var pixels = PixelsOf(marker);
            _markerRect.anchoredPosition = pixels;
            _markerLabelRect.anchoredPosition = pixels + new Vector2(16f, 12f);
            if (player == null) return;
            int distance = Mathf.RoundToInt(MapMarker.DistanceFrom(player.transform.position));
            float bearing = MapMarker.BearingFrom(player.transform.position);
            int compass = Mathf.FloorToInt(bearing / 45f + 0.5f) % 8;
            if (distance == _lastDistance && compass == _lastCompass) return;
            _lastDistance = distance;
            _lastCompass = compass;
            _markerLabel.text = distance + " m";
            _markerReadout.text = "마커 " + distance + " m  ·  " + MapProjection.CompassName(bearing);
        }

        // Pixels from the map's south-west corner, kept inside the picture.
        static Vector2 PixelsOf(Vector3 world)
        {
            var uv = MapImage.WorldToUv(world);
            return new Vector2(Mathf.Clamp01(uv.x) * MapPx, Mathf.Clamp01(uv.y) * MapPx);
        }

        // ---- Building ----

        void Build(WorldInfo world)
        {
            _canvas = UiKit.Canvas("MapCanvas", SortingOrder, false);
            _canvas.transform.SetParent(transform, false);
            var root = _canvas.transform;
            UiKit.Box(UiKit.Fill("Backdrop", root), Backdrop);

            UiKit.Box(UiKit.Rect("Frame", root, Centre, Centre, Vector2.zero, new Vector2(MapPx + 8f, MapPx + 8f)), Frame);
            _map = UiKit.Rect("Map", root, Centre, Centre, Vector2.zero, new Vector2(MapPx, MapPx));
            var picture = _map.gameObject.AddComponent<RawImage>();
            var texture = MapImage.Get();
            picture.texture = texture;
            picture.color = texture != null ? Color.white : Empty;
            picture.raycastTarget = false;

            BuildGrid();
            BuildRegionNames(world);
            BuildPasses(world);
            BuildElevators(world);
            BuildMarker();
            BuildPlayer();
            BuildSidePanel(root);
        }

        void BuildGrid()
        {
            for (int i = 1; i < GridCells; i++)
            {
                float t = MapProjection.GridLine(i, GridCells);
                var v = UiKit.Rect("GridV" + i, _map, BottomLeft, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1.5f, 0f));
                v.anchorMin = new Vector2(t, 0f);
                v.anchorMax = new Vector2(t, 1f);
                UiKit.Box(v, GridColor);
                var h = UiKit.Rect("GridH" + i, _map, BottomLeft, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 1.5f));
                h.anchorMin = new Vector2(0f, t);
                h.anchorMax = new Vector2(1f, t);
                UiKit.Box(h, GridColor);
            }
            float cell = MapPx / GridCells;
            for (int i = 0; i < GridCells; i++)
            {
                var letter = UiKit.Rect("Col" + i, _map, TopLeft, new Vector2(0.5f, 1f), new Vector2((i + 0.5f) * cell, -3f), new Vector2(40f, 22f));
                UiKit.Label(letter, MapProjection.ColumnLetter(i), 16, UiKit.TextDim, TextAnchor.MiddleCenter);
                var number = UiKit.Rect("Row" + i, _map, TopLeft, new Vector2(0f, 0.5f), new Vector2(5f, -(i + 0.5f) * cell), new Vector2(30f, 22f));
                UiKit.Label(number, (i + 1).ToString(), 16, UiKit.TextDim, TextAnchor.MiddleLeft);
            }
        }

        void BuildRegionNames(WorldInfo world)
        {
            foreach (var region in world.Regions)
            {
                var rect = UiKit.Rect("Region " + region.Id, _map, BottomLeft, Centre, PixelsOf(region.Centre), new Vector2(240f, 40f));
                var label = UiKit.Label(rect, region.Name, 30, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                var shadow = rect.gameObject.AddComponent<Shadow>();
                shadow.effectColor = Dark;
                shadow.effectDistance = new Vector2(2f, -2f);
                label.color = new Color(0.96f, 0.96f, 0.92f, 0.92f);
            }
        }

        void BuildPasses(WorldInfo world)
        {
            foreach (var pass in world.Passes)
            {
                // A bar along the crossing direction, so it reads as the way through the ridge.
                var rect = UiKit.Rect("Pass " + pass.Name, _map, BottomLeft, Centre, PixelsOf(pass.Position), new Vector2(18f, 7f));
                rect.localRotation = Quaternion.Euler(0f, 0f, 90f - pass.YawDeg);
                UiKit.Box(UiKit.Fill("Edge", rect, -2f), Dark);
                UiKit.Box(UiKit.Fill("Fill", rect), PassColor);
            }
        }

        void BuildElevators(WorldInfo world)
        {
            foreach (var elevator in world.Elevators)
            {
                var rect = UiKit.Rect("Elevator " + elevator.Name, _map, BottomLeft, Centre, PixelsOf(elevator.Position), new Vector2(15f, 15f));
                rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                UiKit.Box(UiKit.Fill("Edge", rect, -3f), Dark);
                UiKit.Box(UiKit.Fill("Fill", rect), UiKit.Accent);
            }
        }

        void BuildMarker()
        {
            _markerRect = UiKit.Rect("Marker", _map, BottomLeft, Centre, Vector2.zero, new Vector2(17f, 17f));
            _markerRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            UiKit.Box(UiKit.Fill("Edge", _markerRect, -3f), Dark);
            UiKit.Box(UiKit.Fill("Fill", _markerRect), MarkerColor);
            _markerRect.gameObject.SetActive(false);

            _markerLabelRect = UiKit.Rect("MarkerLabel", _map, BottomLeft, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(120f, 24f));
            _markerLabel = UiKit.Label(_markerLabelRect, "", 20, MarkerColor, TextAnchor.MiddleLeft, FontStyle.Bold);
            var shadow = _markerLabelRect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Dark;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            _markerLabelRect.gameObject.SetActive(false);
        }

        void BuildPlayer()
        {
            _arrow = UiKit.Rect("PlayerArrow", _map, BottomLeft, Centre, Vector2.zero, new Vector2(34f, 34f));
            var image = _arrow.gameObject.AddComponent<Image>();
            image.sprite = ArrowSprite();
            image.color = Color.white;
            image.raycastTarget = false;

            _arrowLabel = UiKit.Rect("PlayerLabel", _map, BottomLeft, Centre, Vector2.zero, new Vector2(120f, 24f));
            UiKit.Label(_arrowLabel, "내 위치", 20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            var shadow = _arrowLabel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Dark;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        void BuildSidePanel(Transform root)
        {
            var side = UiKit.Rect("Side", root, Centre, new Vector2(1f, 0.5f), new Vector2(-MapPx * 0.5f - 24f, 0f), new Vector2(420f, MapPx));
            UiKit.Label(UiKit.Rect("Title", side, TopLeft, TopLeft, Vector2.zero, new Vector2(420f, 56f)), "지도", 44, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Box(UiKit.Rect("Rule", side, TopLeft, TopLeft, new Vector2(0f, -62f), new Vector2(420f, 2f)), UiKit.Accent);

            _positionReadout = UiKit.Label(UiKit.Rect("Position", side, TopLeft, TopLeft, new Vector2(0f, -84f), new Vector2(420f, 32f)), "내 위치", 24, UiKit.Text);
            _markerReadout = UiKit.Label(UiKit.Rect("MarkerReadout", side, TopLeft, TopLeft, new Vector2(0f, -122f), new Vector2(420f, 32f)), "마커 없음", 24, MarkerColor);

            string[] hints = { "클릭  마커 지정 / 이동", "우클릭  마커 해제", "M / Esc  닫기" };
            for (int i = 0; i < hints.Length; i++)
                UiKit.Label(UiKit.Rect("Hint" + i, side, TopLeft, TopLeft, new Vector2(0f, -190f - i * 30f), new Vector2(420f, 28f)), hints[i], 20, UiKit.TextDim);

            // Legend.
            float y = -310f;
            Legend(side, ref y, "엘리베이터", UiKit.Accent, 45f, new Vector2(15f, 15f));
            Legend(side, ref y, "통로", PassColor, 0f, new Vector2(18f, 7f));
            Legend(side, ref y, "마커", MarkerColor, 45f, new Vector2(15f, 15f));
        }

        static void Legend(RectTransform side, ref float y, string text, Color colour, float rotation, Vector2 size)
        {
            var swatch = UiKit.Rect("Swatch " + text, side, TopLeft, Centre, new Vector2(14f, y - 14f), size);
            swatch.localRotation = Quaternion.Euler(0f, 0f, rotation);
            UiKit.Box(UiKit.Fill("Edge", swatch, -2f), Dark);
            UiKit.Box(UiKit.Fill("Fill", swatch), colour);
            UiKit.Label(UiKit.Rect("Legend " + text, side, TopLeft, TopLeft, new Vector2(40f, y), new Vector2(360f, 28f)), text, 20, UiKit.Text);
            y -= 34f;
        }

        // A 64 x 64 arrow pointing up (+Y = north on the map), white so the Image colour tints it; made once in code.
        static Sprite ArrowSprite()
        {
            if (_arrowSprite != null) return _arrowSprite;
            const int n = 64;
            var tip = new Vector2(32f, 62f);
            var left = new Vector2(8f, 3f);
            var notch = new Vector2(32f, 18f);
            var right = new Vector2(56f, 3f);
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f);
                            if (InTriangle(p, tip, left, notch) || InTriangle(p, tip, notch, right)) hits++;
                        }
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 9));
                }
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "MapArrow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _arrowSprite = Sprite.Create(texture, new Rect(0, 0, n, n), Centre, 100f);
            return _arrowSprite;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f, positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
