using UnityEngine;

namespace Game.Gameplay
{
    // Player preferences that outlive a run. Loaded from PlayerPrefs before the first scene and pushed to the
    // systems that use them; every change is clamped, applied and saved straight away.
    public static class GameSettings
    {
        public const string MouseSensitivityKey = "settings.mouseSensitivity";
        public const string MasterVolumeKey = "settings.masterVolume";

        public const float MinMouseSensitivity = 0.03f;
        public const float MaxMouseSensitivity = 0.4f;
        public const float DefaultMasterVolume = 1f;

        static float _mouseSensitivity = DeviceInput.DefaultMouseSensitivity;
        static float _masterVolume = DefaultMasterVolume;

        public static float MouseSensitivity
        {
            get => _mouseSensitivity;
            set
            {
                _mouseSensitivity = Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity);
                Apply();
                PlayerPrefs.SetFloat(MouseSensitivityKey, _mouseSensitivity);
                PlayerPrefs.Save();
            }
        }

        public static float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = Mathf.Clamp01(value);
                Apply();
                PlayerPrefs.SetFloat(MasterVolumeKey, _masterVolume);
                PlayerPrefs.Save();
            }
        }

        // Reads the saved values (defaults when nothing is saved) and applies them. Does not write anything.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load()
        {
            _mouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(MouseSensitivityKey, DeviceInput.DefaultMouseSensitivity),
                MinMouseSensitivity, MaxMouseSensitivity);
            _masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume));
            Apply();
        }

        static void Apply()
        {
            DeviceInput.MouseSensitivity = _mouseSensitivity;
            AudioListener.volume = _masterVolume;
        }
    }
}
