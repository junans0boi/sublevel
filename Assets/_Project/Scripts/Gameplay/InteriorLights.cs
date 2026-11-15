using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // Interior lamps (ADR-147): the world builder places one point light per lamp, all off. A few lights near the
    // camera are on at a time, so a map with hundreds of lamps costs the pixel lights of a single building.
    // Tired fluorescent tubes flicker. Nothing here runs on a headless server.
    public sealed class InteriorLights : MonoBehaviour
    {
        public const int MaxOn = 10;
        public const float Radius = 45f;
        const float Interval = 0.2f;

        // Where the camera is when no MainCamera is tagged (capture tests).
        public static Transform Focus;

        [SerializeField] Light[] _lights;
        [SerializeField] bool[] _flicker;
        // The insides of the buildings as unit boxes scaled to size, and the post volume that darkens the view inside.
        [SerializeField] Transform[] _boxes;
        [SerializeField] Volume _grade;
        const float GradeSeconds = 0.35f;
        public float GradeWeight => _grade == null ? 0f : _grade.weight;
        public bool CameraInside { get; private set; }

        Vector3[] _positions;
        float[] _base;
        readonly bool[] _on = new bool[MaxOn * 4];
        int[] _chosen = new int[MaxOn];
        int _count;
        float _next;

        public int TotalLamps => _lights == null ? 0 : _lights.Length;
        public int ActiveCount => _count;

        void Awake()
        {
            if (_lights == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { enabled = false; return; }
            _positions = new Vector3[_lights.Length];
            _base = new float[_lights.Length];
            for (int i = 0; i < _lights.Length; i++)
            {
                _positions[i] = _lights[i].transform.position;
                _base[i] = _lights[i].intensity;
                _lights[i].enabled = false;
            }
        }

        void Update()
        {
            if (Time.time >= _next)
            {
                _next = Time.time + Interval;
                Refresh();
            }
            if (_grade != null) _grade.weight = Mathf.MoveTowards(_grade.weight, CameraInside ? 1f : 0f, Time.deltaTime / GradeSeconds);
            for (int k = 0; k < _count; k++)
            {
                int i = _chosen[k];
                if (_flicker[i]) _lights[i].intensity = _base[i] * Flicker(Time.time, i);
            }
        }

        static float Flicker(float t, int seed)
        {
            // Mostly steady, now and then a burst of stutter.
            float burst = Mathf.PerlinNoise(t * 0.6f, seed * 7.3f);
            if (burst < 0.72f) return 1f;
            return Mathf.PerlinNoise(t * 25f, seed * 3.1f) > 0.45f ? 1f : 0.15f;
        }

        void Refresh()
        {
            var cam = Focus != null ? Focus : Camera.main != null ? Camera.main.transform : null;
            if (cam == null) return;
            var at = cam.position;
            bool inside = false;
            if (_boxes != null)
                for (int i = 0; i < _boxes.Length && !inside; i++)
                {
                    var local = _boxes[i].InverseTransformPoint(at);
                    inside = Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f && Mathf.Abs(local.z) <= 0.5f;
                }
            CameraInside = inside;
            // Nearest MaxOn lamps inside the radius (insertion into a small sorted list).
            var best = new float[MaxOn];
            int n = 0;
            for (int i = 0; i < _positions.Length; i++)
            {
                float d = (_positions[i] - at).sqrMagnitude;
                if (d > Radius * Radius) continue;
                if (n == MaxOn && d >= best[n - 1]) continue;
                int at2 = n < MaxOn ? n++ : n - 1;
                while (at2 > 0 && best[at2 - 1] > d) { best[at2] = best[at2 - 1]; _chosen[at2] = _chosen[at2 - 1]; at2--; }
                best[at2] = d;
                _chosen[at2] = i;
            }
            // Switch: everything off that is not chosen, everything chosen on.
            for (int i = 0; i < _lights.Length; i++) if (_lights[i].enabled) _lights[i].enabled = false;
            for (int k = 0; k < n; k++)
            {
                _lights[_chosen[k]].enabled = true;
                _lights[_chosen[k]].intensity = _base[_chosen[k]];
            }
            _count = n;
        }
    }
}
