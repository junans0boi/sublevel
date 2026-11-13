using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The vehicle readout (ADR-075): while the local player sits in a vehicle, one plate above the bottom-centre
    // stack with the speed (km/h), the fuel gauge (red when low, "연료 없음" when empty) and the vehicle's condition.
    // Above it the jerrycan line: "J 제리캔 주유" when a can could go in here, the pour's progress while pouring.
    // Installs itself next to every match like the kill feed and the team HUD; nothing refers to it.
    public class VehicleHud : MonoBehaviour
    {
        const int SortOrder = 1;
        // Above the HUD's bottom-centre stack: health 36+44, weapon +8+112, reload +8+32, pickup prompt +8+52.
        const float PlateY = 36f + 44f + 8f + 112f + 8f + 32f + 8f + 52f + 8f;
        const float Width = 560f, Height = 52f, CanHeight = 32f, Gap = 8f;
        const float BarWidth = 150f, BarHeight = 8f;

        public static VehicleHud Instance { get; private set; }

        // ---- Read-only state for tests ----
        public bool PlateShown => _canvas != null && _canvas.enabled && _plate.activeSelf;
        public int SpeedShown => _speed.Value;
        public int FuelShown { get; private set; } = -1;
        public int ConditionShown { get; private set; } = -1;
        public bool FuelLowShown { get; private set; }
        public bool JerrycanShown => _canvas != null && _canvas.enabled && _can.activeSelf;
        public string JerrycanText => _canLabel != null ? _canLabel.text : "";

        Canvas _canvas;
        GameObject _plate, _can, _canBar;
        HudDigits _speed;
        RectTransform _fuelFill, _conditionFill, _canFill;
        Image _fuelFillImage;
        Text _fuelText, _conditionText, _canLabel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            MatchState.Started -= Install;
            MatchState.Started += Install;
        }

        static void Install(MatchState state)
        {
            if (Instance != null || state == null) return;
            var go = new GameObject("VehicleHud");
            // Under the match object so it goes away with the scene.
            go.transform.SetParent(state.transform, false);
            go.AddComponent<VehicleHud>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Build();
            _canvas.enabled = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            var me = Combatant.LocalPlayer;
            bool alive = me != null && !me.Dead.Value;
            bool drawn = alive && UiFocus.Current != UiScreen.Inventory && UiFocus.Current != UiScreen.Map;
            Vehicle vehicle = null;
            if (drawn) VehicleSeat.TryGet(me, out vehicle, out _);
            var cans = drawn ? CombatantJerrycan.Local : null;
            bool pouring = cans != null && cans.Using;
            bool canPour = cans != null && !pouring && cans.Count.Value > 0 && CombatantJerrycan.TargetFor(me) != null;

            drawn &= vehicle != null || pouring || canPour;
            if (_canvas.enabled != drawn) _canvas.enabled = drawn;
            if (!drawn) return;

            Show(_plate, vehicle != null);
            if (vehicle != null) TickPlate(vehicle);

            Show(_can, pouring || canPour);
            Show(_canBar, pouring);
            if (pouring)
            {
                SetText(_canLabel, "제리캔 주유 중  ·  J 취소");
                _canFill.anchorMax = new Vector2(Mathf.Clamp01(cans.Progress01), 1f);
            }
            else if (canPour) SetText(_canLabel, "J  제리캔 주유  (" + HudDigits.IntText(cans.Count.Value) + ")");
        }

        void TickPlate(Vehicle vehicle)
        {
            _speed.Set(Mathf.RoundToInt(vehicle.SpeedKmh));
            var state = vehicle.State;
            float fuel = state != null ? state.Fuel01 : 1f;
            float condition = state != null ? state.Health01 : 1f;

            int percent = Fuel.Percent(fuel);
            if (percent != FuelShown)
            {
                FuelShown = percent;
                _fuelFill.anchorMax = new Vector2(Mathf.Clamp01(fuel), 1f);
                _fuelText.text = percent > 0 ? HudDigits.IntText(percent) + "%" : "연료 없음";
            }
            bool low = fuel < FuelRules.Current.LowShare;
            if (low != FuelLowShown)
            {
                FuelLowShown = low;
                _fuelFillImage.color = low ? UiKit.Danger : UiKit.Health;
                _fuelText.color = low ? UiKit.Danger : UiKit.Text;
            }

            int c = Mathf.Clamp(Mathf.RoundToInt(condition * 100f), 0, 100);
            if (c != ConditionShown)
            {
                ConditionShown = c;
                _conditionFill.anchorMax = new Vector2(Mathf.Clamp01(condition), 1f);
                _conditionText.text = HudDigits.IntText(c) + "%";
                _conditionText.color = condition < 0.3f ? UiKit.Danger : UiKit.Text;
            }
        }

        static void Show(GameObject go, bool on)
        {
            if (go.activeSelf != on) go.SetActive(on);
        }

        static void SetText(Text text, string s)
        {
            if (text.text != s) text.text = s;
        }

        // ---- Construction ----

        void Build()
        {
            _canvas = UiKit.Canvas("VehicleHudCanvas", SortOrder, false);
            _canvas.transform.SetParent(transform, false);
            var root = _canvas.transform;
            var bottom = new Vector2(0.5f, 0f);
            var left = new Vector2(0f, 0.5f);

            var plate = Panel("Vehicle", root, bottom, new Vector2(0f, PlateY), new Vector2(Width, Height));
            _speed = HudDigits.Build("Speed", plate, left, left, new Vector2(12f, 0f), 3, 20f, 40f, 28, UiKit.Text);
            _speed.Set(0);
            Label(plate, "Kmh", new Vector2(76f, 0f), new Vector2(50f, 30f), "km/h", 16, UiKit.TextDim);
            Gauge(plate, "Fuel", 136f, "연료", out _fuelFill, out _fuelFillImage, out _fuelText);
            Gauge(plate, "Condition", 348f, "차체", out _conditionFill, out _, out _conditionText);
            _plate = plate.gameObject;

            var can = Panel("Jerrycan", root, bottom, new Vector2(0f, PlateY + Height + Gap), new Vector2(360f, CanHeight));
            _canLabel = UiKit.Label(UiKit.Rect("Label", can, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), new Vector2(340f, 26f)),
                "", 18, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            var track = UiKit.Rect("Progress", can, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(340f, 3f));
            UiKit.Box(track, UiKit.SteelDim);
            _canFill = UiKit.Fill("Fill", track);
            _canFill.anchorMax = new Vector2(0f, 1f);
            UiKit.Box(_canFill, UiKit.Accent);
            _canBar = track.gameObject;
            _can = can.gameObject;
        }

        // Caption, bar and percent, 200 wide, starting at x.
        static void Gauge(RectTransform plate, string name, float x, string caption, out RectTransform fill, out Image fillImage, out Text value)
        {
            var left = new Vector2(0f, 0.5f);
            Label(plate, name + "Caption", new Vector2(x, 0f), new Vector2(40f, 30f), caption, 16, UiKit.TextDim);
            var track = UiKit.Rect(name + "Track", plate, left, left, new Vector2(x + 42f, 0f), new Vector2(BarWidth - 42f, BarHeight));
            UiKit.Box(track, UiKit.SteelDim);
            fill = UiKit.Fill("Fill", track);
            fillImage = UiKit.Box(fill, UiKit.Health);
            value = Label(plate, name + "Value", new Vector2(x + BarWidth + 4f, 0f), new Vector2(54f, 30f), "", 18, UiKit.Text);
        }

        static Text Label(RectTransform parent, string name, Vector2 position, Vector2 size, string text, int fontSize, Color color)
        {
            var left = new Vector2(0f, 0.5f);
            return UiKit.Label(UiKit.Rect(name, parent, left, left, position, size), text, fontSize, color);
        }

        static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = UiKit.Rect(name, parent, anchor, anchor, position, size);
            UiKit.Box(rect, UiKit.Panel);
            UiKit.Frame(rect, 1f, UiKit.SteelDim);
            return rect;
        }
    }
}
