using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Top centre: a heading strip with 15 degree ticks, Korean direction letters and a fixed-width numeric heading.
    // The strip is laid out by each mark's angle from the view direction and masked at its ends; it is moved only
    // when the heading changes. The map marker (ADR-065 (6)) shows on it as a small diamond at its bearing with the
    // distance under it; outside the visible arc it waits at the nearer end with an arrow pointing on.
    public sealed class CompassStrip
    {
        public const float StripWidth = 560f, StripHeight = 40f, PixelsPerDegree = 4f;
        public const float HeadingWidth = 72f, HeadingHeight = 30f;
        const int Steps = 24; // one mark every 15 degrees

        // The marker stops this far in from either end, clear of the mask's soft fade (48 px) with room for its arrow.
        public const float MarkerEdgeInset = 64f;
        public const float MarkerLimit = StripWidth * 0.5f - MarkerEdgeInset;
        const float MarkerMoveDeg = 0.1f;   // smaller turns do not move the diamond
        const float MarkerDiamond = 10f, MarkerArrowSize = 12f, MarkerArrowGap = 13f, MarkerRaise = 12f;
        static readonly Color MarkerRim = new Color(0.04f, 0.05f, 0.06f, 0.95f);

        static readonly string[] Directions = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };

        public RectTransform Strip { get; private set; }
        public RectTransform Heading { get; private set; }

        readonly RectTransform[] _marks = new RectTransform[Steps];
        HudDigits _digits;
        float _shownHeading = float.NaN;

        RectTransform _marker, _markerArrow;
        Image _markerFill, _markerArrowImage;
        Text _markerDistance;
        float _markerRelative = float.NaN;
        int _markerMetres = -1;
        Color _markerColour;

        public int HeadingShown => _digits != null ? _digits.Value : -1;

        // The map marker as shown, for tests: on the strip at all, x from the strip centre (px), its angle from the
        // view direction, whether it is held at an end (with the arrow), and the distance label.
        public bool MarkerShown => _marker != null && _marker.gameObject.activeSelf;
        public float MarkerX => _marker != null ? _marker.anchoredPosition.x : 0f;
        public float MarkerRelativeDeg => _markerRelative;
        public bool MarkerClamped => MarkerShown && _markerArrow.gameObject.activeSelf;
        public string MarkerDistanceText => MarkerShown ? _markerDistance.text : "";

        // Where a bearing sits on the strip while facing `heading` (both compass degrees, any range): pixels from
        // the centre, right positive. Past +-MarkerLimit (outside the visible arc, including straight behind) it is
        // held at that end and `clamped` is true.
        public static float MarkerOffset(float heading, float bearing, out bool clamped)
        {
            float x = Mathf.DeltaAngle(heading, bearing) * PixelsPerDegree;
            clamped = Mathf.Abs(x) > MarkerLimit;
            return clamped ? Mathf.Sign(x) * MarkerLimit : x;
        }

        public static CompassStrip Build(Transform parent, float top)
        {
            var c = new CompassStrip();
            var topCentre = new Vector2(0.5f, 1f);
            var centre = new Vector2(0.5f, 0.5f);
            var strip = UiKit.Rect("Compass", parent, topCentre, topCentre, new Vector2(0f, -top), new Vector2(StripWidth, StripHeight));
            UiKit.Box(strip, UiKit.Panel);
            var mask = strip.gameObject.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(48, 0);
            c.Strip = strip;
            for (int i = 0; i < Steps; i++)
            {
                if (i % 3 != 0)
                {
                    // A plain tick between the lettered directions.
                    c._marks[i] = UiKit.Rect("Tick" + i * 15, strip, centre, centre, new Vector2(0f, 12f), new Vector2(1f, 8f));
                    UiKit.Box(c._marks[i], UiKit.Steel);
                    continue;
                }
                int direction = i / 3;
                bool cardinal = direction % 2 == 0;
                var label = UiKit.Label(UiKit.Rect(Directions[direction], strip, centre, centre, Vector2.zero, new Vector2(60f, 32f)), Directions[direction],
                    cardinal ? 22 : 18, direction == 0 ? UiKit.Accent : cardinal ? UiKit.Text : UiKit.TextDim, TextAnchor.MiddleCenter,
                    cardinal ? FontStyle.Bold : FontStyle.Normal);
                c._marks[i] = label.rectTransform;
            }
            c.BuildMarker(strip);
            // Thin rule under the strip, the view notch and the heading in degrees.
            UiKit.Box(UiKit.Rect("Rule", parent, topCentre, topCentre, new Vector2(0f, -top - StripHeight), new Vector2(StripWidth, 1f)), UiKit.Steel);
            UiKit.Box(UiKit.Rect("Notch", parent, topCentre, topCentre, new Vector2(0f, -top - StripHeight), new Vector2(2f, 8f)), UiKit.Accent);
            var heading = UiKit.Rect("Heading", parent, topCentre, topCentre, new Vector2(0f, -top - StripHeight - 10f), new Vector2(HeadingWidth, HeadingHeight));
            UiKit.Box(heading, UiKit.Panel);
            UiKit.Frame(heading, 1f, UiKit.SteelDim);
            c.Heading = heading;
            c._digits = HudDigits.Build("Digits", heading, centre, centre, Vector2.zero, 3, 18f, HeadingHeight, 20, UiKit.Text);
            c._digits.Pad = true;
            c._digits.Set(0);
            return c;
        }

        // Last child of the strip, so it draws over the ticks and letters (and is cut by the same mask).
        void BuildMarker(RectTransform strip)
        {
            var centre = new Vector2(0.5f, 0.5f);
            _markerColour = MapMarker.Colour;
            _marker = UiKit.Rect("Marker", strip, centre, centre, Vector2.zero, new Vector2(MarkerDiamond, MarkerDiamond));
            // The diamond: a square turned 45 degrees with a dark rim, in the upper half of the strip.
            var diamond = UiKit.Rect("Diamond", _marker, centre, centre, new Vector2(0f, MarkerRaise), new Vector2(MarkerDiamond, MarkerDiamond));
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            UiKit.Box(UiKit.Fill("Rim", diamond, -2f), MarkerRim);
            _markerFill = UiKit.Box(UiKit.Fill("Fill", diamond), _markerColour);
            // The distance under it on a small dark plate so it reads over the direction letters.
            var plate = UiKit.Rect("Distance", _marker, centre, centre, new Vector2(0f, -8f), new Vector2(46f, 17f));
            UiKit.Box(plate, MarkerRim);
            _markerDistance = UiKit.Label(UiKit.Fill("Text", plate), "", 14, _markerColour, TextAnchor.MiddleCenter, FontStyle.Bold);
            // Points on toward the marker while it is held at an end.
            _markerArrow = UiKit.Rect("Arrow", _marker, centre, centre, new Vector2(MarkerArrowGap, MarkerRaise), new Vector2(MarkerArrowSize, MarkerArrowSize));
            _markerArrowImage = UiKit.Box(_markerArrow, _markerColour);
            _markerArrowImage.sprite = HudSprites.Arrow;
            _markerArrow.gameObject.SetActive(false);
            _marker.gameObject.SetActive(false);
        }

        public void Tick(float yaw)
        {
            float heading = Mathf.Repeat(yaw, 360f);
            if (heading == _shownHeading) return;
            _shownHeading = heading;
            // Everything is laid out by its angle from the view direction; the strip's mask cuts off the rest.
            for (int i = 0; i < Steps; i++)
            {
                var mark = _marks[i];
                float x = Mathf.DeltaAngle(heading, i * (360f / Steps)) * PixelsPerDegree;
                mark.anchoredPosition = new Vector2(x, mark.anchoredPosition.y);
            }
            _digits.Set(Mathf.RoundToInt(heading) % 360);
        }

        // Places the map marker for a view heading. `metres` is the whole-metre distance (the label is rebuilt only
        // when it changes) and `show` false hides it. The diamond moves only when its angle from the view changes by
        // more than a tenth of a degree, so a still view writes nothing.
        public void TickMarker(bool show, float heading, float bearing, int metres)
        {
            if (_marker == null) return;
            if (!show)
            {
                if (_marker.gameObject.activeSelf) _marker.gameObject.SetActive(false);
                _markerRelative = float.NaN;
                _markerMetres = -1;
                return;
            }
            if (!_marker.gameObject.activeSelf) _marker.gameObject.SetActive(true);

            var colour = MapMarker.Colour;
            if (colour != _markerColour)
            {
                _markerColour = colour;
                _markerFill.color = colour;
                _markerDistance.color = colour;
                _markerArrowImage.color = colour;
            }

            float relative = Mathf.DeltaAngle(heading, bearing);
            if (float.IsNaN(_markerRelative) || Mathf.Abs(relative - _markerRelative) > MarkerMoveDeg)
            {
                _markerRelative = relative;
                float x = MarkerOffset(heading, bearing, out bool clamped);
                _marker.anchoredPosition = new Vector2(x, 0f);
                if (_markerArrow.gameObject.activeSelf != clamped) _markerArrow.gameObject.SetActive(clamped);
                if (clamped)
                {
                    // The arrow art points up; a negative z turn points it right.
                    bool right = x > 0f;
                    _markerArrow.anchoredPosition = new Vector2(right ? MarkerArrowGap : -MarkerArrowGap, MarkerRaise);
                    _markerArrow.localRotation = Quaternion.Euler(0f, 0f, right ? -90f : 90f);
                }
            }

            if (metres == _markerMetres) return;
            _markerMetres = metres;
            _markerDistance.text = MapMarkerText.Metres(metres);
        }
    }
}
