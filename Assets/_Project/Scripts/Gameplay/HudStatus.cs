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

    // Bottom centre (HUD v2, ADR-563): the vitals block. Top to bottom: the quick-use chips, a row of the armour (icon,
    // wear bar, level), the ammo (Hud puts it in `Row`) and the effect icons, the four-segment boost gauge, and the
    // health bar (Hud builds it in `HealthSlot`). Only what the server replicated; texts change only with their value.
    // Stance, spawn protection and burning are still tracked for the tests; protection and burning show as a tag at the
    // right end of the chip row.
    public sealed class HudStatus
    {
        public const float Width = 520f, Height = 140f, PadX = 10f, Inner = Width - 2f * PadX;
        public const float ChipsTop = 8f, ChipsHeight = 22f, RowTop = 36f, RowHeight = 44f;
        public const float BoostTop = 86f, BoostHeight = 8f, BoostGap = 3f, HealthTop = 100f, HealthHeight = 30f;
        // The gauge's four segments as shares of the full gauge (the approved mock: 20/40/30/10).
        public static readonly float[] BoostShares = { 0.25f, 0.25f, 0.25f, 0.25f };   // one cell per BoostRules step (master call, ADR-563)
        const float IconSize = 34f, IconGap = 8f, LowWear = 0.3f;

        // After the three stances: in a vehicle (ADR-075), the driver's seat or any other seat.
        public const int DrivingShown = 3, RidingShown = 4;

        public static readonly Color Block = new Color(8f / 255f, 11f / 255f, 15f / 255f, 0.58f);
        public static readonly Color Plate = new Color(10f / 255f, 13f / 255f, 18f / 255f, 0.72f);
        public static readonly Color PlateStrong = new Color(10f / 255f, 13f / 255f, 18f / 255f, 0.86f);
        public static readonly Color Line = new Color(160f / 255f, 175f / 255f, 190f / 255f, 0.22f);
        public static readonly Color Mute = new Color(0.424f, 0.467f, 0.514f, 1f);
        public static readonly Color BoostYellow = new Color(0.949f, 0.761f, 0.188f, 1f);
        public static readonly Color HealGreen = new Color(0.341f, 0.780f, 0.522f, 1f);
        public static readonly Color Water = new Color(0.361f, 0.784f, 0.941f, 1f);

        public RectTransform Root { get; private set; }
        public RectTransform Row { get; private set; }
        public RectTransform HealthSlot { get; private set; }

        readonly RectTransform[] _boostFill = new RectTransform[Boost.Steps];
        readonly Image[] _armourIcon = new Image[2];
        readonly RectTransform[] _wearFill = new RectTransform[2];
        readonly Image[] _wearImage = new Image[2];
        readonly Text[] _level = new Text[2];
        readonly int[] _shownGearHash = { int.MinValue, int.MinValue };
        readonly int[] _shownTier = { -1, -1 };
        readonly int[] _chipShown = { int.MinValue, int.MinValue, int.MinValue };
        readonly Text[] _chipCount = new Text[3];
        Text _protectLabel;
        GameObject _lung;
        Image _lungArc;
        CanvasGroup _healFx, _runFx;
        int _shownBoostKey = int.MinValue, _boostItemsShown = -1, _shownStep = -1;
        int _shownStance = -1, _shownProtect = int.MinValue;
        float _shownBreath = -1f;

        public float BoostFillShown(int segment) => _boostFill[segment].anchorMax.x;
        public int BoostItemsShown => _boostItemsShown;
        public int StanceShown => _shownStance;           // Stance as int
        public int ProtectionSecondsShown => _shownProtect > 0 ? _shownProtect : 0;
        public bool BurningShown => _shownProtect == int.MinValue + 1;
        public int MedkitsShown => _chipShown[0];
        public int ArmourTierShown(int slot) => _shownTier[slot]; // -1 before the first tick, 0 = nothing worn
        public bool LungShown => _lung.activeSelf;
        public float BreathShown => _shownBreath;
        public bool HealEffectLit => _healFx.alpha > 0.5f;
        public bool RunEffectLit => _runFx.alpha > 0.5f;

        static Vector2 TopLeft => new Vector2(0f, 1f);
        static Vector2 Mid => new Vector2(0.5f, 0.5f);

        // Fills `block` (Width x Height, already placed by the HUD) with its parts.
        public static HudStatus Build(RectTransform block)
        {
            var s = new HudStatus { Root = block };
            Rounded(UiKit.Box(block, Block), 6);

            // Quick-use chips.
            string[] chip = { "H " + HealRules.MedkitName, "J " + HealRules.PillName, HealRules.DrinkName };
            float x = PadX;
            for (int i = 0; i < chip.Length; i++)
            {
                var c = UiKit.Rect("Chip" + i, block, TopLeft, TopLeft, new Vector2(x, -ChipsTop), new Vector2(100f, ChipsHeight));
                Rounded(UiKit.Box(c, new Color(1f, 1f, 1f, 0.06f)), 3);
                var name = UiKit.Label(UiKit.Rect("Name", c, MidLeftV, MidLeftV, new Vector2(8f, 0f), new Vector2(200f, ChipsHeight)), chip[i], 13, UiKit.TextDim);
                float labelW = Mathf.Ceil(name.preferredWidth) + 5f, w = labelW + 26f;
                name.rectTransform.sizeDelta = new Vector2(labelW, ChipsHeight);
                c.sizeDelta = new Vector2(w, ChipsHeight);
                s._chipCount[i] = UiKit.Label(UiKit.Rect("Count", c, MidLeftV, MidLeftV, new Vector2(8f + labelW, 0f), new Vector2(20f, ChipsHeight)), "0", 14, UiKit.Text,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                x += w + 6f;
            }
            s._protectLabel = UiKit.Label(UiKit.Rect("Protect", block, Vector2.one, Vector2.one, new Vector2(-PadX, -ChipsTop), new Vector2(130f, ChipsHeight)), "", 14,
                UiKit.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            s._protectLabel.gameObject.SetActive(false);

            // The row: armour left, ammo (Hud) in the middle, effects right.
            var row = UiKit.Rect("Row", block, TopLeft, TopLeft, new Vector2(PadX, -RowTop), new Vector2(Inner, RowHeight));
            s.Row = row;
            for (int slot = 0; slot < 2; slot++)
            {
                var arm = UiKit.Rect(slot == InventoryBackend.HelmetSlot ? "Helmet" : "Vest", row, TopLeft, TopLeft, new Vector2(slot * 54f, 0f), new Vector2(44f, RowHeight));
                // The picture reaches a little above the row, as in the mock (the chips end 6 units higher).
                var icon = UiKit.Box(UiKit.Rect("Icon", arm, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 5f), new Vector2(44f, 33f)), UiKit.Health);
                icon.preserveAspect = true;
                s._armourIcon[slot] = icon;
                var track = UiKit.Rect("Wear", arm, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -29f), new Vector2(36f, 4f));
                UiKit.Box(track, new Color(1f, 1f, 1f, 0.15f));
                s._wearFill[slot] = UiKit.Fill("Fill", track);
                s._wearFill[slot].anchorMax = new Vector2(0f, 1f);
                s._wearImage[slot] = UiKit.Box(s._wearFill[slot], UiKit.Health);
                s._level[slot] = UiKit.Label(UiKit.Rect("Level", arm, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(44f, 13f)), "—", 12,
                    UiKit.TextDim, TextAnchor.MiddleCenter);
            }

            // Effects, right-aligned: breath (only in the water), boost heal (step 1+), boost speed (step 3+).
            var run = Effect(row, "Run", 0);
            s._runFx = run.gameObject.AddComponent<CanvasGroup>();
            RunGlyph(run);
            var heal = Effect(row, "Heal", 1);
            s._healFx = heal.gameObject.AddComponent<CanvasGroup>();
            UiKit.Box(UiKit.Rect("A", heal, Mid, Mid, Vector2.zero, new Vector2(16f, 5f)), HealGreen);
            UiKit.Box(UiKit.Rect("B", heal, Mid, Mid, Vector2.zero, new Vector2(5f, 16f)), HealGreen);
            var lung = Effect(row, "Breath", 2);
            s._lung = lung.gameObject;
            Disc(lung, "Back", new Vector2(40f, 40f), Vector2.zero, new Color(1f, 1f, 1f, 0.15f), VehicleCluster.Ring(0.075f));
            s._lungArc = Disc(lung, "Arc", new Vector2(40f, 40f), Vector2.zero, Water, VehicleCluster.Ring(0.075f));
            s._lungArc.type = Image.Type.Filled;
            s._lungArc.fillMethod = Image.FillMethod.Radial360;
            s._lungArc.fillOrigin = (int)Image.Origin360.Top;
            s._lungArc.fillClockwise = true;
            Disc(lung, "L", new Vector2(8f, 13f), new Vector2(-4.5f, -2f), UiKit.Health, null);
            Disc(lung, "R", new Vector2(8f, 13f), new Vector2(4.5f, -2f), UiKit.Health, null);
            UiKit.Box(UiKit.Rect("Pipe", lung, Mid, Mid, new Vector2(0f, 6f), new Vector2(2f, 8f)), UiKit.Health);
            s._lung.SetActive(false);

            // Boost gauge: four segments sized by their share of the gauge.
            float usable = Inner - BoostGap * (Boost.Steps - 1);
            x = PadX;
            for (int i = 0; i < Boost.Steps; i++)
            {
                float w = usable * BoostShares[i];
                var track = UiKit.Rect("Boost" + i, block, TopLeft, TopLeft, new Vector2(x, -BoostTop), new Vector2(w, BoostHeight));
                UiKit.Box(track, new Color(1f, 1f, 1f, 0.1f));
                s._boostFill[i] = UiKit.Fill("Fill", track);
                s._boostFill[i].anchorMax = new Vector2(0f, 1f);
                UiKit.Box(s._boostFill[i], BoostYellow);
                x += w + BoostGap;
            }

            s.HealthSlot = UiKit.Rect("Health", block, TopLeft, TopLeft, new Vector2(PadX, -HealthTop), new Vector2(Inner, HealthHeight));
            return s;
        }

        static Vector2 MidLeftV => new Vector2(0f, 0.5f);

        // Effect icon `index` from the right end of the row: a dark disc, 34 across, bottom-aligned.
        static RectTransform Effect(RectTransform row, string name, int index)
        {
            var rect = UiKit.Rect(name, row, Vector2.right, Vector2.right, new Vector2(-index * (IconSize + IconGap), 0f), new Vector2(IconSize, IconSize));
            Disc(rect, "Plate", new Vector2(IconSize, IconSize), Vector2.zero, Plate, null);
            return rect;
        }

        static Image Disc(RectTransform parent, string name, Vector2 size, Vector2 at, Color color, Sprite sprite)
        {
            var image = UiKit.Box(UiKit.Rect(name, parent, Mid, Mid, at, size), color);
            image.sprite = sprite != null ? sprite : VehicleCluster.Ring(0.5f);
            return image;
        }

        // A little running figure out of bars (no icon asset needed).
        static void RunGlyph(RectTransform icon)
        {
            Disc(icon, "Head", new Vector2(5f, 5f), new Vector2(3.5f, 8f), BoostYellow, null);
            Bar(icon, "Body", new Vector2(1f, 1f), 9f, -20f);
            Bar(icon, "LegA", new Vector2(-3f, -7f), 9f, -35f);
            Bar(icon, "LegB", new Vector2(4f, -7f), 9f, 30f);
            Bar(icon, "ArmA", new Vector2(-3f, 2f), 7f, 60f);
            Bar(icon, "ArmB", new Vector2(5f, 1f), 7f, -60f);
        }

        static void Bar(RectTransform parent, string name, Vector2 at, float length, float deg)
        {
            var bar = UiKit.Rect(name, parent, Mid, Mid, at, new Vector2(2.4f, length));
            bar.localRotation = Quaternion.Euler(0f, 0f, deg);
            UiKit.Box(bar, BoostYellow);
        }

        static readonly System.Collections.Generic.Dictionary<int, Sprite> RoundedSprites = new System.Collections.Generic.Dictionary<int, Sprite>();

        // A white rounded rectangle, nine-sliced, `radius` canvas units at the corners.
        public static Image Rounded(Image image, int radius)
        {
            if (!RoundedSprites.TryGetValue(radius, out var sprite) || sprite == null)
            {
                int k = 4, r = radius * k, n = r * 2 + 4;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f));
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                // k texels per canvas unit: the sprite's pixels-per-unit is k times the canvas reference (100).
                sprite = Sprite.Create(tex, new Rect(0, 0, n, n), Mid, 100f * k, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                sprite.hideFlags = HideFlags.HideAndDontSave;
                RoundedSprites[radius] = sprite;
            }
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            return image;
        }

        public void Tick(Combatant me, bool swimming, float breath)
        {
            _shownStance = VehicleSeat.TryGet(me, out _, out int seat) ? (seat == Vehicles.DriverSeat ? DrivingShown : RidingShown) : (int)HudBackend.StanceOf(me);

            // In the elevator: a countdown to the doors; after that the protection time. On fire (ADR-110): "화상" in red.
            bool doors = HudBackend.DoorsClosed;
            float left = doors ? HudBackend.DoorSecondsLeft : HudBackend.SpawnProtectionLeft(me);
            int seconds = left > 0f ? Mathf.CeilToInt(left) : 0;
            bool burning = me.Burning.Value;
            int key = burning ? int.MinValue + 1 : doors ? -1 - seconds : seconds;
            if (key != _shownProtect)
            {
                _shownProtect = key;
                bool on = burning || doors || seconds > 0;
                if (_protectLabel.gameObject.activeSelf != on) _protectLabel.gameObject.SetActive(on);
                if (on) _protectLabel.text = burning ? "화상" : doors ? "문 열림 " + HudDigits.IntText(seconds) : "보호 " + HudDigits.IntText(seconds) + "초";
                _protectLabel.color = burning ? UiKit.Danger : UiKit.Accent;
            }

            var heal = me.GetComponent<CombatantHealing>();
            Chip(0, me.Medkits.Value);
            Chip(1, heal != null ? heal.Pills.Value : 0);
            Chip(2, heal != null ? heal.Drinks.Value : 0);
            ShowBoost(heal);
            ShowBreath(swimming, breath);
            ShowArmour(me);
        }

        void Chip(int i, int count)
        {
            if (count == _chipShown[i]) return;
            _chipShown[i] = count;
            _chipCount[i].text = HudDigits.IntText(count);
            _chipCount[i].color = count > 0 ? UiKit.Text : Mute;
        }

        void ShowBoost(CombatantHealing heal)
        {
            float gauge = heal != null ? heal.Gauge.Value : 0f;
            int items = heal != null ? heal.Drinks.Value + heal.Pills.Value + heal.Syringes.Value : 0;
            int key = Mathf.RoundToInt(gauge * 4f) * 100 + items;
            if (key == _shownBoostKey) return;
            _shownBoostKey = key;
            _boostItemsShown = items;
            var rules = BoostRules.Current;
            float leftOver = rules.MaxGauge > 0f ? gauge / rules.MaxGauge : 0f;
            for (int i = 0; i < Boost.Steps; i++)
            {
                _boostFill[i].anchorMax = new Vector2(Mathf.Clamp01(leftOver / BoostShares[i]), 1f);
                leftOver -= BoostShares[i];
            }
            int step = Boost.Step(gauge, rules);
            if (step == _shownStep) return;
            _shownStep = step;
            _healFx.alpha = step >= 1 ? 1f : 0.25f;
            _runFx.alpha = step >= 3 ? 1f : 0.25f;
        }

        void ShowBreath(bool swimming, float breath)
        {
            if (_lung.activeSelf != swimming) _lung.SetActive(swimming);
            if (!swimming) { _shownBreath = -1f; return; }
            breath = Mathf.Clamp01(breath);
            if (Mathf.Abs(breath - _shownBreath) < 0.005f) return;
            _shownBreath = breath;
            _lungArc.fillAmount = breath;
            _lungArc.color = breath < 0.25f ? UiKit.Danger : Water;
        }

        void ShowArmour(Combatant me)
        {
            for (int slot = 0; slot < 2; slot++)
            {
                var gear = InventoryBackend.Gear(me, slot);
                int tier = gear.Has ? gear.Tier : 0;
                int hash = (tier * 100003) ^ (gear.Durability * 31) ^ gear.MaxDurability;
                if (hash == _shownGearHash[slot]) continue;
                _shownGearHash[slot] = hash;
                _shownTier[slot] = tier;
                string prefix = slot == InventoryBackend.HelmetSlot ? "helmet_" : "vest_";
                var sprite = ItemIcons.Get(prefix + Mathf.Max(1, tier));
                _armourIcon[slot].sprite = sprite;
                // No picture: a plain light tile stands in. Nothing worn: the level-1 picture, faint.
                _armourIcon[slot].color = gear.Has ? Color.white : new Color(1f, 1f, 1f, 0.2f);
                if (sprite == null) _armourIcon[slot].color = gear.Has ? UiKit.Health : new Color(1f, 1f, 1f, 0.12f);
                float wear = gear.Has ? Mathf.Clamp01(gear.Fraction) : 0f;
                _wearFill[slot].anchorMax = new Vector2(wear, 1f);
                _wearImage[slot].color = wear < LowWear ? UiKit.Danger : UiKit.Health;
                _level[slot].text = gear.Has ? "Lv." + HudDigits.IntText(tier) : "—";
            }
        }
    }
}
