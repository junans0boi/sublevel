using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The instrument cluster in a vehicle (ADR-558): a tachometer (ticks every 1,000 rpm, the red zone, a
    // needle, the gear in the middle and a row of shift lights over it) and beside it the speed, a speed bar, the fuel
    // and the body. All code-built: the rings are drawn into a texture once, the dial is Images and Texts.
    public sealed class VehicleCluster
    {
        const float Sweep = 270f;           // the dial's arc, from bottom-left round the top to bottom-right
        const int Lights = 7;
        const float StackTop = 36f + WeaponStrip.CellHeight + 10f + Minimap.ViewSize + 12f; // Hud's margin, strip, gap, minimap, gap
        const float CentreY = 36f + 44f + 8f;  // Hud's margin and health bar: the weapon panel's place

        // Two placements: over the minimap (tall, a big dial) and, for a driver with empty hands, the weapon panel's
        // place at the bottom centre (wide and low, ADR-558).
        sealed class Layout
        {
            public float D, W, H, DialX, Hub, GearY, GearW, GearH, UnitY, DigitCell, DigitH, DigitY, KmhY, ModeY, SpeedBarY, FuelY, BodyY, BarH;
            public int GearFont, UnitFont, DigitFont, KmhFont, ModeFont, BarFont, NumFont;
            public float TickMajor, TickMinor, TickInset, NumInset;
            public bool LightsOverDial;
        }

        static readonly Layout SideLayout = new Layout
        {
            D = 236f, W = 520f, H = 268f, DialX = 16f, Hub = 54f, GearY = -54f, GearW = 80f, GearH = 56f, GearFont = 46, UnitY = -88f, UnitFont = 13,
            DigitCell = 44f, DigitH = 80f, DigitFont = 64, DigitY = 58f, KmhY = 44f, KmhFont = 18, ModeY = 72f, ModeFont = 14,
            SpeedBarY = 8f, FuelY = -36f, BodyY = -76f, BarH = 24f, BarFont = 15, TickMajor = 14f, TickMinor = 7f, TickInset = 24f, NumInset = 30f, NumFont = 16,
            LightsOverDial = true,
        };

        static readonly Layout CentreLayout = new Layout
        {
            D = 140f, W = 560f, H = 148f, DialX = 10f, Hub = 34f, GearY = -30f, GearW = 52f, GearH = 36f, GearFont = 30, UnitY = -52f, UnitFont = 11,
            DigitCell = 38f, DigitH = 62f, DigitFont = 52, DigitY = 14f, KmhY = 4f, KmhFont = 16, ModeY = 28f, ModeFont = 13,
            SpeedBarY = -24f, FuelY = -44f, BodyY = -62f, BarH = 18f, BarFont = 13, TickMajor = 9f, TickMinor = 5f, TickInset = 14f, NumInset = 22f, NumFont = 12,
            LightsOverDial = false,
        };

        public RectTransform Root { get; }
        public bool Centre { get; }
        public int SpeedShown => _speed.Value;
        public string GearShown => _gear.text;
        public float NeedleDeg => _needle.localEulerAngles.z;
        public int LightsLit { get; private set; }

        readonly Layout _l;
        readonly HudDigits _speed;
        readonly Text _gear, _mode, _unit;
        readonly RectTransform _needle, _speedFill, _fuelFill, _bodyFill, _gearRect;
        readonly Image _rpmArc, _fuelImage, _bodyImage, _speedFillImage;
        readonly Image[] _lights = new Image[Lights];
        readonly Text _fuelText, _bodyText;
        RectTransform _dial;
        Image _redArc;
        float _shownRedline = -1f;
        int _shiftSeen = int.MinValue;
        float _punchAt = float.NegativeInfinity;
        readonly System.Collections.Generic.List<GameObject> _ticks = new System.Collections.Generic.List<GameObject>();

        public int FuelShown { get; private set; } = -1;
        public int ConditionShown { get; private set; } = -1;
        public bool FuelLowShown { get; private set; }

        static readonly Color Lit = new Color(1f, 0.74f, 0.32f, 1f);
        static readonly Color Cold = new Color(0.30f, 0.36f, 0.41f, 0.55f);

        public VehicleCluster(Transform parent, bool centre)
        {
            Centre = centre;
            var l = _l = centre ? CentreLayout : SideLayout;
            float D = l.D;
            if (centre)
            {
                var bottom = new Vector2(0.5f, 0f);
                Root = UiKit.Rect("ClusterCentre", parent, bottom, bottom, new Vector2(0f, CentreY), new Vector2(l.W, l.H));
            }
            else
            {
                // Over the bottom-right column (weapon strip, then the minimap), so neither is covered.
                var corner = new Vector2(1f, 0f);
                Root = UiKit.Rect("Cluster", parent, corner, corner, new Vector2(-36f, StackTop), new Vector2(l.W, l.H));
            }
            var plate = UiKit.Box(Root, UiKit.PanelDeep);
            plate.color = new Color(0.045f, 0.052f, 0.06f, 0.78f);
            UiKit.Frame(Root, 1f, UiKit.SteelDim);

            // ---- Tachometer ----
            var mid = new Vector2(0.5f, 0.5f);
            var dial = UiKit.Rect("Tacho", Root, new Vector2(0f, 0.5f), mid, new Vector2(l.DialX + D / 2f, centre ? 0f : -6f), new Vector2(D, D));
            _dial = dial;
            Disc(dial, "Face", D, new Color(0.02f, 0.025f, 0.03f, 0.9f));
            float arc = D - D * 0.076f;
            Arc(dial, "Track", arc, Ring(0.07f), UiKit.SteelDim, 1f);
            _rpmArc = Arc(dial, "Rpm", arc, Ring(0.07f), UiKit.Steel, 0f);
            _redArc = Arc(dial, "Red", D - D * 0.017f, Ring(0.035f), UiKit.Danger, 0f);
            Disc(dial, "Rim", D, new Color(0.5f, 0.58f, 0.64f, 0.5f), Ring(0.012f));

            _needle = UiKit.Rect("Needle", dial, mid, new Vector2(0.5f, 0.12f), Vector2.zero, new Vector2(centre ? 4f : 5f, D * 0.47f));
            UiKit.Box(_needle, UiKit.Accent);
            Disc(dial, "Hub", l.Hub, new Color(0.09f, 0.1f, 0.11f, 1f));
            Disc(dial, "HubRim", l.Hub, UiKit.SteelDim, Ring(0.1f));

            _gearRect = UiKit.Rect("Gear", dial, mid, mid, new Vector2(0f, l.GearY), new Vector2(l.GearW, l.GearH));
            _gear = UiKit.Label(_gearRect, "N", l.GearFont, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            _unit = UiKit.Label(UiKit.Rect("Unit", dial, mid, mid, new Vector2(0f, l.UnitY), new Vector2(100f, 16f)),
                "x1000 rpm", l.UnitFont, UiKit.TextDim, TextAnchor.MiddleCenter);

            float x0 = l.DialX + D + 18f, w = l.W - x0 - 16f;
            var left = new Vector2(0f, 0.5f);
            // Shift lights: steel -> amber -> red, all red and blinking at the shift point. Over the dial (side) or
            // along the top of the readout (centre).
            for (int i = 0; i < Lights; i++)
            {
                float t = (i - (Lights - 1) / 2f) / (Lights - 1);
                RectTransform led;
                if (l.LightsOverDial)
                    led = UiKit.Rect("Light" + i, Root, left, mid, new Vector2(l.DialX + D / 2f + t * 120f, l.H / 2f - 14f - Mathf.Abs(t) * 8f), new Vector2(14f, 8f));
                else
                    led = UiKit.Rect("Light" + i, Root, left, mid, new Vector2(x0 + 8f + i * 22f, l.H / 2f - 12f), new Vector2(16f, 7f));
                _lights[i] = UiKit.Box(led, Cold);
            }

            // ---- Speed and bars ----
            _speed = HudDigits.Build("Speed", Root, left, left, new Vector2(x0, l.DigitY), 3, l.DigitCell, l.DigitH, l.DigitFont, UiKit.Text);
            _speed.Set(0);
            float after = x0 + 3f * l.DigitCell + 8f;
            UiKit.Label(UiKit.Rect("Kmh", Root, left, left, new Vector2(after, l.KmhY), new Vector2(70f, 24f)), "km/h", l.KmhFont, UiKit.TextDim);
            _mode = UiKit.Label(UiKit.Rect("Mode", Root, left, left, new Vector2(after, l.ModeY), new Vector2(90f, 22f)), "AUTO", l.ModeFont, UiKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);

            var speedTrack = UiKit.Rect("SpeedTrack", Root, left, left, new Vector2(x0, l.SpeedBarY), new Vector2(w, 6f));
            UiKit.Box(speedTrack, UiKit.SteelDim);
            _speedFill = UiKit.Fill("Fill", speedTrack);
            _speedFill.anchorMax = new Vector2(0f, 1f);
            _speedFillImage = UiKit.Box(_speedFill, UiKit.Steel);

            Bar("Fuel", "연료", x0, l.FuelY, w, out _fuelFill, out _fuelImage, out _fuelText);
            Bar("Body", "차체", x0, l.BodyY, w, out _bodyFill, out _bodyImage, out _bodyText);
        }

        public void Tick(Vehicle v)
        {
            var r = v.Rules;
            float redline = v.RedlineRpm;
            if (!Mathf.Approximately(redline, _shownRedline)) Retick(redline, r.Gearbox != null ? r.Gearbox.ShiftUpShare : 0.92f);

            // Needle and arc: the dial runs to the next whole thousand over the redline.
            float max = Mathf.Ceil(redline / 1000f + 0.5f) * 1000f;
            float rpm01 = Mathf.Clamp01(v.EngineRpm / max);
            _needle.localRotation = Quaternion.Euler(0f, 0f, Sweep / 2f - Sweep * rpm01);
            _rpmArc.fillAmount = rpm01 * Sweep / 360f;
            float shiftAt = redline * (r.Gearbox != null ? r.Gearbox.ShiftUpShare : 0.92f);
            _rpmArc.color = v.EngineRpm >= shiftAt ? UiKit.Danger : Color.Lerp(UiKit.Steel, Lit, Mathf.InverseLerp(redline * 0.5f, shiftAt, v.EngineRpm));

            // Gear: R / N / 1..6, punched up for a moment on a change.
            string gear = v.Gear < 0 ? "R" : v.Gear == 0 ? "N" : HudDigits.IntText(v.Gear);
            if (_gear.text != gear) _gear.text = gear;
            if (v.ShiftCount != _shiftSeen) { if (_shiftSeen != int.MinValue) _punchAt = Time.unscaledTime; _shiftSeen = v.ShiftCount; }
            float p = Mathf.Clamp01((Time.unscaledTime - _punchAt) / 0.25f);
            _gearRect.localScale = Vector3.one * (1f + 0.3f * (1f - p) * (1f - p));
            _gear.color = p < 1f ? Lit : v.Gear < 0 ? UiKit.Accent : UiKit.Text;

            // Shift lights from 65% of the shift point; all red, blinking, at it.
            float from = shiftAt * 0.65f;
            float lit01 = Mathf.InverseLerp(from, shiftAt, v.EngineRpm);
            bool at = v.EngineRpm >= shiftAt && v.Gear > 0;
            bool blinkOn = Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.5f;
            int lit = at ? Lights : Mathf.FloorToInt(lit01 * Lights + 0.0001f);
            LightsLit = at && !blinkOn ? 0 : lit;
            for (int i = 0; i < Lights; i++)
            {
                // Lit from the outside in, as a pair, so the row closes on the middle.
                int rank = Mathf.Min(i, Lights - 1 - i);
                bool on = at ? blinkOn : rank < Mathf.CeilToInt(lit / 2f);
                var lamp = at ? UiKit.Danger : rank == Lights / 2 ? UiKit.Danger : rank >= 2 ? Lit : new Color(0.55f, 0.85f, 0.6f, 1f);
                _lights[i].color = on ? lamp : Cold;
            }

            // Speed.
            _speed.Set(Mathf.RoundToInt(v.SpeedKmh));
            float top = Vehicles.Kmh(r.BoostMaxKmh);
            _speedFill.anchorMax = new Vector2(Mathf.Clamp01(Mathf.Abs(v.Speed) / Mathf.Max(1f, top)), 1f);
            _speedFillImage.color = v.SpeedKmh > r.MaxSpeedKmh ? Lit : UiKit.Steel;
            string mode = v.Gear < 0 ? "REVERSE" : v.Shifting ? "SHIFT" : "AUTO";
            if (_mode.text != mode) _mode.text = mode;

            // Fuel and body.
            var state = v.State;
            float fuel = state != null ? state.Fuel01 : 1f, body = state != null ? state.Health01 : 1f;
            int percent = Fuel.Percent(fuel);
            if (percent != FuelShown)
            {
                FuelShown = percent;
                _fuelFill.anchorMax = new Vector2(Mathf.Clamp01(fuel), 1f);
                _fuelText.text = percent > 0 ? HudDigits.IntText(percent) + "%" : "없음";
            }
            bool low = fuel < FuelRules.Current.LowShare;
            if (low != FuelLowShown)
            {
                FuelLowShown = low;
                _fuelImage.color = low ? UiKit.Danger : UiKit.Health;
                _fuelText.color = low ? UiKit.Danger : UiKit.Text;
            }
            int c = Mathf.Clamp(Mathf.RoundToInt(body * 100f), 0, 100);
            if (c != ConditionShown)
            {
                ConditionShown = c;
                _bodyFill.anchorMax = new Vector2(Mathf.Clamp01(body), 1f);
                _bodyText.text = HudDigits.IntText(c) + "%";
                _bodyImage.color = body < 0.3f ? UiKit.Danger : UiKit.Health;
                _bodyText.color = body < 0.3f ? UiKit.Danger : UiKit.Text;
            }
        }

        // The ticks, numbers and red zone for this redline (a car change re-draws them).
        void Retick(float redline, float shiftShare)
        {
            _shownRedline = redline;
            foreach (var t in _ticks) Object.Destroy(t);
            _ticks.Clear();
            float max = Mathf.Ceil(redline / 1000f + 0.5f) * 1000f;
            float radius = _l.D / 2f - _l.TickInset;
            for (int k = 0; k <= Mathf.RoundToInt(max / 500f); k++)
            {
                float rpm = k * 500f;
                bool major = k % 2 == 0;
                float deg = Sweep * (rpm / max) - Sweep / 2f;   // clockwise from 12 o'clock
                var dir = new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad));
                float len = major ? _l.TickMajor : _l.TickMinor;
                var tick = UiKit.Rect("Tick" + k, _dial, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dir * (radius - len / 2f), new Vector2(major ? 3f : 2f, len));
                tick.localRotation = Quaternion.Euler(0f, 0f, -deg);
                UiKit.Box(tick, rpm >= redline ? UiKit.Danger : major ? UiKit.Text : UiKit.TextDim);
                _ticks.Add(tick.gameObject);
                if (!major) continue;
                var num = UiKit.Rect("Num" + k, _dial, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dir * (radius - _l.NumInset), new Vector2(30f, 20f));
                UiKit.Label(num, HudDigits.IntText(Mathf.RoundToInt(rpm / 1000f)), _l.NumFont, rpm >= redline ? UiKit.Danger : UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                _ticks.Add(num.gameObject);
            }
            // The red zone from the redline to the dial's end; the arc fills clockwise from bottom-left.
            float red0 = redline / max;
            _redArc.fillAmount = (1f - red0) * Sweep / 360f;
            _redArc.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Sweep / 2f - 180f - Sweep * red0);
        }

        void Bar(string name, string caption, float x, float y, float w, out RectTransform fill, out Image image, out Text value)
        {
            var left = new Vector2(0f, 0.5f);
            UiKit.Label(UiKit.Rect(name + "Caption", Root, left, left, new Vector2(x, y), new Vector2(44f, _l.BarH)), caption, _l.BarFont, UiKit.TextDim);
            var track = UiKit.Rect(name + "Track", Root, left, left, new Vector2(x + 46f, y), new Vector2(w - 46f - 58f, 8f));
            UiKit.Box(track, UiKit.SteelDim);
            fill = UiKit.Fill("Fill", track);
            image = UiKit.Box(fill, UiKit.Health);
            value = UiKit.Label(UiKit.Rect(name + "Value", Root, left, left, new Vector2(x + w - 54f, y), new Vector2(56f, _l.BarH)), "", _l.BarFont + 1, UiKit.Text, TextAnchor.MiddleRight);
        }

        // ---- Drawn shapes ----

        static Image Disc(RectTransform parent, string name, float size, Color color, Sprite sprite = null)
        {
            var rect = UiKit.Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var image = UiKit.Box(rect, color);
            image.sprite = sprite != null ? sprite : Ring(0.5f);
            return image;
        }

        // A ring filled clockwise from the dial's start (bottom-left): fillAmount 1 = the whole circle.
        static Image Arc(RectTransform parent, string name, float size, Sprite ring, Color color, float fill)
        {
            var rect = UiKit.Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            rect.localRotation = Quaternion.Euler(0f, 0f, Sweep / 2f - 180f);   // the fill's start (6 o'clock) turned to bottom-left
            var image = UiKit.Box(rect, color);
            image.sprite = ring;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Bottom;
            image.fillClockwise = true;
            image.fillAmount = fill * Sweep / 360f;
            return image;
        }

        static readonly System.Collections.Generic.Dictionary<int, Sprite> Rings = new System.Collections.Generic.Dictionary<int, Sprite>();

        // A white anti-aliased ring `thickness` (share of the radius) wide at the edge; 0.5 or more is a full disc.
        static Sprite Ring(float thickness)
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
                float aa = 2f / n;
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
