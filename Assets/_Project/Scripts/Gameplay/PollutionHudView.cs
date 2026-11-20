using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Feeds the pollution into the HUD for the local player (S6 T7, ADR-246): the hazard slot (warning, keys), a red
    // tint and one gas line with the damage per second and the cover left. Installs itself next to every
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
        Text _gas;
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
            _gas = UiKit.Label(UiKit.Rect("GasLine", canvas.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 236f), new Vector2(700f, 40f)), // above the weapon strip (190 overlapped its top)
                "", 26, UiKit.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
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
            if (me == null || me.Dead.Value || match == null) { Apply(default, null, 0); return; }

            var keys = me.GetComponent<KeyHolder>();
            _cover.Cover = keys != null ? new GasCover { Until = keys.CoverUntil.Value, Factor = keys.CoverFactor.Value } : default;
            var p = me.transform.position;
            var model = PollutionHud.Build(match, _director.Now, p.x, p.z, _cover);
            // Sealed in the gas names where the player is, not the next warned region (it read "[봉쇄됨] <a warned region>").
            string region = model.InGas ? _director.RegionNameOf(_director.RegionIndexAt(p)) : model.BannerVisible ? _director.RegionNameOf(model.BannerRegion) : "";
            Apply(model, region, keys != null ? keys.Keys.Value : 0);
        }

        void Apply(PollutionHudModel model, string region, int keys)
        {
            Model = model;
            var hud = Hud.Instance;
            if (hud != null && hud.Hazard != null) hud.Hazard.Set(PollutionHud.ToHazard(model, region, keys, PollutionChipFlag.None));
            string line = PollutionHud.GasLine(model);
            if (line != GasText) { GasText = line; _gas.text = line; }
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
}
