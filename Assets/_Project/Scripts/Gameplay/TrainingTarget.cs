using UnityEngine;

namespace Game.Gameplay
{
    // A shooting-lane target. Anything a shot ends on inside it lights it up for a moment.
    public class TrainingTarget : MonoBehaviour
    {
        public int Meters;
        public int Hits { get; private set; }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color Flash = new Color(1f, 0.25f, 0.15f);
        Renderer[] _renderers;
        MaterialPropertyBlock _block;
        float _until;

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        public void Hit()
        {
            Hits++;
            _until = Time.time + 0.4f;
            _block.SetColor(BaseColor, Flash);
            foreach (var r in _renderers) r.SetPropertyBlock(_block);
        }

        void Update()
        {
            if (_until <= 0f || Time.time < _until) return;
            _until = 0f;
            foreach (var r in _renderers) r.SetPropertyBlock(null);
        }
    }
}
