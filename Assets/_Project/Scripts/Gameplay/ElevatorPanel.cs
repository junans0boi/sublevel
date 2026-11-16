using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Region-to-region elevator (ADR-076, #76). While the local player stands in a station after the deployment:
    // a panel under the region card lists every region with its lockdown state and time, which ones the elevator
    // can reach, and the selected destination (F tap = next, F held = ride). While riding: a band shows
    // the destination and the seconds left (the car itself moves, ADR-079). PlayerController calls the static
    // input and movement hooks.
    public sealed class ElevatorPanel
    {
        public const float Width = 320f, RowHeight = 30f, HeaderHeight = 36f, FooterHeight = 34f;

        // The destination region the local player has selected (-1 none). Kept while it stays a choice.
        public static int Selected { get; private set; } = -1;
        // 0..1 of the F hold toward departing.
        public static float Hold01 { get; private set; }

        static float _heldFor;
        static bool _wasHeld, _fired;
        static readonly List<int> Choices = new List<int>();
        static readonly List<ZoneState> States = new List<ZoneState>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Selected = -1;
            Hold01 = _heldFor = 0f;
            _wasHeld = _fired = false;
        }

        // The local player's station when the elevator can be used there now, else -1.
        public static int LocalStation(Combatant me)
        {
            var rides = ElevatorRides.Instance;
            if (rides == null || me == null || me.Dead.Value || !rides.Running || rides.LocalRiding) return -1;
            return rides.StationAt(me.transform.position);
        }

        // In transit: input is blocked like in the deployment car.
        public static bool Riding => ElevatorRides.Instance != null && ElevatorRides.Instance.LocalRiding;

        // Feet position this frame on a moving car floor: the local rider's car (moved across once at the bottom,
        // `switched`, with the way out to face), or any station car someone merely stands in during a ride cycle.
        public static bool TryHold(Vector3 at, out Vector3 feet, out bool switched, out float yawDeg)
        {
            feet = at;
            switched = false;
            yawDeg = 0f;
            var rides = ElevatorRides.Instance;
            if (rides == null) return false;
            if (rides.TryLocalRide(at, out feet, out switched, out yawDeg)) return true;
            if (!rides.TryCarFloor(at, out float y)) return false;
            feet = new Vector3(at.x, y, at.z);
            return true;
        }

        // F in a station: tap (released before the hold time) = next destination, held = ride. True when the key
        // was the elevator's this frame (no loot pickup then).
        public static bool HandleInteract(Combatant me, bool pressed, bool held, float deltaTime)
        {
            var rides = ElevatorRides.Instance;
            int station = LocalStation(me);
            // Next to a knocked teammate F held revives (ADR-072): that wins over the elevator.
            if (station < 0 || me.Reviving || TeamHud.ReviveCandidate(me) != null)
            {
                _heldFor = Hold01 = 0f;
                _wasHeld = _fired = false;
                return false;
            }
            rides.States(States);
            ElevatorTravel.Destinations(rides.RegionOfStation(station), States, Choices);
            Selected = ElevatorTravel.KeepSelection(Selected, Choices);
            bool usable = Selected >= 0 && rides.LocalCooldownLeft <= 0f && !rides.LocalPending;

            bool consumed = Choices.Count > 0 && (pressed || held || _wasHeld);
            if (held && usable && !_fired)
            {
                _heldFor += deltaTime;
                float need = Mathf.Max(0.05f, DeploymentRules.Current.TravelHoldSeconds);
                Hold01 = Mathf.Clamp01(_heldFor / need);
                if (_heldFor >= need)
                {
                    _fired = true;
                    rides.RequestRide(station, Selected);
                }
            }
            else if (!held)
            {
                // A short press that did not ride: the next destination.
                if (_wasHeld && !_fired) Selected = ElevatorTravel.NextDestination(Selected, Choices);
                _heldFor = Hold01 = 0f;
                _fired = false;
            }
            _wasHeld = held;
            return consumed;
        }

        // ---- HUD ----

        public RectTransform Root { get; private set; }
        public GameObject Transit { get; private set; }

        RectTransform _holdFill;
        Text _footer, _transitText;
        readonly List<Text> _names = new List<Text>(), _states = new List<Text>(), _marks = new List<Text>();
        readonly List<Image> _rowBacks = new List<Image>();
        Transform _rows;
        // Last shown values: texts are rebuilt only when they change (no garbage on a steady frame).
        readonly List<int> _rowKeys = new List<int>();
        int _transitKey = int.MinValue, _footerKey = int.MinValue;

        public bool Shown => Root != null && Root.gameObject.activeSelf;
        public bool TransitShown => Transit != null && Transit.activeSelf;
        public string FooterText => _footer != null ? _footer.text : "";
        public string TransitText => _transitText != null ? _transitText.text : "";
        public string RowText(int region) =>
            region >= 0 && region < _names.Count ? _names[region].text + " | " + _states[region].text + " | " + _marks[region].text : "";

        public static ElevatorPanel Build(Transform canvasRoot, Vector2 anchor, Vector2 position)
        {
            var p = new ElevatorPanel();
            var root = UiKit.Rect("Elevator", canvasRoot, anchor, anchor, position, new Vector2(Width, HeaderHeight + FooterHeight));
            UiKit.Box(root, UiKit.Panel);
            UiKit.Frame(root, 1f, UiKit.SteelDim);
            UiKit.Brackets(root, 10f, 2f, UiKit.Steel);
            var topLeft = new Vector2(0f, 1f);
            UiKit.Label(UiKit.Rect("Caption", root, topLeft, topLeft, new Vector2(14f, -6f), new Vector2(290f, 26f)), "엘리베이터 · 지역 현황", 18, UiKit.TextDim);
            p._rows = root;
            var bottomLeft = new Vector2(0f, 0f);
            p._footer = UiKit.Label(UiKit.Rect("Footer", root, bottomLeft, bottomLeft, new Vector2(14f, 6f), new Vector2(292f, 24f)), "", 17, UiKit.Text);
            var track = UiKit.Rect("Hold", root, bottomLeft, bottomLeft, new Vector2(0f, 0f), new Vector2(Width, 3f));
            p._holdFill = UiKit.Fill("Fill", track);
            p._holdFill.anchorMax = new Vector2(0f, 1f);
            UiKit.Box(p._holdFill, UiKit.Accent);
            p.Root = root;
            root.gameObject.SetActive(false);

            // The ride (the car itself goes down and up, ADR-079): a band above the centre with where it goes.
            var band = new Vector2(0.5f, 0.5f);
            var transit = UiKit.Rect("ElevatorTransit", canvasRoot, band, band, new Vector2(0f, 200f), new Vector2(520f, 84f));
            UiKit.Box(transit, UiKit.Panel);
            UiKit.Frame(transit, 1f, UiKit.SteelDim);
            p._transitText = UiKit.Label(UiKit.Fill("Text", transit), "", 24, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            p.Transit = transit.gameObject;
            p.Transit.SetActive(false);
            return p;
        }

        void EnsureRows(int count)
        {
            while (_names.Count < count)
            {
                int i = _names.Count;
                var topLeft = new Vector2(0f, 1f);
                var row = UiKit.Rect("Row" + i, _rows, topLeft, topLeft, new Vector2(4f, -HeaderHeight - i * RowHeight), new Vector2(Width - 8f, RowHeight));
                _rowBacks.Add(UiKit.Box(row, new Color(0f, 0f, 0f, 0f)));
                _rowKeys.Add(int.MinValue);
                _names.Add(UiKit.Label(UiKit.Rect("Name", row, topLeft, topLeft, new Vector2(10f, 0f), new Vector2(130f, RowHeight)), "", 19, UiKit.Text));
                _states.Add(UiKit.Label(UiKit.Rect("State", row, topLeft, topLeft, new Vector2(140f, 0f), new Vector2(100f, RowHeight)), "", 19, UiKit.Text));
                _marks.Add(UiKit.Label(UiKit.Rect("Mark", row, topLeft, topLeft, new Vector2(236f, 0f), new Vector2(70f, RowHeight)), "", 17, UiKit.TextDim, TextAnchor.MiddleRight));
            }
            for (int i = 0; i < _names.Count; i++) _rowBacks[i].transform.parent.gameObject.SetActive(i < count);
            Root.sizeDelta = new Vector2(Width, HeaderHeight + count * RowHeight + FooterHeight);
        }

        public void Tick(Combatant me)
        {
            var rides = ElevatorRides.Instance;
            bool riding = rides != null && rides.LocalRiding;
            if (Transit.activeSelf != riding) Transit.SetActive(riding);
            if (riding)
            {
                int key = rides.LocalDestination * 1000 + Mathf.CeilToInt(rides.LocalSecondsLeft);
                if (key != _transitKey)
                {
                    _transitKey = key;
                    _transitText.text = "엘리베이터 이동 중\n" + RegionName(rides.LocalDestination) + " · " + Mathf.CeilToInt(rides.LocalSecondsLeft) + "초";
                }
            }

            int station = riding ? -1 : LocalStation(me);
            bool on = station >= 0;
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
            if (!on) return;

            rides.States(States);
            int here = rides.RegionOfStation(station);
            ElevatorTravel.Destinations(here, States, Choices);
            int selected = ElevatorTravel.KeepSelection(Selected, Choices);
            EnsureRows(States.Count);
            var zones = PollutionDirector.Instance;
            for (int r = 0; r < States.Count; r++)
            {
                var state = States[r];
                float left = zones != null ? zones.SecondsLeft(r) : -1f;
                int rowKey = (int)state * 100000 + (left >= 0f ? Mathf.CeilToInt(left) : 99999);
                if (rowKey != _rowKeys[r])
                {
                    _rowKeys[r] = rowKey;
                    _names[r].text = RegionName(r);
                    _states[r].text = ZoneStatus.Label(state) + (left >= 0f ? " " + Clock(left) : "");
                    _states[r].color = StateColour(state);
                }
                bool reachable = Choices.Contains(r);
                _marks[r].text = r == here ? "현재" : reachable ? (r == selected ? "▶ 선택" : "운행") : "중지";
                _marks[r].color = r == selected ? UiKit.Accent : reachable ? UiKit.Text : UiKit.TextDim;
                _rowBacks[r].color = r == selected ? new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.18f) : new Color(0f, 0f, 0f, 0f);
            }

            int cooldown = Mathf.CeilToInt(rides.LocalCooldownLeft);
            int footerKey = !ElevatorTravel.Runs(here >= 0 && here < States.Count ? States[here] : ZoneState.Safe) ? -1
                : Choices.Count == 0 ? -2 : cooldown;
            if (footerKey != _footerKey)
            {
                _footerKey = footerKey;
                _footer.text = footerKey == -1 ? "운행 중지 · 봉쇄된 지역"
                    : footerKey == -2 ? "갈 수 있는 지역 없음"
                    : cooldown > 0 ? "재사용 대기 " + cooldown + "초"
                    : "F 목적지 변경 · F 길게 출발";
            }
            _holdFill.anchorMax = new Vector2(Hold01, 1f);
        }

        public static string RegionName(int region)
        {
            var zones = PollutionDirector.Instance;
            if (zones != null && zones.RegionNameOf(region).Length > 0) return zones.RegionNameOf(region);
            var world = WorldInfo.Current;
            return world != null && region >= 0 && region < world.Regions.Count ? world.Regions[region].Name : "지역 " + (region + 1);
        }

        // 0:32, 2:05
        public static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        static Color StateColour(ZoneState state) =>
            state == ZoneState.Safe ? UiKit.Text : state == ZoneState.Warning ? UiKit.Accent : UiKit.Danger;
    }
}
