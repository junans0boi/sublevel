using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Client-side look of a combatant: shot tracers (pooled) and falling over on death.
    [RequireComponent(typeof(Combatant))]
    public class CombatantView : MonoBehaviour
    {
        const int PoolSize = 12;
        const float TracerLife = 0.06f;

        [SerializeField] Transform _visual;
        [SerializeField] Material _tracerMaterial;

        Combatant _combatant;
        LineRenderer[] _tracers;
        float[] _tracerOff;
        int _next;
        bool _shownDead;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _combatant.ShotFired += OnShot;
            _tracers = new LineRenderer[PoolSize];
            _tracerOff = new float[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = _tracerMaterial;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.startWidth = line.endWidth = 0.03f;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.enabled = false;
                _tracers[i] = line;
            }
        }

        void OnDestroy() => _combatant.ShotFired -= OnShot;

        void OnShot(Vector3 origin, Vector3 end)
        {
            var line = _tracers[_next];
            line.SetPosition(0, origin + (end - origin).normalized * 0.6f);
            line.SetPosition(1, end);
            line.enabled = true;
            _tracerOff[_next] = Time.time + TracerLife;
            _next = (_next + 1) % PoolSize;
        }

        void Update()
        {
            for (int i = 0; i < PoolSize; i++)
                if (_tracers[i].enabled && Time.time >= _tracerOff[i]) _tracers[i].enabled = false;

            bool dead = _combatant.Dead.Value;
            if (dead != _shownDead && _visual != null)
            {
                _shownDead = dead;
                _visual.localRotation = dead ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
                _visual.localPosition = dead ? new Vector3(0f, 0.35f, 0f) : Vector3.zero;
            }
        }
    }
}
