using System;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Engine and horn of a vehicle (ADR-075). Both are synthesised here once per run like ElevatorAudio (no sound
    // asset, nothing downloaded or AI-generated): a looping combustion tone whose pitch and loudness follow
    // EngineNote (a made-up gearbox, so the note climbs and drops at each shift), and a two-tone horn loop played
    // while VehicleHorn.Sounding. 3D sources on the vehicle; every side plays them from replicated state.
    //
    // Sits on the vehicle prefab next to Vehicle (VehicleBuilder).
    [RequireComponent(typeof(Vehicle))]
    public class VehicleAudio : MonoBehaviour
    {
        const int Rate = 22050;
        const float EngineVolume = 0.5f, HornVolume = 0.8f;
        const int Gears = 5;
        const float FadeSeconds = 0.25f;

        static AudioClip _engine, _horn;

        Vehicle _vehicle;
        VehicleHorn _hornState;
        AudioSource _engineSource, _hornSource;
        float _rpm = EngineNote.IdleRpm, _level, _lastSpeed;

        // For tests: what is playing and at what pitch.
        public bool EnginePlaying => _engineSource != null && _engineSource.isPlaying;
        public bool HornPlaying => _hornSource != null && _hornSource.isPlaying;
        public float EnginePitch => _engineSource != null ? _engineSource.pitch : 0f;

        void Awake()
        {
            _vehicle = GetComponent<Vehicle>();
            _hornState = GetComponent<VehicleHorn>();
            _engineSource = Source("Engine", Engine);
            _hornSource = Source("Horn", Horn);
        }

        AudioSource Source(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.7f, 1.2f);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.clip = clip;
            return SpatialAudio.Apply(source, SpatialAudio.Category.Vehicle);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool running = _vehicle.EngineOn.Value && (_vehicle.State == null || Fuel.EngineCanRun(_vehicle.State.Fuel01, _vehicle.State.Disabled));
            // Fade in and out so starting and stalling do not click.
            _level = Mathf.MoveTowards(_level, running ? 1f : 0f, dt / FadeSeconds);
            if (_level > 0f)
            {
                var r = _vehicle.Rules;
                float top = Vehicles.TopSpeed(false, true, r);
                // The throttle is not replicated to every side: gaining speed reads as load.
                float speed = Mathf.Abs(_vehicle.Speed);
                float throttle = Mathf.Clamp01((speed - _lastSpeed) / Mathf.Max(dt, 1e-4f) / 4f);
                _lastSpeed = speed;
                float target = EngineNote.Rpm01(_vehicle.Speed, top, Gears, throttle);
                _rpm = Mathf.Lerp(_rpm, target, 1f - Mathf.Exp(-10f * dt));
                _engineSource.pitch = EngineNote.Pitch(_rpm, _vehicle.IsMotorcycle);
                _engineSource.volume = EngineVolume * _level * EngineNote.Volume(_rpm, throttle) * GameSettings.EffectsVolume;
                if (!_engineSource.isPlaying) _engineSource.Play();
            }
            else if (_engineSource.isPlaying) _engineSource.Stop();

            bool horn = _hornState != null && _hornState.Sounding;
            if (horn)
            {
                _hornSource.volume = HornVolume * GameSettings.EffectsVolume;
                _hornSource.pitch = _vehicle.IsMotorcycle ? 1.35f : 1f;
                if (!_hornSource.isPlaying) _hornSource.Play();
            }
            else if (_hornSource.isPlaying) _hornSource.Stop();
        }

        // ---- Synthesis ----

        static AudioClip Engine => _engine != null ? _engine : (_engine = Make("VehicleEngine", SynthEngine()));
        static AudioClip Horn => _horn != null ? _horn : (_horn = Make("VehicleHorn", SynthHorn()));

        static AudioClip Make(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // 1 s loop at idle: firing pulses at 30 Hz (a four-cylinder at ~900 rpm) shaped into a low growl with
        // harmonics and a little rough noise. Every tone fits whole cycles into the loop; the noise is cross-faded.
        public static float[] SynthEngine()
        {
            int n = Rate;
            int fade = Rate / 50;
            var data = new float[n + fade];
            var rng = new System.Random(75001);
            float rough = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                double t = (double)i / Rate;
                // Sharp pulse per firing, with a slight uneven beat between cylinders (15 Hz).
                double pulse = Math.Pow(0.5 + 0.5 * Math.Sin(2 * Math.PI * 30 * t), 6) * (1.0 + 0.2 * Math.Sin(2 * Math.PI * 15 * t));
                double body = 0.5 * Math.Sin(2 * Math.PI * 60 * t) + 0.3 * Math.Sin(2 * Math.PI * 90 * t + 0.4)
                    + 0.15 * Math.Sin(2 * Math.PI * 120 * t + 1.1);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                rough += 0.3f * (noise - rough);
                data[i] = (float)(pulse * (0.8 + 0.6 * rough) + 0.5 * body * (0.6 + 0.4 * pulse));
            }
            for (int i = 0; i < fade; i++)
            {
                float w = (float)i / fade;
                data[i] = data[i] * w + data[n + i] * (1f - w);
            }
            Array.Resize(ref data, n);
            return Normalise(data, 0.8f);
        }

        // 0.5 s loop: two slightly detuned square-ish tones a minor third apart (a classic car horn pair).
        public static float[] SynthHorn()
        {
            int n = Rate / 2;
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                data[i] = (float)(Square(t, 420) + Square(t, 500));
            }
            return Normalise(data, 0.7f);
        }

        // A soft square wave (first three odd harmonics) so it buzzes like a horn without aliasing.
        static double Square(double t, double f) =>
            Math.Sin(2 * Math.PI * f * t) + Math.Sin(2 * Math.PI * 3 * f * t) / 3.0 + Math.Sin(2 * Math.PI * 5 * f * t) / 5.0;

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
