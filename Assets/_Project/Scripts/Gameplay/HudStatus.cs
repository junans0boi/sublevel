using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Fixed-width number: every digit sits in its own equally wide cell, so a changing value never jitters
    // whatever font is in use ("monospaced digits" of the field-terminal look). Cells are written only on change.
    public sealed class HudDigits
    {
        static readonly string[] Glyphs = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        static readonly string[] SmallInts = new string[1000];
        const string Dash = "—";

        readonly Text[] _cell;
        int _shown = int.MinValue;
        Color _color;

        HudDigits(Text[] cell, Color color)
        {
            _cell = cell;
            _color = color;
        }

        // Keep leading zeros (a heading reads 007).
        public bool Pad;
        public int Value => _shown;
        public RectTransform Root { get; private set; }

        public static HudDigits Build(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, int cells, float cellWidth,
            float height, int fontSize, Color color, FontStyle style = FontStyle.Bold)
        {
            var root = UiKit.Rect(name, parent, anchor, pivot, position, new Vector2(cells * cellWidth, height));
            var text = new Text[cells];
            var left = new Vector2(0f, 0.5f);
            for (int i = 0; i < cells; i++)
            {
                var rect = UiKit.Rect("D" + i, root, left, left, new Vector2(i * cellWidth, 0f), new Vector2(cellWidth, height));
                text[i] = UiKit.Label(rect, "", fontSize, color, TextAnchor.MiddleCenter, style);
            }
            return new HudDigits(text, color) { Root = root };
        }

        // Negative shows a dash. Values too large for the cells show all nines.
        public void Set(int value)
        {
            if (value == _shown) return;
            _shown = value;
            int cells = _cell.Length;
            if (value < 0)
            {
                for (int i = 0; i < cells; i++) Write(i, i == cells - 1 ? Dash : "");
                return;
            }
            int max = 1;
            for (int i = 0; i < cells; i++) max *= 10;
            if (value >= max) value = max - 1;
            for (int i = cells - 1; i >= 0; i--)
            {
                bool used = Pad || value > 0 || i == cells - 1;
                Write(i, used ? Glyphs[value % 10] : "");
                value /= 10;
            }
        }

        public void SetColor(Color color)
        {
            if (color == _color) return;
            _color = color;
            for (int i = 0; i < _cell.Length; i++) _cell[i].color = color;
        }

        void Write(int i, string s)
        {
            if (_cell[i].text != s) _cell[i].text = s;
        }

        // Cached so the numbers that change most never allocate after their first use.
        public static string IntText(int value)
        {
            if (value < 0 || value >= SmallInts.Length) return value.ToString();
            return SmallInts[value] ?? (SmallInts[value] = value.ToString());
        }
    }

    // Bottom left: how the local player stands (stance glyph, armour tiers and wear, spawn protection, medkits).
    // Only what the server replicated; text is rewritten when a value changes.
    // [RESERVED] Squad member list (team mode, M5): the block above this card is left free on purpose. Not rendered.
    public sealed class HudStatus
    {
        public const float Width = 320f, Height = 200f;
        const int Pips = 3;

        // After the three stances: in a vehicle (ADR-075), the driver's seat or any other seat.
        public const int DrivingShown = 3, RidingShown = 4;
        static readonly string[] StanceNames = { "서기", "앉기", "엎드림", "운전", "탑승" };

        public RectTransform Root { get; private set; }

        // Boost gauge (ADR-099): four segments filling bottom up in steps, and how many boost items are carried.
        readonly RectTransform[] _boostFill = new RectTransform[Boost.Steps];
        readonly Image[] _boostImage = new Image[Boost.Steps];
        Text _boostCount;
        int _shownBoostKey = int.MinValue;
        int _boostItemsShown = -1;
        public float BoostFillShown(int step) => _boostFill[step].anchorMax.x;
        public int BoostItemsShown => _boostItemsShown;

        RectTransform _glyphA, _glyphB;
        Text _stanceLabel, _protectLabel, _medkitLabel;
        readonly Image[][] _pip = new Image[2][];
        readonly Text[] _gearValue = new Text[2];
        readonly RectTransform[] _gearFill = new RectTransform[2];
        readonly int[] _shownGearHash = { int.MinValue, int.MinValue };
        readonly int[] _shownTier = { -1, -1 };
        int _shownStance = -1, _shownProtect = int.MinValue, _shownMedkits = int.MinValue;

        public int StanceShown => _shownStance;           // Stance as int
        public int ProtectionSecondsShown => _shownProtect > 0 ? _shownProtect : 0;
        public int MedkitsShown => _shownMedkits;
        public int ArmourTierShown(int slot) => _shownTier[slot]; // -1 before the first tick, 0 = nothing worn

        public static HudStatus Build(Transform parent, Vector2 anchor, Vector2 position)
        {
            var s = new HudStatus();
            var root = UiKit.Rect("Status", parent, anchor, anchor, position, new Vector2(Width, Height));
            UiKit.Box(root, UiKit.Panel);
            UiKit.Frame(root, 1f, UiKit.SteelDim);
            s.Root = root;
            var topLeft = new Vector2(0f, 1f);

            // Stance glyph: plain bars, no figure.
            var glyph = UiKit.Rect("Glyph", root, topLeft, topLeft, new Vector2(14f, -10f), new Vector2(32f, 32f));
            s._glyphA = UiKit.Rect("A", glyph, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 28f));
            UiKit.Box(s._glyphA, UiKit.Text);
            s._glyphB = UiKit.Rect("B", glyph, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 6f));
            UiKit.Box(s._glyphB, UiKit.Text);
            s._stanceLabel = UiKit.Label(UiKit.Rect("Stance", root, topLeft, topLeft, new Vector2(54f, -10f), new Vector2(90f, 32f)), StanceNames[0], 20, UiKit.Text);
            s._protectLabel = UiKit.Label(UiKit.Rect("Protect", root, topLeft, topLeft, new Vector2(140f, -10f), new Vector2(170f, 32f)), "", 18, UiKit.Accent,
                TextAnchor.MiddleRight, FontStyle.Bold);
            s._protectLabel.gameObject.SetActive(false);

            for (int slot = 0; slot < 2; slot++)
            {
                float y = -52f - slot * 36f;
                string name = slot == InventoryBackend.HelmetSlot ? "헬멧" : "조끼";
                UiKit.Label(UiKit.Rect(name, root, topLeft, topLeft, new Vector2(16f, y), new Vector2(60f, 30f)), name, 20, UiKit.TextDim);
                s._pip[slot] = new Image[Pips];
                for (int p = 0; p < Pips; p++)
                    s._pip[slot][p] = UiKit.Box(UiKit.Rect("Pip" + p, root, topLeft, topLeft, new Vector2(84f + p * 20f, y - 8f), new Vector2(14f, 14f)), UiKit.SteelDim);
                var track = UiKit.Rect("Wear", root, topLeft, topLeft, new Vector2(152f, y - 12f), new Vector2(70f, 5f));
                UiKit.Box(track, UiKit.SteelDim);
                s._gearFill[slot] = UiKit.Fill("Fill", track);
                UiKit.Box(s._gearFill[slot], UiKit.Steel);
                s._gearFill[slot].anchorMax = new Vector2(0f, 1f);
                s._gearValue[slot] = UiKit.Label(UiKit.Rect("Value", root, topLeft, topLeft, new Vector2(228f, y), new Vector2(84f, 30f)), "—", 18, UiKit.TextDim, TextAnchor.MiddleRight);
            }

            UiKit.Label(UiKit.Rect("MedkitName", root, topLeft, topLeft, new Vector2(16f, -124f), new Vector2(100f, 30f)), "구급상자", 20, UiKit.TextDim);
            s._medkitLabel = UiKit.Label(UiKit.Rect("MedkitCount", root, topLeft, topLeft, new Vector2(124f, -124f), new Vector2(70f, 30f)), "x0", 22, UiKit.TextDim,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Label(UiKit.Rect("MedkitKey", root, topLeft, topLeft, new Vector2(240f, -124f), new Vector2(64f, 30f)), "[H]", 20, UiKit.TextDim, TextAnchor.MiddleRight);
            UiKit.Label(UiKit.Rect("BoostName", root, topLeft, topLeft, new Vector2(16f, -160f), new Vector2(70f, 30f)), "부스트", 20, UiKit.TextDim);
            for (int i = 0; i < Boost.Steps; i++)
            {
                var track = UiKit.Rect("Boost" + i, root, topLeft, topLeft, new Vector2(92f + i * 50f, -170f), new Vector2(46f, 10f));
                UiKit.Box(track, UiKit.SteelDim);
                s._boostFill[i] = UiKit.Fill("Fill", track);
                s._boostFill[i].anchorMax = new Vector2(0f, 1f);
                s._boostImage[i] = UiKit.Box(s._boostFill[i], UiKit.Accent);
            }
            s._boostCount = UiKit.Label(UiKit.Rect("BoostCount", root, topLeft, topLeft, new Vector2(296f - 56f, -160f), new Vector2(60f, 30f)), "x0 [J]", 18, UiKit.TextDim, TextAnchor.MiddleRight);
            return s;
        }

        void ShowBoost(Combatant me)
        {
            var heal = me.GetComponent<CombatantHealing>();
            float gauge = heal != null ? heal.Gauge.Value : 0f;
            int items = heal != null ? heal.Drinks.Value + heal.Pills.Value + heal.Syringes.Value : 0;
            int key = Mathf.RoundToInt(gauge * 4f) * 100 + items;
            if (key == _shownBoostKey) return;
            _shownBoostKey = key;
            var rules = BoostRules.Current;
            float per = rules.MaxGauge / Boost.Steps;
            int step = Boost.Step(gauge, rules);
            for (int i = 0; i < Boost.Steps; i++)
            {
                _boostFill[i].anchorMax = new Vector2(Mathf.Clamp01((gauge - i * per) / per), 1f);
                // The steps that speed the body up read stronger.
                _boostImage[i].color = i >= 2 ? UiKit.Text : UiKit.Accent;
            }
            if (items != _boostItemsShown)
            {
                _boostItemsShown = items;
                _boostCount.text = "x" + HudDigits.IntText(items) + " [J]";
                _boostCount.color = items > 0 ? UiKit.Text : UiKit.TextDim;
            }
        }

        public void Tick(Combatant me)
        {
            ShowStance(VehicleSeat.TryGet(me, out _, out int seat) ? (seat == Vehicles.DriverSeat ? DrivingShown : RidingShown) : (int)HudBackend.StanceOf(me));

            // In the elevator: a countdown to the doors; after that the protection time.
            bool doors = HudBackend.DoorsClosed;
            float left = doors ? HudBackend.DoorSecondsLeft : HudBackend.SpawnProtectionLeft(me);
            int seconds = left > 0f ? Mathf.CeilToInt(left) : 0;
            int key = doors ? -1 - seconds : seconds;
            if (key != _shownProtect)
            {
                _shownProtect = key;
                bool on = doors || seconds > 0;
                if (_protectLabel.gameObject.activeSelf != on) _protectLabel.gameObject.SetActive(on);
                if (on) _protectLabel.text = doors ? "문 열림 " + HudDigits.IntText(seconds) : "보호 " + HudDigits.IntText(seconds) + "초";
            }

            ShowBoost(me);
            int medkits = me.Medkits.Value;
            if (medkits != _shownMedkits)
            {
                _shownMedkits = medkits;
                _medkitLabel.text = "x" + HudDigits.IntText(medkits);
                _medkitLabel.color = medkits > 0 ? UiKit.Text : UiKit.TextDim;
            }

            for (int slot = 0; slot < 2; slot++)
            {
                var gear = InventoryBackend.Gear(me, slot);
                int tier = gear.Has ? gear.Tier : 0;
                int hash = (tier * 100003) ^ (gear.Durability * 31) ^ gear.MaxDurability;
                if (hash == _shownGearHash[slot]) continue;
                _shownGearHash[slot] = hash;
                _shownTier[slot] = tier;
                for (int p = 0; p < Pips; p++)
                    _pip[slot][p].color = gear.Has && p < gear.Tier ? UiKit.Text : UiKit.SteelDim;
                _gearFill[slot].anchorMax = new Vector2(gear.Has ? Mathf.Clamp01(gear.Fraction) : 0f, 1f);
                _gearValue[slot].text = gear.Has ? HudDigits.IntText(gear.Durability) + "/" + HudDigits.IntText(gear.MaxDurability) : "—";
                _gearValue[slot].color = gear.Has ? UiKit.Text : UiKit.TextDim;
            }
        }

        void ShowStance(int stance)
        {
            if (stance == _shownStance) return;
            _shownStance = stance;
            _stanceLabel.text = stance >= 0 && stance < StanceNames.Length ? StanceNames[stance] : StanceNames[0];
            switch (stance)
            {
                case (int)Stance.Crouch:
                case DrivingShown:
                case RidingShown:
                    Place(_glyphA, new Vector2(-5f, -4f), new Vector2(8f, 18f));
                    Place(_glyphB, new Vector2(2f, -13f), new Vector2(22f, 6f));
                    _glyphB.gameObject.SetActive(true);
                    break;
                case (int)Stance.Prone:
                    Place(_glyphA, new Vector2(0f, -8f), new Vector2(30f, 8f));
                    _glyphB.gameObject.SetActive(false);
                    break;
                default:
                    Place(_glyphA, Vector2.zero, new Vector2(8f, 28f));
                    _glyphB.gameObject.SetActive(false);
                    break;
            }
        }

        static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
