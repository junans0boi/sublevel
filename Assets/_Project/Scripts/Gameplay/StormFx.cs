using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // Lightning and thunder in rain (#207, ADR-163): a strike lights the whole scene for half a second (a spare
    // directional light, no shadows) and the thunder follows after distance / speed of sound, louder and sharper the
    // nearer it is, muffled under a roof. The thunder is made in code (filtered noise, like the rain). Added by SkyAmbient.
    public class StormFx : MonoBehaviour
    {
        public static StormFx Current { get; private set; }

        public int Strikes { get; private set; }
        public int Thunders { get; private set; }
        public float FlashNow { get; private set; }
        public float NextStrikeIn => _nextIn;

        public Transform Root { get; private set; }
        Light _flash;
        AudioSource _thunder;
        float _nextIn, _strikeAt = -100f, _thunderAt = -1f, _distance;
        static AudioClip _crack, _rumble;

        void Awake()
        {
            Current = this;
            Root = FxKit.Root(this, "StormParts");
            var go = new GameObject("LightningLight");
            go.transform.SetParent(Root, false);
            _flash = go.AddComponent<Light>();
            _flash.type = LightType.Directional;
            _flash.color = new Color(0.8f, 0.86f, 1f);
            _flash.shadows = LightShadows.None;
            _flash.intensity = 0f;
            _flash.enabled = false;
            var t = new GameObject("Thunder");
            t.transform.SetParent(Root, false);
            _thunder = t.AddComponent<AudioSource>();
            _thunder.spatialBlend = 0f;
            _thunder.playOnAwake = false;
            _nextIn = LightningRules.NextGap(Random.value);
        }

        void OnDestroy() { if (Current == this) Current = null; }

        void Update()
        {
            var view = EnvironmentView.Current;
            bool storm = view != null && view.Raining;
            if (storm)
            {
                _nextIn -= Time.deltaTime;
                if (_nextIn <= 0f) { _nextIn = LightningRules.NextGap(Random.value); Strike(LightningRules.Distance(Random.value)); }
            }
            float f = LightningRules.Flash(Time.time - _strikeAt) * LightningRules.ThunderVolume(_distance);
            FlashNow = f;
            _flash.enabled = f > 0.001f;
            _flash.intensity = f * 2.2f;
            if (_thunderAt > 0f && Time.time >= _thunderAt)
            {
                _thunderAt = -1f;
                float v = LightningRules.ThunderVolume(_distance) * GameSettings.AmbientVolume * (view != null && view.Indoors ? 0.55f : 1f);
                if (v > 0.01f)
                {
                    _thunder.pitch = Random.Range(0.9f, 1.05f);
                    _thunder.PlayOneShot(_distance < 700f ? Crack : Rumble, Mathf.Clamp01(v));
                    Thunders++;
                }
            }
        }

        // Test and director hook: a strike `distance` metres away, from a random side.
        public void Strike(float distance)
        {
            _distance = distance;
            _strikeAt = Time.time;
            _thunderAt = Time.time + LightningRules.ThunderDelay(distance);
            Strikes++;
            // From above and a random side, so shadows-free light still leans.
            _flash.transform.rotation = Quaternion.Euler(Random.Range(35f, 70f), Random.value * 360f, 0f);
        }

        static AudioClip Crack => _crack != null ? _crack : (_crack = Make("ThunderCrack", Synth(2.8f, 0.04f, 1f)));
        static AudioClip Rumble => _rumble != null ? _rumble : (_rumble = Make("ThunderRumble", Synth(4.5f, 0.35f, 0.7f)));

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, 22050, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Low filtered noise with an attack of `attack` seconds and a long uneven decay (rolling echoes); `crack` adds the sharp front.
        static float[] Synth(float seconds, float attack, float crack)
        {
            const int rate = 22050;
            var data = new float[(int)(rate * seconds)];
            var rng = new System.Random(990 + (int)(attack * 100));
            float a = 0f, b = 0f, peak = 1e-3f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                a += (n - a) * 0.05f;      // lows
                b += (a - b) * 0.2f;       // lower still
                float env = t < attack ? t / attack : Mathf.Exp(-(t - attack) * 1.1f);
                float roll = 0.65f + 0.35f * Mathf.Sin(t * 5.3f) * Mathf.Sin(t * 2.1f + 1f); // echoes rolling
                float hiss = (n - a) * Mathf.Exp(-t * 14f) * crack * 0.9f;
                data[i] = (b * 5f + a * 1.5f) * env * roll + hiss;
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            for (int i = 0; i < data.Length; i++) data[i] *= 0.9f / peak;
            return data;
        }
    }
}
