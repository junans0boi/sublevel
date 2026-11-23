using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The vehicle readout (ADR-075, ADR-558): while the local player sits in a vehicle, the instrument cluster bottom right
    // (VehicleCluster: tachometer, gear, shift lights, speed, fuel, body).
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
        public bool PlateShown => _canvas != null && _canvas.enabled && (_side.Root.gameObject.activeSelf || _centre.Root.gameObject.activeSelf);
        public int SpeedShown => Cluster.SpeedShown;
        public int FuelShown => Cluster.FuelShown;
        public int ConditionShown => Cluster.ConditionShown;
        public bool FuelLowShown => Cluster.FuelLowShown;
        // The cluster on screen: in the weapon panel's place for a driver with empty hands, else over the minimap.
        public VehicleCluster Cluster => TakesWeaponPanel ? _centre : _side;
        // The local driver has nothing in hand: Hud hides its weapon panel and the cluster takes its place (ADR-558).
        public static bool TakesWeaponPanel { get; private set; }
        public bool JerrycanShown => _canvas != null && _canvas.enabled && _can.activeSelf;
        public string JerrycanText => _canLabel != null ? _canLabel.text : "";

        Canvas _canvas;
        GameObject _can, _canBar;
        VehicleCluster _side, _centre;
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
            if (Instance == this) { Instance = null; TakesWeaponPanel = false; }
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
            if (!drawn) { TakesWeaponPanel = false; return; }

            bool centre = vehicle != null && VehicleSeat.TryGet(me, out _, out int seat) && seat == 0 && me.ActiveWeaponId == 0;
            TakesWeaponPanel = centre;
            Show(_side.Root.gameObject, vehicle != null && !centre);
            Show(_centre.Root.gameObject, centre);
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

        void TickPlate(Vehicle vehicle) => Cluster.Tick(vehicle);

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

            _side = new VehicleCluster(root, false);
            _centre = new VehicleCluster(root, true);

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
