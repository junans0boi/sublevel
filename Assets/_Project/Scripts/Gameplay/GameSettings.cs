using System;
using UnityEngine;

namespace Game.Gameplay
{
    // Player preferences that outlive a run (rows listed in SettingsCatalog). Loaded from PlayerPrefs before the first
    // scene and pushed to the systems that use them.
    //
    // Two ways to change a value:
    // - the settings screen previews: Preview() applies at once but saves nothing; Commit() (적용) saves every row,
    //   Revert() (뒤로 가기 with unapplied changes) puts the last saved values back;
    // - the typed setters below (MouseSensitivity = ..., GraphicsOptions.Preset = ...) apply and save straight away.
    //
    // Other systems read the static getters; `Changed` fires after every change so built UI can refresh.
    public static class GameSettings
    {
        public const string MouseSensitivityKey = "settings.mouseSensitivity";
        public const string MasterVolumeKey = "settings.masterVolume";
        public const string AimSensitivityKey = "settings.aimSensitivityFactor";
        public const string DefaultViewKey = "settings.defaultViewFirstPerson";

        public const float MinAimSensitivityFactor = 0.3f;
        public const float MaxAimSensitivityFactor = 1.5f;
        public const float DefaultAimSensitivityFactor = 1f;
        public const float MinMouseSensitivity = 0.03f;
        public const float MaxMouseSensitivity = 0.4f;
        public const float DefaultMasterVolume = 1f;

        static readonly Color[] CrosshairColors =
        {
            new Color(1f, 1f, 1f, 1f),        // 흰색
            new Color(0.35f, 1f, 0.42f, 1f),  // 녹색
            new Color(1f, 0.92f, 0.25f, 1f),  // 노랑
            new Color(0.3f, 0.95f, 1f, 1f),   // 청록
        };

        static readonly float[] Values = SettingsCatalog.Defaults();
        static readonly float[] Committed = SettingsCatalog.Defaults();
        static readonly float[] Scratch = new float[SettingsCatalog.Count];

        // Fires after any value changed (preview, revert, commit of a typed setter, load).
        public static event Action Changed;

        // ---- generic access (the settings screen) ----

        public static float Get(SettingId id) => Values[(int)id];
        public static int GetIndex(SettingId id) => Mathf.RoundToInt(Values[(int)id]);
        public static bool GetToggle(SettingId id) => Values[(int)id] >= 0.5f;

        // Applies a value without saving it (the screen's live preview).
        public static void Preview(SettingId id, float value)
        {
            SettingsCatalog.Change(Values, id, value);
            ApplyLive();
        }

        // True while a previewed value differs from the saved one.
        public static bool HasUnappliedChanges
        {
            get
            {
                for (int i = 0; i < Values.Length; i++)
                    if (!Mathf.Approximately(Values[i], Committed[i])) return true;
                return false;
            }
        }

        // 적용: saves every row; the display (mode, resolution) changes now if it was edited.
        public static void Commit()
        {
            bool display = Differs(SettingId.ScreenMode) || Differs(SettingId.Resolution);
            Array.Copy(Values, Committed, Values.Length);
            for (int i = 0; i < Values.Length; i++) Save(i);
            PlayerPrefs.Save();
            if (display) GraphicsOptions.ApplyDisplay();
            Changed?.Invoke();
        }

        // 뒤로 가기: drops the previewed values.
        public static void Revert()
        {
            if (!HasUnappliedChanges) return;
            Array.Copy(Committed, Values, Values.Length);
            ApplyLive();
        }

        // 기본: previews the defaults of one tab (saved only on 적용).
        public static void ResetTab(SettingsTab tab)
        {
            SettingsCatalog.ResetTab(Values, tab, GraphicsOptions.NativeResolutionIndex);
            ApplyLive();
        }

        // Applies and saves one row at once (and the rows it drags along, e.g. a preset's advanced rows). Other
        // previewed rows stay previewed.
        public static void SetAndSave(SettingId id, float value)
        {
            Array.Copy(Values, Scratch, Values.Length);
            SettingsCatalog.Change(Values, id, value);
            for (int i = 0; i < Values.Length; i++)
            {
                if (i != (int)id && Mathf.Approximately(Values[i], Scratch[i])) continue;
                Committed[i] = Values[i];
                Save(i);
            }
            PlayerPrefs.Save();
            if (SettingsCatalog.Get(id).AppliesOnCommit) GraphicsOptions.ApplyDisplay();
            ApplyLive();
        }

