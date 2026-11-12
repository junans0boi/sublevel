using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Sounds a combatant makes: gunshots, reloads, footsteps and being hit. Clip arrays for weapons are indexed by weapon id.
    [RequireComponent(typeof(Combatant), typeof(CombatantMotion))]
    public class CombatantAudio : MonoBehaviour
    {
        const float PitchJitter = 0.05f;
        const float StrideMeters = 1.9f;
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
        [SerializeField] float _hitVolume = 0.7f;

        readonly FootstepCadence _cadence = new FootstepCadence(StrideMeters);
        readonly ShotGrouper _shots = new ShotGrouper(PelletWindow);
        Combatant _combatant;
        CombatantMotion _motion;
        bool _wasReloading;
        float _lastHp;
        int _nextFootstep;

        // Counters so tests can tell that sounds were triggered without listening.
        public int ShotsPlayed { get; private set; }
        public int ReloadsPlayed { get; private set; }
        public int FootstepsPlayed { get; private set; }
        public int HitsPlayed { get; private set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _motion = GetComponent<CombatantMotion>();
            _combatant.ShotFired += OnShot;
        }

        void OnDestroy() => _combatant.ShotFired -= OnShot;

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

            bool walking = !_combatant.Dead.Value && !_motion.Airborne && _motion.PlanarDistanceThisFrame > 0.0005f;
            if (_cadence.Advance(_motion.PlanarDistanceThisFrame, walking) && _footsteps.Length > 0)
            {
                // Cycle instead of picking at random so the same clip never plays twice in a row.
                if (Play(_bodySource, _footsteps[_nextFootstep], _footstepVolume)) FootstepsPlayed++;
                _nextFootstep = (_nextFootstep + 1) % _footsteps.Length;
            }
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
