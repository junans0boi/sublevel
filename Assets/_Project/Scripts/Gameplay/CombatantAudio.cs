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
        // Last take of each gunshot layer (WeaponSounds.VariantOf), so the same take never plays twice in a row (#287).
        int _nearTake, _farTake, _tailTake, _ricochetTake;
        bool _indoors;
        float _indoorsAt = -10f;
        AudioSource _impactSource; // moved to where a ricochet happens
        AudioSource _tailSource;   // indoor tail layer (#287), with its own low-pass
        AudioLowPassFilter _tailMuffle;
        CombatantHealing _healing; // may be missing
        CombatantSwim _swim;       // may be missing
        bool _wasHealing, _wasSwimming;
        float _nextStrokeAt, _lastGruntAt = -10f;
        double _lastRicochet = double.NegativeInfinity;
        const float IndoorRefreshSeconds = 0.5f, StrokeSeconds = 1.1f, GruntCooldown = 0.6f, MinGruntDamage = 4f;
        [SerializeField] float _tailVolume = 0.8f;
        [SerializeField] float _dryFireVolume = 0.5f;
        [SerializeField] float _ricochetVolume = 0.55f;
        [SerializeField] float _gruntVolume = 0.55f;
        [SerializeField] float _healVolume = 0.5f;
        [SerializeField] float _pickupVolume = 0.4f;
        [SerializeField] float _swimVolume = 0.5f;

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
        public int TailsPlayed { get; private set; }
        public int DryFiresPlayed { get; private set; }
        public int RicochetsPlayed { get; private set; }
        public int GruntsPlayed { get; private set; }
        public int HealsPlayed { get; private set; }
        public int PickupsPlayed { get; private set; }
        public int SwimStrokesPlayed { get; private set; }
        // The take (WeaponSounds.VariantOf index) of the last near layer, for the tests.
        public int LastNearTake => _nearTake;
        // Hook for suppressors (G7): this combatant's shots sound as if SoundRules.SuppressedDistanceFactor times further.
        public bool Suppressed { get; set; }
        CombatantOptics _gear;
        AudioLowPassFilter _stepMuffle;
        Occlusion _occ = Occlusion.Open;
        float _occAt = -10f;

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
                _tailSource = SpatialAudio.Apply(ChildSource(_weaponSource.transform, "TailAudio", _weaponSource), SpatialAudio.Category.Gunshot);
                _tailMuffle = _tailSource.gameObject.AddComponent<AudioLowPassFilter>();
            }
            if (_bodySource != null)
            {
                _stepSource = SpatialAudio.Apply(ChildSource(_bodySource.transform, "StepAudio", _bodySource), SpatialAudio.Category.Body);
                _stepRangeApplied = SpatialAudio.For(SpatialAudio.Category.Body).Max;
                _stepMuffle = _stepSource.gameObject.AddComponent<AudioLowPassFilter>();
                _whizSource = SpatialAudio.Apply(ChildSource(transform, "WhizAudio", _bodySource), SpatialAudio.Category.Body);
                _impactSource = SpatialAudio.Apply(ChildSource(transform, "ImpactAudio", _bodySource), SpatialAudio.Category.Impact);
            }
            _healing = GetComponent<CombatantHealing>();
            _swim = GetComponent<CombatantSwim>();
            _combatant.ShotFired += OnShot;
            _combatant.ShotResolved += OnShotResolved;
            _animation.GestureStarted += OnGesture;
            _combatant.Punched += OnPunch;
            _animation.FootDown += OnFootDown;
            _animation.Landed += OnLanded;
        }

        void OnDestroy()
        {
            _combatant.ShotFired -= OnShot;
            _combatant.ShotResolved -= OnShotResolved;
            _animation.GestureStarted -= OnGesture;
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

        // Every footstep anyone takes, with the ground it landed on (GroundFx leaves prints from it, #205).
        public static event Action<Transform, FootSurface> StepTaken;

        bool PlayFootstep(float volume)
        {
            var id = SurfaceIdAt(transform.position);
            var surface = SurfaceRules.ToFoot(id);
            StepTaken?.Invoke(transform, surface);
            var clips = StepClips(id, out float clipGain);
            if (clips.Length == 0) { clips = _footsteps; clipGain = 1f; }
            if (clips.Length == 0) return false;
            // A lowered stance or a slow walk treads more quietly, a sprint louder; the ground rings or muffles.
            var loco = GameData.Rules.Locomotion;
            var v = _motion != null ? _motion.LocalVelocity : Vector3.zero;
            bool sprinting = SoundRules.IsSprint(new Vector2(v.x, v.z).magnitude, loco.WalkSpeed, loco.SprintSpeed, _stance != null ? _stance.MoveFactor : 1f);
            float loudness = SoundRules.StepLoudness(_stance != null ? _stance.FootstepVolumeFactor : 1f, sprinting, SurfaceSounds.Loudness(id));
            float range = SoundRules.StepRange(loudness);
            if (_stepSource != null && Mathf.Abs(range - _stepRangeApplied) > 0.5f)
            {
                SpatialAudio.Apply(_stepSource, new SpatialAudio.Range(Mathf.Min(2f, range * 0.5f), range));
                _stepRangeApplied = range;
            }
            // Floors and walls between this step and the ear (ADR-251): quieter, and the highs go.
            var occ = OcclusionToListener(transform.position, id == SurfaceId.MetalStairs);
            if (_stepMuffle != null) _stepMuffle.cutoffFrequency = occ.CutoffHz;
            volume *= loudness * clipGain * occ.Gain;
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
            // Closed doors between the shot and the listener muffle it (G11, ADR-104).
            if (SpatialAudio.ListenerPosition(out var ear))
                heard = Doors.HeardMeters(heard, DoorSystem.ClosedBetween(_weaponSource.transform.position, ear), DoorRules.Current);
            SoundRules.ShotLayers(heard, out float near, out float far);
            // Suppressed: as quiet as a shot from `heard` metres (the source's rolloff already applies `meters`).
            float scale = Suppressed || heard > meters ? Mathf.Clamp01(SpatialAudio.Gain(SpatialAudio.Category.Gunshot, heard)
                / Mathf.Max(1e-4f, SpatialAudio.Gain(SpatialAudio.Category.Gunshot, meters))) : 1f;
            float cutoff = SpatialAudio.DistantCutoff(heard);
            var occ = OcclusionToListener(_weaponSource.transform.position, false);
            scale *= occ.Gain;
            cutoff = Mathf.Min(cutoff, occ.CutoffHz);
            SpatialAudio.NoteLoud(Mathf.Clamp01(near * scale)); // ambient beds duck under gunfire (#194)
            bool played = false;
            // Recorded gun sounds by weapon class (ADR-250); weapons without an entry, or a build without the clips,
            // keep the older per-weapon clips.
            bool hasSound = WeaponSounds.TryGet(weapon, out var gun);
            var nearSet = hasSound ? GunSet(Suppressed ? WeaponSounds.SuppressedNear : WeaponSounds.NearPrefix(gun.Class)) : NoClips;
            var farSet = hasSound && !Suppressed ? GunSet(WeaponSounds.FarPrefix(gun.Class)) : NoClips;
            // Each layer plays one of its takes: every clip as cut, then pitched and filtered (#287, at least three).
            _nearTake = WeaponSounds.NextVariant(nearSet.Length, _nearTake, Random.value);
            var nv = WeaponSounds.VariantOf(nearSet.Length, _nearTake);
            var nearClip = nearSet.Length > 0 ? nearSet[nv.Clip] : ClipFor(_shotByWeapon, weapon);
            float nearPitch = nearSet.Length > 0 ? gun.Pitch : 1f;
            float loud = nearSet.Length > 0 ? gun.Loudness : 1f;
            if (_distantShot != null) _distantShot.cutoffFrequency = Mathf.Min(cutoff, nv.CutoffHz);
            if (near > 0f && Play(_weaponSource, nearClip, _shotVolume * loud * near * scale, nearPitch * nv.Pitch))
            {
                NearShotsPlayed++;
                played = true;
            }
            _farTake = WeaponSounds.NextVariant(farSet.Length, _farTake, Random.value);
            var fv = WeaponSounds.VariantOf(farSet.Length, _farTake);
            var farClip = farSet.Length > 0 ? farSet[fv.Clip] : ClipFor(FarShotClips, weapon);
            if (_farMuffle != null) _farMuffle.cutoffFrequency = Mathf.Min(cutoff, fv.CutoffHz);
            if (far > 0f && Play(_farSource, farClip, _shotVolume * loud * far * scale, farSet.Length > 0 ? nearPitch * fv.Pitch : 1f))
            {
                FarShotsPlayed++;
                played = true;
            }
            // Third layer: the room's tail when the shooter is under a roof (the ambient beds' roof rule, #287), through the
            // same distance and occlusion filter as the far layer.
            float tail = hasSound ? WeaponSounds.TailLevel(ShooterIndoors(), near, far) : 0f;
            var tailSet = tail > 0f ? GunSet(WeaponSounds.IndoorTail) : NoClips;
            if (tailSet.Length > 0)
            {
                _tailTake = WeaponSounds.NextVariant(tailSet.Length, _tailTake, Random.value);
                var tv = WeaponSounds.VariantOf(tailSet.Length, _tailTake);
                if (_tailMuffle != null) _tailMuffle.cutoffFrequency = Mathf.Min(cutoff, tv.CutoffHz);
                if (Play(_tailSource, tailSet[tv.Clip], _shotVolume * _tailVolume * loud * tail * scale, nearPitch * tv.Pitch)) TailsPlayed++;
            }
            if (played) ShotsPlayed++;
        }

        // ---- Clips and ground ----

        // By weapon id: none, VK-9, Ordo, Warden, Breacher, Sentinel.
        static readonly string[] FarShotNames = { null, "pistol", "smg", "rifle", "shotgun", "sniper" };
        static AudioClip[] _farShotClips, _whizClips;
        static readonly Dictionary<SurfaceId, AudioClip[]> StepSets = new Dictionary<SurfaceId, AudioClip[]>();

        static readonly AudioClip[] NoClips = new AudioClip[0];
        static readonly Dictionary<string, AudioClip[]> GunSets = new Dictionary<string, AudioClip[]>();

        // Resources/Shots clips whose name starts with prefix (all packs share the folder name).
        static AudioClip[] GunSet(string prefix)
        {
            if (!GunSets.TryGetValue(prefix, out var set))
                GunSets[prefix] = set = Array.FindAll(Resources.LoadAll<AudioClip>("Shots"), c => c.name.StartsWith(prefix, StringComparison.Ordinal));
            return set;
        }

        static AudioClip[] FarShotClips => _farShotClips ??= Array.ConvertAll(FarShotNames, n => n == null ? null : Resources.Load<AudioClip>("Shots/shot_" + n + "_far"));
        static AudioClip[] WhizClips => _whizClips ??= Array.FindAll(Resources.LoadAll<AudioClip>("Shots"), c => c.name.StartsWith("whiz_", StringComparison.Ordinal));

        public static AudioClip[] StepClips(FootSurface surface) => StepClips(SurfaceRules.FromFoot(surface), out _);

        // The step clips of a surface (ADR-183): its own Resources/Steps/<folder> clips named step_*, or the stand-in
        // surface's when it has none (Carpet plays Wood quieter). Older packs share the folders; their names don't start
        // with "step_", so they are skipped until those files are removed.
        public static AudioClip[] StepClips(SurfaceId id, out float gain)
        {
            var e = SurfaceSounds.Of(id);
            string folder = SurfaceSounds.StepFolder(id, f => LoadStepFolder(f).Length > 0);
            gain = folder == "Steps/" + e.Folder ? 1f : e.FallbackGain;
            if (!StepSets.TryGetValue(id, out var clips)) StepSets[id] = clips = LoadStepFolder(folder);
            return clips;
        }

        static AudioClip[] LoadStepFolder(string folder)
        {
            var clips = Array.FindAll(Resources.LoadAll<AudioClip>(folder), c => c.name.StartsWith("step_", StringComparison.Ordinal));
            Array.Sort(clips, (a, b) => string.CompareOrdinal(a.name, b.name));
            return clips;
        }

        // What the ground under `feet` sounds like for bots and ground prints (the six older kinds); SurfaceIdAt has all 18.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RegisterBotEars() => BotHearing.SurfaceAt = (x, y, z) => SurfaceAt(new Vector3(x, y, z));

        public static FootSurface SurfaceAt(Vector3 feet) => SurfaceRules.ToFoot(SurfaceIdAt(feet));

        static bool _warnedUntagged;

        // The surface under `feet` as a SurfaceId: paved road strips have no collider (PavedRoads) and are Asphalt, terrain
        // reads its dominant layer, water is anything below the sea surface, vehicles and the elevator car are metal, and
        // everything else reads the SurfaceIdTag WorldBuilder put on its colliders. No names are parsed (ADR-183). A
        // collider without a tag is concrete, with one warning in development builds so it cannot go unnoticed.
        public static SurfaceId SurfaceIdAt(Vector3 feet)
        {
            if (!Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out var hit, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return SurfaceId.Concrete;
            var world = WorldInfo.Current;
            bool underWater = world != null && hit.point.y < world.SeaLevel;
            if (hit.collider is TerrainCollider terrain)
                return !underWater && PavedRoads.Contains(hit.point) ? SurfaceId.Asphalt : SoundRules.TerrainSurfaceId(LayerAt(terrain, hit.point), underWater);
            if (underWater) return SurfaceId.Water;
            if (hit.collider.GetComponentInParent<Vehicle>() != null || hit.collider.GetComponentInParent<ElevatorCar>() != null) return SurfaceId.Metal;
            var tag = hit.collider.GetComponentInParent<SurfaceIdTag>();
            if (tag != null) return tag.Id;
            if (!_warnedUntagged && Debug.isDebugBuild)
            {
                _warnedUntagged = true;
                Debug.LogWarning($"[CombatantAudio] collider '{hit.collider.name}' has no SurfaceIdTag; footsteps fall back to concrete (docs/research/12_surface_tag_design.md)");
            }
            return SurfaceId.Concrete;
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
            bool landed = punch.Victim != null;
            // A miss is the recorded swing, quicker and higher than a weapon's (#288); older builds keep the pitched hit clip.
            var swing = SfxSet(WeaponSounds.MeleeSwing);
            var clip = landed || swing.Length == 0 ? _hits[Random.Range(0, _hits.Length)] : swing[Random.Range(0, swing.Length)];
            float pitch = landed ? 1f : swing.Length > 0 ? 1.3f : _punchSwingPitch;
            if (Play(_bodySource, clip, landed ? _punchImpactVolume : _punchSwingVolume, pitch)) PunchesPlayed++;
        }

        // Reloads play on the body source: heard as far as footsteps, not as far as shots.
        void Update()
        {
            // ADR-102: a suppressor on the weapon in hand makes this combatant's shots sound further away.
            if (_gear == null) _gear = GetComponent<CombatantOptics>();
            Suppressed = _gear != null && _gear.ActiveMods.Suppressed;
            bool reloading = _combatant.Reloading.Value;
            if (reloading && !_wasReloading && Play(_bodySource, ReloadClip(_combatant.ActiveWeaponId), _reloadVolume))
                ReloadsPlayed++;
            _wasReloading = reloading;

            float hp = _combatant.Hp.Value;
            if (hp < _lastHp && _hits.Length > 0 && Play(_bodySource, _hits[Random.Range(0, _hits.Length)], _hitVolume)) HitsPlayed++;
            // A real wound draws a grunt from the one hit (#288), not every tick of gas or bleeding.
            if (_lastHp - hp >= MinGruntDamage && hp > 0f && Time.time - _lastGruntAt >= GruntCooldown)
            {
                var grunts = SfxSet(WeaponSounds.PainGrunt);
                if (grunts.Length > 0 && Play(_bodySource, grunts[Random.Range(0, grunts.Length)], _gruntVolume)) { GruntsPlayed++; _lastGruntAt = Time.time; }
            }
            _lastHp = hp;

            // Healing starts with the bandage tearing open (replicated use, so everyone near hears it).
            bool healing = _healing != null && _healing.Using;
            if (healing && !_wasHealing && PlayOne(_bodySource, WeaponSounds.HealStart, _healVolume)) HealsPlayed++;
            _wasHealing = healing;

            // Swimming: strokes while moving through the water, the first one at once on getting in.
            bool swimming = _swim != null && _swim.InWater && !_combatant.Dead.Value;
            if (swimming)
            {
                var v = _motion != null ? _motion.LocalVelocity : Vector3.zero;
                bool moving = new Vector2(v.x, v.z).magnitude > 0.5f;
                if ((!_wasSwimming || moving) && Time.time >= _nextStrokeAt && PlayOne(_stepSource != null ? _stepSource : _bodySource, WeaponSounds.SwimStroke, _swimVolume))
                {
                    SwimStrokesPlayed++;
                    _nextStrokeAt = Time.time + StrokeSeconds * Random.Range(0.9f, 1.1f);
                }
            }
            _wasSwimming = swimming;
        }

        // The click of a trigger on an empty chamber (#288): the local player's own fire press with nothing in the magazine.
        public void PlayDryFire()
        {
            var set = GunSet(WeaponSounds.DryFire);
            if (set.Length > 0 && Play(_bodySource, set[Random.Range(0, set.Length)], _dryFireVolume)) DryFiresPlayed++;
        }

        // The rummage of a hand going into loot (own view: the pickup gesture is the local player's).
        void OnGesture(HandGesture g)
        {
            if (g == HandGesture.Pickup && PlayOne(_bodySource, WeaponSounds.Pickup, _pickupVolume)) PickupsPlayed++;
        }

        // A miss that hits something hard may whine off it (#288), heard from where it struck.
        void OnShotResolved(in Combatant.ShotResult shot)
        {
            if (shot.Phase == Combatant.ShotPhase.Launch || shot.Victim != null || shot.Hit == ShotHit.None || _impactSource == null) return;
            float chance = CasingRules.RicochetChance(shot.Surface);
            if (chance <= 0f || Time.timeAsDouble - _lastRicochet < PelletWindow) return; // one roll per shot (a shotgun's pellets)
            _lastRicochet = Time.timeAsDouble;
            if (Random.value >= chance || SpatialAudio.DistanceToListener(shot.End) > SpatialAudio.For(SpatialAudio.Category.Impact).Max) return;
            var set = GunSet(WeaponSounds.Ricochet);
            if (set.Length == 0) return;
            _ricochetTake = WeaponSounds.NextVariant(set.Length, _ricochetTake, Random.value);
            var take = WeaponSounds.VariantOf(set.Length, _ricochetTake);
            _impactSource.transform.position = shot.End;
            if (Play(_impactSource, set[take.Clip], _ricochetVolume, take.Pitch)) RicochetsPlayed++;
        }

        // Whether this combatant stands under a roof, re-checked at most every IndoorRefreshSeconds (five rays).
        bool ShooterIndoors()
        {
            if (Time.time - _indoorsAt < IndoorRefreshSeconds) return _indoors;
            _indoorsAt = Time.time;
            return _indoors = AmbientBeds.UnderRoof(transform.position + Vector3.up * 1.6f);
        }

        // One of the Resources/Sfx clips starting with `prefix`, at random.
        bool PlayOne(AudioSource source, string prefix, float volume)
        {
            var set = SfxSet(prefix);
            return set.Length > 0 && Play(source, set[Random.Range(0, set.Length)], volume);
        }

        static readonly Dictionary<string, AudioClip[]> SfxSets = new Dictionary<string, AudioClip[]>();

        static AudioClip[] SfxSet(string prefix)
        {
            if (!SfxSets.TryGetValue(prefix, out var set))
            {
                SfxSets[prefix] = set = Array.FindAll(Resources.LoadAll<AudioClip>("Sfx"), c => c.name.StartsWith(prefix, StringComparison.Ordinal));
                Array.Sort(set, (a, b) => string.CompareOrdinal(a.name, b.name));
            }
            return set;
        }

        // The Resources/Shots clips starting with `prefix` (spent cases play WeaponSounds.Casing from here).
        public static AudioClip[] Clips(string prefix) => GunSet(prefix);

        // Clip lists by prefix for the tests (Resources/Shots then Resources/Sfx).
        public static int ClipCount(string prefix) => GunSet(prefix).Length + SfxSet(prefix).Length;

        // Occlusion of this combatant's sounds toward the ear, probed at most every SoundOcclusion.RefreshSeconds and
        // only within ProbeRange (ADR-251); the frame's ray allowance is shared by everyone.
        Occlusion OcclusionToListener(Vector3 position, bool throughStairs)
        {
            if (!SpatialAudio.ListenerPosition(out var ear) || !SoundOcclusion.ShouldProbe(Vector3.Distance(position, ear))) return Occlusion.Open;
            float now = Time.unscaledTime;
            if (now - _occAt < SoundOcclusion.RefreshSeconds || !SoundOcclusionProbe.Budget.TryTake(Time.frameCount)) return _occ;
            _occAt = now;
            return _occ = SoundOcclusionProbe.Between(position + Vector3.up * 1.2f, ear, throughStairs);
        }

        // The class's recorded reload (ADR-250), else the per-weapon clip from the prefab.
        AudioClip ReloadClip(int weaponId)
        {
            if (WeaponSounds.TryGet(weaponId, out var gun))
            {
                var set = GunSet(WeaponSounds.ReloadName(gun.Class));
                if (set.Length > 0) return set[0];
            }
            return ClipFor(_reloadByWeapon, weaponId);
        }

        static AudioClip ClipFor(AudioClip[] byWeapon, int weaponId) =>
            weaponId >= 0 && weaponId < byWeapon.Length ? byWeapon[weaponId] : null;

        static bool Play(AudioSource source, AudioClip clip, float volume, float pitch = 1f)
        {
            if (source == null || clip == null) return false;
            source.pitch = pitch + Random.Range(-PitchJitter, PitchJitter);
            source.PlayOneShot(clip, volume * GameSettings.EffectsVolume);
            return true;
        }
    }
}
