using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // Client-side look of a combatant's shots (GAME_FEEL_SPEC 5): muzzle flash, tracers and impacts, all pooled,
    // plus hit confirmation and damage direction for the local player. Body animation lives in CombatantAnimation.
    [RequireComponent(typeof(Combatant))]
    public class CombatantView : MonoBehaviour
    {
        const int TracerPool = 12; // a shotgun shot is 9 pellets
        const int ImpactPool = 12;
        const double PelletWindow = 0.03;
        const float MinTracerMeters = 0.2f;
        const float SurfaceOffset = 0.03f; // keeps impact effects out of the surface they sit on

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Materials are assets made by the builder: shaders cannot be looked up in a player build.
        [SerializeField] Material _glowMaterial; // additive: muzzle flash, tracers, impact flash
        [SerializeField] Material _dustMaterial; // alpha blended
        [SerializeField] Color _dustColor = new Color(0.62f, 0.6f, 0.56f, 0.55f);
        [SerializeField] Color _lightColor = new Color(1f, 0.8f, 0.5f);
        [SerializeField] AudioSource _confirmSource;
        [SerializeField] AudioClip _hitConfirmClip;
        [SerializeField] AudioClip _killConfirmClip;
        // Where the muzzle of the weapon drawn in front of the camera is, in view space (x right, y up, z forward).
        [SerializeField] Vector3 _eyeMuzzleHip = new Vector3(0.13f, -0.17f, 0.95f);
        [SerializeField] Vector3 _eyeMuzzleSights = new Vector3(0f, -0.07f, 0.9f);

        readonly ShotGrouper _shots = new ShotGrouper(PelletWindow);
        Combatant _combatant;
        WeaponVisual _weaponVisual;
        CombatFxRules _fx;
        Transform _fxRoot; // effects that stay where they happened instead of following the combatant
        MaterialPropertyBlock _block;

        Transform _flash;
        MeshRenderer _flashRenderer;
        Light _flashLight;
        float _flashOff;
        Vector3 _muzzle;

        LineRenderer[] _tracers;
        Vector3[] _tracerFrom, _tracerDir;
        float[] _tracerDistance, _tracerHead, _tracerSpeed, _tracerLength;
        int _nextTracer;

        Transform[] _impacts, _impactFlash, _impactDust;
        MeshRenderer[] _impactFlashRenderer, _impactDustRenderer;
        Vector3[] _impactPoint, _impactNormal;
        float[] _impactStart;
        bool[] _impactOn;
        BulletSurface[] _impactSurface;
        int _nextImpact;

        // Blood trail of this combatant (#197): when a bullet last hit it, where the last drop fell, whether its pool is down.
        float _lastBodyHit = float.NegativeInfinity, _nextDrip;
        Vector3 _lastDrip = new Vector3(float.MaxValue, 0f, 0f);
        bool _pooled;

        // Per shot (a shotgun's pellets are one shot): what the local player has already been told.
        bool _hitShown, _killShown, _headShown, _damageShown;

        // Counters so tests can tell that effects were shown without looking.
        public int MuzzleFlashes { get; private set; }
        public int TracersShown { get; private set; }
        public int ImpactsShown { get; private set; }
        public int HitConfirms { get; private set; }
        public int KillConfirms { get; private set; }
        public int HeadshotConfirms { get; private set; }
        public int DamageDirectionsShown { get; private set; }
        public BulletSurface LastImpactSurface { get; private set; }

        // Raised for the local player's own shot when it lands on a head, at most once per shot (true = it killed).
        // Comes right after the usual hit marker call; a HUD that wants a headshot marker listens here and
        // unsubscribes when it or this view goes away.
        public event System.Action<bool> HeadshotConfirmed;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _weaponVisual = GetComponent<WeaponVisual>();
            _fx = GameData.Rules.CombatFx;
            _block = new MaterialPropertyBlock();
            _combatant.ShotResolved += OnShot;
            _combatant.Punched += OnPunch;
            _fxRoot = new GameObject("ShotFx").transform;
            ImpactFx.Use(_dustMaterial, _glowMaterial);
            if (GetComponent<BackpackVisual>() == null) gameObject.AddComponent<BackpackVisual>(); // ADR-091
            if (GetComponent<ArmorVisual>() == null) gameObject.AddComponent<ArmorVisual>(); // #179: worn helmet and vest
            if (GetComponent<AttachmentVisual>() == null) gameObject.AddComponent<AttachmentVisual>(); // ADR-120
            if (GetComponent<CasingFx>() == null) gameObject.AddComponent<CasingFx>(); // #199: spent cases

            _flashRenderer = NewRenderer("MuzzleFlash", transform, CombatFxMeshes.MuzzleFlash, _glowMaterial);
            _flash = _flashRenderer.transform;
            _flashLight = _flash.gameObject.AddComponent<Light>();
            _flashLight.type = LightType.Point;
            _flashLight.color = _lightColor;
            _flashLight.range = _fx.MuzzleLightRange;
            _flashLight.shadows = LightShadows.None;
            _flashLight.enabled = false;

            _tracers = new LineRenderer[TracerPool];
            _tracerFrom = new Vector3[TracerPool];
            _tracerDir = new Vector3[TracerPool];
            _tracerDistance = new float[TracerPool];
            _tracerHead = new float[TracerPool];
            _tracerSpeed = new float[TracerPool];
            _tracerLength = new float[TracerPool];
            for (int i = 0; i < TracerPool; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(_fxRoot, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = _glowMaterial;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.widthMultiplier = _fx.TracerWidth;
                // Fades out toward the tail.
                line.startColor = new Color(1f, 1f, 1f, 0f);
                line.endColor = Color.white;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                _tracers[i] = line;
            }

            _impacts = new Transform[ImpactPool];
            _impactFlash = new Transform[ImpactPool];
            _impactDust = new Transform[ImpactPool];
            _impactFlashRenderer = new MeshRenderer[ImpactPool];
            _impactDustRenderer = new MeshRenderer[ImpactPool];
            _impactPoint = new Vector3[ImpactPool];
            _impactNormal = new Vector3[ImpactPool];
            _impactStart = new float[ImpactPool];
            _impactOn = new bool[ImpactPool];
            _impactSurface = new BulletSurface[ImpactPool];
            for (int i = 0; i < ImpactPool; i++)
            {
                var root = new GameObject("Impact").transform;
                root.SetParent(_fxRoot, false);
                _impacts[i] = root;
                _impactDustRenderer[i] = NewRenderer("Dust", root, CombatFxMeshes.Disc, _dustMaterial);
                _impactFlashRenderer[i] = NewRenderer("Flash", root, CombatFxMeshes.Disc, _glowMaterial);
                _impactDust[i] = _impactDustRenderer[i].transform;
                _impactFlash[i] = _impactFlashRenderer[i].transform;
            }
        }

        static MeshRenderer NewRenderer(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
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
            _combatant.ShotResolved -= OnShot;
            _combatant.Punched -= OnPunch;
            if (_fxRoot != null) Destroy(_fxRoot.gameObject);
        }

        // Once per projectile.
        void OnShot(in Combatant.ShotResult shot)
        {
            var to = shot.End - shot.Origin;
            float distance = to.magnitude;
            if (distance < 1e-3f) return;
            var dir = to / distance;

            // A bullet still flying shows its muzzle now and its impact when it lands (ADR-098).
            if (shot.Phase == Combatant.ShotPhase.Impact)
            {
                if (shot.Hit != ShotHit.None) ShowImpact(shot, dir);
                Confirm(shot);
                return;
            }
            if (_shots.IsNewShot(Time.timeAsDouble))
            {
                _hitShown = _killShown = _headShown = _damageShown = false;
                _muzzle = MuzzlePoint(shot.Origin, dir, out float sizeFactor);
                ShowMuzzleFlash(dir, sizeFactor);
            }
            if (shot.Phase == Combatant.ShotPhase.Launch) return;
            ShowTracer(_muzzle, shot.End);
            if (shot.Hit != ShotHit.None) ShowImpact(shot, dir);
            Confirm(shot);
        }

        // A punch that landed: the same hit marker and confirmation sound as a shot, and the hit direction for
        // the one punched (ADR-059). No flash, tracer or impact.
        void OnPunch(in Combatant.PunchResult punch)
        {
            var local = Combatant.LocalPlayer;
            if (local == null || punch.Victim == null) return;
            bool kill = punch.Hit == ShotHit.Kill;
            if (punch.Victim == local)
            {
                DamageDirectionsShown++;
                Hud.DamageFrom(transform.position);
                PlayerController.Shake(CameraShake.Hit(punch.Head) * 0.7f);
            }
            if (_combatant != local) return;
            HitConfirms++;
            if (kill) KillConfirms++;
            if (punch.Head) HeadshotConfirms++;
            Hud.HitMarker(kill, punch.Head, punch.Zone == HitZone.Limb);
            if (punch.Head) HeadshotConfirmed?.Invoke(kill);
            var clip = kill && _killConfirmClip != null ? _killConfirmClip : _hitConfirmClip;
            if (_confirmSource == null || clip == null) return;
            _confirmSource.pitch = kill ? _fx.KillConfirmPitch : punch.Head ? _fx.HeadConfirmPitch : _fx.HitConfirmPitch;
            _confirmSource.PlayOneShot(clip, (kill ? _fx.KillConfirmVolume : _fx.HitConfirmVolume) * GameSettings.UiVolume);
        }

        // Shots are traced from the eyes; the flash and the tracer start where the weapon is held instead.
        Vector3 MuzzlePoint(Vector3 origin, Vector3 dir, out float sizeFactor)
        {
            var right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 1e-6f) right = transform.right; // shooting straight up or down
            right.Normalize();
            var up = Vector3.Cross(dir, right);
            // The local player's eye view draws the weapon in front of the camera (WeaponVisual).
            if (_weaponVisual != null && _weaponVisual.AtEye)
            {
                var offset = _combatant.AimMode == AimMode.Sights ? _eyeMuzzleSights : _eyeMuzzleHip;
                sizeFactor = _fx.EyeViewFlashFactor;
                return origin + right * offset.x + up * offset.y + dir * offset.z;
            }
            sizeFactor = 1f;
            return origin + dir * _fx.MuzzleForward - up * _fx.MuzzleDrop;
        }

        void ShowMuzzleFlash(Vector3 dir, float sizeFactor)
        {
            int weaponId = _combatant.ActiveWeaponId;
            var sizes = _fx.MuzzleFlashSize;
            float size = (weaponId >= 0 && weaponId < sizes.Length ? sizes[weaponId] : 0.3f) * sizeFactor;
            if (size <= 0f) return;
            // A different roll and size every shot, so automatic fire flickers instead of showing one frozen shape.
            _flash.SetPositionAndRotation(_muzzle, Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            _flash.localScale = Vector3.one * (size * Random.Range(0.85f, 1.15f));
            _flashRenderer.enabled = true;
            _flashLight.intensity = _fx.MuzzleLightIntensity * size;
            _flashLight.enabled = true;
            _flashOff = Time.time + _fx.MuzzleFlashSeconds;
            MuzzleFlashes++;
        }

        void ShowTracer(Vector3 from, Vector3 end)
        {
            var to = end - from;
            float distance = to.magnitude;
            if (distance < MinTracerMeters) return;
            int i = _nextTracer;
            _nextTracer = (_nextTracer + 1) % TracerPool;
            _tracerFrom[i] = from;
            _tracerDir[i] = to / distance;
            _tracerDistance[i] = distance;
            _tracerLength[i] = Mathf.Min(_fx.TracerLength, distance * 0.6f);
            // The streak is visible from the first frame, and a short shot slows it down so it does not vanish in one.
            _tracerHead[i] = _tracerLength[i];
            _tracerSpeed[i] = Mathf.Min(_fx.TracerSpeed, distance / Mathf.Max(_fx.TracerMinSeconds, 1e-3f));
            PlaceTracer(i, ViewCamera());
            _tracers[i].enabled = true;
            TracersShown++;
        }

        void PlaceTracer(int i, Transform camera)
        {
            var line = _tracers[i];
            var head = _tracerFrom[i] + _tracerDir[i] * Mathf.Min(_tracerHead[i], _tracerDistance[i]);
            line.SetPosition(0, _tracerFrom[i] + _tracerDir[i] * Mathf.Max(_tracerHead[i] - _tracerLength[i], 0f));
            line.SetPosition(1, head);
            // Wider the further away it is, so it stays visible across the map.
            float width = _fx.TracerWidth;
            if (camera != null) width = Mathf.Max(width, Vector3.Distance(camera.position, head) * _fx.TracerWidthPerMeter);
            line.widthMultiplier = width;
        }

        // The flash and dust of an impact, the hole it leaves, what flies off, or blood on a body; a splash instead
        // when the shot went into water first (the ground under it is not seen).
        void ShowImpact(in Combatant.ShotResult shot, Vector3 dir)
        {
            if (shot.Surface != BulletSurface.Body && ImpactFx.WaterOnPath(shot.Origin, shot.End, out var splash))
            {
                ImpactFx.Splash(splash);
                return;
            }
            ShowImpact(shot.End, shot.Normal, shot.Surface);
            if (shot.Surface != BulletSurface.Body) ImpactFx.Debris(shot.End, shot.Normal.sqrMagnitude > 1e-6f ? shot.Normal.normalized : -dir, dir, shot.Surface);
            else if (shot.Victim != null)
            {
                ImpactFx.Blood(shot.End, dir, shot.Head, shot.Victim.transform);
                var victimView = shot.Victim.GetComponent<CombatantView>();
                if (victimView != null) victimView._lastBodyHit = Time.time;
            }
        }

        void ShowImpact(Vector3 point, Vector3 normal, BulletSurface surface)
        {
            int i = _nextImpact;
            _nextImpact = (_nextImpact + 1) % ImpactPool;
            _impactPoint[i] = point;
            _impactNormal[i] = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            _impactStart[i] = Time.time;
            _impactOn[i] = true;
            _impactSurface[i] = surface;
            var look = ImpactLooks.Of(surface);
            _impactFlashRenderer[i].enabled = look.Flash > 0f;
            _impactDustRenderer[i].enabled = GameSettings.HighEffects; // low effects: the flash only
            PlaceImpact(i, ViewCamera());
            ImpactsShown++;
            LastImpactSurface = surface;
            BulletMarks.Add(point, normal, surface, look); // stays on walls and ground (#152, #198)
        }

        // Returns false when the impact has run its course.
        bool PlaceImpact(int i, Transform camera)
        {
            float age = Time.time - _impactStart[i];
            if (age >= _fx.DustSeconds) return false;
            float k = age / _fx.DustSeconds;
            // The discs face the camera; the dust drifts off the surface, grows and fades.
            var rotation = camera != null ? camera.rotation : Quaternion.LookRotation(_impactNormal[i]);
            _impacts[i].SetPositionAndRotation(_impactPoint[i] + _impactNormal[i] * (SurfaceOffset + _fx.DustDriftSpeed * age), rotation);
            var look = ImpactLooks.Of(_impactSurface[i]);
            _impactDust[i].localScale = Vector3.one * (_fx.DustSize * look.DustSize * Mathf.Lerp(1f, _fx.DustGrowth, k));
            var color = look.Dust;
            if (_impactSurface[i] == BulletSurface.Body && GameSettings.Blood == BloodLevel.Off) color = new Color(0.55f, 0.53f, 0.5f, color.a); // 피 효과 끔: no red mist
            color.a *= 1f - k;
            _block.SetColor(BaseColorId, color);
            _impactDustRenderer[i].SetPropertyBlock(_block);

            if (age < _fx.ImpactFlashSeconds)
            {
                _impactFlash[i].localScale = Vector3.one * (_fx.ImpactFlashSize * look.Flash * (1f - 0.5f * age / _fx.ImpactFlashSeconds));
                _block.SetColor(BaseColorId, look.FlashColor);
                _impactFlashRenderer[i].SetPropertyBlock(_block);
            }
            else if (_impactFlashRenderer[i].enabled)
                _impactFlashRenderer[i].enabled = false;
            return true;
        }

        // Tells the local player about hits they made (marker + sound) and hits they took (direction).
        void Confirm(in Combatant.ShotResult shot)
        {
            var local = Combatant.LocalPlayer;
            if (local == null || shot.Victim == null) return;
            bool kill = shot.Hit == ShotHit.Kill;
            if (shot.Victim == local && !_damageShown)
            {
                _damageShown = true;
                DamageDirectionsShown++;
                Hud.DamageFrom(transform.position);
                if (PlayerController.Local != null) PlayerController.Local.TakeAimPunch(); // ADR-090
                PlayerController.Shake(CameraShake.Hit(shot.Head)); // ADR-471, view only
            }
            if (_combatant != local) return;
            // One marker per shot; a later pellet of the same shot that kills or lands on the head upgrades it.
            bool head = shot.Head && !_headShown;
            if (_hitShown && !(kill && !_killShown) && !head) return;
            if (!_hitShown) HitConfirms++;
            if (kill && !_killShown) KillConfirms++;
            if (head) HeadshotConfirms++;
            _hitShown = true;
            _killShown |= kill;
            _headShown |= head;
            Hud.HitMarker(kill || _killShown, _headShown, shot.Zone == HitZone.Limb);
            if (head) HeadshotConfirmed?.Invoke(kill || _killShown);
            var clip = kill && _killConfirmClip != null ? _killConfirmClip : _hitConfirmClip;
            if (_confirmSource == null || clip == null) return;
            _confirmSource.pitch = kill ? _fx.KillConfirmPitch : shot.Head ? _fx.HeadConfirmPitch : _fx.HitConfirmPitch;
            _confirmSource.PlayOneShot(clip, (kill ? _fx.KillConfirmVolume : _fx.HitConfirmVolume) * GameSettings.UiVolume);
        }

        static Transform ViewCamera()
        {
            var player = PlayerController.Local;
            return player != null && player.Camera != null ? player.Camera.transform : null;
        }

        // After movement and the camera have been placed for this frame.
        void LateUpdate()
        {
            if (_flashRenderer.enabled && Time.time >= _flashOff)
            {
                _flashRenderer.enabled = false;
                _flashLight.enabled = false;
            }

            var camera = ViewCamera();
            for (int i = 0; i < TracerPool; i++)
            {
                if (!_tracers[i].enabled) continue;
                _tracerHead[i] += _tracerSpeed[i] * Time.deltaTime;
                if (_tracerHead[i] - _tracerLength[i] >= _tracerDistance[i]) _tracers[i].enabled = false;
                else PlaceTracer(i, camera);
            }

            for (int i = 0; i < ImpactPool; i++)
            {
                if (!_impactOn[i] || PlaceImpact(i, camera)) continue;
                _impactOn[i] = false;
                _impactFlashRenderer[i].enabled = false;
                _impactDustRenderer[i].enabled = false;
            }
            Bleed();
        }

        // A hit body drips for a while and a knocked one bleeds on (drops while it moves); where it goes down a pool
        // stays (#197). Client only, nothing the server reads.
        void Bleed()
        {
            if (!_combatant.IsClientStarted) return;
            bool down = _combatant.Dead.Value || _combatant.Knocked.Value;
            if (!down) _pooled = false;
            else if (!_pooled)
            {
                _pooled = true;
                ImpactFx.Drip(transform.position, Random.Range(0.9f, 1.3f), transform);
            }
            if (_combatant.Dead.Value || Time.time < _nextDrip) return;
            if (!BloodRules.Drips(GameSettings.Blood, _combatant.Knocked.Value, Time.time - _lastBodyHit)) return;
            var feet = transform.position;
            if ((feet - _lastDrip).sqrMagnitude < BloodRules.DripMinMeters * BloodRules.DripMinMeters) return;
            _nextDrip = Time.time + BloodRules.DripEverySeconds * Random.Range(0.7f, 1.3f);
            _lastDrip = feet;
            var side = Random.insideUnitCircle * 0.2f;
            ImpactFx.Drip(feet + new Vector3(side.x, 0f, side.y), Random.Range(0.12f, 0.22f), transform);
        }
    }
}
