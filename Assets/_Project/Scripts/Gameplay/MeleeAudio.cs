using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Sounds of melee weapon swings (ADR-073); punches stay with CombatantAudio. Every swing makes a whoosh (recorded,
    // #288; filtered noise made in code when the clip is missing), lower for a heavier, slower weapon. A swing that lands adds a heavy
    // impact (Kenney CC0), and the pan also rings (per-weapon clip, indexed by weapon id).
    [RequireComponent(typeof(Combatant))]
    public class MeleeAudio : MonoBehaviour
    {
        const float PitchJitter = 0.06f;
        const int SampleRate = 44100;
        const float SwooshSeconds = 0.28f;

        [SerializeField] AudioSource _source;
        [SerializeField] AudioClip[] _impacts = new AudioClip[0];
        [SerializeField] AudioClip[] _ringByWeapon = new AudioClip[0];
        [SerializeField] float _swooshVolume = 0.45f;
        [SerializeField] float _impactVolume = 0.9f;
        [SerializeField] float _ringVolume = 0.55f;
        // Swoosh pitch for the fastest and the slowest weapon (by swing interval 0.5 s .. 0.9 s).
        [SerializeField] float _fastPitch = 1.2f;
        [SerializeField] float _slowPitch = 0.8f;

        static AudioClip _swoosh;
        Combatant _combatant;
        int _nextImpact;

        // Counters so tests can tell that sounds were triggered without listening.
        public int SwingsPlayed { get; private set; }
        public int ImpactsPlayed { get; private set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _combatant.Punched += OnPunch;
            SpatialAudio.Apply(_source, SpatialAudio.Category.Body);
        }

        void OnDestroy() => _combatant.Punched -= OnPunch;

        void OnPunch(in Combatant.PunchResult punch)
        {
            if (punch.WeaponId == 0 || _source == null) return;
            var weapon = GameData.Weapons.Find(punch.WeaponId);
            float heavy = weapon != null ? Mathf.InverseLerp(0.5f, 0.9f, weapon.FireInterval) : 0.5f;
            if (Play(Swoosh, _swooshVolume, Mathf.Lerp(_fastPitch, _slowPitch, heavy))) SwingsPlayed++;
            if (punch.Victim == null) return;
            if (_impacts.Length > 0 && Play(_impacts[_nextImpact], _impactVolume, Mathf.Lerp(1.05f, 0.9f, heavy))) ImpactsPlayed++;
            if (_impacts.Length > 0) _nextImpact = (_nextImpact + 1) % _impacts.Length;
            var ring = punch.WeaponId < _ringByWeapon.Length ? _ringByWeapon[punch.WeaponId] : null;
            Play(ring, _ringVolume, 1f);
        }

        bool Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return false;
            // PlayOneShot so the whoosh and the impact overlap; pitch is per source, so it is set for the pair.
            _source.pitch = pitch + Random.Range(-PitchJitter, PitchJitter);
            _source.PlayOneShot(clip, volume * GameSettings.EffectsVolume);
            return true;
        }

        // A short whoosh: noise through a low-pass filter whose cutoff rises and falls with a swelling envelope.
        // Fixed seed, so it is the same clip every run.
        static AudioClip Swoosh
        {
            get
            {
                if (_swoosh != null) return _swoosh;
                // The recorded swing (#288, Freesound CC0, ledger S-0047); the filtered noise below is the fallback.
                _swoosh = Resources.Load<AudioClip>("Sfx/" + WeaponSounds.MeleeSwing + "01");
                if (_swoosh != null) return _swoosh;
                int count = Mathf.RoundToInt(SwooshSeconds * SampleRate);
                var data = new float[count];
                var rng = new System.Random(73);
                float low = 0f, band = 0f;
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)(count - 1);
                    float envelope = Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.7f));
                    // Cutoff as a share of the sample rate: brighter in the middle of the swing.
                    float cutoff = Mathf.Lerp(0.01f, 0.08f, envelope);
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    low += cutoff * (noise - low);
                    band += cutoff * (low - band);
                    data[i] = (low - band) * envelope * 6f;
                }
                _swoosh = AudioClip.Create("MeleeSwoosh", count, 1, SampleRate, false);
                _swoosh.SetData(data, 0);
                return _swoosh;
            }
        }
    }
}
