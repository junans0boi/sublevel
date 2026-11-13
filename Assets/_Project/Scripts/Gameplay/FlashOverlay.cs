using FishNet.Object;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // What a flash grenade does to the local player (ADR-105): a white-out over the 3D view (the HUD stays on top
    // of it) and the master volume ducked while the ears ring. Each client works it out for its own player from the
    // flash's position (distance, view angle, clear line of sight: FlashRules), so nothing about it is replicated.
    // Installs itself once and survives scene loads.
    public sealed class FlashOverlay : MonoBehaviour
    {
        const int SightHits = 16;
        static readonly RaycastHit[] Hits = new RaycastHit[SightHits];

        public static FlashOverlay Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Instance != null) return;
            var go = new GameObject("FlashOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<FlashOverlay>();
        }

        Image _white;
        float _blindStart, _blindSeconds, _ringStart, _ringSeconds;
        bool _ducking;

        // Now: 0..1 opacity of the white-out and the volume factor, for tests.
        public float Whiteness { get; private set; }
        public float Duck { get; private set; } = 1f;
        public int FlashesFelt { get; private set; }
        public float LastBlindSeconds { get; private set; }
        public float LastRingSeconds { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // Beneath the HUD (order 0): the white covers the world, not the numbers.
            var canvas = UiKit.Canvas("FlashCanvas", -1, false);
            canvas.transform.SetParent(transform, false);
            var rect = UiKit.Rect("White", canvas.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _white = UiKit.Box(rect, new Color(1f, 1f, 0.97f, 0f));
            _white.raycastTarget = false;
            ThrownItem.ClientFlashed += OnFlashed;
        }

        void OnDestroy()
        {
            ThrownItem.ClientFlashed -= OnFlashed;
            if (Instance == this) Instance = null;
            Restore();
        }

        void OnFlashed(Vector3 centre, NetworkObject thrower)
        {
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            if (me == null || pc == null || me.Dead.Value || pc.Camera == null) return;
            var eye = me.EyePosition;
            var to = centre - eye;
            float distance = to.magnitude;
            float angle = distance < 0.05f ? 0f : Vector3.Angle(pc.Camera.transform.forward, to);
            var rules = FlashRules.Current;
            bool sight = Clear(centre + Vector3.up * 0.15f, eye);
            float blind = Flash.Blind(distance, angle, sight, rules);
            float ring = Flash.Tinnitus(distance, blind, rules);
            Apply(blind, ring);
        }

        // The stronger of what is running and what is new wins.
        public void Apply(float blindSeconds, float ringSeconds)
        {
            if (blindSeconds <= 0f && ringSeconds <= 0f) return;
            FlashesFelt++;
            LastBlindSeconds = blindSeconds;
            LastRingSeconds = ringSeconds;
            float now = Time.unscaledTime;
            if (blindSeconds > _blindSeconds - (now - _blindStart)) { _blindStart = now; _blindSeconds = blindSeconds; }
            if (ringSeconds > _ringSeconds - (now - _ringStart)) { _ringStart = now; _ringSeconds = ringSeconds; }
        }

        static bool Clear(Vector3 from, Vector3 to)
        {
            var path = to - from;
            float length = path.magnitude;
            if (length < 0.01f) return true;
            int count = Physics.RaycastNonAlloc(from, path / length, Hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count >= SightHits) return false; // err on the side of cover
            for (int i = 0; i < count; i++)
            {
                var c = Hits[i].collider;
                if (c.GetComponentInParent<Combatant>() != null || c.GetComponentInParent<ThrownItem>() != null) continue;
                return false;
            }
            return true;
        }

        void Update()
        {
            var me = Combatant.LocalPlayer;
            if (me == null || me.Dead.Value) { _blindSeconds = _ringSeconds = 0f; }
            float now = Time.unscaledTime;
            var rules = FlashRules.Current;
            Whiteness = Flash.Whiteness(now - _blindStart, _blindSeconds, rules);
            var color = _white.color;
            if (!Mathf.Approximately(color.a, Whiteness)) { color.a = Whiteness; _white.color = color; }
            Duck = Flash.Volume(now - _ringStart, _ringSeconds, rules);
            if (Duck < 1f) { _ducking = true; AudioListener.volume = GameSettings.MasterVolume * Duck; }
            else if (_ducking) Restore();
        }

        void Restore()
        {
            if (!_ducking) return;
            _ducking = false;
            AudioListener.volume = GameSettings.MasterVolume;
        }
    }
}
