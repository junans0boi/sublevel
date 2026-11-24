using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The in-game HUD (UI_SPEC 1, ADR-057): separate areas built in code with UiKit, a "field terminal" look.
    // It only shows what the server decided and reads server data through HudBackend. Texts are rewritten only
    // when the shown value changes, so a steady frame allocates nothing.
    //
    // Layout (reference 1920x1080; every area is a direct child of the canvas with a point anchor, so the
    // layout tests can place them at any resolution):
    //   top centre  compass            top left   region card          top right  alive/kill cards + kill feed
    //   bottom left squad list          bottom centre  vitals block     bottom right  minimap, weapon stack, zone
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
        const string Dash = "—";

        // Bottom centre (HUD v2, ADR-563): the vitals block (HudStatus) and the transient prompts above it.
        const float StackGap = 8f, AmmoWidth = 210f;
        // The damage trail holds this long after a hit, then drains.
        const float TrailHold = 0.25f;
        const float StatWidth = 150f, StatHeight = 44f, TopMargin = 24f;
        const float StripGap = 10f;

        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
        static readonly Vector2 MidLeft = new Vector2(0f, 0.5f);
        static readonly Vector2 MidRight = new Vector2(1f, 0.5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 BottomRight = new Vector2(1f, 0f);

        Canvas _canvas;
        readonly List<RectTransform> _areas = new List<RectTransform>();

        // Parts.
        CompassStrip _compass;
        RegionCard _region;
        ElevatorPanel _elevator;
        Text _doorNotice;
        GameObject _doorNoticeRoot;
        int _doorShown = -1;
        HudStatus _status;
        HudHazardSlot _hazard; // pollution warning, keys, chip (ADR-246)
        WeaponStrip _strip;
        MinimapView _minimap;
        WorldMarkerView _worldMarker;

        // Bottom centre: the health bar of the vitals block (fill over the heal-incoming stripes over the damage trail).
        RectTransform _healthFill, _healthLag, _healIncoming, _healProgressFill;
        Image _healthFillImage;
        RawImage _healStripes;
        Texture2D _stripeTexture;
        Text _healthNumber;
        GameObject _reload, _prompt;
        string _shownHealName;
        float _lagHp = -1f, _lagHoldUntil, _shownHealTo = -1f;
        Text _promptLabel;
        GameObject _promptIconRoot;
        InventoryItemIcon _promptIcon;
        LootItem _promptFor;
        float _shownHp = float.NaN;
        int _shownHpInt = int.MinValue;
        bool _lowShown;

        // Ammo of the weapon in hand (in the vitals row; the weapon pictures are in the weapon stack, bottom right).
        Text _mag, _reserve, _fireMode;
        string _weaponName = "", _weaponIconKey = "";
        int _shownActive = int.MinValue, _shownAmmoWeapon = int.MinValue, _shownMag = int.MinValue, _shownReserve = int.MinValue;
        string _shownFireMode;

        // Top right.
        HudDigits _aliveDigits, _killDigits;
        int _shownAlive = int.MinValue, _shownKills = int.MinValue;

        // Centre.
        GameObject _crosshair, _dot, _bars;
        GameObject _weaponPanel; // the ammo box: hidden while a magnified scope is up (ADR-060 (5)) and while driving
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
        public string FireModeText => _shownFireMode ?? "";
        public string ActiveWeaponIconKey => _weaponIconKey;
        public int ActiveSlotShown => _shownActive;
        public string ActiveWeaponName => _shownAmmoWeapon > 0 ? _weaponName : "";
        public bool CrosshairVisible => Visible && _crosshair.activeSelf;
        public bool WeaponPanelVisible => Visible && _weaponPanel.activeSelf;
        public bool HitMarkerVisible => _hitMarker != null && _hitMarker.activeSelf;
        public bool HeadMarkerShown { get; private set; }         // the last hit marker was for a head hit
        public bool LimbMarkerShown { get; private set; }         // ... for the legs (and not a kill)
        public Color HitMarkerColor { get; private set; }
        public bool ReloadingShown => _reload.activeSelf;
        public string PromptText => _prompt.activeSelf ? _promptLabel.text : "";
        public string PromptIconKey => _promptIcon != null && _prompt.activeSelf ? _promptIcon.Key : "";
        public bool GrenadeShown => _strip != null && _strip.GrenadesShown > 0;
        public bool LowHealthShown => _vignette.alpha > 0f;
        public bool DeathShown => _death.activeSelf;
        public bool DoorNoticeShown => _doorNoticeRoot.activeSelf;
        public int AliveShown => _shownAlive;
        public int KillsShown => _shownKills;
        // A timed heal is under way: green stripes from the health to where it will end, a solid line in them fills toward done.
        public bool HealingShown => _healIncoming.gameObject.activeSelf;
        public bool HealProgressShown => HealingShown;
        public float HealProgress { get; private set; }
        public string HealingText => _shownHealName ?? "";
        public float HealIncomingTo => _shownHealTo / MaxHp;      // 0..1, where the heal under way ends (negative: none)
        public float DamageTrailFill => _lagHp / MaxHp;           // 0..1, the red trail behind the health after a hit
        public bool AmmoEmptyShown => _mag.color == UiKit.Danger;
        public CompassStrip Compass => _compass;
        public RegionCard Region => _region;
        public ElevatorPanel Elevator => _elevator;
        public HudStatus Status => _status;
        public HudHazardSlot Hazard => _hazard;
        public WeaponStrip Strip => _strip;
        public MinimapView Minimap => _minimap;
        public WorldMarkerView WorldMarker => _worldMarker;
        // The fixed areas of this canvas, for the layout tests (rects as placed, shown or not).
        public IReadOnlyList<RectTransform> LayoutAreas => _areas;
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
        // A hit on the legs shows smaller and pale blue (#152): body white, legs pale blue, head accent, kill red.
        public static void HitMarker(bool kill, bool head = false, bool limb = false)
        {
            if (Instance != null) Instance.ShowHitMarker(kill, head, limb);
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
            BuildTop(root);
            // Shown while the elevator doors are shut (ADR-055): where you are and when it opens.
            var door = UiKit.Rect("DoorNotice", root, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 48f));
            UiKit.Box(door, UiKit.Panel);
            UiKit.Frame(door, 1f, UiKit.SteelDim);
            _doorNotice = UiKit.Label(UiKit.Fill("Text", door), "", 26, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            _doorNoticeRoot = door.gameObject;
            _doorNoticeRoot.SetActive(false);
            _hazard = HudHazardSlot.Build(root, new Vector2(0.5f, 1f), new Vector2(0f, -TopMargin - 96f));
            BuildHealthAndWeapon(root);
            BuildBottomSides(root);
            // The map marker in the world, under the crosshair (not a fixed area: it follows the marked point).
            _worldMarker = WorldMarkerView.Build(root);
            BuildCrosshair(root);
            BuildDeath(root);
            _canvas.enabled = false;
            foreach (var image in _crosshair.GetComponentsInChildren<Image>(true))
                if (image.name == "Core") _crosshairCores.Add(image);
            ApplyCrosshairColor();
            GameSettings.Changed += ApplyCrosshairColor;
        }

        readonly List<Image> _crosshairCores = new List<Image>();

        // The crosshair's light core takes the colour picked in the settings (the dark rim stays).
        void ApplyCrosshairColor()
        {
            var c = GameSettings.CrosshairColor;
            c.a = 0.92f;
            foreach (var image in _crosshairCores) image.color = c;
        }

        void OnDestroy()
        {
            GameSettings.Changed -= ApplyCrosshairColor;
            if (Instance == this) Instance = null;
            MapMarker.Clear(); // the marker belongs to this match
            if (_fadeUp != null) Destroy(_fadeUp);
            if (_fadeRight != null) Destroy(_fadeRight);
            if (_fadeUpTexture != null) Destroy(_fadeUpTexture);
            if (_fadeRightTexture != null) Destroy(_fadeRightTexture);
            if (_stripeTexture != null) Destroy(_stripeTexture);
        }

        // After the player controller has placed the camera for this frame.
        void LateUpdate()
        {
            if (_hitMarker.activeSelf && Time.unscaledTime >= _hitMarkerUntil) _hitMarker.SetActive(false);

            var me = Combatant.LocalPlayer;
            bool show = me != null;
            // The inventory has its own copy of everything the HUD shows (armour, ammo, grenades) and the map
            // screen covers the minimap's job; drawn through them the HUD only clutters (and overlapped the header).
            // After death the death cam / result / spectate views take over (ADR-094).
            // An event race (ADR-519) has its own HUD (RaceHudView) and the vehicle panel: the battle royale's is off.
            bool drawn = show && UiFocus.Current != UiScreen.Inventory && UiFocus.Current != UiScreen.Map && !ResultScreen.HidesHud && !Vehicle.EventRace;
            if (_canvas.enabled != drawn) _canvas.enabled = drawn;
            if (!show) return;

            var player = PlayerController.Local;
            bool dead = me.Dead.Value;
            TickHealth(me, dead);
            TickWeapons(me);
            bool driving = VehicleSeat.IsDriver(me);
            Show(_weaponPanel, !ScopeView.HidesHudWeapon && !driving);
            Show(_strip.Root.gameObject, !driving); // the speedometer stands where the weapon stack is (ADR-563)
            _strip.Tick(me);
            var swim = me.GetComponent<CombatantSwim>();
            _status.Tick(me, swim != null && swim.InWater && !dead, swim != null ? swim.Breath : 1f);
            TickDoorNotice();
            TickTopRight();
            TickPrompt(me, player, dead);
            if (player != null)
            {
                _compass.Tick(player.Yaw);
                _region.Tick(me.transform.position, player.Yaw);
            }
            _elevator.Tick(me);
            _minimap.Tick(me.transform.position, player != null ? player.Yaw : 0f, player != null);
            TickMapMarker(me, player, dead);
            TickCrosshair(me, player, dead);
            TickDamageRing(me, player);
            // The death cam and result panel say it all (ADR-094); the band is for a match without them.
            Show(_death, dead && ResultScreen.Instance == null);
        }

        // ---- Per-frame updates ----

        void TickHealth(Combatant me, bool dead)
        {
            float hp = me.Hp.Value;
            float now = Time.unscaledTime;
            if (hp != _shownHp)
            {
                // A drop holds the trail where the health was; a rise (or the first frame) takes it along.
                if (!float.IsNaN(_shownHp) && hp < _shownHp) _lagHoldUntil = now + TrailHold;
                _shownHp = hp;
                HealthFill = Mathf.Clamp01(hp / MaxHp);
                _healthFill.anchorMax = new Vector2(HealthFill, 1f);
                int shown = Mathf.CeilToInt(hp);
                if (shown != _shownHpInt)
                {
                    _shownHpInt = shown;
                    _healthNumber.text = IntText(Mathf.Max(0, shown));
                }
                bool low = hp < LowHp;
                if (low != _lowShown)
                {
                    _lowShown = low;
                    _healthFillImage.color = low ? UiKit.Danger : UiKit.Health;
                    _healthNumber.color = low ? UiKit.Text : NumberOnBar;
                }
            }
            // The red trail: drains toward the health once the hold after the last hit is over.
            float lag = _lagHp < hp ? hp : _lagHp;
            if (lag > hp && now >= _lagHoldUntil) lag = Mathf.MoveTowards(lag, hp, Mathf.Max(25f, (lag - hp) * 3f) * Time.unscaledDeltaTime);
            if (lag != _lagHp)
            {
                _lagHp = lag;
                _healthLag.anchorMax = new Vector2(Mathf.Clamp01(lag / MaxHp), 1f);
            }

            // A timed heal (bandage, kit, medkit) under way: stripes up to where it will end, a line that fills until done.
            var heal = CombatantHealing.Local;
            var item = !dead && heal != null && heal.Using ? Healing.For(heal.Kind, HealRules.Current) : null;
            float healTo = item != null && item.Amount > 0f ? Mathf.Min(MaxHp, Mathf.Min(item.MaxHp, hp + item.Amount)) : -1f;
            bool healing = healTo > hp;
            if (healing)
            {
                HealProgress = heal.Progress01;
                _healProgressFill.anchorMax = new Vector2(HealProgress, 1f);
                _shownHealName = heal.ItemName;
                if (healTo != _shownHealTo || _healIncoming.anchorMin.x != HealthFill)
                {
                    _shownHealTo = healTo;
                    float to = healTo / MaxHp;
                    _healIncoming.anchorMin = new Vector2(HealthFill, 0f);
                    _healIncoming.anchorMax = new Vector2(to, 1f);
                    _healStripes.uvRect = new Rect(0f, 0f, HudStatus.Inner * (to - HealthFill) / 12f, 1f);
                }
            }
            else if (_shownHealTo >= 0f)
            {
                _shownHealTo = -1f;
                _shownHealName = null;
                HealProgress = 0f;
            }
            Show(_healIncoming.gameObject, healing);

            // Stronger the closer to zero, with a slow pulse; a canvas group fades it without rebuilding meshes.
            float vignette = 0f;
            if (dead) vignette = 0.8f;
            else if (hp < LowHp)
                vignette = (0.35f + 0.45f * (1f - Mathf.Clamp01(hp / LowHp))) * (0.88f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f));
            if (_vignette.alpha != vignette) _vignette.alpha = vignette;
        }

        void TickWeapons(Combatant me)
        {
            int active = HudBackend.ActiveSlot(me);
            int id = HudBackend.ActiveWeaponId(me);
            WeaponDef weapon = id != 0 ? GameData.Weapons.Find(id) : null;
            if (weapon == null) id = 0;
            int mag = weapon != null ? HudBackend.ActiveMag(me) : 0;
            int reserve = HudBackend.Reserve(me, weapon);
            string mode = weapon != null ? HudBackend.FireModeOf(me, active) : "";
            if (active == _shownActive && id == _shownAmmoWeapon && mag == _shownMag && reserve == _shownReserve && mode == _shownFireMode) return;
            _shownActive = active;
            _shownAmmoWeapon = id;
            _shownMag = mag;
            _shownReserve = reserve;
            _shownFireMode = mode;
            if (weapon == null || weapon.IsMelee)
            {
                // Empty hands or a melee weapon (ADR-073): no rounds.
                AmmoText = Dash;
                AmmoTypeText = weapon == null ? "맨손" : "근접";
                _weaponName = weapon == null ? "맨손" : weapon.Name;
                _weaponIconKey = weapon == null ? "" : InventoryBackend.WeaponKey(id);
                _mag.text = Dash;
                _mag.color = UiKit.TextDim;
                _reserve.text = "";
                _fireMode.text = weapon == null ? "" : "근접";
                return;
            }
            AmmoText = IntText(mag) + " / " + IntText(reserve);
            AmmoTypeText = InventoryBackend.AmmoName(InventoryBackend.AmmoIndex(weapon));
            _weaponName = weapon.Name;
            _weaponIconKey = InventoryBackend.WeaponKey(id);
            _mag.text = IntText(mag);
            _mag.color = mag == 0 ? UiKit.Danger : UiKit.Text;
            _reserve.text = "/ " + IntText(reserve);
            _fireMode.text = mode;
        }

        void TickTopRight()
        {
            int alive = HudBackend.Alive();
            if (alive != _shownAlive)
            {
                _shownAlive = alive;
                _aliveDigits.Set(alive);
            }
            int kills = HudBackend.Kills();
            if (kills != _shownKills)
            {
                _shownKills = kills;
                _killDigits.Set(kills);
            }
        }

        void TickPrompt(Combatant me, PlayerController player, bool dead)
        {
            Show(_reload, me.Reloading.Value && !dead);

            LootItem loot = !dead && player != null ? player.NearbyLoot : null;
            bool hasLoot = loot != null && !PickupList.Showing; // two or more: the list by the crosshair (G5)
            if (hasLoot && !ReferenceEquals(loot, _promptFor))
            {
                _promptFor = loot;
                _promptLabel.text = "[F]  " + LootLabel(loot);
                string name = InventoryBackend.LootName(loot);
                _promptIcon.Set(InventoryBackend.LootIconKey(loot), name);
            }
            // No loot to take and a vehicle with a free seat in reach: F gets in (#190).
            bool vehicle = !dead && loot == null && HudBackend.VehicleEnterable(me);
            if (vehicle)
            {
                _promptFor = null;
                _promptLabel.text = "[F]  탑승";
            }
            // The waiting island's lift call button (ADR-490): the player clears the loot while it is in reach.
            bool liftCall = !dead && !vehicle && player != null && player.LiftCallInReach;
            if (liftCall)
            {
                _promptFor = null;
                _promptLabel.text = "[F]  엘리베이터 호출";
            }
            // By the race gate with no car in reach yet: the way in (#483).
            bool raceHint = !dead && loot == null && !vehicle && !liftCall && HudBackend.RaceHallHint(me);
            if (raceHint)
            {
                _promptFor = null;
                _promptLabel.text = "경주장  ·  차 옆에서 [F] 탑승";
            }
            Show(_promptIconRoot, hasLoot);
            Show(_prompt, hasLoot || vehicle || liftCall || raceHint);
        }

        // Shows how the weapon is aimed (ADR-048): four lines at the edge of the spread cone from the hip, closer
        // together when shouldered, a single dot when looking down the sights or with empty hands.
        // Hidden while a magnified scope overlay is up (it draws its own reticle).
        void TickCrosshair(Combatant me, PlayerController player, bool dead)
        {
            bool visible = !dead && UiFocus.Current == UiScreen.None && !HudBackend.ScopeActive;
            Show(_crosshair, visible);
            if (!visible)
            {
                _gap = -1f;
                return;
            }
            WeaponDef weapon = me.ActiveWeapon;
            AimMode aim = me.AimMode;
            Camera cam = player != null ? player.Camera : null;
            bool dot = weapon == null || weapon.IsMelee || aim == AimMode.Sights || cam == null;
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

        static readonly Color LimbMarker = new Color(0.66f, 0.82f, 1f, 1f);

        void ShowHitMarker(bool kill, bool head, bool limb)
        {
            HeadMarkerShown = head;
            LimbMarkerShown = limb && !head && !kill;
            Color color = kill ? UiKit.Danger : head ? UiKit.Accent : LimbMarkerShown ? LimbMarker : Color.white;
            HitMarkerColor = color;
            for (int i = 0; i < _hitArms.Length; i++) _hitArms[i].color = color;
            float scale = kill ? (head ? 1.8f : 1.5f) : head ? 1.25f : LimbMarkerShown ? 0.85f : 1f;
            _hitMarker.transform.localScale = new Vector3(scale, scale, 1f);
            _hitMarkerUntil = Time.unscaledTime + HitMarkerSeconds;
            _hitMarker.SetActive(true);
        }

        void ShowDamageFrom(Vector3 worldPosition)
        {
            if (!GameSettings.ShowDamageDirection) return;
            // A free slot, else the one that has faded the most.
            int slot = 0;
            for (int i = 1; i < DamageSlots; i++)
                if (_damageLeft[i] < _damageLeft[slot]) slot = i;
            _damageFrom[slot] = worldPosition;
            _damageLeft[slot] = DamageSeconds;
            _damageGroup[slot].alpha = 1f;
            _damagePivot[slot].gameObject.SetActive(true);
        }

        // The map marker (ADR-065 (6)): cleared on arrival, else shown on the compass strip and in the world with its
        // distance. One whole-metre distance feeds both labels, so they change together and only when it does.
        void TickMapMarker(Combatant me, PlayerController player, bool dead)
        {
            var position = me.transform.position;
            if (!dead) MapMarker.TickReach(position);
            bool has = MapMarker.Has;
            int metres = has ? Mathf.RoundToInt(MapMarker.DistanceFrom(position)) : 0;
            float bearing = has ? MapMarker.BearingFrom(position) : 0f;
            _compass.TickMarker(has && player != null, player != null ? player.Yaw : 0f, bearing, metres);
            Camera cam = player != null ? player.Camera : null;
            _worldMarker.Tick(cam, _canvas.scaleFactor, has && !dead && UiFocus.Current == UiScreen.None, metres);
        }

        void TickDoorNotice()
        {
            // Not over the result panel (#101): the match is decided, the doors no longer matter.
            bool over = ResultScreen.Instance != null && ResultScreen.Instance.IsShown;
            int left = HudBackend.DoorsClosed && !over ? Mathf.Max(1, Mathf.CeilToInt(HudBackend.DoorSecondsLeft)) : 0;
            if (left == _doorShown) return;
            _doorShown = left;
            _doorNoticeRoot.SetActive(left > 0);
            if (left > 0) _doorNotice.text = "엘리베이터 상승 중  ·  문 열림 " + left;
        }

        // ---- Construction ----

        // A dark plate with a thin steel outline.
        static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = UiKit.Rect(name, parent, anchor, anchor, position, size);
            UiKit.Box(rect, UiKit.Panel);
            UiKit.Frame(rect, 1f, UiKit.SteelDim);
            return rect;
        }

        static Text Label(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, string text, int fontSize, Color color,
            TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            return UiKit.Label(UiKit.Rect(name, parent, anchor, anchor, position, size), text, fontSize, color, align, style);
        }

        // Top centre compass, top left region card, top right alive and kill cards (the kill feed stacks below them).
        void BuildTop(Transform root)
        {
            _compass = CompassStrip.Build(root, TopMargin);
            _areas.Add(_compass.Strip);
            _areas.Add(_compass.Heading);

            _region = RegionCard.Build(root, TopLeft, new Vector2(Margin, -TopMargin));
            _areas.Add(_region.Root);
            // Under the region card, only while standing in a station (ADR-076).
            _elevator = ElevatorPanel.Build(root, TopLeft, new Vector2(Margin, -TopMargin - RegionCard.Height - StackGap));

            _killDigits = StatCard(root, "Kills", "처치", -Margin);
            _aliveDigits = StatCard(root, "Alive", "생존", -Margin - StatWidth - StackGap);
        }

        HudDigits StatCard(Transform root, string name, string caption, float x)
        {
            var card = Panel(name, root, Vector2.one, new Vector2(x, -TopMargin), new Vector2(StatWidth, StatHeight));
            Label("Caption", card, MidLeft, new Vector2(14f, 0f), new Vector2(60f, 40f), caption, 20, UiKit.TextDim);
            var digits = HudDigits.Build("Digits", card, MidRight, MidRight, new Vector2(-12f, 0f), 3, 20f, 40f, 28, UiKit.Text);
            digits.Set(0);
            _areas.Add(card);
            return digits;
        }

        static readonly Color NumberOnBar = new Color(0.067f, 0.086f, 0.11f, 1f);
        static readonly Color TrailRed = new Color(0.82f, 0.22f, 0.18f, 0.7f);

        // The vitals block (HUD v2, ADR-563): 520 wide, centred, 36 off the bottom. HudStatus builds the chips, armour,
        // effects and boost gauge; the ammo box and the health bar are built here.
        void BuildHealthAndWeapon(Transform root)
        {
            var bottom = new Vector2(0.5f, 0f);
            var block = UiKit.Rect("Vitals", root, bottom, bottom, new Vector2(0f, Margin), new Vector2(HudStatus.Width, HudStatus.Height));
            _status = HudStatus.Build(block);
            _areas.Add(block);

            // Ammo, centred in the row: the magazine large, "/ reserve" and the fire mode small.
            var top = new Vector2(0.5f, 1f);
            var ammo = UiKit.Rect("Ammo", block, top, top, new Vector2(0f, -HudStatus.RowTop), new Vector2(AmmoWidth, HudStatus.RowHeight));
            HudStatus.Rounded(UiKit.Box(ammo, HudStatus.Plate), 4);
            _mag = Label("Mag", ammo, MidLeft, new Vector2(4f, 0f), new Vector2(84f, 44f), Dash, 40, UiKit.TextDim, TextAnchor.MiddleRight, FontStyle.Bold);
            _reserve = Label("Reserve", ammo, MidLeft, new Vector2(94f, -4f), new Vector2(70f, 30f), "", 18, UiKit.TextDim);
            _fireMode = Label("FireMode", ammo, MidLeft, new Vector2(160f, -5f), new Vector2(46f, 26f), "", 13, HudStatus.Mute);
            _weaponPanel = ammo.gameObject;

            // Health: the red trail of the last hit, over it the heal stripes, over those the health fill.
            var health = _status.HealthSlot;
            HudStatus.Rounded(UiKit.Box(health, HudStatus.PlateStrong), 3);
            var inside = UiKit.Fill("Inside", health, 1f);
            _healthLag = UiKit.Fill("Trail", inside);
            UiKit.Box(_healthLag, TrailRed);
            _healIncoming = UiKit.Fill("HealIncoming", inside);
            _stripeTexture = new Texture2D(12, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            for (int i = 0; i < 12; i++) _stripeTexture.SetPixel(i, 0, new Color(1f, 1f, 1f, i < 6 ? 0.55f : 0.25f));
            _stripeTexture.Apply();
            _healStripes = _healIncoming.gameObject.AddComponent<RawImage>();
            _healStripes.texture = _stripeTexture;
            _healStripes.color = HudStatus.HealGreen;
            _healStripes.raycastTarget = false;
            var line = UiKit.Rect("Progress", _healIncoming, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            line.anchorMax = new Vector2(1f, 0f);
            line.sizeDelta = new Vector2(0f, 3f);
            _healProgressFill = UiKit.Fill("Fill", line);
            _healProgressFill.anchorMax = new Vector2(0f, 1f);
            UiKit.Box(_healProgressFill, HudStatus.HealGreen);
            _healIncoming.gameObject.SetActive(false);
            _healthFill = UiKit.Fill("Fill", inside);
            _healthFillImage = UiKit.Box(_healthFill, UiKit.Health);
            UiKit.Frame(health, 1f, HudStatus.Line);
            _healthNumber = Label("Value", health, MidLeft, new Vector2(12f, 0f), new Vector2(60f, 30f), "100", 22, NumberOnBar, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Transient prompts above the block (not part of the fixed layout).
            float reloadY = Margin + HudStatus.Height + StackGap;
            var reload = Panel("Reloading", root, bottom, new Vector2(0f, reloadY), new Vector2(200f, 32f));
            UiKit.Label(UiKit.Fill("Label", reload), "재장전 중", 18, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            _reload = reload.gameObject;
            _reload.SetActive(false);

            var prompt = Panel("Pickup", root, bottom, new Vector2(0f, reloadY + 32f + StackGap), new Vector2(460f, 52f));
            var promptIconRect = UiKit.Rect("Icon", prompt, MidLeft, MidLeft, new Vector2(8f, 0f), new Vector2(40f, 40f));
            _promptIcon = InventoryItemIcon.Build(promptIconRect);
            _promptIconRoot = promptIconRect.gameObject;
            _promptLabel = UiKit.Label(UiKit.Rect("Label", prompt, MidLeft, MidLeft, new Vector2(60f, 0f), new Vector2(384f, 46f)), "", 22, UiKit.Text, TextAnchor.MiddleCenter);
            _prompt = prompt.gameObject;
            _prompt.SetActive(false);
        }

        // Bottom left: the player's own status. Bottom right: the minimap over the weapon strip.
        void BuildBottomSides(Transform root)
        {
            _strip = WeaponStrip.Build(root, BottomRight, BottomRight, new Vector2(-Margin, Margin));
            _areas.Add(_strip.Root);
            _minimap = MinimapView.Build(root, BottomRight, BottomRight, new Vector2(-Margin, Margin + WeaponStrip.CellHeight + StripGap));
            _areas.Add(_minimap.Root);
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

        // Cached so the numbers that change most never allocate after their first use.
        static string IntText(int value) => HudDigits.IntText(value);

        static string LootLabel(LootItem item)
        {
            return InventoryBackend.LootName(item) + ((LootKind)item.Kind.Value == LootKind.Weapon ? "" : " x" + item.Amount.Value);
        }
    }
}
