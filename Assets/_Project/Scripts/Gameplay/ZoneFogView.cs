using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Applies the lockdown's radiation fog (ZoneFog.Current, eased by ZoneBanner for the local player) to the scene's
    // render settings, and puts the scene's own fog back once it has cleared. Installs itself next to every
    // ZoneDirector like the banner; nothing in the scene refers to it.
    public class ZoneFogView : MonoBehaviour
    {
        bool _saved, _applied;
        bool _baseFog;
        FogMode _baseMode;
        Color _baseColour;
        float _baseDensity;
        float _baseStart, _baseEnd;

        // The fog is drawn as linear fog: the builds keep only the fog modes the scenes use (automatic stripping) and
        // the World uses linear fog, so exponential fog would not show at all in a player build. These turn the
        // exponential-squared density of ZoneFog into a linear start and end (about the same look at mid range).
        const float StartPerDensity = 0.15f, EndPerDensity = 1.6f;

        // True while the radiation fog is what the render settings show.
        public bool Applied => _applied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            ZoneDirector.Started -= Install;
            ZoneDirector.Started += Install;
        }

        static void Install(ZoneDirector director)
        {
            if (director == null || director.GetComponentInChildren<ZoneFogView>() != null) return;
            var go = new GameObject("ZoneFogView");
            // Under the director so it goes away with the scene (and restores the fog on the way).
            go.transform.SetParent(director.transform, false);
            go.AddComponent<ZoneFogView>();
        }

        void LateUpdate()
        {
            var fog = ZoneFog.Current;
            bool want = fog.Active && Combatant.LocalPlayer != null;
            if (want)
            {
                if (!_applied) Save();
                _applied = true;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = fog.Colour;
                // A scene that already has linear fog never gets thinner fog than its own.
                float density = Mathf.Max(fog.Density, 1e-5f);
                float start = StartPerDensity / density, end = EndPerDensity / density;
                if (_baseFog && _baseMode == FogMode.Linear)
                {
                    start = Mathf.Min(start, _baseStart);
                    end = Mathf.Min(end, _baseEnd);
                }
                RenderSettings.fogStartDistance = start;
                RenderSettings.fogEndDistance = Mathf.Max(start + 1f, end);
            }
            else if (_applied)
            {
                Restore();
            }
        }

        void OnDisable()
        {
            if (_applied) Restore();
        }

        void Save()
        {
            _saved = true;
            _baseFog = RenderSettings.fog;
            _baseMode = RenderSettings.fogMode;
            _baseColour = RenderSettings.fogColor;
            _baseDensity = RenderSettings.fogDensity;
            _baseStart = RenderSettings.fogStartDistance;
            _baseEnd = RenderSettings.fogEndDistance;
        }

        void Restore()
        {
            _applied = false;
            if (!_saved) return;
            RenderSettings.fog = _baseFog;
            RenderSettings.fogMode = _baseMode;
            RenderSettings.fogColor = _baseColour;
            RenderSettings.fogDensity = _baseDensity;
            RenderSettings.fogStartDistance = _baseStart;
            RenderSettings.fogEndDistance = _baseEnd;
        }
    }
}
