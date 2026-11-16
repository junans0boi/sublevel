using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Small runtime-made sprites for the HUD (no texture assets).
    internal static class HudSprites
    {
        static Texture2D _arrowTexture;
        static Sprite _arrow;

        // A chevron pointing up, white (tint it with the Image colour).
        public static Sprite Arrow
        {
            get
            {
                if (_arrow != null) return _arrow;
                const int n = 32;
                _arrowTexture = new Texture2D(n, n, TextureFormat.RGBA32, false);
                _arrowTexture.hideFlags = HideFlags.HideAndDontSave;
                _arrowTexture.wrapMode = TextureWrapMode.Clamp;
                float half = n * 0.5f - 1f;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float dx = Mathf.Abs(x + 0.5f - n * 0.5f);
                        // Wide at the bottom, a point at the top, with a notch cut into the base.
                        float reach = half * (1f - y / (n - 1f));
                        float notch = 0.34f * n * (1f - dx / half);
                        bool on = dx <= reach && y >= notch;
                        _arrowTexture.SetPixel(x, y, on ? Color.white : new Color(1f, 1f, 1f, 0f));
                    }
                }
                _arrowTexture.Apply();
                _arrow = Sprite.Create(_arrowTexture, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                _arrow.hideFlags = HideFlags.HideAndDontSave;
                return _arrow;
            }
        }
    }

    // The minimap (bottom right): a square window onto the island, north up, centred on the player, with a
    // rotating player arrow, region borders and names, passes and elevator stations from WorldInfo.Current.
    // The map picture comes from HudBackend.MapImage(); without one a faint grid is drawn instead.
    // Positions are rewritten only after the player has moved a little, and the marker pools are built once per map.
    public sealed class MinimapView
    {
        const float GridMeters = 100f;
        const int GridLines = 5;                 // ViewMeters / GridMeters + 2 spare
        const float MoveEpsilon = 0.25f;         // metres before the content is placed again
        const float ArrowSize = 22f;

        public RectTransform Root { get; private set; }
        // The heading the arrow shows (degrees, 0 = north) and its actual z rotation; for tests.
        public float ArrowHeading { get; private set; } = float.NaN;
        public float ArrowRotationZ => _arrow != null ? _arrow.localEulerAngles.z : 0f;
        public bool HasMapImage => _mapRect != null && _mapRect.gameObject.activeSelf;
        public bool GridShown => _gridRoot != null && _gridRoot.activeSelf;
        public int ElevatorMarkers => _elevators.Count;
        public int BorderLines => _borders.Count;
        public string RegionName { get; private set; } = "";
        // The map marker (ADR-065 (6)): shown or not, where its diamond sits from the view centre (held just inside
        // the edge when the marker is farther away) and the length of the line to it.
        public bool MarkerShown => _markerRoot != null && _markerRoot.activeSelf;
        public Vector2 MarkerViewPosition => _markerDot != null ? _markerDot.anchoredPosition : Vector2.zero;
        public float MarkerLineLength => _markerLine != null ? _markerLine.sizeDelta.x : 0f;

        const float MarkerEdge = Minimap.ViewSize * 0.5f - 7f;

        RectTransform _content, _arrow, _mapRect, _zoneRect;
        RawImage _zoneImage;
        Texture2D _zoneTexture;
        int[] _zoneCell;       // region index per overlay texel
        ZoneState[] _zoneShown = new ZoneState[0];
        // What the overlay shows, for tests: the state painted for a region, and the tag under its name.
        public ZoneState ShownState(int region) => region >= 0 && region < _zoneShown.Length ? _zoneShown[region] : ZoneState.Safe;
        public Texture2D ZoneTexture => _zoneTexture;
        public string NameLabelOf(int region) => region >= 0 && region < _nameText.Count ? _nameText[region].text : "";
        readonly List<Text> _nameText = new List<Text>();
        GameObject _markerRoot;
        RectTransform _markerDot, _markerLine;
        Image _markerDotFill, _markerLineImage;
        int _markerVersion = -1;
        RawImage _mapImage;
        GameObject _gridRoot;
        Text _regionLabel;
        readonly RectTransform[] _gridV = new RectTransform[GridLines];
        readonly RectTransform[] _gridH = new RectTransform[GridLines];

        WorldInfo _world;
        GameObject _worldRoot;
        readonly List<RectTransform> _elevators = new List<RectTransform>();
        readonly List<Vector3> _elevatorPos = new List<Vector3>();
        readonly List<RectTransform> _borders = new List<RectTransform>();
        readonly List<Vector3> _borderPos = new List<Vector3>();
        readonly List<RectTransform> _passMarks = new List<RectTransform>();
        readonly List<Vector3> _passPos = new List<Vector3>();
        readonly List<RectTransform> _names = new List<RectTransform>();
        readonly List<Vector3> _namePos = new List<Vector3>();
        Vector3 _placedAt = new Vector3(float.NaN, 0f, 0f);
        int _shownRegion = -2;
        Texture _shownTexture;

        public static MinimapView Build(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position)
        {
            var v = new MinimapView();
            var root = UiKit.Rect("Minimap", parent, anchor, pivot, position, new Vector2(Minimap.ViewSize, Minimap.ViewSize));
            UiKit.Box(root, new Color(0.05f, 0.06f, 0.07f, 0.86f));
            v.Root = root;

            var clip = UiKit.Fill("Clip", root);
            clip.gameObject.AddComponent<RectMask2D>();
            var centre = new Vector2(0.5f, 0.5f);
            v._content = UiKit.Rect("Content", clip, centre, centre, Vector2.zero, Vector2.zero);

            v._mapRect = UiKit.Rect("MapImage", v._content, centre, centre, Vector2.zero, Vector2.zero);
            v._mapImage = v._mapRect.gameObject.AddComponent<RawImage>();
            v._mapImage.raycastTarget = false;
            v._mapImage.color = new Color(1f, 1f, 1f, 0.9f);
            v._mapRect.gameObject.SetActive(false);

            // Region states: a picture over the map, painted when a region changes state.
            v._zoneRect = UiKit.Rect("ZoneStates", v._content, centre, centre, Vector2.zero, Vector2.zero);
            v._zoneImage = v._zoneRect.gameObject.AddComponent<RawImage>();
            v._zoneImage.raycastTarget = false;
            v._zoneRect.gameObject.SetActive(false);

            var gridColor = new Color(UiKit.Steel.r, UiKit.Steel.g, UiKit.Steel.b, 0.16f);
            v._gridRoot = UiKit.Rect("Grid", v._content, centre, centre, Vector2.zero, Vector2.zero).gameObject;
            for (int i = 0; i < GridLines; i++)
            {
                v._gridV[i] = UiKit.Rect("V" + i, v._gridRoot.transform, centre, centre, Vector2.zero, new Vector2(1f, Minimap.ViewSize + 4f));
                UiKit.Box(v._gridV[i], gridColor);
                v._gridH[i] = UiKit.Rect("H" + i, v._gridRoot.transform, centre, centre, Vector2.zero, new Vector2(Minimap.ViewSize + 4f, 1f));
                UiKit.Box(v._gridH[i], gridColor);
            }

            // The map marker and a thin line to it from the player, over the map and under the player arrow.
            v._markerRoot = UiKit.Rect("MapMarker", clip, centre, centre, Vector2.zero, Vector2.zero).gameObject;
            v._markerLine = UiKit.Rect("Line", v._markerRoot.transform, centre, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 1.5f));
            v._markerLineImage = UiKit.Box(v._markerLine, MapMarker.Colour);
            v._markerDot = UiKit.Rect("Diamond", v._markerRoot.transform, centre, centre, Vector2.zero, new Vector2(9f, 9f));
            v._markerDot.localRotation = Quaternion.Euler(0f, 0f, 45f);
            UiKit.Box(UiKit.Fill("Rim", v._markerDot, -1.5f), new Color(0.04f, 0.05f, 0.06f, 0.95f));
            v._markerDotFill = UiKit.Box(UiKit.Fill("Fill", v._markerDot), MapMarker.Colour);
            v._markerRoot.SetActive(false);

            // Above the world markers, below the frame: the player.
            v._arrow = UiKit.Rect("Player", root, centre, centre, Vector2.zero, new Vector2(ArrowSize, ArrowSize));
            var arrowImage = UiKit.Box(v._arrow, UiKit.Accent);
            arrowImage.sprite = HudSprites.Arrow;

            UiKit.Label(UiKit.Rect("North", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(30f, 22f)), "북", 18, UiKit.TextDim,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            v._regionLabel = UiKit.Label(UiKit.Rect("Region", root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(10f, 6f), new Vector2(180f, 24f)), "", 18, UiKit.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);

            UiKit.Frame(root, 1f, UiKit.SteelDim);
            UiKit.Brackets(root, 16f, 2f, UiKit.Steel);
            return v;
        }

        public void Tick(Vector3 playerPosition, float yaw, bool hasYaw)
        {
            EnsureWorld(WorldInfo.Current);

            if (hasYaw && yaw != ArrowHeading)
            {
                ArrowHeading = Mathf.Repeat(yaw, 360f);
                _arrow.localRotation = Quaternion.Euler(0f, 0f, Minimap.ArrowRotationZ(ArrowHeading));
            }

            ShowRegionName(playerPosition);
            RefreshZones();

            var flat = new Vector3(playerPosition.x, 0f, playerPosition.z);
            var texture = HudBackend.MapImage();
            if (!ReferenceEquals(texture, _shownTexture))
            {
                _shownTexture = texture;
                _placedAt = new Vector3(float.NaN, 0f, 0f);
                _mapImage.texture = texture;
                _mapRect.gameObject.SetActive(texture != null);
                _gridRoot.SetActive(texture == null);
            }
            bool markerChanged = MapMarker.Version != _markerVersion;
            if (markerChanged) _markerVersion = MapMarker.Version;
            if (!float.IsNaN(_placedAt.x) && (flat - _placedAt).sqrMagnitude < MoveEpsilon * MoveEpsilon)
            {
                if (markerChanged) PlaceMarker(_placedAt);
                return;
            }
            _placedAt = flat;
            PlaceContent(flat);
            PlaceMarker(flat);
        }

        // The marker's diamond relative to the player (held just inside the square's edge, along its direction, when
        // it is out of view) and the line from the centre to it.
        void PlaceMarker(Vector3 centre)
        {
            bool has = MapMarker.Has;
            if (_markerRoot.activeSelf != has) _markerRoot.SetActive(has);
            if (!has) return;
            var colour = MapMarker.Colour;
            if (_markerDotFill.color != colour)
            {
                _markerDotFill.color = colour;
                _markerLineImage.color = colour;
            }
            var at = Minimap.ToView(MapMarker.Position, centre);
            float reach = Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.y));
            if (reach > MarkerEdge) at *= MarkerEdge / reach;
            _markerDot.anchoredPosition = at;
            _markerLine.sizeDelta = new Vector2(at.magnitude, 1.5f);
            _markerLine.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(at.y, at.x) * Mathf.Rad2Deg);
        }

        void PlaceContent(Vector3 centre)
        {
            if (_gridRoot.activeSelf)
            {
                float firstX = Mathf.Floor((centre.x - Minimap.ViewMeters * 0.5f) / GridMeters) * GridMeters;
                float firstZ = Mathf.Floor((centre.z - Minimap.ViewMeters * 0.5f) / GridMeters) * GridMeters;
                for (int i = 0; i < GridLines; i++)
                {
                    _gridV[i].anchoredPosition = new Vector2((firstX + i * GridMeters - centre.x) * Minimap.PixelsPerMeter, 0f);
                    _gridH[i].anchoredPosition = new Vector2(0f, (firstZ + i * GridMeters - centre.z) * Minimap.PixelsPerMeter);
                }
            }
            if (_zoneRect.gameObject.activeSelf && _world != null && _world.Size > 0f)
            {
                float size = _world.Size;
                _zoneRect.sizeDelta = new Vector2(size * Minimap.PixelsPerMeter, size * Minimap.PixelsPerMeter);
                _zoneRect.anchoredPosition = Minimap.ToView(new Vector3(size * 0.5f, 0f, size * 0.5f), centre);
            }
            if (_mapRect.gameObject.activeSelf && _world != null && _world.Size > 0f)
            {
                float size = _world.Size;
                _mapRect.sizeDelta = new Vector2(size * Minimap.PixelsPerMeter, size * Minimap.PixelsPerMeter);
                _mapRect.anchoredPosition = Minimap.ToView(new Vector3(size * 0.5f, 0f, size * 0.5f), centre);
            }
            for (int i = 0; i < _elevators.Count; i++) _elevators[i].anchoredPosition = Minimap.ToView(_elevatorPos[i], centre);
            for (int i = 0; i < _borders.Count; i++) _borders[i].anchoredPosition = Minimap.ToView(_borderPos[i], centre);
            for (int i = 0; i < _passMarks.Count; i++) _passMarks[i].anchoredPosition = Minimap.ToView(_passPos[i], centre);
            for (int i = 0; i < _names.Count; i++) _names[i].anchoredPosition = Minimap.ToView(_namePos[i], centre);
        }

        void ShowRegionName(Vector3 position)
        {
            int index = _world != null ? Minimap.NearestRegion(_world.Regions, position) : -1;
            if (index == _shownRegion) return;
            _shownRegion = index;
            RegionName = index >= 0 ? _world.Regions[index].Name ?? "" : "";
            _regionLabel.text = RegionName;
        }

        // Repaints the region states when any of them changed (the director tells clients only when one does).
        void RefreshZones()
        {
            if (_world == null || _zoneCell == null) return;
            var director = PollutionDirector.Instance;
            bool changed = false;
            for (int i = 0; i < _zoneShown.Length; i++)
            {
                var now = director != null ? director.StateOf(i) : ZoneState.Safe;
                if (now == _zoneShown[i]) continue;
                _zoneShown[i] = now;
                changed = true;
            }
            if (!changed) return;
            int n = Minimap.ZoneRes;
            var pixels = _zoneTexture.GetRawTextureData<Color32>();
            bool any = false;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int r = _zoneCell[y * n + x];
                    var state = r >= 0 ? _zoneShown[r] : ZoneState.Safe;
                    bool edge = state != ZoneState.Safe && (Cell(x - 1, y) != r || Cell(x + 1, y) != r || Cell(x, y - 1) != r || Cell(x, y + 1) != r);
                    pixels[y * n + x] = Minimap.ZoneTexel(state, edge, x, y);
                    any |= state != ZoneState.Safe;
                }
            _zoneTexture.Apply();
            _zoneRect.gameObject.SetActive(any);
            _placedAt = new Vector3(float.NaN, 0f, 0f); // place it
            for (int i = 0; i < _nameText.Count; i++)
            {
                string tag = Minimap.ZoneTag(_zoneShown[i]);
                string name = _world.Regions[i].Name ?? "";
                _nameText[i].text = tag.Length == 0 ? name : name + "\n<size=14><b>" + tag + "</b></size>";
                var zc = Minimap.ZoneColour(_zoneShown[i]);
                _nameText[i].color = tag.Length == 0 ? new Color(UiKit.TextDim.r, UiKit.TextDim.g, UiKit.TextDim.b, 0.8f) : new Color(zc.r, zc.g, zc.b, 1f);
            }
        }

        int Cell(int x, int y)
        {
            int n = Minimap.ZoneRes;
            return x < 0 || y < 0 || x >= n || y >= n ? -1 : _zoneCell[y * n + x];
        }

        // Builds the marker pools when a different map shows up (once per scene load; not per frame).
        void EnsureWorld(WorldInfo world)
        {
            if (ReferenceEquals(world, _world) && (world == null || _worldRoot != null)) return;
            if (_worldRoot != null) Object.Destroy(_worldRoot);
            _worldRoot = null;
            _elevators.Clear(); _elevatorPos.Clear();
            _borders.Clear(); _borderPos.Clear();
            _passMarks.Clear(); _passPos.Clear();
            _names.Clear(); _namePos.Clear(); _nameText.Clear();
            if (_zoneTexture != null) Object.Destroy(_zoneTexture);
            _zoneTexture = null; _zoneCell = null; _zoneShown = new ZoneState[0];
            _zoneRect.gameObject.SetActive(false);
            _world = world;
            _shownRegion = -2;
            _placedAt = new Vector3(float.NaN, 0f, 0f);
            if (world == null) return;

            var centre = new Vector2(0.5f, 0.5f);
            _worldRoot = UiKit.Rect("World", _content, centre, centre, Vector2.zero, Vector2.zero).gameObject;
            var root = _worldRoot.transform;
            var borderColor = new Color(UiKit.Steel.r, UiKit.Steel.g, UiKit.Steel.b, 0.65f);
            var regions = world.Regions;
            var passes = world.Passes;

            // One border line per neighbouring pair (two regions that share a pass).
            var seen = new List<long>();
            for (int i = 0; i < passes.Count; i++)
            {
                int a = Mathf.Min(passes[i].RegionA, passes[i].RegionB), b = Mathf.Max(passes[i].RegionA, passes[i].RegionB);
                if (a < 0 || b >= regions.Count || a == b) continue;
                long key = a * 1000L + b;
                if (seen.Contains(key)) continue;
                seen.Add(key);
                var line = UiKit.Rect("Border" + a + b, root, centre, centre, Vector2.zero, new Vector2(Minimap.ViewMeters * 4f, 1.5f));
                UiKit.Box(line, borderColor);
                line.localRotation = Quaternion.Euler(0f, 0f, Minimap.BorderAngleDeg(regions[a].Centre, regions[b].Centre));
                _borders.Add(line);
                _borderPos.Add(Minimap.BorderPoint(regions[a].Centre, regions[b].Centre));
            }
            for (int i = 0; i < passes.Count; i++)
            {
                // A short bar across the road where it leaves a region.
                var mark = UiKit.Rect("Pass" + i, root, centre, centre, Vector2.zero, new Vector2(14f, 3f));
                UiKit.Box(mark, UiKit.Text);
                mark.localRotation = Quaternion.Euler(0f, 0f, -passes[i].YawDeg);
                _passMarks.Add(mark);
                _passPos.Add(passes[i].Position);
            }
            for (int i = 0; i < regions.Count; i++)
            {
                var label = UiKit.Rect("Name" + i, root, centre, centre, Vector2.zero, new Vector2(120f, 24f));
                var text = UiKit.Label(label, regions[i].Name ?? "", 18, UiKit.TextDim, TextAnchor.MiddleCenter);
                text.color = new Color(UiKit.TextDim.r, UiKit.TextDim.g, UiKit.TextDim.b, 0.8f);
                label.sizeDelta = new Vector2(120f, 44f);
                _names.Add(label);
                _nameText.Add(text);
                _namePos.Add(regions[i].Centre);
            }
            BuildZoneCells(world);
            var elevators = world.Elevators;
            for (int i = 0; i < elevators.Count; i++)
            {
                // A diamond: a square turned 45 degrees.
                var marker = UiKit.Rect("Elevator" + i, root, centre, centre, Vector2.zero, new Vector2(8f, 8f));
                UiKit.Box(marker, UiKit.Text);
                marker.localRotation = Quaternion.Euler(0f, 0f, 45f);
                _elevators.Add(marker);
                _elevatorPos.Add(elevators[i].Position);
            }
        }

        // Which region each overlay texel belongs to (the nearest region centre, like ZoneGeometry.RegionAt).
        void BuildZoneCells(WorldInfo world)
        {
            if (world.Size <= 0f || world.Regions.Count == 0) return;
            int n = Minimap.ZoneRes;
            _zoneCell = new int[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    _zoneCell[y * n + x] = Minimap.NearestRegion(world.Regions, new Vector3((x + 0.5f) / n * world.Size, 0f, (y + 0.5f) / n * world.Size));
            _zoneShown = new ZoneState[world.Regions.Count];
            _zoneTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            _zoneImage.texture = _zoneTexture;
            var clear = new Color32[n * n];
            _zoneTexture.SetPixels32(clear);
            _zoneTexture.Apply();
        }
    }
}
