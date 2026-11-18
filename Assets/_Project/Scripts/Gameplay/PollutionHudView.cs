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
        public string GasText { get; private set; } = "";
        public float TintAlpha => _tint != null ? _tint.color.a : 0f;

        PollutionDirector _director;
        Image _tint;
        Text _gas;
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
        static void ResetStatics() => Model = default;

        void Awake()
        {
            _director = GetComponentInParent<PollutionDirector>();
            var canvas = UiKit.Canvas("PollutionHudCanvas", 1, false);
            canvas.transform.SetParent(transform, false);
            _tint = UiKit.Box(UiKit.Fill("GasTint", canvas.transform), new Color(0.55f, 0.1f, 0.05f, 0f));
            _gas = UiKit.Label(UiKit.Rect("GasLine", canvas.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(700f, 40f)),
                "", 26, UiKit.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        void OnDestroy() => Model = default;

        void Update()
        {
            if (_director == null) { _director = PollutionDirector.Instance; if (_director == null) return; }
            var me = Combatant.LocalPlayer;
            var match = _director.Match;
            if (me == null || me.Dead.Value || match == null) { Apply(default, null, 0); return; }

            var keys = me.GetComponent<KeyHolder>();
            _cover.Cover = keys != null ? new GasCover { Until = keys.CoverUntil.Value, Factor = keys.CoverFactor.Value } : default;
            var p = me.transform.position;
            var model = PollutionHud.Build(match, _director.Now, p.x, p.z, _cover);
            string region = model.BannerVisible ? _director.RegionNameOf(model.BannerRegion) : "";
            Apply(model, region, keys != null ? keys.Keys.Value : 0);
        }

        void Apply(PollutionHudModel model, string region, int keys)
        {
            Model = model;
            var hud = Hud.Instance;
            if (hud != null && hud.Hazard != null) hud.Hazard.Set(PollutionHud.ToHazard(model, region, keys, PollutionChipFlag.None));
            string line = PollutionHud.GasLine(model);
            if (line != GasText) { GasText = line; _gas.text = line; }
            var c = _tint.color;
            float a = model.InGas ? 0.08f + 0.22f * model.Vignette01 : 0f;
            if (!Mathf.Approximately(c.a, a)) { c.a = a; _tint.color = c; }
        }
    }
}