        static bool Differs(SettingId id) => !Mathf.Approximately(Values[(int)id], Committed[(int)id]);

        // ---- typed access (what the rest of the game reads) ----

        public static float MouseSensitivity
        {
            get => Values[(int)SettingId.MouseSensitivity];
            set => SetAndSave(SettingId.MouseSensitivity, value);
        }

        // 0..1, also AudioListener.volume.
        public static float MasterVolume
        {
            get => Values[(int)SettingId.MasterVolume];
            set => SetAndSave(SettingId.MasterVolume, value);
        }

        // Multiplies the mouse sensitivity while aiming (shouldered or down sights).
        public static float AimSensitivityFactor
        {
            get => Values[(int)SettingId.AimSensitivity];
            set => SetAndSave(SettingId.AimSensitivity, value);
        }

        // The view the local player starts a match in: false = third person, true = first person.
        public static bool DefaultViewFirstPerson
        {
            get => GetToggle(SettingId.DefaultView);
            set => SetAndSave(SettingId.DefaultView, value ? 1f : 0f);
        }

        // 0..1 multipliers on top of the master volume (no AudioMixer): combat sounds, surroundings, UI and confirms.
        public static float EffectsVolume => Values[(int)SettingId.EffectsVolume];
        public static float AmbientVolume => Values[(int)SettingId.AmbientVolume];
        public static float UiVolume => Values[(int)SettingId.UiVolume];

        // Extra factor through a magnified scope at `zoom`, on top of ScopeView.SensitivityFactor: the setting of the
        // nearest magnifications (2/3/4/6/8/15x), blended in between for variable scopes.
        public static float ScopeFactorAt(float zoom)
        {
            var stops = SettingsCatalog.ScopeZoomStops;
            int last = stops.Length - 1;
            if (zoom <= stops[0]) return Values[(int)SettingId.ScopeSens2x];
            if (zoom >= stops[last]) return Values[(int)SettingId.ScopeSens2x + last];
            int i = 0;
            while (zoom > stops[i + 1]) i++;
            float t = (zoom - stops[i]) / (stops[i + 1] - stops[i]);
            return Mathf.Lerp(Values[(int)SettingId.ScopeSens2x + i], Values[(int)SettingId.ScopeSens2x + i + 1], t);
        }

        // 정조준 꾹 / 견착 탭 (the aim button's style, #139).
        public static bool SightsHold => GetIndex(SettingId.SightsStyle) == 1;
        public static bool ShoulderToggle => GetIndex(SettingId.ShoulderStyle) == 1;
        public static bool InvertMouseY => GetToggle(SettingId.InvertMouseY);

        // Vertical field of view of the first-person hip view, degrees.
        public static float FirstPersonFov => Values[(int)SettingId.FirstPersonFov];

        // 0 = 낮음, 1 = 높음. Effect spawners scale their particle counts with it.
        public static int EffectsQuality => GetIndex(SettingId.Effects);
        public static bool HighEffects => EffectsQuality > 0;

        // Camera far clip of the chosen 거리 보기 (the fog is matched by GraphicsOptions).
        public static float ViewDistanceMeters => GraphicsOptions.FarClip(GetIndex(SettingId.ViewDistance));

        public static Color CrosshairColor => CrosshairColors[Mathf.Clamp(GetIndex(SettingId.CrosshairColor), 0, CrosshairColors.Length - 1)];
        public static bool ShowDamageDirection => GetToggle(SettingId.DamageDirection);
        public static bool ShowKillFeed => GetToggle(SettingId.KillFeed);

        // 쉬움 / 보통 / 어려움 (ADR-117). The bots read Game.Core.BotLevels.Level when they start.
        public static Game.Core.BotDifficulty BotDifficulty => (Game.Core.BotDifficulty)Mathf.Clamp(GetIndex(SettingId.BotDifficulty), 0, 2);

        // The mouse look of one frame after the settings: the 1x aim factor while aiming without a magnified scope, the
        // setting of the scope's magnification (`zoom`) blended in as its own slow-down sets in (`scopeFactor` =
        // ScopeView.SensitivityFactor), and the vertical invert.
        public static Vector2 AdjustLook(Vector2 look, bool aiming, float scopeFactor, float zoom = 1f)
        {
            float factor = scopeFactor;
            // 1 = no scope; a 2x scope is about 0.57, so the scope factor is fully in from 0.75 down.
            float scoped = Mathf.Clamp01((1f - scopeFactor) * 4f);
            float aim = aiming ? AimSensitivityFactor : 1f;
            factor *= Mathf.Lerp(aim, ScopeFactorAt(zoom), scoped);
            look *= factor;
            if (InvertMouseY) look.y = -look.y;
            return look;
        }

