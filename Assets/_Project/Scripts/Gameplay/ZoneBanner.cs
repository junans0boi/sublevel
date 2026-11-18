using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The lockdown as the player is told about it: a banner at the top centre ("○○ 지역 봉쇄 경고 — N초", "봉쇄",
    // "폭격 시작", "폐쇄", the final region once named), a prompt for using a key at a barrier, and the sound hooks.
    // It also keeps ZoneStatus.Current (for the HUD) and ZoneFog.Current (for the camera) up to date every frame.
    // Texts are rebuilt only when what they say changes (about once a second), never per frame.
    public class ZoneBanner : MonoBehaviour
    {
        const int SortOrder = 2;                    // above the HUD and the kill feed
        const float Top = 116f; // below the compass and its heading readout
        const float BannerWidth = 780f, BannerHeight = 58f;
        const float PromptWidth = 560f, PromptHeight = 42f, PromptGap = 8f;
        const float MessageSeconds = 5f;
        const float RevealSeconds = 7f;
        const float IncomingNear = 6f;              // a missile this much beyond its blast radius counts as near

        [SerializeField] AudioClip _warningClip;
        [SerializeField] AudioClip _lockdownClip;
        [SerializeField] AudioClip _bombardmentClip;
        [SerializeField] AudioClip _incomingClip;

        public static ZoneBanner Instance { get; private set; }
        // A spectator camera (-autoplay, #77): with the local player out, the status card and the radiation fog
        // follow this point instead. Null = nothing while the player is out.
        public static Vector3? SpectatorFeet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSpectator() => SpectatorFeet = null;

        PollutionDirector _director;
        AudioSource _audio;
        GameObject _bannerBox, _promptBox;
        Text _bannerText, _promptText;
        Image _bannerFill;

        string _message = "";
        Color _messageColour;
        float _messageUntil;

        int _shownKey = int.MinValue;
        int _promptKey = int.MinValue;
        string _text = "";
        string _prompt = "";

        // ---- Read-only state for tests and the HUD ----

        // What the banner says right now; "" when it is hidden.
        public string Shown => _text;
        public bool Visible => _text.Length > 0;
        // What the key prompt says right now; "" when hidden.
        public string Prompt => _prompt;

        // Installs itself next to every lockdown; nothing in the scene refers to it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            PollutionDirector.Started -= Install;
            PollutionDirector.Started += Install;
        }

        static void Install(PollutionDirector director)
        {
            if (director == null || director.GetComponentInChildren<ZoneBanner>() != null) return;
            var go = new GameObject("ZoneBanner");
            // Under the director so it goes away with the scene.
            go.transform.SetParent(director.transform, false);
            go.AddComponent<ZoneBanner>();
        }

        // Sounds are optional: whoever has the clips hands them over (the master's wiring), else the banner is silent.
        public void SetClips(AudioClip warning, AudioClip lockdown, AudioClip bombardment, AudioClip incoming)
        {
            _warningClip = warning;
            _lockdownClip = lockdown;
            _bombardmentClip = bombardment;
            _incomingClip = incoming;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _director = GetComponentInParent<PollutionDirector>();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            Build();
            PollutionDirector.ClientRegionStateChanged += OnRegionState;
            PollutionDirector.ClientFinalRevealed += OnFinalRevealed;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            PollutionDirector.ClientRegionStateChanged -= OnRegionState;
            PollutionDirector.ClientFinalRevealed -= OnFinalRevealed;
            Instance = null;
            ZoneStatus.Current = default;
            ZoneFog.Current = default;
        }

        // ---- Events ----

        void OnRegionState(int region, ZoneState state)
        {
            if (_director == null) return;
            string name = _director.RegionNameOf(region);
            switch (state)
            {
                case ZoneState.Warning:
                    Play(_warningClip);
                    break;
                case ZoneState.Closed:
                    Say(name + " 지역 오염 확정", UiKit.Danger, MessageSeconds);
                    break;
            }
        }

        void OnFinalRevealed(int region)
        {
            if (_director == null) return;
            Say("최종 구역 — " + _director.RegionNameOf(region), UiKit.Accent, RevealSeconds);
        }

        void Say(string text, Color colour, float seconds)
        {
            _message = text;
            _messageColour = colour;
            _messageUntil = Time.time + seconds;
            _shownKey = int.MinValue;
        }

        void Play(AudioClip clip)
        {
            if (clip != null && _audio != null) _audio.PlayOneShot(clip, GameSettings.UiVolume);
        }

        // ---- Per frame ----

        void Update()
        {
            if (_director == null)
            {
                _director = PollutionDirector.Instance;
                if (_director == null) return;
            }
            UpdateStatus();
            UpdateBanner();
            UpdatePrompt();
        }

        // ZoneStatus.Current and the fog, for the region the local player is in.
        void UpdateStatus()
        {
            var me = Combatant.LocalPlayer;
            var state = ZoneState.Safe;
            bool alive = me != null && !me.Dead.Value;
            if (!alive && SpectatorFeet == null)
            {
                ZoneStatus.Current = default;
            }
            else
            {
                var feet = alive ? me.transform.position : SpectatorFeet.Value;
                int region = _director.RegionIndexAt(feet);
                if (region < 0)
                {
                    ZoneStatus.Current = default;
                }
                else
                {
                    state = _director.StateOf(region);
                    var s = default(ZoneStatus);
                    s.Valid = true;
                    s.RegionIndex = region;
                    s.RegionId = _director.RegionIdOf(region);
                    s.RegionName = _director.RegionNameOf(region);
                    s.State = state;
                    s.SecondsLeft = _director.SecondsLeft(region);
                    s.FinalRevealed = _director.FinalRegionRevealed;
                    s.FinalRegionName = s.FinalRevealed ? _director.RegionNameOf(_director.FinalRegion) : "";
                    ZoneStatus.Current = s;
                }
            }
            ZoneFog.Step(state, Time.deltaTime);
        }

        // One line: a message, a missile close by, a region counting down to its lockdown, or what is happening in the
        // region the player stands in.
        void UpdateBanner()
        {
            string text = "";
            Color colour = UiKit.Accent;
            int key = 0;
            float now = Time.time;
            var status = ZoneStatus.Current;

            if (_messageUntil > now)
            {
                text = _message;
                colour = _messageColour;
                key = _message.GetHashCode();
            }
            else
            {
                // The region closest to its lockdown among those still counting down.
                int warned = -1;
                float least = float.MaxValue;
                for (int r = 0; r < _director.RegionCount; r++)
                {
                    if (_director.StateOf(r) != ZoneState.Warning) continue;
                    float left = _director.SecondsLeft(r);
                    if (left >= 0f && left < least) { least = left; warned = r; }
                }
                if (warned >= 0)
                {
                    int seconds = Mathf.CeilToInt(least);
                    key = 100000 + warned * 1000 + Mathf.Min(seconds, 999);
                    if (key != _shownKey) text = _director.RegionNameOf(warned) + " 지역 오염 경보 — " + seconds + "초 (확정 후 초당 " + DpsText(PollutionRules.Current.DamagePerSecond(_director.Match.Timeline.Confirmed(_director.Now) + 1)) + ")";
                    else text = _text;
                    colour = UiKit.Accent;
                }
            }

            if (key == _shownKey && key != 0 && text == _text) return;
            if (key == 0 && _text.Length == 0) return;
            _shownKey = key;
            _text = text;
            bool visible = text.Length > 0;
            if (_bannerBox.activeSelf != visible) _bannerBox.SetActive(visible);
            if (!visible) return;
            _bannerText.text = text;
            _bannerText.color = UiKit.Text;
            _bannerFill.color = new Color(colour.r * 0.35f, colour.g * 0.35f, colour.b * 0.35f, 0.86f);
        }

        // "4.5" / "11": health per second, one decimal at most.
        public static string DpsText(float dps) => dps.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        // The key elevator prompt under the banner while standing in a station: insert, close, choose.
        void UpdatePrompt()
        {
            var me = Combatant.LocalPlayer;
            var keys = KeyHolder.Local;
            int station = ElevatorPanel.LocalStation(me);
            string text = "";
            if (station >= 0 && _director.Ready)
            {
                KeyElevatorNet.LastStatus.TryGetValue(station, out var st);
                text = PollutionHud.KeyPrompt((KeyPhase)st.Phase, st.Holder == me.ObjectId, keys != null ? keys.Keys.Value : 0, st.Left);
            }
            if (text == _prompt) return;
            _prompt = text;
            bool visible = text.Length > 0;
            if (_promptBox.activeSelf != visible) _promptBox.SetActive(visible);
            if (visible) _promptText.text = text;
        }

        void Build()
        {
            var canvas = UiKit.Canvas("ZoneBannerCanvas", SortOrder, false);
            canvas.transform.SetParent(transform, false);
            var top = new Vector2(0.5f, 1f);

            var banner = UiKit.Rect("Banner", canvas.transform, top, top, new Vector2(0f, -Top), new Vector2(BannerWidth, BannerHeight));
            _bannerFill = UiKit.Box(banner, UiKit.Panel);
            var bannerLabel = UiKit.Fill("Text", banner);
            bannerLabel.offsetMin = new Vector2(16f, 0f);
            bannerLabel.offsetMax = new Vector2(-16f, 0f);
            _bannerText = UiKit.Label(bannerLabel, "", 28, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            _bannerBox = banner.gameObject;
            _bannerBox.SetActive(false);

            var prompt = UiKit.Rect("KeyPrompt", canvas.transform, top, top, new Vector2(0f, -Top - BannerHeight - PromptGap), new Vector2(PromptWidth, PromptHeight));
            UiKit.Box(prompt, UiKit.Panel);
            var promptLabel = UiKit.Fill("Text", prompt);
            promptLabel.offsetMin = new Vector2(12f, 0f);
            promptLabel.offsetMax = new Vector2(-12f, 0f);
            _promptText = UiKit.Label(promptLabel, "", 22, UiKit.Accent, TextAnchor.MiddleCenter);
            _promptBox = prompt.gameObject;
            _promptBox.SetActive(false);
        }
    }
}
