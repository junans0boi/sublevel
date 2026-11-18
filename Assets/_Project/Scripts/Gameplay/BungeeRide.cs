using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The local player's side of a bungee jump on the waiting island (ADR-386, #419): carried up, the fall, the bounce, lowered back,
    // all from the fixed BungeeCurve. The body is driven here (PlayerController.ExternalControl stops walking and falling), the camera
    // gets a wider view with speed and a little shake. Nothing hurts: the island holds all damage at zero (ADR-383).
    [RequireComponent(typeof(PlayerController))]
    public sealed class BungeeRide : MonoBehaviour
    {
        public static float ExtraFov;   // added to the camera's field of view by PlayerController while riding
        const float FovPerMetrePerSecond = 0.9f, MaxExtraFov = 38f, ShakeMetres = 0.05f;

        PlayerController _player;
        CombatantStance _stance;
        Vector3 _pad;
        float _t = -1f;
        float _lastHeight;
        float _shake;

        void Start()
        {
            _player = GetComponent<PlayerController>();
            _stance = GetComponent<CombatantStance>();
            if (_stance != null) _stance.BungeeBegan += OnBegan;
        }

        void OnDestroy() { if (_stance != null) _stance.BungeeBegan -= OnBegan; ExtraFov = 0f; }

        void OnBegan(Vector3 pad)
        {
            if (_t >= 0f) return;
            _pad = pad; _t = 0f; _lastHeight = 0f;
            _player.ExternalControl = true;
        }

        void Update()
        {
            if (_t < 0f) return;
            _t += Time.deltaTime;
            float h = BungeeCurve.Height(_t);
            float speed = Mathf.Abs(h - _lastHeight) / Mathf.Max(Time.deltaTime, 1e-3f);
            _lastHeight = h;
            _player.Teleport(_pad + Vector3.up * (h + 0.1f));
            // The view widens with the speed of the fall (and of the first rise), and shakes a little while it is fast.
            ExtraFov = Mathf.Min(MaxExtraFov, speed * FovPerMetrePerSecond);
            _shake = speed > 8f ? ShakeMetres * Mathf.Min(3f, speed / 8f) : 0f;
            if (BungeeCurve.Over(_t)) { _t = -1f; ExtraFov = 0f; _shake = 0f; _player.ExternalControl = false; }
        }

        void LateUpdate()
        {
            if (_shake <= 0f || _player == null || _player.Camera == null) return;
            _player.Camera.transform.position += Random.insideUnitSphere * _shake;
        }
    }
}
