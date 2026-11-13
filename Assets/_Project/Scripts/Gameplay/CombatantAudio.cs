using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Sounds a combatant makes: gunshots, reloads, footsteps and being hit. Clip arrays for weapons are indexed by weapon id.
    // Footsteps sound when the animation puts a foot down, so they always match what the legs are doing.
    [RequireComponent(typeof(Combatant), typeof(CombatantAnimation))]
    public class CombatantAudio : MonoBehaviour
    {
        const float PitchJitter = 0.05f;
        const double PelletWindow = 0.03;

        [SerializeField] AudioSource _weaponSource;
        [SerializeField] AudioSource _bodySource;
        [SerializeField] AudioClip[] _shotByWeapon = new AudioClip[0];
        [SerializeField] AudioClip[] _reloadByWeapon = new AudioClip[0];
        [SerializeField] AudioClip[] _footsteps = new AudioClip[0];
        [SerializeField] AudioClip[] _hits = new AudioClip[0];
        [SerializeField] float _shotVolume = 0.8f;
        [SerializeField] float _reloadVolume = 0.6f;
        [SerializeField] float _footstepVolume = 0.35f;
        [SerializeField] float _landingVolume = 0.6f;
        [SerializeField] float _hitVolume = 0.7f;

        readonly ShotGrouper _shots = new ShotGrouper(PelletWindow);
        Combatant _combatant;
        CombatantAnimation _animation;
        CombatantStance _stance; // may be missing
        bool _wasReloading;
        float _lastHp;
        int _nextFootstep;

        // Counters so tests can tell that sounds were triggered without listening.
        public int ShotsPlayed { get; private set; }
        public int ReloadsPlayed { get; private set; }
        public int FootstepsPlayed { get; private set; }
        public int LandingsPlayed { get; private set; }
        public int HitsPlayed { get; private set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _animation = GetComponent<CombatantAnimation>();
            _stance = GetComponent<CombatantStance>();
            _combatant.ShotFired += OnShot;
            _animation.FootDown += OnFootDown;
            _animation.Landed += OnLanded;
        }

        void OnDestroy()
        {
            _combatant.ShotFired -= OnShot;
            _animation.FootDown -= OnFootDown;
            _animation.Landed -= OnLanded;
        }

        // Both feet at once: a footstep, louder.
        void OnLanded()
        {
            if (PlayFootstep(_landingVolume)) LandingsPlayed++;
        }

        void OnFootDown()
        {
            if (PlayFootstep(_footstepVolume)) FootstepsPlayed++;
        }

        bool PlayFootstep(float volume)
        {
            if (_footsteps.Length == 0) return false;
            // Cycle instead of picking at random so the same clip never plays twice in a row.
            // A lowered stance treads more quietly.
            if (_stance != null) volume *= _stance.FootstepVolumeFactor;
            bool played = Play(_bodySource, _footsteps[_nextFootstep], volume);
            _nextFootstep = (_nextFootstep + 1) % _footsteps.Length;
            return played;
        }

        void OnShot(Vector3 origin, Vector3 end)
        {
            if (!_shots.IsNewShot(Time.timeAsDouble)) return;
            if (Play(_weaponSource, ClipFor(_shotByWeapon, _combatant.ActiveWeaponId), _shotVolume)) ShotsPlayed++;
        }

        void Update()
        {
            bool reloading = _combatant.Reloading.Value;
            if (reloading && !_wasReloading && Play(_weaponSource, ClipFor(_reloadByWeapon, _combatant.ActiveWeaponId), _reloadVolume))
                ReloadsPlayed++;
            _wasReloading = reloading;

            float hp = _combatant.Hp.Value;
            if (hp < _lastHp && _hits.Length > 0 && Play(_bodySource, _hits[Random.Range(0, _hits.Length)], _hitVolume)) HitsPlayed++;
            _lastHp = hp;
        }

        static AudioClip ClipFor(AudioClip[] byWeapon, int weaponId) =>
            weaponId >= 0 && weaponId < byWeapon.Length ? byWeapon[weaponId] : null;

        static bool Play(AudioSource source, AudioClip clip, float volume)
        {
            if (source == null || clip == null) return false;
            source.pitch = 1f + Random.Range(-PitchJitter, PitchJitter);
            source.PlayOneShot(clip, volume);
            return true;
        }
    }
}
