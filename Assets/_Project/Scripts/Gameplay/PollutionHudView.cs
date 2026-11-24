using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Feeds the pollution into the HUD for the local player (S6 T7, ADR-246): the hazard slot (warning, keys), a red
    // tint and the zone panel over the minimap (HUD v2: next sealing, its clock, sealed count, the gas damage). Installs itself next to every
    // PollutionDirector like ZoneBanner; nothing in the scene refers to it. Text is rebuilt only when it changes.
    public class PollutionHudView : MonoBehaviour
    {
        public static PollutionHudModel Model { get; private set; }
        // Geiger (#270, #291): this view clicks the recorded counter (Sfx/geiger_click_*) at this rate; the event is for others.
        public static float GeigerClicksPerSecond { get; private set; }
        public static event System.Action<float> GeigerRateChanged;
        public string CoverIconKey { get; private set; } = "";
        public bool CoverBadgeShown => _badge != null && _badge.activeSelf;
        GameObject _badge;
        Image _badgeIcon;
        Text _badgeText;
        int _badgeSeconds = -1;
        public string GasText { get; private set; } = "";
        public float TintAlpha => _tint != null ? _tint.color.a : 0f;

        PollutionDirector _director;
        Image _tint;
        AudioSource _geiger;
        float _clickIn;
        static AudioClip[] _clicks;
        static AudioClip[] Clicks => _clicks ??= System.Array.FindAll(new[] { Resources.Load<AudioClip>("Sfx/geiger_click_01"), Resources.Load<AudioClip>("Sfx/geiger_click_02") }, c => c != null);
        readonly PollutionExposure _cover = new PollutionExposure();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            PollutionDirector.Started -= Install;
            PollutionDirector.Started += Install;
        }

        static void Install(PollutionDirector director)
        {
            if (director == null || director.GetComponentInChildren<PollutionHudView>() != null) return;
            var go = new GameObject("PollutionHudView");
            go.transform.SetParent(director.transform, false);
            go.AddComponent<PollutionHudView>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Model = default; GeigerClicksPerSecond = 0f; GeigerRateChanged = null; }

        void Awake()
        {
            _director = GetComponentInParent<PollutionDirector>();
            var canvas = UiKit.Canvas("PollutionHudCanvas", 1, false);
            canvas.transform.SetParent(transform, false);
            _tint = UiKit.Box(UiKit.Fill("GasTint", canvas.transform), new Color(0.55f, 0.1f, 0.05f, 0f));
            // The cover badge above the gas line: the filter or antidote icon and the time left, while a cover runs.
            var badge = UiKit.Rect("CoverBadge", canvas.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 282f), new Vector2(150f, 48f));
            UiKit.Box(badge, UiKit.Panel);
            UiKit.Frame(badge, 1f, UiKit.SteelDim);
            var icon = UiKit.Rect("Icon", badge, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(42f, 42f));
            _badgeIcon = icon.gameObject.AddComponent<Image>();
            _badgeIcon.preserveAspect = true;
            _badgeIcon.raycastTarget = false;
            _badgeText = UiKit.Label(UiKit.Rect("Time", badge, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(92f, 40f)),
                "", 24, UiKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            _badge = badge.gameObject;
            _badge.SetActive(false);
            _geiger = gameObject.AddComponent<AudioSource>();
            _geiger.playOnAwake = false;
            _geiger.spatialBlend = 0f; // the counter is on the player
        }

        void TickGeiger()
        {
            float rate = GeigerClicksPerSecond;
            if (rate <= 0f) { _clickIn = 0f; return; }
            _clickIn -= Time.deltaTime;
            if (_clickIn > 0f) return;
            _clickIn = PollutionHud.NextGeigerGap(rate, Random.value);
            var clips = Clicks;
            if (clips.Length > 0) _geiger.PlayOneShot(clips[Random.Range(0, clips.Length)], 0.55f * GameSettings.EffectsVolume);
        }

        void OnDestroy() => Model = default;

        void Update()
        {
            TickGeiger();
            if (_director == null) { _director = PollutionDirector.Instance; if (_director == null) return; }
            var me = Combatant.LocalPlayer;
            var match = _director.Match;
            if (me == null || me.Dead.Value || match == null) { Apply(default, null, 0); ShowZone(false, default, "", "", 0f, 0, 0); return; }

            var keys = me.GetComponent<KeyHolder>();
            _cover.Cover = keys != null ? new GasCover { Until = keys.CoverUntil.Value, Factor = keys.CoverFactor.Value } : default;
            var p = me.transform.position;
            var model = PollutionHud.Build(match, _director.Now, p.x, p.z, _cover);
            // Sealed in the gas names where the player is, not the next warned region (it read "[봉쇄됨] <a warned region>").
            string region = model.InGas ? _director.RegionNameOf(_director.RegionIndexAt(p)) : model.BannerVisible ? _director.RegionNameOf(model.BannerRegion) : "";
            Apply(model, region, keys != null ? keys.Keys.Value : 0);

            // The zone panel: the announced region and its clock; before any announcement, the time to the next one.
            var tl = match.Timeline;
            int next = tl.NextConfirmIndex(_director.Now);
            float lead = match.Rules.WarningLeadSeconds;
            if (model.BannerVisible)
                ShowZone(true, model, _director.RegionNameOf(model.BannerRegion) + " 봉쇄까지", PollutionHud.Clock(model.BannerSeconds), 1f - model.BannerSeconds / Mathf.Max(1f, lead), model.Confirmed, match.RegionCount);
            else if (next >= 0)
                ShowZone(true, model, "다음 경보까지", PollutionHud.Clock((float)(tl.WarningAt(tl.Order[next]) - _director.Now)), 0f, model.Confirmed, match.RegionCount);
            else
                ShowZone(true, model, "모든 구역 봉쇄", "", 1f, model.Confirmed, match.RegionCount);
        }

        static void ShowZone(bool on, PollutionHudModel model, string title, string clock, float bar, int sealedCount, int total)
        {
            var zone = Hud.Instance != null ? Hud.Instance.Zone : null;
            if (zone != null) zone.Set(on, title, clock, bar, sealedCount, total, model.InGas ? PollutionHud.GasLine(model) : "");
        }

        void Apply(PollutionHudModel model, string region, int keys)
        {
            Model = model;
            var hud = Hud.Instance;
            if (hud != null && hud.Hazard != null) hud.Hazard.Set(PollutionHud.ToHazard(model, region, keys, PollutionChipFlag.None));
            string line = PollutionHud.GasLine(model);
            if (line != GasText) GasText = line; // shown on the zone panel
            ShowCover(model);
            float geiger = PollutionHud.GeigerClicksPerSecond(model);
            if (!Mathf.Approximately(geiger, GeigerClicksPerSecond)) { GeigerClicksPerSecond = geiger; GeigerRateChanged?.Invoke(geiger); }
            var c = _tint.color;
            float a = model.InGas ? 0.08f + 0.22f * model.Vignette01 : 0f;
            if (!Mathf.Approximately(c.a, a)) { c.a = a; _tint.color = c; }
        }

        void ShowCover(PollutionHudModel model)
        {
            string key = PollutionHud.CoverIconKey(model);
            if (key != CoverIconKey)
            {
                CoverIconKey = key;
                _badgeIcon.sprite = key.Length > 0 ? ItemIcons.Get(key) : null;
                _badge.SetActive(key.Length > 0);
                _badgeSeconds = -1;
            }
            int s = Mathf.CeilToInt(model.CoverSeconds);
            if (key.Length > 0 && s != _badgeSeconds) { _badgeSeconds = s; _badgeText.text = PollutionHud.Clock(model.CoverSeconds); }
        }
    }

    // The zone panel (HUD v2, bottom right over the minimap): "<region> 봉쇄까지 1:24", a bar filling toward the
    // sealing, "봉쇄된 구역 2 / 9", and while the player is in the gas a purple outline and the damage line.
    // Built by the HUD, fed by PollutionHudView; text is rewritten only when it changes.
    public sealed class ZonePanel
    {
        public const float Width = 220f, Bottom = 300f;
        const float Pad = 10f, TitleHeight = 28f, BarHeight = 6f, LineHeight = 20f;
        const float BaseHeight = 6f + TitleHeight + 5f + BarHeight + 4f + LineHeight + 6f;
        public static readonly Color Gas = new Color(0.69f, 0.42f, 0.94f, 1f); // #b06cf0, the mock's pollution colour

        public RectTransform Root { get; private set; }
        Text _title, _clock, _count, _gas;
        RectTransform _fill;
        Image[] _outline;
        string _shownTitle, _shownClock, _shownGas;
        int _shownCount = -1;
        float _shownBar = -1f;

        // ---- Read-only state for tests ----
        public bool Shown => Root.gameObject.activeSelf;
        public string TitleText => _title.text;
        public string ClockText => _clock.text;
        public string CountText => _count.text;
        public string GasText => _gas.text;
        public bool Hot => _outline[0].enabled;
        public float Bar => _shownBar;

        public static ZonePanel Build(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position)
        {
            var z = new ZonePanel();
            var root = UiKit.Rect("ZonePanel", parent, anchor, pivot, position, new Vector2(Width, BaseHeight));
            UiKit.Box(root, UiKit.Panel);
            z.Root = root;
            var topLeft = new Vector2(0f, 1f);
            var topRight = Vector2.one;
            z._title = UiKit.Label(UiKit.Rect("Title", root, topLeft, topLeft, new Vector2(Pad, -6f), new Vector2(Width - 2f * Pad - 56f, TitleHeight)), "", 15, UiKit.TextDim);
            z._title.resizeTextForBestFit = true; // a long region name shrinks rather than running under the clock
            z._title.resizeTextMinSize = 11;
            z._title.resizeTextMaxSize = 15;
            z._clock = UiKit.Label(UiKit.Rect("Clock", root, topRight, topRight, new Vector2(-Pad, -6f), new Vector2(56f, TitleHeight)), "", 22, UiKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            var track = UiKit.Rect("Track", root, topLeft, topLeft, new Vector2(Pad, -6f - TitleHeight - 5f), new Vector2(Width - 2f * Pad, BarHeight));
            UiKit.Box(track, new Color(1f, 1f, 1f, 0.12f));
            z._fill = UiKit.Fill("Fill", track);
            z._fill.anchorMax = new Vector2(0f, 1f);
            UiKit.Box(z._fill, Gas);
            float lineY = -6f - TitleHeight - 5f - BarHeight - 4f;
            z._count = UiKit.Label(UiKit.Rect("Sealed", root, topLeft, topLeft, new Vector2(Pad, lineY), new Vector2(Width - 2f * Pad, LineHeight)), "", 14, UiKit.TextDim);
            z._gas = UiKit.Label(UiKit.Rect("Gas", root, topLeft, topLeft, new Vector2(Pad, lineY - LineHeight), new Vector2(Width - 2f * Pad, LineHeight)), "", 14, Gas, TextAnchor.MiddleLeft, FontStyle.Bold);
            z._gas.resizeTextForBestFit = true;
            z._gas.resizeTextMinSize = 10;
            z._gas.resizeTextMaxSize = 14;
            z._outline = UiKit.Frame(root, 2f, Gas);
            foreach (var line in z._outline) line.enabled = false;
            root.gameObject.SetActive(false);
            return z;
        }

        public void Set(bool on, string title, string clock, float bar, int sealedCount, int total, string gasLine)
        {
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
            if (!on) return;
            if (title != _shownTitle) { _shownTitle = title; _title.text = title; }
            if (clock != _shownClock) { _shownClock = clock; _clock.text = clock; }
            bar = Mathf.Clamp01(bar);
            if (!Mathf.Approximately(bar, _shownBar)) { _shownBar = bar; _fill.anchorMax = new Vector2(bar, 1f); }
            int countKey = sealedCount * 1000 + total;
            if (countKey != _shownCount) { _shownCount = countKey; _count.text = "봉쇄된 구역 " + HudDigits.IntText(sealedCount) + " / " + HudDigits.IntText(total); }
            if (gasLine != _shownGas)
            {
                _shownGas = gasLine;
                _gas.text = gasLine;
                bool hot = gasLine.Length > 0;
                foreach (var line in _outline) line.enabled = hot;
                // The panel grows upward by the gas line; its bottom stays on the contract line.
                Root.sizeDelta = new Vector2(Width, BaseHeight + (hot ? LineHeight : 0f));
            }
        }
    }
}