        // ---- persistence ----

        // Reads the saved values (defaults when nothing is saved) and applies them. Does not write anything.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load()
        {
            GraphicsOptions.Initialize();
            var all = SettingsCatalog.All;
            bool displaySaved = false;
            for (int i = 0; i < all.Length; i++)
            {
                var def = all[i];
                bool has = PlayerPrefs.HasKey(def.Key);
                if (has && def.AppliesOnCommit) displaySaved = true;
                Values[i] = SettingsCatalog.Clamp(def, has ? Read(def) : FirstRunValue(def));
            }
            // A preset row always matches its advanced rows (older saves had only the preset).
            SettingsCatalog.ApplyPreset(Values, GetIndex(SettingId.QualityPreset));
            Array.Copy(Values, Committed, Values.Length);
            if (displaySaved) GraphicsOptions.ApplyDisplay();
            ApplyLive();
        }

        static float Read(SettingDef def)
        {
            if (def.Id == SettingId.Resolution) return GraphicsOptions.ResolutionIndex(PlayerPrefs.GetString(def.Key, ""));
            if (def.Kind == SettingKind.Slider) return PlayerPrefs.GetFloat(def.Key, def.Default);
            return PlayerPrefs.GetInt(def.Key, Mathf.RoundToInt(def.Default));
        }

        // Before anything is saved the display rows describe what the player already has, so a first launch changes nothing.
        static float FirstRunValue(SettingDef def)
        {
            if (def.Id == SettingId.Resolution) return GraphicsOptions.CurrentResolutionIndex();
            if (def.Id == SettingId.ScreenMode) return GraphicsOptions.CurrentScreenModeIndex();
            return def.Default;
        }

        static void Save(int index)
        {
            var def = SettingsCatalog.All[index];
            float value = Values[index];
            if (def.Id == SettingId.Resolution) PlayerPrefs.SetString(def.Key, GraphicsOptions.ResolutionText(Mathf.RoundToInt(value)));
            else if (def.Kind == SettingKind.Slider) PlayerPrefs.SetFloat(def.Key, value);
            else PlayerPrefs.SetInt(def.Key, Mathf.RoundToInt(value));
        }

        // Everything but the display mode and resolution, which wait for 적용.
        static void ApplyLive()
        {
            DeviceInput.MouseSensitivity = MouseSensitivity;
            AudioListener.volume = MasterVolume;
            Game.Core.BotLevels.Level = BotDifficulty;
            GraphicsOptions.Apply();
            Changed?.Invoke();
        }

        // ---- test support: the developer's own saved settings must survive test runs ----

        public sealed class PrefsSnapshot
        {
            internal readonly bool[] Had = new bool[SettingsCatalog.Count];
            internal readonly int[] Ints = new int[SettingsCatalog.Count];
            internal readonly float[] Floats = new float[SettingsCatalog.Count];
            internal readonly string[] Strings = new string[SettingsCatalog.Count];
        }

        public static PrefsSnapshot CapturePrefs()
        {
            var s = new PrefsSnapshot();
            for (int i = 0; i < SettingsCatalog.Count; i++)
            {
                var def = SettingsCatalog.All[i];
                s.Had[i] = PlayerPrefs.HasKey(def.Key);
                s.Ints[i] = PlayerPrefs.GetInt(def.Key, 0);
                s.Floats[i] = PlayerPrefs.GetFloat(def.Key, 0f);
                s.Strings[i] = PlayerPrefs.GetString(def.Key, "");
            }
            return s;
        }

        // Puts the captured keys back (deleting the ones that did not exist) and reloads.
        public static void RestorePrefs(PrefsSnapshot s)
        {
            for (int i = 0; i < SettingsCatalog.Count; i++)
            {
                var def = SettingsCatalog.All[i];
                if (!s.Had[i]) PlayerPrefs.DeleteKey(def.Key);
                else if (def.Id == SettingId.Resolution) PlayerPrefs.SetString(def.Key, s.Strings[i]);
                else if (def.Kind == SettingKind.Slider) PlayerPrefs.SetFloat(def.Key, s.Floats[i]);
                else PlayerPrefs.SetInt(def.Key, s.Ints[i]);
            }
            PlayerPrefs.Save();
            Load();
        }
    }
}
