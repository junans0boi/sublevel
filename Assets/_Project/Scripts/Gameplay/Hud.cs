using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The in-game HUD (UI_SPEC 1): separate areas built in code with UiKit. It only shows what the server decided.
    // Texts are rewritten only when the shown value changes, so a steady frame allocates nothing.
    public class Hud : MonoBehaviour
    {
        public static Hud Instance { get; private set; }

        const float MaxHp = 100f, LowHp = 30f;
        const float Margin = 36f;
        const float CrosshairLine = 9f, CrosshairThickness = 2f, MinGap = 3f, DotSize = 3f;
        const float GapChangePerSecond = 14f;
        // The canvas scales with screen height, so one reference unit is one pixel at 1080p.
        const float ReferenceHeight = 1080f;
        const float HitMarkerSeconds = 0.12f;
        const float DamageSeconds = 1f, DamageRadius = 240f;
        const int DamageSlots = 4, DamageSegments = 5;
        const float DamageSegmentDeg = 9f;
        const float CompassWidth = 560f, CompassPixelsPerDegree = 4f;
        const int CompassSteps = 24; // one mark every 15 degrees
        const string Dash = "—";

        static readonly string[] Directions = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };
        static readonly string[] SmallInts = new string[1000];
        static readonly string[] Headings = new string[360];

        Canvas _canvas;

        // Bottom centre.
        RectTransform _healthFill;
        Image _healthFillImage;
        Text _healthLabel;
        GameObject _reload, _prompt;
        Text _promptLabel;
        GameObject _promptIconRoot;
        InventoryItemIcon _promptIcon;
        LootItem _promptFor;
        float _shownHp = float.NaN;
        int _shownHpInt = int.MinValue;

        // Bottom right.
        readonly Image[] _slotBack = new Image[2];
        readonly Image[] _slotBar = new Image[2];
        readonly Text[] _slotKey = new Text[2];
        readonly Text[] _slotName = new Text[2];
        readonly int[] _slotWeapon = { int.MinValue, int.MinValue };
        Text _magLabel, _reserveLabel, _ammoTypeLabel;
        GameObject _activeWeaponIconRoot;
        InventoryItemIcon _activeWeaponIcon;
        int _shownActive = -1, _shownAmmoWeapon = int.MinValue, _shownMag = int.MinValue, _shownReserve = int.MinValue;

        // Bottom left, top right.
        Text _medkitLabel, _aliveLabel;
        readonly Text[] _gearLabel = new Text[2];
        readonly RectTransform[] _gearFill = new RectTransform[2];
        GameObject _grenadeRow;
        Text _grenadeLabel;
        int _shownMedkits = int.MinValue, _shownAlive = int.MinValue;
        readonly int[] _shownGearHash = { int.MinValue, int.MinValue };
        int _shownGrenades = int.MinValue;

        // Top centre.
        readonly RectTransform[] _compassMarks = new RectTransform[CompassSteps];
        Text _headingLabel;
        float _shownHeading = float.NaN;
        int _shownHeadingInt = int.MinValue;

        // Centre.
        GameObject _crosshair, _dot, _bars;
        RectTransform _barLeft, _barRight, _barUp, _barDown;
        float _gap = -1f, _placedGap = -1f;
        GameObject _hitMarker;
        readonly Image[] _hitArms = new Image[4];
        float _hitMarkerUntil;

        // Screen edge.
        readonly RectTransform[] _damagePivot = new RectTransform[DamageSlots];
        readonly CanvasGroup[] _damageGroup = new CanvasGroup[DamageSlots];
        readonly Vector3[] _damageFrom = new Vector3[DamageSlots];
        readonly float[] _damageLeft = new float[DamageSlots];
        CanvasGroup _vignette;
        Texture2D _fadeUpTexture, _fadeRightTexture;
        Sprite _fadeUp, _fadeRight;

        GameObject _death;

        // ---- Read-only state for tests ----

        public bool Visible => _canvas != null && _canvas.enabled;
        public float HealthFill { get; private set; } = 1f;      // 0..1
        public string AmmoText { get; private set; } = Dash;     // "mag / reserve" of the weapon in hand
        public string AmmoTypeText { get; private set; } = "맨손";
        public string ActiveWeaponIconKey => _activeWeaponIcon != null ? _activeWeaponIcon.Key : "";
        public int ActiveSlotShown => _shownActive;
        public string ActiveWeaponName => _shownActive >= 0 && _shownActive < 2 ? _slotName[_shownActive].text : "";
        public bool CrosshairVisible => Visible && _crosshair.activeSelf;
        public bool HitMarkerVisible => _hitMarker != null && _hitMarker.activeSelf;
        public bool HeadMarkerShown { get; private set; }         // the last hit marker was for a head hit
        public bool ReloadingShown => _reload.activeSelf;
        public string PromptText => _prompt.activeSelf ? _promptLabel.text : "";
        public string PromptIconKey => _promptIcon != null && _prompt.activeSelf ? _promptIcon.Key : "";
        public bool GrenadeShown => _grenadeRow != null && _grenadeRow.activeSelf;
        public bool LowHealthShown => _vignette.alpha > 0f;
        public bool DeathShown => _death.activeSelf;
        public int AliveShown => _shownAlive;
        public int DamageIndicatorsShown
        {
            get
            {
                int n = 0;
                for (int i = 0; i < DamageSlots; i++)
                    if (_damageLeft[i] > 0f) n++;
                return n;
            }
        }

        // ---- Combat feedback entry points; safe to call when no HUD exists ----

        // An X around the crosshair when the local player's shot lands; red and larger for a kill.
        // A hit on the head shows in the accent colour (a kill stays red).
        public static void HitMarker(bool kill, bool head = false)
        {
            if (Instance != null) Instance.ShowHitMarker(kill, head);
        }

        // An arc on a ring around the screen centre, pointing at whoever hurt the local player.
        public static void DamageFrom(Vector3 worldPosition)
        {
            if (Instance != null) Instance.ShowDamageFrom(worldPosition);
        }

        void Awake()
        {
            Instance = this;
            _canvas = UiKit.Canvas("HudCanvas", 0, false);
            // Under the HUD object so it goes away with the scene.
            _canvas.transform.SetParent(transform, false);
            var root = _canvas.transform;

            BuildVignette(root);
            BuildDamageRing(root);
            BuildCompass(root);
            BuildAlive(root);
            BuildHealth(root);
            BuildWeapons(root);
            BuildConsumables(root);
            BuildCrosshair(root);
            BuildDeath(root);
            _canvas.enabled = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_fadeUp != null) Destroy(_fadeUp);
            if (_fadeRight != null) Destroy(_fadeRight);
            if (_fadeUpTexture != null) Destroy(_fadeUpTexture);
            if (_fadeRightTexture != null) Destroy(_fadeRightTexture);
        }

        // After the player controller has placed the camera for this frame.
        void LateUpdate()
        {
            if (_hitMarker.activeSelf && Time.unscaledTime >= _hitMarkerUntil) _hitMarker.SetActive(false);

            var me = Combatant.LocalPlayer;
            bool show = me != null;
            if (_canvas.enabled != show) _canvas.enabled = show;
            if (!show) return;

            var player = PlayerController.Local;
            bool dead = me.Dead.Value;
            TickHealth(me, dead);
            TickWeapons(me);
            TickSmallTexts(me);
            TickPrompt(me, player, dead);
            if (player != null) TickCompass(player.Yaw);
            TickCrosshair(me, player, dead);
            TickDamageRing(me, player);
            Show(_death, dead);
        }

        // ---- Per-frame updates ----

        void TickHealth(Combatant me, bool dead)
        {
            float hp = me.Hp.Value;
            if (hp != _shownHp)
            {
                _shownHp = hp;
                HealthFill = Mathf.Clamp01(hp / MaxHp);
                _healthFill.anchorMax = new Vector2(HealthFill, 1f);
                _healthFillImage.color = hp < LowHp ? UiKit.Danger : UiKit.Health;
                int shown = Mathf.CeilToInt(hp);
                if (shown != _shownHpInt)
                {
                    _shownHpInt = shown;
                    _healthLabel.text = IntText(shown);
                }
            }

            // Stronger the closer to zero, with a slow pulse; a canvas group fades it without rebuilding meshes.
            float vignette = 0f;
            if (dead) vignette = 0.8f;
            else if (hp < LowHp)
                vignette = (0.35f + 0.45f * (1f - Mathf.Clamp01(hp / LowHp))) * (0.88f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f));
            if (_vignette.alpha != vignette) _vignette.alpha = vignette;
        }

        void TickWeapons(Combatant me)
        {
            int active = me.ActiveSlot.Value;
            ShowSlotWeapon(0, me.Slot0.Value);
            ShowSlotWeapon(1, me.Slot1.Value);
            if (active != _shownActive)
            {
                _shownActive = active;
                for (int i = 0; i < 2; i++)
                {
                    bool on = i == active;
                    _slotBack[i].enabled = on;
                    _slotBar[i].enabled = on;
                    _slotName[i].color = on ? UiKit.Text : UiKit.TextDim;
                    _slotName[i].fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                    _slotKey[i].color = on ? UiKit.Accent : UiKit.TextDim;
                }
            }

            WeaponDef weapon = me.ActiveWeapon;
            int id = weapon != null ? weapon.Id : 0;
            int mag = weapon != null ? me.ActiveMag : 0;
            int reserve = weapon != null ? InventoryBackend.Reserve(me, InventoryBackend.AmmoIndex(weapon)) : 0;
            if (id == _shownAmmoWeapon && mag == _shownMag && reserve == _shownReserve) return;
            _shownAmmoWeapon = id;
            _shownMag = mag;
            _shownReserve = reserve;
            if (weapon == null)
            {
                AmmoText = Dash;
                AmmoTypeText = "맨손";
                _magLabel.text = Dash;
                _magLabel.color = UiKit.TextDim;
                _reserveLabel.text = "";
                _ammoTypeLabel.text = "맨손";
                _activeWeaponIcon.Set("", "맨손");
                _activeWeaponIconRoot.SetActive(false);
                return;
            }
            int ammoIndex = InventoryBackend.AmmoIndex(weapon);
            AmmoText = IntText(mag) + " / " + IntText(reserve);
            AmmoTypeText = InventoryBackend.AmmoName(ammoIndex);
            _magLabel.text = IntText(mag);
            _magLabel.color = mag == 0 ? UiKit.Danger : UiKit.Text;
            _reserveLabel.text = "/ " + IntText(reserve);
            _ammoTypeLabel.text = AmmoTypeText;
            _activeWeaponIcon.Set(InventoryBackend.WeaponKey(id), weapon.Name);
            _activeWeaponIconRoot.SetActive(true);
        }

        void ShowSlotWeapon(int slot, int weaponId)
        {
            if (_slotWeapon[slot] == weaponId) return;
            _slotWeapon[slot] = weaponId;
            WeaponDef weapon = GameData.Weapons.Find(weaponId);
            _slotName[slot].text = weapon != null ? weapon.Name : Dash;
        }

        void TickSmallTexts(Combatant me)
        {
            int medkits = me.Medkits.Value;
            if (medkits != _shownMedkits)
            {
                _shownMedkits = medkits;
                _medkitLabel.text = "x" + IntText(medkits);
                _medkitLabel.color = medkits > 0 ? UiKit.Text : UiKit.TextDim;
            }

            for (int slot = 0; slot < 2; slot++)
            {
                var gear = InventoryBackend.Gear(me, slot);
                int hash = (gear.Has ? 1 : 0) ^ gear.Tier * 397 ^ gear.Durability * 31 ^ gear.MaxDurability;
                if (hash == _shownGearHash[slot]) continue;
                _shownGearHash[slot] = hash;
                string name = slot == InventoryBackend.HelmetSlot ? "헬멧" : "조끼";
                _gearLabel[slot].text = gear.Has ? name + " " + gear.Tier + "단계  " + gear.Durability + "/" + gear.MaxDurability : name + "  —";
                _gearLabel[slot].color = gear.Has ? UiKit.Text : UiKit.TextDim;
                _gearFill[slot].anchorMax = new Vector2(Mathf.Clamp01(gear.Fraction), 1f);
            }

            int grenades = InventoryBackend.Grenades(me);
            if (grenades != _shownGrenades)
            {
                _shownGrenades = grenades;
                _grenadeLabel.text = "x" + IntText(grenades);
                Show(_grenadeRow, grenades > 0);
            }

            // The match's own count when it has one (it is what the kill feed and placements use).
            int alive = 0;
            var match = MatchState.Instance;
            if (match != null && match.TotalCount > 0) alive = match.AliveCount;
            else
            {
                var all = Combatant.All;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && !all[i].Dead.Value) alive++;
            }
            if (alive != _shownAlive)
            {
                _shownAlive = alive;
                _aliveLabel.text = IntText(alive);
            }
        }

        void TickPrompt(Combatant me, PlayerController player, bool dead)
        {
            Show(_reload, me.Reloading.Value && !dead);

            LootItem loot = !dead && player != null ? player.NearbyLoot : null;
            bool hasLoot = loot != null;
            if (hasLoot && !ReferenceEquals(loot, _promptFor))
            {
                _promptFor = loot;
                _promptLabel.text = "[F]  " + LootLabel(loot);
                string name = InventoryBackend.LootName(loot);
                _promptIcon.Set(InventoryBackend.LootIconKey(loot), name);
            }
            Show(_promptIconRoot, hasLoot);
            Show(_prompt, hasLoot);
        }

        void TickCompass(float yaw)
        {
            float heading = Mathf.Repeat(yaw, 360f);
            if (heading == _shownHeading) return;
            _shownHeading = heading;
            // Everything is laid out by its angle from the view direction; the strip's mask cuts off the rest.
            for (int i = 0; i < CompassSteps; i++)
            {
                var mark = _compassMarks[i];
                float x = Mathf.DeltaAngle(heading, i * (360f / CompassSteps)) * CompassPixelsPerDegree;
                mark.anchoredPosition = new Vector2(x, mark.anchoredPosition.y);
            }
            int shown = Mathf.RoundToInt(heading) % 360;
            if (shown != _shownHeadingInt)
            {
                _shownHeadingInt = shown;
                _headingLabel.text = Headings[shown] ?? (Headings[shown] = shown.ToString("000"));
            }
        }

        // Shows how the weapon is aimed (ADR-048): four lines at the edge of the spread cone from the hip, closer
        // together when shouldered, a single dot when looking down the sights or with empty hands.
        void TickCrosshair(Combatant me, PlayerController player, bool dead)
        {
            bool visible = !dead && UiFocus.Current == UiScreen.None;
            Show(_crosshair, visible);
            if (!visible)
            {
                _gap = -1f;
                return;
            }
            WeaponDef weapon = me.ActiveWeapon;
            AimMode aim = me.AimMode;
            Camera cam = player != null ? player.Camera : null;
            bool dot = weapon == null || aim == AimMode.Sights || cam == null;
            Show(_dot, dot);
            Show(_bars, !dot);
            if (dot)
            {
                _gap = -1f;
                return;
            }
            float wanted = Mathf.Max(MinGap, Crosshair.GapPixels(weapon.SpreadDeg * me.SpreadFactor, cam.fieldOfView, ReferenceHeight));
            _gap = _gap < 0f ? wanted : Mathf.Lerp(_gap, wanted, 1f - Mathf.Exp(-GapChangePerSecond * Time.unscaledDeltaTime));
            if (Mathf.Abs(_gap - _placedGap) < 0.02f) return;
            _placedGap = _gap;
            // Each bar's rect includes its one-unit dark rim.
            float edge = _gap - 1f;
            _barLeft.anchoredPosition = new Vector2(-edge, 0f);
            _barRight.anchoredPosition = new Vector2(edge, 0f);
            _barUp.anchoredPosition = new Vector2(0f, edge);
            _barDown.anchoredPosition = new Vector2(0f, -edge);
        }

        void TickDamageRing(Combatant me, PlayerController player)
        {
            Camera cam = player != null ? player.Camera : null;
            float viewYaw = cam != null ? cam.transform.eulerAngles.y : player != null ? player.Yaw : 0f;
            Vector3 origin = me.transform.position;
            for (int i = 0; i < DamageSlots; i++)
            {
                if (_damageLeft[i] <= 0f) continue;
                _damageLeft[i] -= Time.unscaledDeltaTime;
                if (_damageLeft[i] <= 0f)
                {
                    _damageLeft[i] = 0f;
                    _damagePivot[i].gameObject.SetActive(false);
                    continue;
                }
                Vector3 to = _damageFrom[i] - origin;
                float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg; // 0 = north (+z), 90 = east (+x)
                // The arc sits at the top of its pivot; clockwise on screen is a negative z rotation.
                _damagePivot[i].localRotation = Quaternion.Euler(0f, 0f, -Mathf.DeltaAngle(viewYaw, bearing));
                _damageGroup[i].alpha = Mathf.Clamp01(_damageLeft[i] / DamageSeconds);
            }
        }

        void ShowHitMarker(bool kill, bool head)
        {
            HeadMarkerShown = head;
            Color color = kill ? UiKit.Danger : head ? UiKit.Accent : Color.white;
            for (int i = 0; i < _hitArms.Length; i++) _hitArms[i].color = color;
            float scale = kill ? (head ? 1.8f : 1.5f) : head ? 1.25f : 1f;
            _hitMarker.transform.localScale = new Vector3(scale, scale, 1f);
            _hitMarkerUntil = Time.unscaledTime + HitMarkerSeconds;
            _hitMarker.SetActive(true);
        }

        void ShowDamageFrom(Vector3 worldPosition)
        {
            // A free slot, else the one that has faded the most.
            int slot = 0;
            for (int i = 1; i < DamageSlots; i++)
                if (_damageLeft[i] < _damageLeft[slot]) slot = i;
            _damageFrom[slot] = worldPosition;
            _damageLeft[slot] = DamageSeconds;
            _damageGroup[slot].alpha = 1f;
            _damagePivot[slot].gameObject.SetActive(true);
        }

        // ---- Construction ----

        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
        static readonly Vector2 MidLeft = new Vector2(0f, 0.5f);
        static readonly Vector2 MidRight = new Vector2(1f, 0.5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);

        static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = UiKit.Rect(name, parent, anchor, anchor, position, size);
            UiKit.Box(rect, UiKit.Panel);
            return rect;
        }

        static Text Label(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, string text, int fontSize, Color color,
            TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            return UiKit.Label(UiKit.Rect(name, parent, anchor, anchor, position, size), text, fontSize, color, align, style);
        }

        void BuildHealth(Transform root)
        {
            var bottom = new Vector2(0.5f, 0f);
            var panel = Panel("Health", root, bottom, new Vector2(0f, Margin), new Vector2(480f, 48f));
            _healthLabel = Label("Value", panel, MidLeft, new Vector2(16f, 0f), new Vector2(64f, 40f), "100", 28, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            var track = UiKit.Rect("Track", panel, MidLeft, MidLeft, new Vector2(88f, 0f), new Vector2(376f, 12f));
            UiKit.Box(track, UiKit.PanelLight);
            _healthFill = UiKit.Fill("Fill", track);
            _healthFillImage = UiKit.Box(_healthFill, UiKit.Health);

            var reload = Panel("Reloading", root, bottom, new Vector2(0f, Margin + 58f), new Vector2(200f, 32f));
            UiKit.Label(UiKit.Fill("Label", reload), "재장전 중", 18, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            _reload = reload.gameObject;
            _reload.SetActive(false);

            var prompt = Panel("Pickup", root, bottom, new Vector2(0f, Margin + 100f), new Vector2(460f, 52f));
            var promptIconRect = UiKit.Rect("Icon", prompt, MidLeft, MidLeft, new Vector2(8f, 0f), new Vector2(40f, 40f));
            _promptIcon = InventoryItemIcon.Build(promptIconRect);
            _promptIconRoot = promptIconRect.gameObject;
            _promptLabel = UiKit.Label(UiKit.Rect("Label", prompt, MidLeft, MidLeft, new Vector2(60f, 0f), new Vector2(384f, 46f)), "", 22, UiKit.Text, TextAnchor.MiddleCenter);
            _prompt = prompt.gameObject;
            _prompt.SetActive(false);
        }

        void BuildWeapons(Transform root)
        {
            var corner = new Vector2(1f, 0f);
            var panel = Panel("Weapons", root, corner, new Vector2(-Margin, Margin), new Vector2(380f, 220f));
            var iconRect = UiKit.Rect("WeaponIcon", panel, TopLeft, TopLeft, new Vector2(18f, -14f), new Vector2(82f, 72f));
            _activeWeaponIcon = InventoryItemIcon.Build(iconRect);
            _activeWeaponIconRoot = iconRect.gameObject;
            _activeWeaponIconRoot.SetActive(false);
            _ammoTypeLabel = Label("AmmoType", panel, TopLeft, new Vector2(114f, -12f), new Vector2(236f, 24f), "맨손", 18, UiKit.TextDim);
            _magLabel = Label("Mag", panel, TopLeft, new Vector2(104f, -36f), new Vector2(126f, 60f), Dash, 50, UiKit.TextDim, TextAnchor.MiddleRight, FontStyle.Bold);
            _reserveLabel = Label("Reserve", panel, TopLeft, new Vector2(240f, -42f), new Vector2(120f, 60f), "", 27, UiKit.TextDim);

            for (int i = 0; i < 2; i++)
            {
                var row = UiKit.Rect("Slot" + (i + 1), panel, TopLeft, TopLeft, new Vector2(12f, -128f - 42f * i), new Vector2(356f, 38f));
                _slotBack[i] = UiKit.Box(row, UiKit.PanelLight);
                _slotBack[i].enabled = false;
                _slotBar[i] = UiKit.Box(UiKit.Rect("Active", row, MidLeft, MidLeft, Vector2.zero, new Vector2(4f, 38f)), UiKit.Accent);
                _slotBar[i].enabled = false;
                _slotKey[i] = Label("Key", row, MidLeft, new Vector2(16f, 0f), new Vector2(24f, 38f), i == 0 ? "1" : "2", 20, UiKit.TextDim);
                _slotName[i] = Label("Name", row, MidLeft, new Vector2(48f, 0f), new Vector2(296f, 38f), Dash, 22, UiKit.TextDim);
            }
        }

        void BuildConsumables(Transform root)
        {
            var panel = Panel("Consumables", root, Vector2.zero, new Vector2(Margin, Margin), new Vector2(320f, 190f));
            Label("MedkitName", panel, TopLeft, new Vector2(16f, -10f), new Vector2(116f, 36f), "구급상자", 20, UiKit.TextDim);
            _medkitLabel = Label("MedkitCount", panel, TopLeft, new Vector2(138f, -10f), new Vector2(70f, 36f), "x0", 24, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            Label("MedkitKey", panel, TopLeft, new Vector2(250f, -10f), new Vector2(50f, 36f), "[H]", 20, UiKit.TextDim, TextAnchor.MiddleRight);

            for (int slot = 0; slot < 2; slot++)
            {
                float y = -58f - slot * 48f;
                _gearLabel[slot] = Label(slot == 0 ? "Helmet" : "Vest", panel, TopLeft, new Vector2(16f, y), new Vector2(288f, 28f), slot == 0 ? "헬멧  —" : "조끼  —", 18, UiKit.TextDim);
                var track = UiKit.Rect(slot == 0 ? "HelmetTrack" : "VestTrack", panel, TopLeft, TopLeft, new Vector2(16f, y - 29f), new Vector2(288f, 6f));
                UiKit.Box(track, UiKit.PanelLight);
                _gearFill[slot] = UiKit.Fill("Fill", track);
                UiKit.Box(_gearFill[slot], new Color(0.32f, 0.72f, 0.58f, 1f));
                _gearFill[slot].anchorMax = new Vector2(0f, 1f);
            }

            _grenadeRow = UiKit.Rect("Grenade", panel, TopLeft, TopLeft, new Vector2(16f, -154f), new Vector2(288f, 28f)).gameObject;
            Label("Name", _grenadeRow.transform, MidLeft, Vector2.zero, new Vector2(160f, 28f), "수류탄", 18, UiKit.TextDim);
            _grenadeLabel = Label("Count", _grenadeRow.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(80f, 28f), "x0", 20, UiKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            _grenadeRow.SetActive(false);
        }

        void BuildAlive(Transform root)
        {
            var panel = Panel("Alive", root, Vector2.one, new Vector2(-Margin, -24f), new Vector2(170f, 44f));
            Label("Name", panel, MidLeft, new Vector2(16f, 0f), new Vector2(80f, 40f), "생존", 20, UiKit.TextDim);
            _aliveLabel = Label("Count", panel, MidRight, new Vector2(-16f, 0f), new Vector2(70f, 40f), "0", 26, UiKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
        }

        void BuildCompass(Transform root)
        {
            var top = new Vector2(0.5f, 1f);
            var strip = Panel("Compass", root, top, new Vector2(0f, -24f), new Vector2(CompassWidth, 40f));
            var mask = strip.gameObject.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(48, 0);
            for (int i = 0; i < CompassSteps; i++)
            {
                if (i % 3 != 0)
                {
                    // A plain mark between the lettered directions.
                    _compassMarks[i] = UiKit.Rect("Mark" + i * 15, strip, Centre, Centre, new Vector2(0f, 10f), new Vector2(2f, 8f));
                    UiKit.Box(_compassMarks[i], UiKit.TextDim);
                    continue;
                }
                int direction = i / 3;
                bool cardinal = direction % 2 == 0;
                var label = Label(Directions[direction], strip, Centre, Vector2.zero, new Vector2(60f, 32f), Directions[direction], cardinal ? 22 : 18,
                    direction == 0 ? UiKit.Accent : cardinal ? UiKit.Text : UiKit.TextDim, TextAnchor.MiddleCenter, cardinal ? FontStyle.Bold : FontStyle.Normal);
                _compassMarks[i] = label.rectTransform;
            }
            // The view direction: a notch under the strip and the heading in degrees.
            UiKit.Box(UiKit.Rect("Notch", root, top, top, new Vector2(0f, -64f), new Vector2(2f, 8f)), UiKit.Accent);
            var heading = Panel("Heading", root, top, new Vector2(0f, -74f), new Vector2(72f, 30f));
            _headingLabel = UiKit.Label(UiKit.Fill("Label", heading), "000", 20, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        void BuildCrosshair(Transform root)
        {
            var crosshair = UiKit.Rect("Crosshair", root, Centre, Centre, Vector2.zero, Vector2.zero);
            _crosshair = crosshair.gameObject;

            _dot = Rimmed("Dot", crosshair, Centre, new Vector2(DotSize, DotSize)).gameObject;
            var bars = UiKit.Rect("Bars", crosshair, Centre, Centre, Vector2.zero, Vector2.zero);
            _bars = bars.gameObject;
            var across = new Vector2(CrosshairLine, CrosshairThickness);
            var upright = new Vector2(CrosshairThickness, CrosshairLine);
            // Pivots on the inner end, so a bar's position is simply the gap.
            _barLeft = Rimmed("Left", bars, MidRight, across);
            _barRight = Rimmed("Right", bars, MidLeft, across);
            _barUp = Rimmed("Up", bars, new Vector2(0.5f, 0f), upright);
            _barDown = Rimmed("Down", bars, new Vector2(0.5f, 1f), upright);
            _bars.SetActive(false);

            var marker = UiKit.Rect("HitMarker", root, Centre, Centre, Vector2.zero, Vector2.zero);
            _hitMarker = marker.gameObject;
            for (int i = 0; i < 4; i++)
            {
                // Four short diagonal strokes pointing away from the centre.
                float angle = 45f + 90f * i;
                float rad = angle * Mathf.Deg2Rad;
                var outward = new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));
                var arm = UiKit.Rect("Arm" + i, marker, Centre, new Vector2(0.5f, 0f), outward * 9f, new Vector2(2.5f, 11f));
                arm.localRotation = Quaternion.Euler(0f, 0f, angle);
                _hitArms[i] = UiKit.Box(arm, Color.white);
            }
            _hitMarker.SetActive(false);
        }

        // A light bar with a dark rim so it reads on bright and dark backgrounds alike.
        static RectTransform Rimmed(string name, Transform parent, Vector2 pivot, Vector2 size)
        {
            var rim = UiKit.Rect(name, parent, Centre, pivot, Vector2.zero, size + new Vector2(2f, 2f));
            UiKit.Box(rim, new Color(0f, 0f, 0f, 0.55f));
            UiKit.Box(UiKit.Fill("Core", rim, 1f), new Color(1f, 1f, 1f, 0.92f));
            return rim;
        }

        void BuildDamageRing(Transform root)
        {
            var color = new Color(UiKit.Danger.r, UiKit.Danger.g, UiKit.Danger.b, 0.9f);
            // Short straight pieces along the ring read as one arc.
            float segmentLength = 2f * DamageRadius * Mathf.Sin(DamageSegmentDeg * 0.5f * Mathf.Deg2Rad) + 1f;
            for (int i = 0; i < DamageSlots; i++)
            {
                var pivot = UiKit.Rect("DamageFrom" + i, root, Centre, Centre, Vector2.zero, Vector2.zero);
                _damagePivot[i] = pivot;
                _damageGroup[i] = pivot.gameObject.AddComponent<CanvasGroup>();
                _damageGroup[i].blocksRaycasts = false;
                _damageGroup[i].interactable = false;
                for (int s = 0; s < DamageSegments; s++)
                {
                    float offset = (s - (DamageSegments - 1) * 0.5f) * DamageSegmentDeg;
                    float rad = offset * Mathf.Deg2Rad;
                    var position = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * DamageRadius;
                    var piece = UiKit.Rect("Piece" + s, pivot, Centre, Centre, position, new Vector2(segmentLength, 7f));
                    piece.localRotation = Quaternion.Euler(0f, 0f, -offset);
                    UiKit.Box(piece, color);
                }
                pivot.gameObject.SetActive(false);
            }
        }

        void BuildVignette(Transform root)
        {
            _fadeUp = Fade(true, out _fadeUpTexture);
            _fadeRight = Fade(false, out _fadeRightTexture);
            var all = UiKit.Fill("LowHealth", root);
            _vignette = all.gameObject.AddComponent<CanvasGroup>();
            _vignette.blocksRaycasts = false;
            _vignette.interactable = false;
            _vignette.alpha = 0f;
            const float depthY = 170f, depthX = 230f;
            Edge("Top", all, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -depthY * 0.5f), new Vector2(0f, depthY), _fadeUp, false, false);
            Edge("Bottom", all, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, depthY * 0.5f), new Vector2(0f, depthY), _fadeUp, false, true);
            Edge("Right", all, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-depthX * 0.5f, 0f), new Vector2(depthX, 0f), _fadeRight, false, false);
            Edge("Left", all, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(depthX * 0.5f, 0f), new Vector2(depthX, 0f), _fadeRight, true, false);
        }

        // One screen edge, stretched along it; the sprite fades toward the middle of the screen.
        static void Edge(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Sprite sprite, bool flipX, bool flipY)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = Centre;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = new Vector3(flipX ? -1f : 1f, flipY ? -1f : 1f, 1f);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = UiKit.Danger;
            image.raycastTarget = false;
        }

        // A tiny white strip whose alpha rises toward the top (or the right), made here so no texture asset is needed.
        static Sprite Fade(bool upward, out Texture2D texture)
        {
            const int steps = 16;
            texture = new Texture2D(upward ? 1 : steps, upward ? steps : 1, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            for (int i = 0; i < steps; i++)
            {
                float t = i / (steps - 1f);
                texture.SetPixel(upward ? 0 : i, upward ? i : 0, new Color(1f, 1f, 1f, t * t));
            }
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), Centre, 100f, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        void BuildDeath(Transform root)
        {
            var go = new GameObject("Death", typeof(RectTransform));
            var band = (RectTransform)go.transform;
            band.SetParent(root, false);
            band.anchorMin = new Vector2(0f, 0.5f);
            band.anchorMax = new Vector2(1f, 0.5f);
            band.pivot = Centre;
            band.anchoredPosition = new Vector2(0f, 60f);
            band.sizeDelta = new Vector2(0f, 190f);
            UiKit.Box(band, UiKit.Panel);
            Label("Title", band, Centre, new Vector2(0f, 26f), new Vector2(800f, 80f), "사망", 64, UiKit.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            Label("Hint", band, Centre, new Vector2(0f, -48f), new Vector2(800f, 34f), "Enter " + Dash + " 다시 시작", 24, UiKit.Text, TextAnchor.MiddleCenter);
            _death = go;
            _death.SetActive(false);
        }

        // ---- Text helpers ----

        static void Show(GameObject go, bool on)
        {
            if (go.activeSelf != on) go.SetActive(on);
        }

        // Cached so the numbers that change most (health, ammo) never allocate after their first use.
        static string IntText(int value)
        {
            if (value < 0 || value >= SmallInts.Length) return value.ToString();
            return SmallInts[value] ?? (SmallInts[value] = value.ToString());
        }

        static string LootLabel(LootItem item)
        {
            return InventoryBackend.LootName(item) + ((LootKind)item.Kind.Value == LootKind.Weapon ? "" : " x" + item.Amount.Value);
        }
    }
}
