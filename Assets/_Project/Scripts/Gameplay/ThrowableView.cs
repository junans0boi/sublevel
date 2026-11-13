using FishNet.Object;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // The owner's side of throwing (hold G: preview arc, let go: throw) and everyone's view of this combatant's
    // grenades going off: a flash, a short light and a puff of dust and smoke, all pooled. No debris, no gore.
    [RequireComponent(typeof(Combatant), typeof(ThrowableUser))]
    public class ThrowableView : MonoBehaviour
    {
        const int BlastPool = 2;  // a combatant's own blasts never overlap more than this (one throw a second, short effect)
        const int PuffsPerBlast = 5;
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

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Materials are assets made by the builder: shaders cannot be looked up in a player build.
        [SerializeField] Material _glowMaterial; // additive: blast flash, preview arc, landing mark
        [SerializeField] Material _dustMaterial; // alpha blended: smoke puffs
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

        // Counters and state so tests can tell what was shown without looking.
        public int BlastsShown { get; private set; }
        public int BlastSoundsPlayed { get; private set; }
        public int ThrowsRequested { get; private set; }
        public bool PreviewVisible => _arc != null && _arc.enabled;
        public int PreviewPointCount => PreviewVisible ? _arc.positionCount : 0;
        public Vector3 LastBlastCentre { get; private set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _user = GetComponent<ThrowableUser>();
            _block = new MaterialPropertyBlock();
            ThrownItem.ClientExploded += OnExploded;
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
            if (_fxRoot != null) Destroy(_fxRoot.gameObject);
        }

        // Every view hears about every grenade; each shows its own combatant's. One whose thrower has left the
        // match is shown by the local player's view, so it never goes off unseen.
        void OnExploded(Vector3 centre, NetworkObject thrower)
        {
            bool mine = thrower != null ? thrower.gameObject == gameObject : _combatant == Combatant.LocalPlayer;
            if (mine) ShowBlast(centre);
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
            bool able = !_combatant.Dead.Value && _user.Grenades.Value > 0;
            if (input.ThrowPressed && able) _aiming = true;
            if (!_aiming) return;
            // Dead, nothing left to throw, or a screen took the keys (the input is then empty): the throw is off.
            if (!able || (!input.ThrowHeld && !input.ThrowReleased))
            {
                StopAiming();
                return;
            }

            // Along where the character looks, like its shots.
            var direction = Quaternion.Euler(-player.Pitch, player.Yaw, 0f) * Vector3.forward;
            if (input.ThrowReleased)
            {
                StopAiming();
                ThrowsRequested++;
                // The host is the server: throw now instead of waiting for the next network tick.
                if (_user.IsServerInitialized) _user.ServerThrowAimed(direction);
                else _user.CmdThrow(direction);
                return;
            }
            ShowArc(_user.ThrowOrigin(direction), ThrowableUser.ThrowVelocity(direction, ThrowableRules.Current));
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

        void ShowBlast(Vector3 centre)
        {
            LastBlastCentre = centre;
            BlastsShown++;
            if (_blastSource != null && _blastClip != null)
            {
                _blastSource.transform.position = centre;
                _blastSource.pitch = _blastPitch;
                _blastSource.PlayOneShot(_blastClip, _blastVolume);
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
            for (int p = 0; p < PuffsPerBlast; p++)
            {
                int index = i * PuffsPerBlast + p;
                // A different cloud every time; never below the grenade, where the floor usually is.
                var offset = Random.insideUnitSphere * PuffSpread;
                offset.y = Mathf.Abs(offset.y);
                _puffOffset[index] = offset;
                _puffs[index].enabled = true;
            }
            PlaceBlast(i, ViewCamera());
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
