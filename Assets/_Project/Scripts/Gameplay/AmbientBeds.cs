using System;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // Environment sound of the outdoor map (#194, ADR-151): wind, birds by day, crickets by night, water near the
    // shore, room tone under a roof, mixed from where the listener stands (AmbientRules) and ducked under gunfire
    // (SpatialAudio.NoteLoud). Wind, crickets and birds are CC0 recordings in Resources/Sfx (#290, ADR-540, cut by
    // tools/sound_cut.py with tools/sound_ambient.json, loops crossfaded); water and room tone are still synthesised here,
    // and the synthesis is the fallback when a recording is missing. Birds are 3D one-shots in the trees around the
    // listener; the beds are 2D (a wind has no direction worth panning).
    public class AmbientBeds : MonoBehaviour
    {
        const int Rate = 22050;
        const float TickSeconds = 0.25f, IndoorProbe = 14f, ShoreMetres = 8f;
        const int BirdVoices = 4;

        sealed class Bed
        {
            public AudioSource Source;
            public float Gain;
            public float Level;
        }

        static AudioClip _wind, _crickets, _water, _room;
        static AudioClip[] _birds;

        Bed _windBed, _cricketBed, _waterBed, _roomBed;
        AudioSource[] _voices;
        float _nextBird, _tick, _roofTick;
        bool _indoors;
        Terrain _terrain;
        WorldInfo _info;

        // What the mix is now, for tests and captures.
        public AmbientLevels Levels { get; private set; }
        public AmbientInput Input { get; private set; }
        public int BirdsPlayed { get; private set; }
        public float WindLoopVolume => _windBed != null ? _windBed.Source.volume : 0f;

        void Awake()
        {
            _info = WorldInfo.Current;
            _windBed = NewBed("Wind", Wind);
            _cricketBed = NewBed("Crickets", Crickets);
            _waterBed = NewBed("Water", Water);
            _roomBed = NewBed("RoomTone", Room);
            _voices = new AudioSource[BirdVoices];
            for (int v = 0; v < BirdVoices; v++)
            {
                var go = new GameObject("Bird" + v);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                // Heard from a good way off in a quiet valley, but not across the map.
                _voices[v] = SpatialAudio.Apply(source, new SpatialAudio.Range(6f, 70f));
            }
            _nextBird = Time.time + 4f;
        }

        Bed NewBed(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            return new Bed { Source = source };
        }

        void OnDestroy() => Shader.SetGlobalFloat("_GameWindBoost", 0f);

        void Update()
        {
            _tick -= Time.deltaTime;
            if (_tick <= 0f)
            {
                _tick = TickSeconds;
                Refresh();
            }
            // The beds ease toward their targets (about a second) so crossing a region or a doorway is a fade.
            float k = 1f - Mathf.Exp(-Time.deltaTime * 1.6f);
            float master = GameSettings.AmbientVolume;
            Follow(_windBed, Levels.Wind * master, k);
            Follow(_cricketBed, Levels.Crickets * master, k);
            Follow(_waterBed, Levels.Water * master, k);
            Follow(_roomBed, Levels.RoomTone * master, k);

            if (Time.time >= _nextBird) TryBird(master);
            Shader.SetGlobalFloat("_GameWindBoost", Mathf.Lerp(Shader.GetGlobalFloat("_GameWindBoost"), Levels.WindBoost, k));
        }

        static void Follow(Bed bed, float target, float k)
        {
            // Falling is quick (a shot must cut through at once), rising slow.
            bed.Level = Mathf.Lerp(bed.Level, target, target < bed.Level ? Mathf.Min(1f, k * 5f) : k);
            var source = bed.Source;
            source.volume = bed.Level;
            if (bed.Level > 0.003f && !source.isPlaying) source.Play();
            else if (bed.Level <= 0.003f && source.isPlaying) source.Stop();
        }

        // Under a roof when the spot overhead and nearly all of a patch round it are covered (a doorway or a gap is not
        // enough to call it open air; a tree has no collider overhead). Five rays; also the gunshot's indoor tail (#287).
        public static bool UnderRoof(Vector3 at)
        {
            int covered = 0;
            for (int i = 0; i < 5; i++)
            {
                var offset = i == 0 ? Vector3.zero : new Vector3(i == 1 ? 1.5f : i == 2 ? -1.5f : 0f, 0f, i == 3 ? 1.5f : i == 4 ? -1.5f : 0f);
                if (Physics.Raycast(at + offset, Vector3.up, IndoorProbe, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) covered++;
                if (covered + (4 - i) < 4) return false; // cannot reach four any more
            }
            return covered >= 4;
        }

        void Refresh()
        {
            if (!SpatialAudio.ListenerPosition(out var at)) return;
            var view = EnvironmentView.Current;
            _roofTick -= TickSeconds;
            if (_roofTick <= 0f)
            {
                _roofTick = 0.5f;
                _indoors = UnderRoof(at);
            }

            if (_terrain == null) _terrain = Terrain.activeTerrain;
            float shore = 0f;
            if (_terrain != null && _info != null)
            {
                float ground = _terrain.SampleHeight(at) + _terrain.transform.position.y;
                shore = 1f - Mathf.SmoothStep(0f, 1f, (ground - _info.SeaLevel) / ShoreMetres);
            }

            var input = new AmbientInput
            {
                Hour = view != null ? view.Hour : WorldSky.DayHour,
                Weather = view != null ? view.Weather : WeatherKind.Clear,
                Region = NearestRegion(at),
                Indoors = _indoors,
                Shore = shore,
                Duck = SpatialAudio.Loud01,
            };
            Input = input;
            Levels = AmbientRules.Mix(input);
        }

        RegionKind NearestRegion(Vector3 at)
        {
            if (_info == null || _info.Regions.Count == 0) return RegionKind.Town;
            var best = RegionKind.Town;
            float bestSq = float.MaxValue;
            foreach (var r in _info.Regions)
            {
                float dx = r.Centre.x - at.x, dz = r.Centre.z - at.z, sq = dx * dx + dz * dz;
                if (sq < bestSq) { bestSq = sq; best = r.Kind; }
            }
            return best;
        }

        void TryBird(float master)
        {
            float perMinute = Levels.BirdsPerMinute;
            if (perMinute <= 0.01f || master <= 0.01f || !SpatialAudio.ListenerPosition(out var at))
            {
                _nextBird = Time.time + 3f;
                return;
            }
            // Random gaps around the mean, never a metronome.
            _nextBird = Time.time + 60f / perMinute * UnityEngine.Random.Range(0.4f, 1.6f);
            foreach (var voice in _voices)
            {
                if (voice.isPlaying) continue;
                float angle = UnityEngine.Random.value * Mathf.PI * 2f, distance = UnityEngine.Random.Range(9f, 38f);
                voice.transform.position = at + new Vector3(Mathf.Cos(angle) * distance, UnityEngine.Random.Range(3f, 9f), Mathf.Sin(angle) * distance);
                voice.pitch = UnityEngine.Random.Range(0.88f, 1.18f);
                voice.PlayOneShot(Birds[UnityEngine.Random.Range(0, Birds.Length)], Levels.BirdVolume * master * UnityEngine.Random.Range(0.6f, 1f));
                BirdsPlayed++;
                return;
            }
        }

        // ---- Recordings, else synthesis (made once per run) ----

        static AudioClip Wind => _wind != null ? _wind : (_wind = Resources.Load<AudioClip>("Sfx/amb_wind_loop") ?? Make("AmbientWind", SynthWind()));
        static AudioClip Crickets => _crickets != null ? _crickets : (_crickets = Resources.Load<AudioClip>("Sfx/amb_crickets_loop") ?? Make("AmbientCrickets", SynthCrickets()));
        static AudioClip Water => _water != null ? _water : (_water = Make("AmbientWater", SynthWater()));
        static AudioClip Room => _room != null ? _room : (_room = Make("AmbientRoom", SynthRoom()));
        static AudioClip[] Birds => _birds ??= RecordedBirds() is { Length: > 0 } recorded ? recorded : Array.ConvertAll(SynthBirds(), d => Make("AmbientBird", d));

        static AudioClip[] RecordedBirds() => Array.FindAll(Array.ConvertAll(new[] { 1, 2, 3, 4 }, i => Resources.Load<AudioClip>("Sfx/amb_bird_0" + i)), c => c != null);

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // `extended` is the loop plus `fade` more samples of the same signal; the extra samples are blended into the
        // start (equal power), so the last sample runs straight into the first and the loop has no seam.
        static float[] Loop(float[] extended, int fade)
        {
            int length = extended.Length - fade;
            var data = new float[length];
            Array.Copy(extended, data, length);
            for (int k = 0; k < fade; k++)
            {
                float w = k / (float)fade;
                data[k] = extended[k] * Mathf.Sqrt(w) + extended[length + k] * Mathf.Sqrt(1f - w);
            }
            return data;
        }

        static void Normalise(float[] data, float peak)
        {
            float max = 1e-4f;
            foreach (float v in data) max = Mathf.Max(max, Mathf.Abs(v));
            for (int i = 0; i < data.Length; i++) data[i] *= peak / max;
        }

        // Wind: low-passed noise whose loudness swells and ebbs (two slow whole-cycle waves) and whose brightness
        // follows the swell, so a gust is louder and hissier than a lull.
        public static float[] SynthWind()
        {
            const int seconds = 8, fade = Rate / 2;
            var data = new float[Rate * seconds + fade];
            var rng = new System.Random(4411);
            float a = 0f, b = 0f, c = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float swell = 0.55f + 0.3f * Mathf.Sin(2f * Mathf.PI * t / seconds * 1f + 0.6f) + 0.15f * Mathf.Sin(2f * Mathf.PI * t / seconds * 3f + 2.1f);
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                a += (n - a) * (0.03f + 0.05f * swell);
                b += (a - b) * 0.12f;
                c += (n - c) * 0.35f;
                data[i] = (b * 2.4f + (n - c) * 0.12f * swell) * swell;
            }
            data = Loop(data, fade);
            Normalise(data, 0.85f);
            return data;
        }

        // Crickets: several singers on different pitches, each a burst of pulses repeating a whole number of times in
        // the loop, so the loop is seamless and the chorus never lines up.
        public static float[] SynthCrickets()
        {
            const int seconds = 4;
            var data = new float[Rate * seconds];
            var rng = new System.Random(9021);
            for (int singer = 0; singer < 6; singer++)
            {
                float pitch = 3700f + 1300f * (float)rng.NextDouble();
                int bursts = 5 + singer % 4;                       // bursts per loop
                float offset = (float)rng.NextDouble() * seconds / bursts;
                float amp = 0.12f + 0.1f * (float)rng.NextDouble();
                for (int burst = 0; burst < bursts; burst++)
                {
                    float start = offset + burst * (seconds / (float)bursts);
                    for (int pulse = 0; pulse < 4; pulse++)
                    {
                        float p0 = start + pulse * 0.026f;
                        int s0 = (int)(p0 * Rate);
                        int length = (int)(0.018f * Rate);
                        for (int k = 0; k < length; k++)
                        {
                            int index = (s0 + k) % data.Length;
                            float env = Mathf.Sin(Mathf.PI * k / length);
                            data[index] += Mathf.Sin(2f * Mathf.PI * pitch * k / Rate) * env * env * amp;
                        }
                    }
                }
            }
            Normalise(data, 0.7f);
            return data;
        }

        // Water: noise washing in and out, a wave every four seconds (two in the loop) with a bright hiss at the crest.
        public static float[] SynthWater()
        {
            const int seconds = 8, fade = Rate / 2;
            var data = new float[Rate * seconds + fade];
            var rng = new System.Random(5150);
            float low = 0f, mid = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float wave = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t / 4f + 1.2f), 2f);
                wave = 0.25f + 0.75f * wave * (0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * t / seconds * 3f));
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (n - low) * 0.05f;
                mid += (n - mid) * 0.3f;
                data[i] = (low * 2.2f + (n - mid) * 0.35f * wave * wave) * wave;
            }
            data = Loop(data, fade);
            Normalise(data, 0.8f);
            return data;
        }

        // Room tone: a low mains hum with its octave and very quiet air. Whole cycles of 50 Hz in the two seconds.
        public static float[] SynthRoom()
        {
            const int seconds = 2, fade = Rate / 10;
            var data = new float[Rate * seconds + fade];
            var rng = new System.Random(77);
            float air = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                air += (n - air) * 0.015f;
                data[i] = Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 100f * t + 0.5f) * 0.14f + Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.05f + air * 3.5f;
            }
            data = Loop(data, fade);
            Normalise(data, 0.5f);
            return data;
        }

        // Four songs: a trill, a two-note whistle, a falling "tsee-tsee" and a warble. Each note is a pitch glide with
        // a soft envelope and a quieter octave above.
        public static float[][] SynthBirds()
        {
            var songs = new[]
            {
                new[] { new[] { 0f, 3300f, 4300f, 0.05f }, new[] { 0.07f, 3400f, 4400f, 0.05f }, new[] { 0.14f, 3500f, 4500f, 0.05f }, new[] { 0.21f, 3600f, 4600f, 0.05f },
                        new[] { 0.28f, 3700f, 4700f, 0.05f }, new[] { 0.35f, 3800f, 4800f, 0.06f } },
                new[] { new[] { 0f, 2500f, 3200f, 0.28f }, new[] { 0.36f, 3200f, 2200f, 0.4f } },
                new[] { new[] { 0f, 5200f, 3600f, 0.12f }, new[] { 0.2f, 5000f, 3500f, 0.12f }, new[] { 0.46f, 5300f, 3700f, 0.12f }, new[] { 0.66f, 5100f, 3500f, 0.12f } },
                new[] { new[] { 0f, 1800f, 2600f, 0.1f }, new[] { 0.11f, 2600f, 1900f, 0.1f }, new[] { 0.22f, 1900f, 2800f, 0.1f }, new[] { 0.33f, 2800f, 2000f, 0.1f },
                        new[] { 0.44f, 2000f, 3000f, 0.14f } },
            };
            var result = new float[songs.Length][];
            for (int s = 0; s < songs.Length; s++)
            {
                float end = 0f;
                foreach (var n in songs[s]) end = Mathf.Max(end, n[0] + n[3]);
                var data = new float[(int)((end + 0.1f) * Rate)];
                foreach (var n in songs[s])
                {
                    int s0 = (int)(n[0] * Rate), length = (int)(n[3] * Rate);
                    float phase = 0f;
                    for (int k = 0; k < length && s0 + k < data.Length; k++)
                    {
                        float u = k / (float)length;
                        float f = Mathf.Lerp(n[1], n[2], u) * (1f + 0.012f * Mathf.Sin(u * 60f));
                        phase += 2f * Mathf.PI * f / Rate;
                        float env = Mathf.Sin(Mathf.PI * u);
                        data[s0 + k] += (Mathf.Sin(phase) + 0.25f * Mathf.Sin(phase * 2f)) * env * env;
                    }
                }
                Normalise(data, 0.8f);
                result[s] = data;
            }
            return result;
        }
    }
}
