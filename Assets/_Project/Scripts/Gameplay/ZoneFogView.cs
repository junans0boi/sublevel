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
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fog.Colour;
                // A scene that already has exponential fog never gets thinner fog than its own.
                float floor = _baseFog && _baseMode != FogMode.Linear ? _baseDensity : 0f;
                RenderSettings.fogDensity = Mathf.Max(floor, fog.Density);
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
        }

        void Restore()
        {
            _applied = false;
            if (!_saved) return;
            RenderSettings.fog = _baseFog;
            RenderSettings.fogMode = _baseMode;
            RenderSettings.fogColor = _baseColour;
            RenderSettings.fogDensity = _baseDensity;
        }
    }
}
