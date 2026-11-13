using FishNet.Object;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // The owner's side of throwing (hold G: preview arc, let go: throw) and everyone's view of this combatant's
    // grenades going off: a flash, a short light and a puff of dust and smoke, all pooled. No debris, no gore.
    // The selected throwable (frag or smoke) is thrown the same way; a smoke grenade leaves a cloud of pooled
    // camera-facing discs that lasts as long as the server's cloud does.
    [RequireComponent(typeof(Combatant), typeof(ThrowableUser))]
    public class ThrowableView : MonoBehaviour
    {
        const int BlastPool = 2;  // a combatant's own blasts never overlap more than this (one throw a second, short effect)
        const int PuffsPerBlast = 5;
        const int LowEffectsPuffs = 2;  // blast puffs shown with the effects setting on low
        const float FlashSeconds = 0.12f;
        const float LightSeconds = 0.25f;
        const float SmokeSeconds = 1.6f;
        const float FlashSize = 5f;
        const float PuffSize = 1.8f;
        const float PuffGrowth = 2.6f;   // times its first size by the end
        const float PuffSpread = 1.3f;   // metres the puffs sit away from the centre
        const float PuffRiseSpeed = 0.9f;
        const float BlastLift = 0.4f;    // the effect sits this far above the grenade so the floor does not cut it in half
        const float LandingMarkSize = 0.5f;
        const float SurfaceOffset = 0.03f;

        const int CloudPool = 3;          // clouds shown at once per thrower (carry limit 3; the oldest makes room)
        const int CloudPuffs = 12;
        const float CloudGrowSeconds = 1.5f;
        const float CloudFadeSeconds = 5f; // PUBG: thinner for the last 5 s (ADR-101)
        const float CloudSpread = 0.6f;   // of the radius: how far from the centre the puffs sit
        const float CloudPuffMin = 0.55f; // puff size as a share of the radius
        const float CloudPuffMax = 0.8f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Materials are assets made by the builder: shaders cannot be looked up in a player build.
        [SerializeField] Material _glowMaterial; // additive: blast flash, preview arc, landing mark
        public Material GlowMaterial => _glowMaterial; // shared with VehicleDamageVisual (#90)
        public Material DustMaterial => _dustMaterial;
        [SerializeField] Material _dustMaterial; // alpha blended: smoke puffs
        [SerializeField] Material _smokeMaterial; // alpha blended: smoke cloud puffs; falls back to _dustMaterial
        [SerializeField] Color _cloudColor = new Color(0.8f, 0.81f, 0.82f, 0.32f);
        [SerializeField] Color _smokeColor = new Color(0.42f, 0.4f, 0.37f, 0.7f);
        [SerializeField] Color _lightColor = new Color(1f, 0.75f, 0.45f);
        [SerializeField] float _lightRange = 14f;
        [SerializeField] float _lightIntensity = 8f;
        [SerializeField] float _arcWidth = 0.035f;
        [SerializeField] AudioSource _blastSource; // 3D; moved to where the blast is
        [SerializeField] AudioClip _blastClip;
        [SerializeField] float _blastPitch = 1f;
        [SerializeField] float _blastVolume = 1f;

        Combatant _combatant;
        ThrowableUser _user;
        Transform _fxRoot; // effects that stay where they happened instead of following the combatant
        MaterialPropertyBlock _block;
        bool _built, _warned;

        LineRenderer _arc;
        Vector3[] _arcPoints;
        MeshRenderer _landing;
        bool _aiming;

        Transform[] _blasts;
        MeshRenderer[] _flash;
        Light[] _light;
        MeshRenderer[] _puffs;      // BlastPool x PuffsPerBlast
        Vector3[] _puffOffset;
        Vector3[] _blastCentre;
        float[] _blastStart;
        bool[] _blastOn;
        int _nextBlast;

        MeshRenderer[] _cloudPuffs; // CloudPool x CloudPuffs
        Vector3[] _cloudOffset;
        float[] _cloudSize;
        Vector3[] _cloudCentre;
        float[] _cloudStart, _cloudSeconds;
        bool[] _cloudOn;
        int _nextCloud;

        // Counters and state so tests can tell what was shown without looking.
        public int BlastsShown { get; private set; }
        public int BlastSoundsPlayed { get; private set; }
        public int ThrowsRequested { get; private set; }
        // Holding the throw key (pin in or not): R and the reload key are then the cook key (ADR-101).
        public bool Aiming => _aiming;
        public bool PreviewVisible => _arc != null && _arc.enabled;
        public int PreviewPointCount => PreviewVisible ? _arc.positionCount : 0;
        public Vector3 LastBlastCentre { get; private set; }
        public int SmokesShown { get; private set; }
        public Vector3 LastSmokeCentre { get; private set; }
        public int ActiveClouds
        {
            get
            {
                int n = 0;
                if (_cloudOn != null) foreach (bool on in _cloudOn) if (on) n++;
                return n;
            }
        }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _user = GetComponent<ThrowableUser>();
            _block = new MaterialPropertyBlock();
            SpatialAudio.Apply(_blastSource, SpatialAudio.Category.Explosion);
            ThrownItem.ClientExploded += OnExploded;
            ThrownItem.ClientSmoked += OnSmoked;
            ThrownItem.ClientFlashed += OnFlashedBurst;
            Build();
        }

        // Without its materials the view shows nothing (and says so once) instead of drawing pink shapes.
        void Build()
        {
            if (_glowMaterial == null || _dustMaterial == null)
            {
                if (!_warned) Debug.LogWarning($"[ThrowableView] {name}: effect materials are not assigned; the throw preview and blast effects are off");
                _warned = true;
                return;
            }
            _fxRoot = new GameObject("ThrowFx").transform;

            var arc = new GameObject("ThrowArc");
            arc.transform.SetParent(_fxRoot, false);
            _arc = arc.AddComponent<LineRenderer>();
            _arc.sharedMaterial = _glowMaterial;
            _arc.useWorldSpace = true;
            _arc.widthMultiplier = _arcWidth;
            // Fades out toward the far end.
            _arc.startColor = Color.white;
            _arc.endColor = new Color(1f, 1f, 1f, 0.25f);
            _arc.shadowCastingMode = ShadowCastingMode.Off;
            _arc.receiveShadows = false;
            _arc.enabled = false;
            _arcPoints = new Vector3[Throwables.PreviewPoints(ThrowableRules.Current)];
            _landing = NewRenderer("Landing", _fxRoot, _glowMaterial);
            _landing.transform.localScale = Vector3.one * LandingMarkSize;

            _blasts = new Transform[BlastPool];
            _flash = new MeshRenderer[BlastPool];
            _light = new Light[BlastPool];
            _puffs = new MeshRenderer[BlastPool * PuffsPerBlast];
            _puffOffset = new Vector3[BlastPool * PuffsPerBlast];
            _blastCentre = new Vector3[BlastPool];
            _blastStart = new float[BlastPool];
            _blastOn = new bool[BlastPool];
            for (int i = 0; i < BlastPool; i++)
            {
                var root = new GameObject("Blast").transform;
                root.SetParent(_fxRoot, false);
                _blasts[i] = root;
                for (int p = 0; p < PuffsPerBlast; p++) _puffs[i * PuffsPerBlast + p] = NewRenderer("Puff", root, _dustMaterial);
                _flash[i] = NewRenderer("Flash", root, _glowMaterial);
                var light = root.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = _lightColor;
                light.range = _lightRange;
                light.shadows = LightShadows.None;
                light.enabled = false;
                _light[i] = light;
            }

            var cloudMaterial = _smokeMaterial != null ? _smokeMaterial : _dustMaterial;
            _cloudPuffs = new MeshRenderer[CloudPool * CloudPuffs];
            _cloudOffset = new Vector3[CloudPool * CloudPuffs];
            _cloudSize = new float[CloudPool * CloudPuffs];
            _cloudCentre = new Vector3[CloudPool];
            _cloudStart = new float[CloudPool];
            _cloudSeconds = new float[CloudPool];
            _cloudOn = new bool[CloudPool];
            for (int i = 0; i < _cloudPuffs.Length; i++) _cloudPuffs[i] = NewRenderer("CloudPuff", _fxRoot, cloudMaterial);

            // The sound plays where the blast is, not where the thrower stands.
            if (_blastSource != null) _blastSource.transform.SetParent(_fxRoot, true);
            _built = true;
        }

        static MeshRenderer NewRenderer(string name, Transform parent, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = CombatFxMeshes.Disc;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
            return renderer;
        }

        void OnDestroy()
        {
            ThrownItem.ClientExploded -= OnExploded;
            ThrownItem.ClientSmoked -= OnSmoked;
            ThrownItem.ClientFlashed -= OnFlashedBurst;
            if (_fxRoot != null) Destroy(_fxRoot.gameObject);
        }

        // Every view hears about every grenade; each shows its own combatant's. One whose thrower has left the
        // match is shown by the local player's view, so it never goes off unseen.
        void OnExploded(Vector3 centre, NetworkObject thrower)
        {
            bool mine = thrower != null ? thrower.gameObject == gameObject : _combatant == Combatant.LocalPlayer;
            if (mine) ShowBlast(centre);
        }

        // The burst of a flash grenade: a white flash and a light, no smoke, a high short bang.
        void OnFlashedBurst(Vector3 centre, NetworkObject thrower)
        {
            bool mine = thrower != null ? thrower.gameObject == gameObject : _combatant == Combatant.LocalPlayer;
            if (mine) ShowBlast(centre, true);
        }

        // Same rule as for blasts: each view shows its own combatant's clouds.
        void OnSmoked(Vector3 centre, float radius, float seconds, NetworkObject thrower)
        {
            bool mine = thrower != null ? thrower.gameObject == gameObject : _combatant == Combatant.LocalPlayer;
            if (mine) ShowCloud(centre, radius, seconds);
        }

        // ---- Owner: aiming and throwing ----

        void Update()
        {
            var player = PlayerController.Local;
            if (player == null || player.gameObject != gameObject)
            {
                if (_aiming) StopAiming();
                return;
            }
            var input = player.LastInput;
            bool able = !_combatant.Dead.Value && _user.SelectedCount > 0;
            if (input.ThrowPressed && able) _aiming = true;
            if (!_aiming) return;
            // Dead, nothing left to throw, or a screen took the keys (the input is then empty): the throw is off.
            if (!able || (!input.ThrowHeld && !input.ThrowReleased))
            {
                StopAiming();
                return;
            }

            // Along where the character looks, like its shots. The aim button held turns the throw into a roll.
            var direction = Quaternion.Euler(-player.Pitch, player.Yaw, 0f) * Vector3.forward;
            bool roll = input.AimHeld;
            if (input.Reload) _user.RequestCook();
            if (input.ThrowReleased)
            {
                StopAiming();
                ThrowsRequested++;
                // The host is the server: throw now instead of waiting for the next network tick.
                if (_user.IsServerInitialized) _user.ServerThrowAimed(direction, _user.SelectedKind, roll);
                else _user.CmdThrowRoll(direction, roll);
                return;
            }
            var rules = ThrowableRules.Current;
            if (roll)
            {
                Throwables.RollVelocity(direction.x, direction.y, direction.z, rules, out float rx, out float rz);
                ShowArc(transform.position + Vector3.up * rules.RollHandHeight, new Vector3(rx, 0f, rz));
            }
            else ShowArc(_user.ThrowOrigin(direction), ThrowableUser.ThrowVelocity(direction, rules));
        }

        void StopAiming()
        {
            _aiming = false;
            if (!_built) return;
            _arc.enabled = false;
            _landing.enabled = false;
        }

        // The free flight up to the first thing in its way. Bounces are not predicted.
        void ShowArc(Vector3 origin, Vector3 velocity)
        {
            if (!_built) return;
            var rules = ThrowableRules.Current;
            int count = 1;
            bool landed = false;
            var normal = Vector3.up;
            _arcPoints[0] = origin;
            for (int i = 1; i < _arcPoints.Length; i++)
            {
                Throwables.PositionAt(origin.x, origin.y, origin.z, velocity.x, velocity.y, velocity.z, rules.Gravity,
                    i * rules.PreviewStepSeconds, out float x, out float y, out float z);
                var from = _arcPoints[i - 1];
                var to = new Vector3(x, y, z);
                var step = to - from;
                float length = step.magnitude;
                if (length > 1e-5f && Physics.SphereCast(from, rules.Radius, step / length, out var hit, length,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
                {
                    _arcPoints[count++] = from + step / length * hit.distance;
                    normal = hit.normal;
                    landed = true;
                    break;
                }
                _arcPoints[count++] = to;
            }
            _arc.positionCount = count;
            _arc.SetPositions(_arcPoints);
            _arc.enabled = true;

            _landing.enabled = landed;
            if (landed)
                _landing.transform.SetPositionAndRotation(_arcPoints[count - 1] + normal * SurfaceOffset, Quaternion.LookRotation(normal));
        }

        // ---- Everyone: the blast ----

        void ShowBlast(Vector3 centre, bool flash = false)
        {
            LastBlastCentre = centre;
            BlastsShown++;
            if (_blastSource != null && _blastClip != null)
            {
                _blastSource.transform.position = centre;
                _blastSource.pitch = flash ? _blastPitch * 1.8f : _blastPitch;
                _blastSource.PlayOneShot(_blastClip, _blastVolume * GameSettings.EffectsVolume * (flash ? 0.6f : 1f));
                BlastSoundsPlayed++;
            }
            if (!_built) return;
            int i = _nextBlast;
            _nextBlast = (_nextBlast + 1) % BlastPool;
            _blastCentre[i] = centre + Vector3.up * BlastLift;
            _blastStart[i] = Time.time;
            _blastOn[i] = true;
            _flash[i].enabled = true;
            _light[i].enabled = true;
            _light[i].color = flash ? Color.white : _lightColor;
            _light[i].intensity = flash ? _lightIntensity * 3f : _lightIntensity;
            for (int p = 0; p < PuffsPerBlast; p++)
            {
                int index = i * PuffsPerBlast + p;
                // A different cloud every time; never below the grenade, where the floor usually is.
                var offset = Random.insideUnitSphere * PuffSpread;
                offset.y = Mathf.Abs(offset.y);
                _puffOffset[index] = offset;
                _puffs[index].enabled = !flash && (GameSettings.HighEffects || p < LowEffectsPuffs);
            }
            PlaceBlast(i, ViewCamera());
        }

        // ---- Everyone: the smoke cloud ----

        void ShowCloud(Vector3 centre, float radius, float seconds)
        {
            LastSmokeCentre = centre;
            SmokesShown++;
            if (!_built) return;
            int i = _nextCloud;
            _nextCloud = (_nextCloud + 1) % CloudPool;
            _cloudCentre[i] = centre + Vector3.up * SmokeRules.Current.CentreLiftMeters;
            _cloudStart[i] = Time.time;
            _cloudSeconds[i] = seconds;
            _cloudOn[i] = true;
            for (int p = 0; p < CloudPuffs; p++)
            {
                int index = i * CloudPuffs + p;
                // A different cloud every time; the lower half is flattened where the floor is.
                var offset = Random.insideUnitSphere * (radius * CloudSpread);
                offset.y *= offset.y < 0f ? 0.4f : 0.8f;
                _cloudOffset[index] = offset;
                _cloudSize[index] = radius * Random.Range(CloudPuffMin, CloudPuffMax);
                _cloudPuffs[index].enabled = true;
            }
            PlaceCloud(i, ViewCamera());
        }

        // Returns false when the cloud has run its course.
        bool PlaceCloud(int i, Transform camera)
        {
            float age = Time.time - _cloudStart[i];
            float seconds = _cloudSeconds[i];
            if (age >= seconds) return false;
            var rotation = camera != null ? camera.rotation : Quaternion.identity;
            float grow = Mathf.Clamp01(age / CloudGrowSeconds);
            grow = 1f - (1f - grow) * (1f - grow);
            var color = _cloudColor;
            color.a *= Mathf.Clamp01((seconds - age) / CloudFadeSeconds);
            _block.SetColor(BaseColorId, color);
            for (int p = 0; p < CloudPuffs; p++)
            {
                int index = i * CloudPuffs + p;
                var puff = _cloudPuffs[index];
                puff.transform.SetPositionAndRotation(_cloudCentre[i] + _cloudOffset[index] * grow, rotation);
                puff.transform.localScale = Vector3.one * (_cloudSize[index] * Mathf.Lerp(0.5f, 1f, grow));
                puff.SetPropertyBlock(_block);
            }
            return true;
        }

        // Returns false when the blast has run its course.
        bool PlaceBlast(int i, Transform camera)
        {
            float age = Time.time - _blastStart[i];
            if (age >= SmokeSeconds) return false;
            // The discs face the camera.
            var rotation = camera != null ? camera.rotation : Quaternion.identity;
            _blasts[i].position = _blastCentre[i];

            if (age < FlashSeconds)
            {
                _flash[i].transform.rotation = rotation;
                _flash[i].transform.localScale = Vector3.one * (FlashSize * Mathf.Lerp(0.5f, 1f, age / FlashSeconds));
            }
            else if (_flash[i].enabled) _flash[i].enabled = false;

            if (age < LightSeconds) _light[i].intensity = _lightIntensity * (1f - age / LightSeconds);
            else if (_light[i].enabled) _light[i].enabled = false;

            float k = age / SmokeSeconds;
            // Out fast, then slowly on; fading all the way.
            float size = PuffSize * Mathf.Lerp(1f, PuffGrowth, 1f - (1f - k) * (1f - k));
            var color = _smokeColor;
            color.a *= 1f - k;
            _block.SetColor(BaseColorId, color);
            for (int p = 0; p < PuffsPerBlast; p++)
            {
                int index = i * PuffsPerBlast + p;
                var puff = _puffs[index].transform;
                puff.SetPositionAndRotation(_blastCentre[i] + _puffOffset[index] + Vector3.up * (PuffRiseSpeed * age), rotation);
                puff.localScale = Vector3.one * size;
                _puffs[index].SetPropertyBlock(_block);
            }
            return true;
        }

        static Transform ViewCamera()
        {
            var player = PlayerController.Local;
            return player != null && player.Camera != null ? player.Camera.transform : null;
        }

        // After movement and the camera have been placed for this frame.
        void LateUpdate()
        {
            if (!_built) return;
            var camera = ViewCamera();
            for (int i = 0; i < CloudPool; i++)
            {
                if (!_cloudOn[i] || PlaceCloud(i, camera)) continue;
                _cloudOn[i] = false;
                for (int p = 0; p < CloudPuffs; p++) _cloudPuffs[i * CloudPuffs + p].enabled = false;
            }
            for (int i = 0; i < BlastPool; i++)
            {
                if (!_blastOn[i] || PlaceBlast(i, camera)) continue;
                _blastOn[i] = false;
                _flash[i].enabled = false;
                _light[i].enabled = false;
                for (int p = 0; p < PuffsPerBlast; p++) _puffs[i * PuffsPerBlast + p].enabled = false;
            }
        }
    }
}
