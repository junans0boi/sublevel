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

        static AudioClip _engine, _horn, _whine, _screech, _wind, _crunch, _clunk;

        Vehicle _vehicle;
        VehicleHorn _hornState;
        AudioSource _engineSource, _hornSource, _whineSource, _screechSource, _windSource, _oneShot;
        VehicleDamage _damage;
        VehicleTyreFx _tyres;
        float _rpm = EngineNote.IdleRpm, _level, _lastSpeed, _shiftUntil, _smoothThrottle;
        int _gear;

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
            // Layers (#203): a high whine that builds with load and revs, tyre screech, wind at speed, one-shots (shift, impacts).
            _whineSource = Source("EngineWhine", Whine);
            _screechSource = Source("TyreScreech", Screech);
            _windSource = Source("Wind", Wind);
            _oneShot = Source("OneShot", null);
            _oneShot.loop = false;
            // The lamps and the tyre effects are views of this vehicle added here, so the prefabs need no change.
            _damage = GetComponent<VehicleDamage>();
            if (_damage != null)
            {
                if (GetComponent<VehicleLights>() == null) gameObject.AddComponent<VehicleLights>();
                _tyres = GetComponent<VehicleTyreFx>() ?? gameObject.AddComponent<VehicleTyreFx>();
            }
        }

        void OnEnable()
        {
            if (_damage != null) _damage.Dented += OnDented;
        }

        void OnDisable()
        {
            if (_damage != null) _damage.Dented -= OnDented;
        }

        // A collision (a deep dent; bullets are shallow) crunches by how hard it was.
        void OnDented(Vector3 local, Vector3 dir, float radius, float depth)
        {
            float v = VehicleFxRules.ImpactVolume(depth);
            if (v <= 0f || _oneShot == null) return;
            _oneShot.pitch = Mathf.Lerp(1.15f, 0.8f, v) * (0.95f + 0.1f * UnityEngine.Random.value);
            _oneShot.PlayOneShot(Crunch, v * GameSettings.EffectsVolume);
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
                // The vehicle's own gearbox (ADR-558): the note follows its needle, so the HUD and the ear agree.
                float target = r.Gearbox != null ? Mathf.Clamp01(_vehicle.EngineRpm / Mathf.Max(1f, _vehicle.RedlineRpm)) : EngineNote.Rpm01(_vehicle.Speed, top, Gears, throttle);
                target = Mathf.Max(EngineNote.IdleRpm, target);
                _rpm = Mathf.Lerp(_rpm, target, 1f - Mathf.Exp(-14f * dt));
                _smoothThrottle = Mathf.Lerp(_smoothThrottle, throttle, 1f - Mathf.Exp(-6f * dt));
                // A gear change: the note falls, the engine lets go for a moment and a clunk comes through the floor.
                int gear = r.Gearbox != null ? _vehicle.ShiftCount : EngineNote.Gear(_vehicle.Speed, top, Gears);
                if (gear != _gear)
                {
                    bool up = r.Gearbox != null ? _vehicle.LastShift > 0 : gear > _gear;
                    if (up && speed > 4f) { _shiftUntil = Time.time + 0.16f; _oneShot.pitch = 1f; _oneShot.PlayOneShot(Clunk, 0.35f * GameSettings.EffectsVolume); }
                    _gear = gear;
                }
                float lift = Time.time < _shiftUntil || _vehicle.Shifting ? 0.55f : 1f;
                float fx = GameSettings.EffectsVolume;
                _engineSource.pitch = EngineNote.Pitch(_rpm, _vehicle.IsMotorcycle);
                _engineSource.volume = EngineVolume * _level * EngineNote.Volume(_rpm, _smoothThrottle) * lift * fx;
                _whineSource.pitch = 0.7f + _rpm * 1.9f;
                _whineSource.volume = 0.3f * _level * Mathf.Clamp01(_smoothThrottle * 1.3f) * Mathf.Clamp01((_rpm - 0.2f) / 0.5f) * lift * fx;
                if (!_engineSource.isPlaying) _engineSource.Play();
                if (!_whineSource.isPlaying) _whineSource.Play();
            }
            else
            {
                if (_engineSource.isPlaying) _engineSource.Stop();
                if (_whineSource.isPlaying) _whineSource.Stop();
            }
            Loop(_screechSource, _tyres != null ? VehicleFxRules.ScreechVolume(_tyres.Ground, _tyres.Slip01) * 0.7f : 0f,
                0.85f + Mathf.Abs(_vehicle.Speed) / 45f);
            Loop(_windSource, VehicleFxRules.WindVolume(_vehicle.Speed) * 0.7f, 0.8f + Mathf.Abs(_vehicle.Speed) / 80f);

            bool horn = _hornState != null && _hornState.Sounding;
            if (horn)
            {
                _hornSource.volume = HornVolume * GameSettings.EffectsVolume;
                _hornSource.pitch = _vehicle.IsMotorcycle ? 1.35f : 1f;
                if (!_hornSource.isPlaying) _hornSource.Play();
            }
            else if (_hornSource.isPlaying) _hornSource.Stop();
        }

        // A loop that fades with `volume` and is stopped when it is 0.
        void Loop(AudioSource source, float volume, float pitch)
        {
            volume *= GameSettings.EffectsVolume;
            source.volume = Mathf.MoveTowards(source.volume, volume, Time.deltaTime * 4f);
            source.pitch = pitch;
            if (source.volume > 0.005f) { if (!source.isPlaying) source.Play(); }
            else if (source.isPlaying) source.Stop();
        }

        // ---- Synthesis ----

        static AudioClip Engine => _engine != null ? _engine : (_engine = Resources.Load<AudioClip>("Sfx/veh_engine_idle") ?? Make("VehicleEngine", SynthEngine()));
        static AudioClip Whine => _whine != null ? _whine : (_whine = Make("VehicleWhine", SynthWhine()));
        static AudioClip Screech => _screech != null ? _screech : (_screech = Resources.Load<AudioClip>("Sfx/veh_tyre_squeal") ?? Make("TyreScreech", SynthScreech()));
        static AudioClip Wind => _wind != null ? _wind : (_wind = Make("VehicleWind", SynthWind()));
        static AudioClip Crunch => _crunch != null ? _crunch : (_crunch = Resources.Load<AudioClip>("Sfx/veh_crash_01") ?? Make("VehicleCrunch", SynthCrunch()));
        static AudioClip Clunk => _clunk != null ? _clunk : (_clunk = Make("GearClunk", SynthClunk()));
        static AudioClip Horn => _horn != null ? _horn : (_horn = Resources.Load<AudioClip>("Sfx/veh_horn") ?? Make("VehicleHorn", SynthHorn()));

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

        // 1 s loop of the engine's top end: a buzzing saw at 240 Hz with its harmonics and a little noise. Whole cycles, so it loops.
        public static float[] SynthWhine()
        {
            var data = new float[Rate];
            var rng = new System.Random(75002);
            float rough = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                double t = (double)i / Rate;
                double saw = 0;
                for (int h = 1; h <= 6; h++) saw += Math.Sin(2 * Math.PI * 240 * h * t) / h;
                rough += 0.35f * ((float)(rng.NextDouble() * 2.0 - 1.0) - rough);
                data[i] = (float)(saw * (0.8 + 0.2 * Math.Sin(2 * Math.PI * 12 * t)) + 0.25 * rough);
            }
            return Normalise(data, 0.7f);
        }

        // 1 s loop of tyre squeal: a wavering 1.1 kHz tone (whole cycles) over band-passed noise.
        public static float[] SynthScreech()
        {
            var data = new float[Rate];
            var rng = new System.Random(75003);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                double t = (double)i / Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.5f * (noise - lp);
                lp2 += 0.04f * (lp - lp2);
                float band = lp - lp2; // highs without the lows
                double tone = Math.Sin(2 * Math.PI * (1100 + 40 * Math.Sin(2 * Math.PI * 3 * t)) * t) * 0.5 + Math.Sin(2 * Math.PI * 1650 * t) * 0.25;
                data[i] = (float)(tone * (0.7 + 0.3 * Math.Sin(2 * Math.PI * 7 * t)) + band * 1.1);
            }
            return Normalise(data, 0.7f);
        }

        // 2 s loop of wind: low-passed noise with a slow swell, cross-faded at the seam.
        public static float[] SynthWind()
        {
            int n = Rate * 2, fade = Rate / 10;
            var data = new float[n + fade];
            var rng = new System.Random(75004);
            float a = 0f, b = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                a += 0.12f * (noise - a);
                b += 0.5f * (a - b);
                data[i] = b * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.5f * i / Rate));
            }
            for (int i = 0; i < fade; i++)
            {
                float w = (float)i / fade;
                data[i] = data[i] * w + data[n + i] * (1f - w);
            }
            Array.Resize(ref data, n);
            return Normalise(data, 0.8f);
        }

        // 0.7 s metal crunch: a low thump, a burst of noise and a ringing sheet-metal clang, decaying.
        public static float[] SynthCrunch()
        {
            int n = (int)(Rate * 0.7f);
            var data = new float[n];
            var rng = new System.Random(75005);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.35f * (noise - lp);
                float thump = Mathf.Sin(2f * Mathf.PI * (70f + 60f * Mathf.Exp(-t * 14f)) * t) * Mathf.Exp(-t * 9f);
                float crash = (noise * 0.6f + lp) * Mathf.Exp(-t * 11f);
                float ring = (Mathf.Sin(2f * Mathf.PI * 410f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 733f * t)) * 0.25f * Mathf.Exp(-t * 7f);
                data[i] = Mathf.Clamp((thump * 0.9f + crash + ring) * Mathf.Clamp01(t / 0.002f), -1f, 1f);
            }
            return Normalise(data, 0.9f);
        }

        // 0.15 s gearbox clunk: a short low knock and a click.
        public static float[] SynthClunk()
        {
            int n = (int)(Rate * 0.15f);
            var data = new float[n];
            var rng = new System.Random(75006);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float knock = Mathf.Sin(2f * Mathf.PI * 95f * t) * Mathf.Exp(-t * 30f);
                float click = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 120f) * 0.5f;
                data[i] = (knock + click) * Mathf.Clamp01(t / 0.001f);
            }
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
