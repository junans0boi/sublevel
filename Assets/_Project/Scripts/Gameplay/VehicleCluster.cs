using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Where the driver's cluster sits (ADR-558, re-placed for HUD v2 / ADR-563): the tachometer in the gap between the
    // squad list and the vitals block, the speedometer right of the vitals block where the weapon stack stood (that
    // stack is hidden while driving), the shift lights in a row just over the vitals block.
    // Pure numbers in canvas pixels (origin bottom-left, height 1080) so a test can check it never covers the HUD.
    // The HUD rects are the HUD v2 layout contract (1920 reference; left pieces hug the left edge, right pieces the
    // right edge, the vitals block the centre).
    public static class ClusterLayout
    {
        public const float Margin = 36f, Gap = 4f, CompactScale = 0.8f;
        public const float SquadWidth = 300f, SquadHeight = 300f, TachGap = 32f, SpeedGap = 16f, SpeedWidth = 200f;
        public const float SideWidth = 220f, ZoneBottom = 300f, ZoneHeight = 60f, StackRight = 268f;
        public const int LightCount = 7;
        public const float LightW = 18f, LightH = 5f, LightGap = 4f;
        public const float LightsWidth = LightCount * LightW + (LightCount - 1) * LightGap;

        public static Vector2 TachSize(char style) => style == 'B' ? new Vector2(300f, 150f) : style == 'C' ? new Vector2(300f, 164f) : new Vector2(216f, 216f);
        public static Vector2 SpeedSize(char style) => style == 'B' ? new Vector2(232f, 116f) : style == 'C' ? new Vector2(220f, 164f) : new Vector2(216f, 216f);

        // The HUD pieces the cluster must keep clear of.
        public static Rect Squad => new Rect(Margin, Margin, SquadWidth, SquadHeight);
        public static Rect Vitals(float w) => new Rect(w / 2f - HudStatus.Width / 2f, Margin, HudStatus.Width, HudStatus.Height);
        public static Rect WeaponStack(float w) => new Rect(w - StackRight - SideWidth, Margin, SideWidth, 200f); // hidden while driving
        public static Rect MinimapRect(float w) => new Rect(w - Margin - SideWidth, Margin, SideWidth, SideWidth);
        public static Rect Zone(float w) => new Rect(w - Margin - SideWidth, ZoneBottom, SideWidth, ZoneHeight);
        public static Rect Lights(float w) => new Rect(w / 2f - LightsWidth / 2f, Margin + HudStatus.Height + 2f, LightsWidth, LightH);

        // The two gauges for a canvas `w` wide. The tachometer is right-aligned to the vitals block and shrinks to fit
        // the gap left of it (16:10); the speedometer starts right of the vitals block, at most 200 wide.
        public static void Place(float w, char style, bool compact, out Rect tach, out Rect speed)
        {
            float k = compact ? CompactScale : 1f;
            Vector2 ts = TachSize(style) * k, ss = SpeedSize(style) * k;
            float right = Vitals(w).xMin - TachGap, left = Squad.xMax + TachGap;
            float s = Mathf.Clamp((right - left) / ts.x, 0.1f, 1f);
            tach = new Rect(right - ts.x * s, Margin, ts.x * s, ts.y * s);
            float x0 = Vitals(w).xMax + SpeedGap, room = Mathf.Min(SpeedWidth, MinimapRect(w).xMin - Gap - x0);
            float s2 = Mathf.Clamp(room / ss.x, 0.1f, 1f);
            speed = new Rect(x0, Margin, ss.x * s2, ss.y * s2);
        }
    }

    // The driver's instrument cluster (ADR-558, amended): one of four styles per car model (VehicleHandling.Cluster,
    // data in rules.json) in that model's accent colour, its dials scaled to the model's top speed and redline.
    //   A twin round: two 270° dials, an amber rpm arc with the gear in the middle, a speed arc with big digits, fuel in the gap.
    //   B half-moon: two semicircles on the bottom line, fuel and body as 10-segment bars in their bases.
    //   C digital sport: a rising 24-segment LED tach with rpm and gear, a 3-digit speed with an 8-block fuel column.
    //   D classic analog: dark faces, ticks and numbers, red zone, needles on a hub, a "D5" gear window and a fuel sub-gauge.
    // All code-built: the rings are drawn into a texture once, the rest is Images and Texts.
    public sealed class VehicleCluster
    {
        const int Leds = 24, FuelBlocks = 8, Segments = 10;

        public RectTransform Root { get; }
        public RectTransform Tach { get; }
        public RectTransform Speed { get; }
        public char Style { get; }
        public bool Compact { get; }
        public Color Accent { get; }
        public int SpeedShown { get; private set; }
        public string GearShown => _gear.text;
        public float NeedleDeg => _needle != null ? _needle.localEulerAngles.z : 0f;
        public int LightsLit { get; private set; }
        public int FuelShown { get; private set; } = -1;
        public int ConditionShown { get; private set; } = -1;
        public bool FuelLowShown { get; private set; }

        static readonly Color Lit = new Color(1f, 0.74f, 0.32f, 1f);
        static readonly Color Cold = new Color(0.30f, 0.36f, 0.41f, 0.55f);
        static readonly Color Green = new Color(0.35f, 0.9f, 0.45f, 1f);
        static readonly Color Face = new Color(0.03f, 0.035f, 0.04f, 0.86f);
        static readonly Color Rim = new Color(0.5f, 0.58f, 0.64f, 0.55f);

        readonly VehicleHandling _row;
        readonly float _redline, _shiftAt, _rpmMax, _top;
        readonly Text _gear, _speedText, _rpmText, _fuelText, _bodyText;
        readonly RectTransform _gearRect, _needle, _speedNeedle, _fuelNeedle;
        readonly Image _rpmArc, _speedArc, _fuelArc;
        readonly float _rpmSweep, _speedSweep;
        readonly Image[] _lights = new Image[ClusterLayout.LightCount];
        Image[] _leds, _fuelBlocks, _fuelSegs, _bodySegs;
        int _shiftSeen = int.MinValue, _rpmTenths = -1;
        float _punchAt = float.NegativeInfinity, _placedWidth = -1f;

        public VehicleCluster(Transform parent, VehicleHandling row, float redline, float shiftShare, float topKmh)
        {
            _row = row;
            Style = row != null && !string.IsNullOrEmpty(row.Cluster) ? char.ToUpperInvariant(row.Cluster[0]) : 'C';
            if (Style < 'A' || Style > 'D') Style = 'C';
            Compact = row != null && row.ClusterCompact;
            Accent = row != null && ColorUtility.TryParseHtmlString(row.ClusterAccent, out var c) ? c : UiKit.Accent;
            _redline = redline;
            _shiftAt = redline * shiftShare;
            _rpmMax = RpmMax(redline);
            _top = topKmh;

            Root = UiKit.Fill("Cluster" + Style, parent);
            var corner = Vector2.zero;
            Tach = UiKit.Rect("Tacho", Root, corner, corner, Vector2.zero, ClusterLayout.TachSize(Style));
            Speed = UiKit.Rect("Speedo", Root, corner, corner, Vector2.zero, ClusterLayout.SpeedSize(Style));
            BuildLights();

            switch (Style)
            {
                case 'A': BuildTwinRound(out _gear, out _gearRect, out _rpmArc, out _speedArc, out _speedText, out _fuelArc, out _fuelText, out _bodyText); _rpmSweep = _speedSweep = 270f; break;
                case 'B': BuildHalfMoon(out _gear, out _gearRect, out _rpmArc, out _speedArc, out _speedText, out _fuelText, out _bodyText); _rpmSweep = _speedSweep = 180f; break;
                case 'D': BuildClassic(out _gear, out _gearRect, out _needle, out _speedNeedle, out _fuelNeedle, out _speedText, out _fuelText, out _bodyText); break;
                default: BuildDigital(out _gear, out _gearRect, out _rpmText, out _speedText, out _fuelText, out _bodyText); break;
            }
        }

        // The tachometer runs to the next whole thousand over the redline; the speedometer to the boost top in 20s.
        public static float RpmMax(float redline) => Mathf.Ceil(redline / 1000f + 0.5f) * 1000f;
        public static float SpeedMax(float boostMaxKmh) => Mathf.Max(60f, Mathf.Ceil(boostMaxKmh / 20f) * 20f);

        public bool BuiltFor(VehicleHandling row, float redline, float topKmh) =>
            ReferenceEquals(row, _row) && Mathf.Approximately(redline, _redline) && Mathf.Approximately(topKmh, _top);

        // Puts the two gauges in their gaps for a canvas this wide (the canvas height is always 1080).
        public void Place(float canvasWidth)
        {
            if (Mathf.Approximately(canvasWidth, _placedWidth)) return;
            _placedWidth = canvasWidth;
            ClusterLayout.Place(canvasWidth, Style, Compact, out var t, out var s);
            Put(Tach, t);
            Put(Speed, s);
        }

        static void Put(RectTransform rect, Rect at)
        {
            rect.anchoredPosition = at.position;
            float k = at.width / rect.sizeDelta.x;
            rect.localScale = new Vector3(k, k, 1f);
        }

        public void Tick(Vehicle v)
        {
            float rpm = v.EngineRpm;
            float rpm01 = Mathf.Clamp01(rpm / _rpmMax);
            bool hot = rpm >= _shiftAt;

            if (_rpmArc != null)
            {
                _rpmArc.fillAmount = rpm01 * _rpmSweep / 360f;
                _rpmArc.color = hot ? UiKit.Danger : Style == 'A' ? Lit : Accent;
            }
            if (_needle != null) _needle.localRotation = Quaternion.Euler(0f, 0f, -Angle(rpm01, 270f));
            if (_leds != null)
            {
                int lit = Mathf.RoundToInt(rpm01 * Leds);
                for (int i = 0; i < Leds; i++)
                {
                    float at = (i + 1f) / Leds * _rpmMax;
                    var on = at > _shiftAt ? UiKit.Danger : at > _shiftAt * 0.7f ? Lit : Green;
                    _leds[i].color = i < lit ? on : new Color(on.r, on.g, on.b, 0.14f);
                }
            }
            if (_rpmText != null)
            {
                int tenths = Mathf.RoundToInt(rpm / 100f);
                if (tenths != _rpmTenths) { _rpmTenths = tenths; _rpmText.text = HudDigits.IntText(tenths / 10) + "." + HudDigits.IntText(tenths % 10) + "k"; }
                _rpmText.color = hot ? UiKit.Danger : UiKit.Text;
            }

            // Gear: R / N / 1..6 ("D5" in the classic window), punched up for a moment on a change.
            string gear = v.Gear < 0 ? "R" : v.Gear == 0 ? "N" : Style == 'D' ? DGear(v.Gear) : HudDigits.IntText(v.Gear);
            if (_gear.text != gear) _gear.text = gear;
            if (v.ShiftCount != _shiftSeen) { if (_shiftSeen != int.MinValue) _punchAt = Time.unscaledTime; _shiftSeen = v.ShiftCount; }
            float p = Mathf.Clamp01((Time.unscaledTime - _punchAt) / 0.25f);
            _gearRect.localScale = Vector3.one * (1f + 0.3f * (1f - p) * (1f - p));
            var rest = Style == 'C' ? Accent : Style == 'D' ? UiKit.Accent : UiKit.Text;
            _gear.color = p < 1f ? Lit : v.Gear < 0 ? UiKit.Danger : rest;

            TickLights(rpm, v.Gear);

            // Speed.
            int kmh = Mathf.RoundToInt(v.SpeedKmh);
            float speed01 = Mathf.Clamp01(v.SpeedKmh / _top);
            if (kmh != SpeedShown || _speedText.text.Length == 0)
            {
                SpeedShown = kmh;
                _speedText.text = Style == 'C' ? Pad3(kmh) : HudDigits.IntText(kmh);
            }
            if (_speedArc != null) _speedArc.fillAmount = speed01 * _speedSweep / 360f;
            if (_speedNeedle != null) _speedNeedle.localRotation = Quaternion.Euler(0f, 0f, -Angle(speed01, 270f));

            // Fuel and body.
            var state = v.State;
            float fuel = state != null ? state.Fuel01 : 1f, body = state != null ? state.Health01 : 1f;
            int percent = Fuel.Percent(fuel);
            bool low = fuel < FuelRules.Current.LowShare;
            if (percent != FuelShown || low != FuelLowShown)
            {
                FuelShown = percent;
                FuelLowShown = low;
                var fuelColour = low ? UiKit.Danger : UiKit.Health;
                string value = percent > 0 ? HudDigits.IntText(percent) + "%" : "없음";
                _fuelText.text = Style == 'C' ? value : "연료 " + value;
                _fuelText.color = low ? UiKit.Danger : UiKit.Text;
                if (_fuelArc != null) { _fuelArc.fillAmount = Mathf.Clamp01(fuel) * 70f / 360f; _fuelArc.color = fuelColour; }
                if (_fuelNeedle != null) _fuelNeedle.localRotation = Quaternion.Euler(0f, 0f, -(-50f + 100f * Mathf.Clamp01(fuel)));
                if (_fuelSegs != null) Segs(_fuelSegs, fuel, fuelColour);
                if (_fuelBlocks != null) Segs(_fuelBlocks, fuel, low ? UiKit.Danger : Green);   // not the accent: a red car's fuel must not read "low"
            }
            int cond = Mathf.Clamp(Mathf.RoundToInt(body * 100f), 0, 100);
            if (cond != ConditionShown)
            {
                ConditionShown = cond;
                _bodyText.text = "차체 " + HudDigits.IntText(cond) + "%";
                _bodyText.color = body < 0.3f ? UiKit.Danger : UiKit.TextDim;
                if (_bodySegs != null) Segs(_bodySegs, body, body < 0.3f ? UiKit.Danger : UiKit.Health);
            }
        }

        // Shift lights from 65% of the shift point, lit from the outside in as pairs; all red and blinking at it.
        void TickLights(float rpm, int gearNow)
        {
            const int n = ClusterLayout.LightCount;
            float lit01 = Mathf.InverseLerp(_shiftAt * 0.65f, _shiftAt, rpm);
            bool at = rpm >= _shiftAt && gearNow > 0;
            bool blinkOn = Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.5f;
            int lit = at ? n : Mathf.FloorToInt(lit01 * n + 0.0001f);
            LightsLit = at && !blinkOn ? 0 : lit;
            for (int i = 0; i < n; i++)
            {
                int rank = Mathf.Min(i, n - 1 - i);
                bool on = at ? blinkOn : rank < Mathf.CeilToInt(lit / 2f);
                var lamp = at ? UiKit.Danger : rank == n / 2 ? UiKit.Danger : rank >= 2 ? Lit : Green;
                _lights[i].color = on ? lamp : Cold;
            }
        }

        static void Segs(Image[] segs, float share, Color on)
        {
            int lit = Mathf.CeilToInt(Mathf.Clamp01(share) * segs.Length - 0.001f);
            for (int i = 0; i < segs.Length; i++) segs[i].color = i < lit ? on : Cold;
        }

        // ---- The four styles ----

        void BuildLights()
        {
            var bottom = new Vector2(0.5f, 0f);
            float y = ClusterLayout.Lights(0f).y;
            for (int i = 0; i < ClusterLayout.LightCount; i++)
            {
                float x = -ClusterLayout.LightsWidth / 2f + ClusterLayout.LightW / 2f + i * (ClusterLayout.LightW + ClusterLayout.LightGap);
                var led = UiKit.Rect("Light" + i, Root, bottom, bottom, new Vector2(x, y), new Vector2(ClusterLayout.LightW, ClusterLayout.LightH));
                _lights[i] = UiKit.Box(led, Cold);
            }
        }

        void BuildTwinRound(out Text gear, out RectTransform gearRect, out Image rpmArc, out Image speedArc, out Text speed, out Image fuelArc, out Text fuel, out Text body)
        {
            const float D = 216f, band = 0.075f;
            float arc = D - 14f;
            var t = Square(Tach, D, false);
            Disc(t, "Face", D, Face);
            Disc(t, "Rim", D, Rim, Ring(0.012f));
            Arc(t, "Track", arc, Ring(band), new Color(1f, 1f, 1f, 0.08f), -135f, 270f, 1f);
            Arc(t, "Red", arc, Ring(band), new Color(UiKit.Danger.r, UiKit.Danger.g, UiKit.Danger.b, 0.45f), Angle(_redline / _rpmMax, 270f), 270f * (1f - _redline / _rpmMax), 1f);
            rpmArc = Arc(t, "Rpm", arc, Ring(band), Lit, -135f, 270f, 0f);
            Ticks(t, 270f, _rpmMax, 1000f, 0f, _redline, arc / 2f - 18f, 6f, 0f, arc / 2f - 34f, 15, 1000f);
            gearRect = UiKit.Rect("Gear", t, Mid, Mid, new Vector2(0f, 6f), new Vector2(90f, 80f));
            gear = UiKit.Label(gearRect, "N", 72, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(t, "Unit", new Vector2(0f, -40f), new Vector2(110f, 16f), "x1000 rpm", 12, UiKit.TextDim);
            body = Label(t, "Body", new Vector2(0f, -D / 2f + 20f), new Vector2(110f, 20f), "", 14, UiKit.TextDim);

            var s = Square(Speed, D, false);
            Disc(s, "Face", D, Face);
            Disc(s, "Rim", D, Rim, Ring(0.012f));
            Arc(s, "Track", arc, Ring(band), new Color(1f, 1f, 1f, 0.08f), -135f, 270f, 1f);
            speedArc = Arc(s, "Speed", arc, Ring(band), Accent, -135f, 270f, 0f);
            Ticks(s, 270f, _top, _top > 180f ? 40f : 20f, 0f, float.MaxValue, arc / 2f - 18f, 6f, 0f, 0f, 0, 0f);
            speed = Label(s, "Digits", new Vector2(0f, 10f), new Vector2(150f, 64f), "0", 56, UiKit.Text, FontStyle.Bold);
            Label(s, "Kmh", new Vector2(0f, -28f), new Vector2(80f, 18f), "km/h", 14, UiKit.TextDim);
            // Fuel: a short arc in the dial's bottom gap, filling left to right, with its reading over it.
            Arc(s, "FuelTrack", arc, Ring(band), new Color(1f, 1f, 1f, 0.1f), 215f, 70f, 1f, false);
            fuelArc = Arc(s, "Fuel", arc, Ring(band), UiKit.Health, 215f, 70f, 1f, false);
            fuel = Label(s, "FuelText", new Vector2(0f, -D / 2f + 34f), new Vector2(110f, 20f), "", 15, UiKit.Text, FontStyle.Bold);
        }

        void BuildHalfMoon(out Text gear, out RectTransform gearRect, out Image rpmArc, out Image speedArc, out Text speed, out Text fuel, out Text body)
        {
            const float band = 0.08f;
            float D = 300f, arc = D - 12f;
            var t = Square(Tach, D, true);
            Arc(t, "Face", D, Ring(0.5f), Face, -90f, 180f, 1f);
            Arc(t, "Rim", D, Ring(0.012f), Rim, -90f, 180f, 1f);
            Base(Tach, D);
            Arc(t, "Track", arc, Ring(band), new Color(1f, 1f, 1f, 0.08f), -90f, 180f, 1f);
            Arc(t, "Red", arc, Ring(band), new Color(UiKit.Danger.r, UiKit.Danger.g, UiKit.Danger.b, 0.5f), Angle(_redline / _rpmMax, 180f), 180f * (1f - _redline / _rpmMax), 1f);
            rpmArc = Arc(t, "Rpm", arc, Ring(band), Accent, -90f, 180f, 0f);
            Ticks(t, 180f, _rpmMax, 1000f, 500f, _redline, arc / 2f - 14f, 9f, 5f, arc / 2f - 34f, 15, 1000f);
            Label(t, "Unit", new Vector2(0f, 88f), new Vector2(110f, 16f), "x1000 rpm", 12, UiKit.TextDim);
            gearRect = UiKit.Rect("Gear", t, Mid, Mid, new Vector2(0f, 50f), new Vector2(80f, 64f));
            gear = UiKit.Label(gearRect, "N", 52, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            _bodySegs = SegmentBar(t, "BodyBar", new Vector2(-86f, 10f), 100f, 8f, Segments);
            body = LeftLabel(t, "Body", new Vector2(20f, 10f), new Vector2(80f, 18f), 13);

            D = 232f; arc = D - 12f;
            var s = Square(Speed, D, true);
            Arc(s, "Face", D, Ring(0.5f), Face, -90f, 180f, 1f);
            Arc(s, "Rim", D, Ring(0.012f), Rim, -90f, 180f, 1f);
            Base(Speed, D);
            Arc(s, "Track", arc, Ring(band), new Color(1f, 1f, 1f, 0.08f), -90f, 180f, 1f);
            speedArc = Arc(s, "Speed", arc, Ring(band), Accent, -90f, 180f, 0f);
            Ticks(s, 180f, _top, _top > 180f ? 40f : 20f, 0f, float.MaxValue, arc / 2f - 14f, 7f, 0f, 0f, 0, 0f);
            speed = Label(s, "Digits", new Vector2(0f, 64f), new Vector2(130f, 48f), "0", 44, UiKit.Text, FontStyle.Bold);
            Label(s, "Kmh", new Vector2(0f, 34f), new Vector2(80f, 16f), "km/h", 12, UiKit.TextDim);
            _fuelSegs = SegmentBar(s, "FuelBar", new Vector2(-80f, 10f), 82f, 8f, Segments);
            fuel = LeftLabel(s, "FuelText", new Vector2(8f, 10f), new Vector2(84f, 18f), 13);
        }

        void BuildDigital(out Text gear, out RectTransform gearRect, out Text rpm, out Text speed, out Text fuel, out Text body)
        {
            Plate(Tach);
            Plate(Speed);
            var tl = new Vector2(0f, 1f);
            // Rising LED tach: 24 segments across the top, each a little taller than the last.
            _leds = new Image[Leds];
            const float x0 = 14f, w = 300f - 28f, gap = 2f;
            float seg = (w - gap * (Leds - 1)) / Leds;
            for (int i = 0; i < Leds; i++)
            {
                float h = Mathf.Lerp(10f, 44f, i / (Leds - 1f));
                var r = UiKit.Rect("Led" + i, Tach, tl, Vector2.zero, new Vector2(x0 + i * (seg + gap), -60f), new Vector2(seg, h));
                _leds[i] = UiKit.Box(r, Cold);
            }
            var left = new Vector2(0f, 0f);
            gearRect = UiKit.Rect("Gear", Tach, left, new Vector2(0.5f, 0.5f), new Vector2(60f, 54f), new Vector2(84f, 92f));
            gear = UiKit.Label(gearRect, "N", 84, Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            rpm = UiKit.Label(UiKit.Rect("Rpm", Tach, left, left, new Vector2(112f, 46f), new Vector2(130f, 44f)), "0.0k", 38, UiKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.Label(UiKit.Rect("RpmUnit", Tach, left, left, new Vector2(246f, 46f), new Vector2(44f, 20f)), "rpm", 13, UiKit.TextDim);
            body = UiKit.Label(UiKit.Rect("Body", Tach, left, left, new Vector2(112f, 12f), new Vector2(174f, 20f)), "", 14, UiKit.TextDim, TextAnchor.MiddleRight);

            speed = UiKit.Label(UiKit.Rect("Digits", Speed, left, left, new Vector2(10f, 62f), new Vector2(166f, 84f)), "000", 72, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Label(UiKit.Rect("Kmh", Speed, left, left, new Vector2(10f, 40f), new Vector2(166f, 20f)), "km/h", 15, UiKit.TextDim, TextAnchor.MiddleCenter);
            fuel = UiKit.Label(UiKit.Rect("FuelText", Speed, left, left, new Vector2(10f, 10f), new Vector2(166f, 22f)), "", 16, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Label(UiKit.Rect("FuelIcon", Speed, left, left, new Vector2(10f, 10f), new Vector2(40f, 22f)), "연료", 12, UiKit.TextDim);
            // The fuel column: 8 blocks bottom (E) to top (F), red when low.
            _fuelBlocks = new Image[FuelBlocks];
            for (int i = 0; i < FuelBlocks; i++)
                _fuelBlocks[i] = UiKit.Box(UiKit.Rect("Fuel" + i, Speed, left, left, new Vector2(186f, 28f + i * 14f), new Vector2(20f, 11f)), Cold);
            UiKit.Label(UiKit.Rect("F", Speed, left, left, new Vector2(186f, 140f), new Vector2(20f, 18f)), "F", 13, UiKit.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Label(UiKit.Rect("E", Speed, left, left, new Vector2(186f, 6f), new Vector2(20f, 18f)), "E", 13, UiKit.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        void BuildClassic(out Text gear, out RectTransform gearRect, out RectTransform needle, out RectTransform speedNeedle, out RectTransform fuelNeedle, out Text speed, out Text fuel, out Text body)
        {
            const float D = 216f;
            float r = D / 2f - 10f;
            var t = Square(Tach, D, false);
            ClassicFace(t, D);
            Arc(t, "Red", D - 22f, Ring(0.05f), UiKit.Danger, Angle(_redline / _rpmMax, 270f), 270f * (1f - _redline / _rpmMax), 1f);
            Ticks(t, 270f, _rpmMax, 1000f, 500f, _redline, r, 13f, 7f, r - 26f, 16, 1000f);
            Label(t, "Unit", new Vector2(0f, 34f), new Vector2(110f, 16f), "x1000 rpm", 11, UiKit.TextDim);
            var window = UiKit.Rect("GearWindow", t, Mid, Mid, new Vector2(0f, -44f), new Vector2(50f, 30f));
            UiKit.Box(window, new Color(0f, 0f, 0f, 0.9f));
            UiKit.Frame(window, 1f, UiKit.SteelDim);
            gearRect = UiKit.Rect("Gear", window, Mid, Mid, Vector2.zero, new Vector2(50f, 30f));
            gear = UiKit.Label(gearRect, "N", 22, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            body = Label(t, "Body", new Vector2(0f, -D / 2f + 16f), new Vector2(110f, 18f), "", 13, UiKit.TextDim);
            needle = Needle(t, D * 0.42f, 4f, Accent);
            Hub(t);

            var s = Square(Speed, D, false);
            ClassicFace(s, D);
            float major = _top > 180f ? 40f : 20f;
            Ticks(s, 270f, _top, major, major / 2f, float.MaxValue, r, 13f, 7f, r - 26f, 14, 1f);
            Label(s, "Kmh", new Vector2(0f, 44f), new Vector2(80f, 16f), "km/h", 11, UiKit.TextDim);
            speed = Label(s, "Digits", new Vector2(0f, 25f), new Vector2(80f, 22f), "0", 17, UiKit.Text, FontStyle.Bold);
            // The fuel sub-gauge: a small E-F arc low in the face with its own needle.
            var sub = UiKit.Rect("FuelGauge", s, Mid, Mid, new Vector2(0f, -50f), new Vector2(64f, 64f));
            Arc(sub, "Scale", 64f, Ring(0.08f), UiKit.Steel, -50f, 100f, 1f);
            Arc(sub, "Low", 64f, Ring(0.08f), UiKit.Danger, -50f, 100f * FuelRules.Current.LowShare, 1f);
            Label(sub, "E", new Vector2(-28f, 4f), new Vector2(16f, 16f), "E", 11, UiKit.Danger, FontStyle.Bold);
            Label(sub, "F", new Vector2(28f, 4f), new Vector2(16f, 16f), "F", 11, UiKit.Text, FontStyle.Bold);
            fuelNeedle = Needle(sub, 26f, 2.5f, Accent);
            Disc(sub, "Hub", 9f, new Color(0.1f, 0.1f, 0.11f, 1f));
            fuel = Label(s, "FuelText", new Vector2(0f, -D / 2f + 16f), new Vector2(110f, 18f), "", 13, UiKit.Text, FontStyle.Bold);
            speedNeedle = Needle(s, D * 0.42f, 4f, Accent);
            Hub(s);
        }

        // ---- Pieces ----

        static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);

        // Clockwise degrees from 12 o'clock for a share of a dial `sweep` wide, centred on 12 o'clock.
        static float Angle(float share, float sweep) => -sweep / 2f + sweep * share;
        static Vector2 Dir(float deg) => new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad));

        static string DGear(int g) => g <= 9 ? DGears[g] : "D";
        static readonly string[] DGears = { "N", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9" };

        static string[] _pad3;
        static string Pad3(int n)
        {
            if (_pad3 == null) { _pad3 = new string[1000]; for (int i = 0; i < 1000; i++) _pad3[i] = i.ToString("000"); }
            return _pad3[Mathf.Clamp(n, 0, 999)];
        }

        // The square a dial is drawn in: centred on the gauge, or (half) centred on its bottom edge.
        static RectTransform Square(RectTransform gauge, float d, bool half) =>
            UiKit.Rect("Dial", gauge, half ? new Vector2(0.5f, 0f) : Mid, Mid, Vector2.zero, new Vector2(d, d));

        static void Base(RectTransform gauge, float d) =>
            UiKit.Box(UiKit.Rect("Base", gauge, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(d, 2f)), Rim);

        static void Plate(RectTransform gauge)
        {
            UiKit.Box(UiKit.Fill("Plate", gauge), new Color(0.03f, 0.035f, 0.04f, 0.86f));
            UiKit.Frame(gauge, 1f, UiKit.SteelDim);
        }

        void ClassicFace(RectTransform dial, float d)
        {
            Disc(dial, "Bezel", d, new Color(0.22f, 0.25f, 0.28f, 0.95f));
            Disc(dial, "Face", d - 8f, new Color(0.02f, 0.022f, 0.026f, 0.96f));
            Disc(dial, "Inner", d * 0.62f, new Color(0.05f, 0.055f, 0.06f, 1f));
        }

        static RectTransform Needle(RectTransform dial, float length, float width, Color color)
        {
            var n = UiKit.Rect("Needle", dial, Mid, new Vector2(0.5f, 0.14f), Vector2.zero, new Vector2(width, length));
            UiKit.Box(n, color);
            n.localRotation = Quaternion.Euler(0f, 0f, 135f);
            return n;
        }

        static void Hub(RectTransform dial)
        {
            Disc(dial, "Hub", 22f, new Color(0.09f, 0.1f, 0.11f, 1f));
            Disc(dial, "HubRim", 22f, UiKit.Accent, Ring(0.18f));
        }

        static Image[] SegmentBar(RectTransform parent, string name, Vector2 leftCentre, float width, float height, int count)
        {
            const float gap = 2f;
            var segs = new Image[count];
            float w = (width - gap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                var r = UiKit.Rect(name + i, parent, Mid, new Vector2(0f, 0.5f), leftCentre + new Vector2(i * (w + gap), 0f), new Vector2(w, height));
                segs[i] = UiKit.Box(r, Cold);
            }
            return segs;
        }

        static Text LeftLabel(RectTransform parent, string name, Vector2 at, Vector2 size, int font) =>
            UiKit.Label(UiKit.Rect(name, parent, Mid, new Vector2(0f, 0.5f), at, size), "", font, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);

        static Text Label(RectTransform parent, string name, Vector2 at, Vector2 size, string text, int font, Color color, FontStyle style = FontStyle.Normal) =>
            UiKit.Label(UiKit.Rect(name, parent, Mid, Mid, at, size), text, font, color, TextAnchor.MiddleCenter, style);

        // Ticks every `minor` (0 = none) and `major` from 0 to `max`, red from `redFrom`, numbers (value / `divide`) inside.
        void Ticks(RectTransform dial, float sweep, float max, float major, float minor, float redFrom, float radius, float majorLen, float minorLen, float numRadius, int numFont, float divide)
        {
            float step = minor > 0f ? minor : major;
            int n = Mathf.RoundToInt(max / step);
            for (int k = 0; k <= n; k++)
            {
                float value = k * step;
                bool isMajor = Mathf.Abs(Mathf.Repeat(value + 0.01f, major)) < 0.1f;
                float deg = Angle(value / max, sweep);
                var dir = Dir(deg);
                float len = isMajor ? majorLen : minorLen;
                bool red = value >= redFrom - 0.01f;
                var tick = UiKit.Rect("Tick" + k, dial, Mid, Mid, dir * (radius - len / 2f), new Vector2(isMajor ? 3f : 2f, len));
                tick.localRotation = Quaternion.Euler(0f, 0f, -deg);
                UiKit.Box(tick, red ? UiKit.Danger : isMajor ? UiKit.Text : UiKit.TextDim);
                if (!isMajor || numFont <= 0) continue;
                if (sweep <= 180f && (k == 0 || k == n)) continue;   // a half-moon's end numbers would sit on its base line
                Label(dial, "Num" + k, dir * numRadius, new Vector2(34f, 20f), HudDigits.IntText(Mathf.RoundToInt(value / divide)), numFont, red ? UiKit.Danger : UiKit.Text, FontStyle.Bold);
            }
        }

        static Image Disc(RectTransform parent, string name, float size, Color color, Sprite sprite = null)
        {
            var image = UiKit.Box(UiKit.Rect(name, parent, Mid, Mid, Vector2.zero, new Vector2(size, size)), color);
            image.sprite = sprite != null ? sprite : Ring(0.5f);
            return image;
        }

        // A ring from `startDeg` (clockwise from 12 o'clock) running `sweepDeg` clockwise (or back), `fill` of it drawn.
        static Image Arc(RectTransform parent, string name, float size, Sprite ring, Color color, float startDeg, float sweepDeg, float fill, bool clockwise = true)
        {
            var rect = UiKit.Rect(name, parent, Mid, Mid, Vector2.zero, new Vector2(size, size));
            rect.localRotation = Quaternion.Euler(0f, 0f, 180f - startDeg);   // the fill's start (6 o'clock) turned to startDeg
            var image = UiKit.Box(rect, color);
            image.sprite = ring;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Bottom;
            image.fillClockwise = clockwise;
            image.fillAmount = fill * sweepDeg / 360f;
            return image;
        }

        static readonly Dictionary<int, Sprite> Rings = new Dictionary<int, Sprite>();

        // A white anti-aliased ring `thickness` (share of the radius) wide at the edge; 0.5 or more is a full disc.
        internal static Sprite Ring(float thickness)
        {
            int key = Mathf.RoundToInt(thickness * 1000f);
            if (Rings.TryGetValue(key, out var cached) && cached != null) return cached;
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            float inner = thickness >= 0.5f ? -1f : 1f - thickness * 2f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                const float aa = 2f / n;
                float a = Mathf.Clamp01((1f - d) / aa) * (inner < 0f ? 1f : Mathf.Clamp01((d - inner) / aa));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            Rings[key] = sprite;
            return sprite;
        }
    }
}
