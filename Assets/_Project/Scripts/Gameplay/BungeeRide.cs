using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Gameplay
{
    // The local player's side of the bungee on the waiting island (ADR-386/387/495, #419): the express lift up or down the tower
    // (BungeeLift's S-curve, a slight FOV dip on the start and a chime on arrival) and the jump itself (the fixed BungeeCurve). The body
    // is driven here (PlayerController.ExternalControl stops walking and falling); the camera, the sound and the screen effects make it a
    // fall (ADR-498): the lean over the edge, a head-first dive with the ground rushing up (wider view, buffeting, wind, streaks), slow
    // motion and a hard yank as the cord bites at the bottom, the float of the rebound, then a hang until F (or HangSeconds) lowers
    // them onto the pad. A server correction (the deployment, a fall put back) ends either ride at once. Nothing hurts (ADR-383).
    [DefaultExecutionOrder(50)]   // after PlayerController: the jump's camera has the last word on the frame
    [RequireComponent(typeof(PlayerController))]
    public sealed class BungeeRide : MonoBehaviour
    {
        public static float ExtraFov;   // added to the camera's field of view by PlayerController while riding
        const float LiftStartFov = -2f, LiftStopFov = 1.2f;   // the press of the start, the float of the stop

        // The jump's camera (ADR-498). Pitches are degrees above the horizon (negative = looking down).
        const float LeanPitch = -50f, DivePitch = -80f, HangPitch = -40f;
        const float ChaseDistance = 3.2f, ChaseSide = 0.55f;
        const float FallFovPerMps = 1.05f, MaxFallFov = 30f, SlowFov = -14f, SnapFov = 10f;
        const float BuffetDeg = 1.6f, SnapShakeDeg = 7f, TumbleDeg = 6f;
        const float TopSpeed = 28f;

        PlayerController _player;
        CombatantStance _stance;
        CombatantAnimation _anim;
        Vector3 _pad;
        float _t = -1f;
        float _lowerAt = float.PositiveInfinity;
        float _accel, _trauma, _speedN, _slow;
        bool _bit, _snapped;
        Vector3 _liftFrom;
        bool _liftUp;
        float _liftT = -1f;

        Volume _volume;
        Vignette _vignette;
        ChromaticAberration _chroma;
        LensDistortion _lens;
        AudioSource _wind, _sfx;
        ParticleSystem _streaks;
        static AudioClip _windClip, _snapClip;
        static GUIStyle _promptStyle, _subStyle;

        public bool Riding => _t >= 0f || _liftT >= 0f;
        public bool Jumping => _t >= 0f;
        public float JumpSeconds => _t;
        public BungeePhase Phase => _t < 0f ? BungeePhase.Done : BungeeCurve.PhaseAt(_t, _lowerAt);
        public bool AskedDown => !float.IsPositiveInfinity(_lowerAt);
        public float Trauma => _trauma;

        // The line at the bottom of the screen while hanging and lowering ("" = none).
        public string Prompt => Phase == BungeePhase.Hang && !AskedDown ? "F  내려가기" : Phase == BungeePhase.Hang || Phase == BungeePhase.Lower ? "내려가는 중" : "";
        public string PromptDetail => Phase == BungeePhase.Hang && !AskedDown ? $"{Mathf.CeilToInt(BungeeCurve.AutoLowerAt - _t)}초 뒤 자동으로 내려갑니다" : "";

        void Start()
        {
            _player = GetComponent<PlayerController>();
            _stance = GetComponent<CombatantStance>();
            _anim = GetComponent<CombatantAnimation>();
            if (_stance != null) { _stance.BungeeBegan += OnBegan; _stance.LiftBegan += OnLift; _stance.Corrected += OnCorrected; }
            // Everyone on the island sees the cord of whoever is jumping (drawn from the bodies' places, BungeeCordView).
            var island = GameObject.Find("WaitingIsland");
            if (island != null && island.GetComponent<BungeeCordView>() == null) island.AddComponent<BungeeCordView>();
        }

        void OnDestroy()
        {
            if (_stance != null) { _stance.BungeeBegan -= OnBegan; _stance.LiftBegan -= OnLift; _stance.Corrected -= OnCorrected; }
            EndFx();
            ExtraFov = 0f;
        }

        void OnBegan(Vector3 pad)
        {
            if (_t >= 0f) return;
            _liftT = -1f;
            _pad = pad; _t = 0f; _lowerAt = float.PositiveInfinity;
            _accel = _trauma = _speedN = _slow = 0f; _bit = _snapped = false;
            _player.ExternalControl = true;
            BeginFx();
        }

        void OnLift(Vector3 from, bool up)
        {
            if (_t >= 0f) return;
            _liftFrom = from; _liftUp = up; _liftT = 0f;
            _player.ExternalControl = true;
        }

        void OnCorrected(Vector3 _) { if (Riding) End(); }

        void End() { _t = -1f; _liftT = -1f; ExtraFov = 0f; EndFx(); _player.ExternalControl = false; }

        void Update()
        {
            if (_liftT >= 0f) { UpdateLift(); return; }
            if (_t < 0f) return;
            float dt = Time.deltaTime;
            _t += dt;
            // Hanging: F asks to come down (the server starts the same lowering when the request arrives).
            if (Phase == BungeePhase.Hang && !AskedDown && _player.LastInput.Interact)
            {
                _lowerAt = _t;
                if (_stance != null) _stance.CmdBungeeLower();
            }
            float ft = BungeeCurve.FeelTime(_t);
            _player.Teleport(_pad + Vector3.up * (BungeeCurve.Height(ft, _lowerAt) + 0.1f));
            float v = BungeeCurve.Velocity(ft, _lowerAt);
            _accel = Mathf.Lerp(_accel, BungeeCurve.Acceleration(ft, _lowerAt), 1f - Mathf.Exp(-12f * dt));
            _speedN = Mathf.Clamp01(-v / TopSpeed);
            _slow = dt > 0f ? Mathf.Clamp01(1f - (BungeeCurve.FeelTime(_t) - BungeeCurve.FeelTime(_t - dt)) / dt) : 0f;
            // Two beats: the cord bites as it goes taut (a jolt, ~2 g at once), then the climax at the lowest point, where it yanks them
            // back up (the slow motion leads into it).
            if (!_bit && ft >= BungeeCurve.TautAt) { _bit = true; _trauma = Mathf.Max(_trauma, 0.55f); PlaySnap(0.55f, 1.25f); }
            if (!_snapped && ft >= BungeeCurve.SnapAt) { _snapped = true; _trauma = 1f; PlaySnap(1f, 1f); }
            _trauma = Mathf.Max(0f, _trauma - 1.6f * dt);
            // The view widens with the speed of the fall, tunnels in as time slows, and kicks out as the cord yanks.
            ExtraFov = Mathf.Min(MaxFallFov, Mathf.Max(0f, -v) * FallFovPerMps) + SlowFov * _slow + SnapFov * _trauma * _trauma;
            UpdateFx(v);
            if (BungeeCurve.Over(_t, _lowerAt)) End();
        }

        void UpdateLift()
        {
            _liftT += Time.deltaTime;
            float d = BungeeLift.Travelled(_liftT);
            var p = transform.position;
            _player.Teleport(new Vector3(p.x, _liftFrom.y + (_liftUp ? d : -d), p.z));
            // A smooth express: a slight press of the view while it speeds up, a slight lift while it slows, nothing at cruise.
            float ease = BungeeLift.EaseSeconds, T = BungeeLift.TravelSeconds;
            ExtraFov = _liftT < ease ? LiftStartFov * Mathf.Sin(Mathf.PI * _liftT / ease)
                     : _liftT > T - ease ? LiftStopFov * Mathf.Sin(Mathf.PI * (T - _liftT) / ease) : 0f;
            if (_liftT < T) return;
            AudioSource.PlayClipAtPoint(ElevatorAudio.ArrivalChime, transform.position + Vector3.up * 2.2f, 0.6f * GameSettings.EffectsVolume);
            End();
        }

        // ---- the jump's camera: placed after PlayerController placed (and shook) its own ----

        void LateUpdate()
        {
            if (_t < 0f || _player == null || _player.Camera == null) return;
            var cam = _player.Camera.transform;
            Vector3 rigAt = cam.position; Quaternion rigTurn = cam.rotation;   // where the normal view put it this frame
            float pitch = Mathf.Lerp(_player.ViewPitch, DiveTarget(out float weight), weight);
            // A slow lean and sway while falling fast (never a spin), the buffeting of the air, the jolt of the yank.
            float shake = GameSettings.CameraShake;
            float time = Time.time;
            float roll = TumbleDeg * _speedN * (Mathf.Sin(time * 0.9f) * 0.7f + Mathf.Sin(time * 0.37f + 1.3f) * 0.3f);
            float buffet = BuffetDeg * _speedN * _speedN + SnapShakeDeg * _trauma * _trauma;
            float jx = (Mathf.PerlinNoise(time * 17f, 0.3f) - 0.5f) * 2f * buffet * shake;
            float jy = (Mathf.PerlinNoise(0.7f, time * 17f) - 0.5f) * 2f * buffet * shake;
            var rotation = Quaternion.Euler(-(pitch + jx), _player.ViewYaw + jy, roll * shake);
            var pivot = transform.position + Vector3.up * BungeePose.PivotHeight;
            float flip = _anim != null ? _anim.BungeeFlip : 0f;
            Vector3 at;
            if (_player.Rig.Mode == ViewMode.FirstPerson)
                at = pivot + Quaternion.AngleAxis(180f * flip, transform.right) * (Vector3.up * 0.65f + transform.forward * 0.12f);   // the head, wherever the turn put it
            else
                at = pivot - rotation * Vector3.forward * ChaseDistance + rotation * Vector3.right * ChaseSide;
            // The camera trails the body's acceleration: a touch high as the fall pulls away, dragged down past it as the cord bites.
            at += Vector3.up * Mathf.Clamp(-_accel * 0.03f, -0.9f, 0.5f);
            // Eased in from the normal view over the first moments on the board, and back to it over the lowering.
            float own = Phase == BungeePhase.Lower ? 1f - Smooth((_t - BungeeCurve.LowerStart(_lowerAt)) / (BungeeCurve.LowerSeconds * 0.8f)) : Smooth(_t / 0.4f);
            cam.SetPositionAndRotation(Vector3.Lerp(rigAt, at, own), Quaternion.Slerp(rigTurn, rotation, own));
        }

        static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        // Where the jump looks (pitch) and how much of the player's own look that overrides (0..1).
        float DiveTarget(out float weight)
        {
            switch (Phase)
            {
                case BungeePhase.Board: weight = Smooth(_t / BungeeCurve.HoldSeconds); return LeanPitch;   // over the edge, the drop below
                case BungeePhase.Fall: weight = 1f; return Mathf.Lerp(LeanPitch, DivePitch, Smooth((_t - BungeeCurve.HoldSeconds) / 0.8f));
                case BungeePhase.Bounce:
                {
                    // Head-first down the dive until the yank, then the view lifts as they float back up.
                    float after = Smooth((BungeeCurve.FeelTime(_t) - BungeeCurve.SnapAt) / 1.8f);
                    weight = Mathf.Lerp(1f, 0.6f, after); return Mathf.Lerp(DivePitch, HangPitch, after);
                }
                case BungeePhase.Hang: weight = 0.6f; return HangPitch;
                default:
                {
                    float into = (_t - BungeeCurve.LowerStart(_lowerAt)) / BungeeCurve.LowerSeconds;
                    weight = 0.6f * (1f - Smooth(into * 1.4f)); return HangPitch;
                }
            }
        }

        // ---- sound and screen effects (made in code: no new assets) ----

        void BeginFx()
        {
            if (_volume == null)
            {
                var go = new GameObject("BungeeFallFx");
                go.transform.SetParent(transform, false);
                _volume = go.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 50f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "BungeeFall";
                _vignette = profile.Add<Vignette>(false);
                _vignette.smoothness.Override(0.55f);
                _chroma = profile.Add<ChromaticAberration>(false);
                _lens = profile.Add<LensDistortion>(false);
                _volume.sharedProfile = profile;
                _wind = go.AddComponent<AudioSource>();
                _wind.clip = WindClip; _wind.loop = true; _wind.spatialBlend = 0f; _wind.playOnAwake = false;
                _sfx = go.AddComponent<AudioSource>();
                _sfx.spatialBlend = 0f; _sfx.playOnAwake = false;
                // Specks of dust in the air that streak upward past the camera as it drops (stretched by the camera's own speed).
                _streaks = FxKit.Particles("BungeeStreaks", go.transform, FxKit.Material(FxKit.Sprite.Dot, false), 160, ParticleSystemRenderMode.Stretch);
                var main = _streaks.main;
                main.startLifetime = 1.4f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                main.startColor = new Color(1f, 1f, 1f, 0.45f);
                var shape = _streaks.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(10f, 2f, 10f); shape.position = Vector3.down * 14f;
                var renderer = _streaks.GetComponent<ParticleSystemRenderer>();
                renderer.cameraVelocityScale = 0.06f; renderer.velocityScale = 0f; renderer.lengthScale = 1f;
            }
            _volume.gameObject.SetActive(true);
            _volume.weight = 1f;
            _wind.volume = 0f; _wind.Play();
            var emission = _streaks.emission; emission.enabled = true; emission.rateOverTime = 0f;
            UpdateFx(0f);
        }

        void UpdateFx(float v)
        {
            if (_volume == null) return;
            float ease = Mathf.Clamp01(_t / BungeeCurve.HoldSeconds);   // the lean: the edges darken a little before the drop
            _vignette.intensity.Override(0.12f * ease + 0.3f * _speedN + 0.22f * _slow + 0.2f * _trauma);
            _chroma.intensity.Override(0.35f * _speedN * _speedN + 0.4f * _trauma * _trauma);
            _lens.intensity.Override(-0.22f * _speedN - 0.1f * _slow);
            // Wind: a rising roar with the speed (pitched down in the slow motion), a breeze while hanging.
            float effects = GameSettings.EffectsVolume;
            _wind.volume = effects * Mathf.Lerp(0.06f, 1f, Mathf.Pow(Mathf.Clamp01(Mathf.Abs(v) / TopSpeed), 1.3f));
            _wind.pitch = Mathf.Lerp(0.7f, 1.7f, Mathf.Clamp01(Mathf.Abs(v) / TopSpeed)) * (1f - 0.45f * _slow);
            var emission = _streaks.emission; emission.rateOverTime = 260f * _speedN;
        }

        void EndFx()
        {
            if (_volume != null) _volume.gameObject.SetActive(false);
            if (_streaks != null) { var emission = _streaks.emission; emission.enabled = false; _streaks.Clear(); }
        }

        void PlaySnap(float volume, float pitch)
        {
            if (_sfx == null) return;
            _sfx.pitch = pitch;
            _sfx.PlayOneShot(SnapClip, volume * GameSettings.EffectsVolume);
        }

        const int Rate = 22050;

        static AudioClip WindClip
        {
            get
            {
                if (_windClip != null) return _windClip;
                var data = AmbientBeds.SynthWind();
                _windClip = AudioClip.Create("BungeeWind", data.Length, 1, Rate, false);
                _windClip.SetData(data, 0);
                return _windClip;
            }
        }

        // The cord biting: a deep thump, the rubber's groan sliding down, a short rasp of stretch.
        public static float[] SynthSnap()
        {
            int n = (int)(Rate * 1.1f);
            var data = new float[n];
            var rng = new System.Random(4195);
            float noise = 0f, phaseThump = 0f, phaseTwang = 0f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                phaseThump += 2f * Mathf.PI * Mathf.Lerp(70f, 42f, Mathf.Clamp01(t * 6f)) / Rate;
                phaseTwang += 2f * Mathf.PI * Mathf.Lerp(150f, 85f, Mathf.Clamp01(t * 1.5f)) / Rate;
                noise += ((float)(rng.NextDouble() * 2.0 - 1.0) - noise) * 0.25f;
                float thump = Mathf.Sin(phaseThump) * Mathf.Exp(-t * 8f);
                float twang = (Mathf.Sin(phaseTwang) + 0.35f * Mathf.Sin(2f * phaseTwang)) * Mathf.Exp(-t * 3.2f) * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 13f * t)) * Mathf.Clamp01(t * 40f);
                float rasp = noise * Mathf.Exp(-t * 14f);
                data[i] = thump * 1f + twang * 0.35f + rasp * 0.5f;
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            for (int i = 0; i < n; i++) data[i] *= 0.9f / peak;
            return data;
        }

        static AudioClip SnapClip
        {
            get
            {
                if (_snapClip != null) return _snapClip;
                var data = SynthSnap();
                _snapClip = AudioClip.Create("BungeeSnap", data.Length, 1, Rate, false);
                _snapClip.SetData(data, 0);
                return _snapClip;
            }
        }

        void OnGUI()
        {
            string prompt = Prompt;
            if (string.IsNullOrEmpty(prompt)) return;
            if (_promptStyle == null)
            {
                _promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _subStyle = new GUIStyle(_promptStyle) { fontSize = 18, fontStyle = FontStyle.Normal };
            }
            float y = Screen.height * 0.7f;
            Label(new Rect(0, y, Screen.width, 44), prompt, _promptStyle);
            string detail = PromptDetail;
            if (detail.Length > 0) Label(new Rect(0, y + 42, Screen.width, 28), detail, _subStyle);
        }

        static void Label(Rect r, string text, GUIStyle style)
        {
            var color = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }
    }
}
