using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The vehicle readout (ADR-075, ADR-558): while the local player sits in a vehicle, the instrument cluster either side of
    // the health bar (VehicleCluster: the model's style of tachometer, gear, shift lights, speed, fuel, body).
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
        public bool PlateShown => _canvas != null && _canvas.enabled && _cluster != null && _cluster.Root.gameObject.activeSelf;
        public int SpeedShown => _cluster != null ? _cluster.SpeedShown : 0;
        public int FuelShown => _cluster != null ? _cluster.FuelShown : -1;
        public int ConditionShown => _cluster != null ? _cluster.ConditionShown : -1;
        public bool FuelLowShown => _cluster != null && _cluster.FuelLowShown;
        // The cluster on screen, built for the vehicle's model (rebuilt on a change of car); null before the first one.
        public VehicleCluster Cluster => _cluster;
        public bool JerrycanShown => _canvas != null && _canvas.enabled && _can.activeSelf;
        public string JerrycanText => _canLabel != null ? _canLabel.text : "";

        Canvas _canvas;
        GameObject _can, _canBar;
        VehicleCluster _cluster;
        RectTransform _canFill;
        Text _canLabel;

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
            // An event race's results table takes the screen (ADR-519): the speed, fuel and body bars go with the race.
            drawn &= !(Vehicle.EventRace && (RaceHud.Phase == Game.Core.RacePhase.Results || RaceHud.Phase == Game.Core.RacePhase.Over));
            if (_canvas.enabled != drawn) _canvas.enabled = drawn;
            if (!drawn) return;

            if (_cluster != null) Show(_cluster.Root.gameObject, vehicle != null);
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

        void TickPlate(Vehicle v)
        {
            var r = v.Rules;
            var row = VehicleHandling.ClusterFor(v.ModelName, v.IsMotorcycle);
            float redline = v.RedlineRpm, top = VehicleCluster.SpeedMax(r.BoostMaxKmh);
            if (_cluster == null || !_cluster.BuiltFor(row, redline, top))
            {
                if (_cluster != null) Destroy(_cluster.Root.gameObject);
                _cluster = new VehicleCluster(_canvas.transform, row, redline, r.Gearbox != null ? r.Gearbox.ShiftUpShare : 0.92f, top);
            }
            Relayout();
            _cluster.Tick(v);
        }

        // Places the gauges for the canvas's current width (a capture renders at another size between frames).
        public void Relayout()
        {
            if (_cluster != null) _cluster.Place(((RectTransform)_canvas.transform).rect.width);
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

        static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = UiKit.Rect(name, parent, anchor, anchor, position, size);
            UiKit.Box(rect, UiKit.Panel);
            UiKit.Frame(rect, 1f, UiKit.SteelDim);
            return rect;
        }
    }
}
