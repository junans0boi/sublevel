using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The local player's side of the bungee on the waiting island (ADR-386/387, #419): the express lift up or down the tower
    // (BungeeLift's S-curve, a slight FOV dip on the start and a chime on arrival) and the jump itself (the fixed BungeeCurve: the
    // fall, the bounce, lowered to the pad). The body is driven here (PlayerController.ExternalControl stops walking and falling).
    // A server correction (the deployment, a fall put back) ends either ride at once. Nothing hurts: the island holds damage at zero (ADR-383).
    [RequireComponent(typeof(PlayerController))]
    public sealed class BungeeRide : MonoBehaviour
    {
        public static float ExtraFov;   // added to the camera's field of view by PlayerController while riding
        const float FovPerMetrePerSecond = 0.9f, MaxExtraFov = 38f, ShakeMetres = 0.05f;
        const float LiftStartFov = -2f, LiftStopFov = 1.2f;   // the press of the start, the float of the stop

        PlayerController _player;
        CombatantStance _stance;
        Vector3 _pad;
        float _t = -1f;
        float _lastHeight;
        float _shake;
        Vector3 _liftFrom;
        bool _liftUp;
        float _liftT = -1f;

        public bool Riding => _t >= 0f || _liftT >= 0f;

        void Start()
        {
            _player = GetComponent<PlayerController>();
            _stance = GetComponent<CombatantStance>();
            if (_stance != null) { _stance.BungeeBegan += OnBegan; _stance.LiftBegan += OnLift; _stance.Corrected += OnCorrected; }
        }

        void OnDestroy()
        {
            if (_stance != null) { _stance.BungeeBegan -= OnBegan; _stance.LiftBegan -= OnLift; _stance.Corrected -= OnCorrected; }
            ExtraFov = 0f;
        }

        void OnBegan(Vector3 pad)
        {
            if (_t >= 0f) return;
            _liftT = -1f;
            _pad = pad; _t = 0f; _lastHeight = BungeeCurve.Top;
            _player.ExternalControl = true;
        }

        void OnLift(Vector3 from, bool up)
        {
            if (_t >= 0f) return;
            _liftFrom = from; _liftUp = up; _liftT = 0f;
            _player.ExternalControl = true;
        }

        void OnCorrected(Vector3 _) { if (Riding) End(); }

        void End() { _t = -1f; _liftT = -1f; ExtraFov = 0f; _shake = 0f; _player.ExternalControl = false; }

        void Update()
        {
            if (_liftT >= 0f) { UpdateLift(); return; }
            if (_t < 0f) return;
            _t += Time.deltaTime;
            float h = BungeeCurve.Height(_t);
            float speed = Mathf.Abs(h - _lastHeight) / Mathf.Max(Time.deltaTime, 1e-3f);
            _lastHeight = h;
            _player.Teleport(_pad + Vector3.up * (h + 0.1f));
            // The view widens with the speed of the fall, and shakes a little while it is fast.
            ExtraFov = Mathf.Min(MaxExtraFov, speed * FovPerMetrePerSecond);
            _shake = speed > 8f ? ShakeMetres * Mathf.Min(3f, speed / 8f) : 0f;
            if (BungeeCurve.Over(_t)) End();
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

        void LateUpdate()
        {
            if (_shake <= 0f || _player == null || _player.Camera == null) return;
            _player.Camera.transform.position += Random.insideUnitSphere * _shake;
        }
    }
}
