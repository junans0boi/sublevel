using System;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Sounds of an elevator car (ADR-065 (1)): a low mechanical hum with a rattle while it rises (louder and higher
    // with speed), a clunk when it stops and a two-note chime when the doors open. All three are synthesised here
    // from sine tones and seeded noise when first needed (no sound asset fits; nothing is downloaded or recorded):
    // see SynthHum / SynthClunk / SynthChime. 3D sources on the car, so only riders and people near the station
    // hear it.
    [RequireComponent(typeof(ElevatorCar))]
    public class ElevatorAudio : MonoBehaviour
    {
        const int Rate = 22050;
        const float HumVolume = 0.55f;
        const float ClunkVolume = 0.9f;
        const float ChimeVolume = 0.6f;

        static AudioClip _hum, _clunk, _chime;

        ElevatorCar _car;
        AudioSource _loop, _shot;
        ElevatorPhase _last;
        bool _started;

        // Counters so tests can tell the sounds were triggered without listening.
        public int ClunksPlayed { get; private set; }
        public int ChimesPlayed { get; private set; }
        public int HumsStarted { get; private set; }
        public bool HumPlaying => _loop != null && _loop.isPlaying;

        void Awake()
        {
            _car = GetComponent<ElevatorCar>();
            _loop = Source("Hum");
            _loop.loop = true;
            _loop.clip = Hum;
            _shot = Source("Cues");
        }

        AudioSource Source(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            return SpatialAudio.Apply(source, SpatialAudio.Category.Elevator);
        }

        // After ElevatorCar.Update has set this frame's phase (order between components is not guaranteed, so a
        // phase change is at most a frame late).
        void LateUpdate()
        {
            var phase = _car.Phase;
            if (!_started)
            {
                _started = true;
                _last = phase;
            }

            if (phase == ElevatorPhase.Rising)
            {
                float speed = _car.Speed01;
                if (!_loop.isPlaying)
                {
                    _loop.Play();
                    HumsStarted++;
                }
                _loop.volume = HumVolume * (0.35f + 0.65f * speed) * GameSettings.EffectsVolume;
                _loop.pitch = 0.8f + 0.3f * speed;
            }
            else if (_loop.isPlaying) _loop.Stop();

            if (_last == ElevatorPhase.Rising && phase != ElevatorPhase.Rising && phase != ElevatorPhase.Waiting)
            {
                _shot.pitch = 1f;
                _shot.PlayOneShot(Clunk, ClunkVolume * GameSettings.EffectsVolume);
                ClunksPlayed++;
            }
            if ((_last == ElevatorPhase.Settling || _last == ElevatorPhase.Rising) && phase == ElevatorPhase.Open)
            {
                _shot.pitch = 1f;
                _shot.PlayOneShot(Chime, ChimeVolume * GameSettings.EffectsVolume);
                ChimesPlayed++;
            }
            _last = phase;
        }

        // ---- Synthesis (made once per run; a scene change keeps them through the static references) ----

        static AudioClip Hum => _hum != null ? _hum : (_hum = Make("ElevatorHum", SynthHum()));
        static AudioClip Clunk => _clunk != null ? _clunk : (_clunk = Make("ElevatorClunk", SynthClunk()));
        static AudioClip Chime => _chime != null ? _chime : (_chime = Make("ElevatorChime", SynthChime()));

        static AudioClip Make(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // 2 s loop: a 55 Hz motor tone with two harmonics, a slow swell, a 9 Hz rattle of filtered noise and a
        // little air. Every periodic part fits a whole number of cycles into the loop, so it repeats seamlessly.
        public static float[] SynthHum()
        {
            const float seconds = 2f;
            int n = (int)(Rate * seconds);
            int fade = Rate / 50;
            // Made 20 ms longer than the loop; that tail is blended into the head (same phase of every tone).
            var data = new float[n + fade];
            var rng = new System.Random(65001);
            float low = 0f, air = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                double t = (double)i / Rate;
                double tone = 0.45 * Math.Sin(2 * Math.PI * 55 * t) + 0.25 * Math.Sin(2 * Math.PI * 110 * t + 0.7)
                    + 0.12 * Math.Sin(2 * Math.PI * 165 * t + 1.9);
                tone *= 1.0 + 0.15 * Math.Sin(2 * Math.PI * 6 * t);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += 0.25f * (noise - low);
                air += 0.05f * (noise - air);
                double rattle = Math.Pow(Math.Max(0.0, Math.Sin(2 * Math.PI * 9 * t)), 8) * 1.6 * low;
                data[i] = (float)(tone + rattle + 0.25 * air);
            }
            // The filtered noise is not periodic: the head fades in from the tail so the seam does not click.
            for (int i = 0; i < fade; i++)
            {
                float w = (float)i / fade;
                data[i] = data[i] * w + data[n + i] * (1f - w);
            }
            Array.Resize(ref data, n);
            return Normalise(data, 0.8f);
        }

        // 0.6 s: a falling low thump, a short metal clank and a burst of noise.
        public static float[] SynthClunk()
        {
            int n = (int)(Rate * 0.6f);
            var data = new float[n];
            var rng = new System.Random(65002);
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                double freq = 45.0 + 45.0 * Math.Exp(-t * 12.0);
                phase += 2 * Math.PI * freq / Rate;
                double thump = Math.Sin(phase) * Math.Exp(-t * 9.0);
                double clank = (Math.Sin(2 * Math.PI * 420 * t) + 0.6 * Math.Sin(2 * Math.PI * 1130 * t)) * Math.Exp(-t * 25.0) * 0.3;
                double hit = (rng.NextDouble() * 2.0 - 1.0) * Math.Exp(-t * 40.0) * 0.4;
                double attack = Math.Min(1.0, t / 0.003);
                data[i] = (float)((thump * 0.9 + clank + hit) * attack);
            }
            return Normalise(data, 0.9f);
        }

        // 1.5 s: two bell-like notes, high then lower ("ding-dong").
        public static float[] SynthChime()
        {
            int n = (int)(Rate * 1.5f);
            var data = new float[n];
            AddBell(data, 0.0, 1318.5);
            AddBell(data, 0.45, 1046.5);
            return Normalise(data, 0.7f);
        }

        static void AddBell(float[] data, double start, double freq)
        {
            for (int i = (int)(start * Rate); i < data.Length; i++)
            {
                double t = (double)i / Rate - start;
                double envelope = Math.Min(1.0, t / 0.005) * Math.Exp(-t * 3.2);
                double partials = Math.Sin(2 * Math.PI * freq * t) + 0.35 * Math.Sin(2 * Math.PI * 2 * freq * t)
                    + 0.12 * Math.Sin(2 * Math.PI * 3.004 * freq * t);
                data[i] += (float)(partials * envelope);
            }
        }

        static float[] Normalise(float[] data, float peak)
        {
            float max = 0f;
            foreach (float v in data) max = Mathf.Max(max, Mathf.Abs(v));
            if (max > 1e-6f)
                for (int i = 0; i < data.Length; i++) data[i] *= peak / max;
            return data;
        }
    }
}
