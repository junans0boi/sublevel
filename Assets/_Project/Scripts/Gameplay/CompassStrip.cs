using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Top centre: a heading strip with 15 degree ticks, Korean direction letters and a fixed-width numeric heading.
    // The strip is laid out by each mark's angle from the view direction and masked at its ends; it is moved only
    // when the heading changes.
    public sealed class CompassStrip
    {
        public const float StripWidth = 560f, StripHeight = 40f, PixelsPerDegree = 4f;
        public const float HeadingWidth = 72f, HeadingHeight = 30f;
        const int Steps = 24; // one mark every 15 degrees

        static readonly string[] Directions = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };

        public RectTransform Strip { get; private set; }
        public RectTransform Heading { get; private set; }

        readonly RectTransform[] _marks = new RectTransform[Steps];
        HudDigits _digits;
        float _shownHeading = float.NaN;

        public int HeadingShown => _digits != null ? _digits.Value : -1;

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
    }
}
