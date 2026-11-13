using System;
using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Game.Gameplay
{
    // Sounds a combatant makes: gunshots, reloads, footsteps, punches and being hit. Clip arrays for weapons are indexed by weapon id.
    // Footsteps sound when the animation puts a foot down, so they always match what the legs are doing.
    // Sound cues (#120, ADR-092): steps sound like the ground under them and carry as far as gait and stance allow;
    // a shot is a near "crack/body" layer crossfaded into a far "thump/echo" layer by distance; a round passing
    // close to the listener whizzes.
    [RequireComponent(typeof(Combatant), typeof(CombatantAnimation))]
    public class CombatantAudio : MonoBehaviour
    {
        const float PitchJitter = 0.05f;
        const double PelletWindow = 0.03;

        [SerializeField] AudioSource _weaponSource;
        [SerializeField] AudioSource _bodySource;
        [SerializeField] AudioClip[] _shotByWeapon = new AudioClip[0];
        [SerializeField] AudioClip[] _reloadByWeapon = new AudioClip[0];
        // Playback pitch per weapon id (missing entries play at 1). The Sentinel reuses the rifle shot, pitched lower for a heavier crack.
        [SerializeField] float[] _shotPitchByWeapon = { 1f, 1f, 1f, 1f, 1f, 0.72f };
        [SerializeField] AudioClip[] _footsteps = new AudioClip[0];
        [SerializeField] AudioClip[] _hits = new AudioClip[0];
        [SerializeField] float _shotVolume = 0.8f;
        [SerializeField] float _reloadVolume = 0.6f;
        [SerializeField] float _footstepVolume = 0.35f;
        [SerializeField] float _landingVolume = 0.6f;
        [SerializeField] float _hitVolume = 0.7f;
        // Punches reuse the hit clips (ADR-059: no new asset): a landed punch is a full impact, a miss a quick,
        // quiet, higher swish.
        [SerializeField] float _punchImpactVolume = 0.8f;
        [SerializeField] float _punchSwingVolume = 0.25f;
        [SerializeField] float _punchSwingPitch = 1.6f;

        readonly ShotGrouper _shots = new ShotGrouper(PelletWindow);
        Combatant _combatant;
        CombatantAnimation _animation;
        CombatantStance _stance; // may be missing
        AudioLowPassFilter _distantShot; // muffles far gunshots (SpatialAudio.DistantCutoff)
        AudioSource _farSource;   // far gunshot layer: a child of the weapon source with its own distant low-pass
        AudioLowPassFilter _farMuffle;
        AudioSource _stepSource;  // footsteps: its own source so each step can carry its own distance
        AudioSource _whizSource;  // moved to where a passing round came closest to the listener
        CombatantMotion _motion;  // may be missing
        float _stepRangeApplied;
        double _lastWhiz = double.NegativeInfinity;
        bool _wasReloading;
        float _lastHp;
        int _nextFootstep;

        // Counters so tests can tell that sounds were triggered without listening.
        public int ShotsPlayed { get; private set; }
        public int ReloadsPlayed { get; private set; }
        public int FootstepsPlayed { get; private set; }
        public int LandingsPlayed { get; private set; }
        public int HitsPlayed { get; private set; }
        public int PunchesPlayed { get; private set; }
        // Volume of the last footstep or landing, before the effects volume setting (stance and slow walk applied).
        public float LastFootstepVolume { get; private set; }
        public FootSurface LastSurface { get; private set; }
        public float LastFootstepRange { get; private set; }
        public int NearShotsPlayed { get; private set; }
        public int FarShotsPlayed { get; private set; }
        public int WhizzesPlayed { get; private set; }
        // Hook for suppressors (G7): this combatant's shots sound as if SoundRules.SuppressedDistanceFactor times further.
        public bool Suppressed { get; set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _animation = GetComponent<CombatantAnimation>();
            _stance = GetComponent<CombatantStance>();
            _motion = GetComponent<CombatantMotion>();
            SpatialAudio.Apply(_weaponSource, SpatialAudio.Category.Gunshot);
            SpatialAudio.Apply(_bodySource, SpatialAudio.Category.Body);
            if (_weaponSource != null)
            {
                _distantShot = _weaponSource.gameObject.AddComponent<AudioLowPassFilter>();
                _farSource = SpatialAudio.Apply(ChildSource(_weaponSource.transform, "FarShotAudio", _weaponSource), SpatialAudio.Category.Gunshot);
                _farMuffle = _farSource.gameObject.AddComponent<AudioLowPassFilter>();
            }
            if (_bodySource != null)
            {
                _stepSource = SpatialAudio.Apply(ChildSource(_bodySource.transform, "StepAudio", _bodySource), SpatialAudio.Category.Body);
                _stepRangeApplied = SpatialAudio.For(SpatialAudio.Category.Body).Max;
                _whizSource = SpatialAudio.Apply(ChildSource(transform, "WhizAudio", _bodySource), SpatialAudio.Category.Body);
            }
            _combatant.ShotFired += OnShot;
            _combatant.Punched += OnPunch;
            _animation.FootDown += OnFootDown;
            _animation.Landed += OnLanded;
        }

        void OnDestroy()
        {
            _combatant.ShotFired -= OnShot;
            _combatant.Punched -= OnPunch;
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
            var surface = SurfaceAt(transform.position);
            var clips = surface == FootSurface.Concrete ? _footsteps : StepClips(surface);
            if (clips.Length == 0) clips = _footsteps;
            if (clips.Length == 0) return false;
            // A lowered stance or a slow walk treads more quietly, a sprint louder; the ground rings or muffles.
            var loco = GameData.Rules.Locomotion;
            var v = _motion != null ? _motion.LocalVelocity : Vector3.zero;
            bool sprinting = SoundRules.IsSprint(new Vector2(v.x, v.z).magnitude, loco.WalkSpeed, loco.SprintSpeed, _stance != null ? _stance.MoveFactor : 1f);
            float loudness = SoundRules.StepLoudness(_stance != null ? _stance.FootstepVolumeFactor : 1f, sprinting, surface);
            float range = SoundRules.StepRange(loudness);
            if (_stepSource != null && Mathf.Abs(range - _stepRangeApplied) > 0.5f)
            {
                SpatialAudio.Apply(_stepSource, new SpatialAudio.Range(Mathf.Min(2f, range * 0.5f), range));
                _stepRangeApplied = range;
            }
            volume *= loudness;
            LastFootstepVolume = volume;
            LastFootstepRange = range;
            LastSurface = surface;
            // Cycle instead of picking at random so the same clip never plays twice in a row.
            _nextFootstep = (_nextFootstep + 1) % clips.Length;
            return Play(_stepSource != null ? _stepSource : _bodySource, clips[_nextFootstep], volume);
        }

        void OnShot(Vector3 origin, Vector3 end)
        {
            // Every pellet may pass the listener; one whiz per shot.
            double now = Time.timeAsDouble;
            if (now - _lastWhiz > PelletWindow && _whizSource != null && SpatialAudio.ListenerPosition(out var listener)
                && SoundRules.Whiz(N(origin), N(end), N(listener), out var passing) && WhizClips.Length > 0)
            {
                _lastWhiz = now;
                _whizSource.transform.position = new Vector3(passing.X, passing.Y, passing.Z);
                if (Play(_whizSource, WhizClips[Random.Range(0, WhizClips.Length)], 1f)) WhizzesPlayed++;
            }
            if (!_shots.IsNewShot(now)) return;

            int weapon = _combatant.ActiveWeaponId;
            float meters = SpatialAudio.DistanceToListener(_weaponSource.transform.position);
            float heard = SoundRules.HeardDistance(meters, Suppressed);
            SoundRules.ShotLayers(heard, out float near, out float far);
            // Suppressed: as quiet as a shot from `heard` metres (the source's rolloff already applies `meters`).
            float scale = Suppressed ? Mathf.Clamp01(SpatialAudio.Gain(SpatialAudio.Category.Gunshot, heard)
                / Mathf.Max(1e-4f, SpatialAudio.Gain(SpatialAudio.Category.Gunshot, meters))) : 1f;
            float cutoff = SpatialAudio.DistantCutoff(heard);
            if (_distantShot != null) _distantShot.cutoffFrequency = cutoff;
            if (_farMuffle != null) _farMuffle.cutoffFrequency = cutoff;
            bool played = false;
            if (near > 0f && Play(_weaponSource, ClipFor(_shotByWeapon, weapon), _shotVolume * near * scale, PitchFor(_shotPitchByWeapon, weapon)))
            {
                NearShotsPlayed++;
                played = true;
            }
            if (far > 0f && Play(_farSource, ClipFor(FarShotClips, weapon), _shotVolume * far * scale))
            {
                FarShotsPlayed++;
                played = true;
            }
            if (played) ShotsPlayed++;
        }

        // ---- Clips and ground ----

        // By weapon id: none, VK-9, Ordo, Warden, Breacher, Sentinel.
        static readonly string[] FarShotNames = { null, "pistol", "smg", "rifle", "shotgun", "sniper" };
        static AudioClip[] _farShotClips, _whizClips;
        static readonly Dictionary<FootSurface, AudioClip[]> StepSets = new Dictionary<FootSurface, AudioClip[]>();

        static AudioClip[] FarShotClips => _farShotClips ??= Array.ConvertAll(FarShotNames, n => n == null ? null : Resources.Load<AudioClip>("Shots/shot_" + n + "_far"));
        static AudioClip[] WhizClips => _whizClips ??= Array.FindAll(Resources.LoadAll<AudioClip>("Shots"), c => c.name.StartsWith("whiz_", StringComparison.Ordinal));

        public static AudioClip[] StepClips(FootSurface surface)
        {
            if (!StepSets.TryGetValue(surface, out var clips))
            {
                clips = Resources.LoadAll<AudioClip>(SoundRules.StepClips(surface));
                Array.Sort(clips, (a, b) => string.CompareOrdinal(a.name, b.name));
                StepSets[surface] = clips;
            }
            return clips;
        }

        // What the ground under `feet` sounds like: asphalt (road strips have no collider, see PavedRoads), the
        // terrain layer there (or water below the sea surface), a
        // building's floor by its kind (WorldBuilder names buildings "Lot03_House", "Station01"; their boxes sit on
        // a "Colliders" child), an elevator car or vehicle (metal); concrete otherwise.
        public static FootSurface SurfaceAt(Vector3 feet)
        {
            if (!Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out var hit, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return FootSurface.Concrete;
            var world = WorldInfo.Current;
            bool underWater = world != null && hit.point.y < world.SeaLevel;
            if (hit.collider is TerrainCollider terrain)
                return !underWater && PavedRoads.Contains(hit.point) ? FootSurface.Concrete : SoundRules.TerrainSurface(LayerAt(terrain, hit.point), underWater);
            if (underWater) return FootSurface.Water;
            if (hit.collider.GetComponentInParent<Vehicle>() != null) return FootSurface.Metal;
            var root = hit.collider.transform.parent;
            if (hit.collider.name != "Colliders" || root == null) return FootSurface.Concrete;
            if (root.name.StartsWith("ElevatorCar", StringComparison.Ordinal)) return FootSurface.Metal;
            BuildingKind kind;
            if (root.name.StartsWith("Station", StringComparison.Ordinal)) kind = BuildingKind.ElevatorStation;
            else if (!Enum.TryParse(root.name.Substring(root.name.LastIndexOf('_') + 1), out kind)) return FootSurface.Concrete;
            // shortcut: a box narrower than a stair run counts as a stair step and every other box as floor (props
            // and roofs are not told apart); tag the boxes in WorldBuilder if that is ever wrong where it matters.
            var box = hit.collider as BoxCollider;
            var piece = box != null && Mathf.Min(box.size.x, box.size.z) < 0.4f ? SurfaceKind.Stairs : SurfaceKind.Floor;
            return SoundRules.BuildingSurface(kind, piece);
        }

        // Name of the terrain layer with the most weight at `point`.
        static string LayerAt(TerrainCollider collider, Vector3 point)
        {
            var data = collider.terrainData;
            if (data == null || data.alphamapLayers == 0) return null;
            var local = point - collider.transform.position;
            int x = Mathf.Clamp(Mathf.RoundToInt(local.x / data.size.x * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(local.z / data.size.z * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);
            var weights = data.GetAlphamaps(x, z, 1, 1);
            int best = 0;
            for (int i = 1; i < data.alphamapLayers; i++)
                if (weights[0, 0, i] > weights[0, 0, best]) best = i;
            var layers = data.terrainLayers;
            return best < layers.Length && layers[best] != null ? layers[best].name : null;
        }

        static System.Numerics.Vector3 N(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        static AudioSource ChildSource(Transform parent, string name, AudioSource like)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.outputAudioMixerGroup = like.outputAudioMixerGroup;
            source.priority = like.priority;
            return source;
        }

        void OnPunch(in Combatant.PunchResult punch)
        {
            // Melee weapon swings have their own sounds (MeleeAudio, ADR-073).
            if (_hits.Length == 0 || punch.WeaponId != 0) return;
            var clip = _hits[Random.Range(0, _hits.Length)];
            bool landed = punch.Victim != null;
            if (Play(_bodySource, clip, landed ? _punchImpactVolume : _punchSwingVolume, landed ? 1f : _punchSwingPitch)) PunchesPlayed++;
        }

        // Reloads play on the body source: heard as far as footsteps, not as far as shots.
        void Update()
        {
            bool reloading = _combatant.Reloading.Value;
            if (reloading && !_wasReloading && Play(_bodySource, ClipFor(_reloadByWeapon, _combatant.ActiveWeaponId), _reloadVolume))
                ReloadsPlayed++;
            _wasReloading = reloading;

            float hp = _combatant.Hp.Value;
            if (hp < _lastHp && _hits.Length > 0 && Play(_bodySource, _hits[Random.Range(0, _hits.Length)], _hitVolume)) HitsPlayed++;
            _lastHp = hp;
        }

        static AudioClip ClipFor(AudioClip[] byWeapon, int weaponId) =>
            weaponId >= 0 && weaponId < byWeapon.Length ? byWeapon[weaponId] : null;

        static float PitchFor(float[] byWeapon, int weaponId) =>
            byWeapon != null && weaponId >= 0 && weaponId < byWeapon.Length && byWeapon[weaponId] > 0f ? byWeapon[weaponId] : 1f;

        static bool Play(AudioSource source, AudioClip clip, float volume, float pitch = 1f)
        {
            if (source == null || clip == null) return false;
            source.pitch = pitch + Random.Range(-PitchJitter, PitchJitter);
            source.PlayOneShot(clip, volume * GameSettings.EffectsVolume);
            return true;
        }
    }
}
