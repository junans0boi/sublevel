using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Colours a pickup by what it holds.
    [RequireComponent(typeof(LootItem))]
    public class LootView : MonoBehaviour
    {
        [SerializeField] Renderer _renderer;
        [SerializeField] Material[] _materialsByKind = new Material[0];

        LootItem _item;
        int _shownKind = -1;

        void Awake() => _item = GetComponent<LootItem>();

        void Update()
        {
            int kind = _item.Kind.Value;
            if (kind == _shownKind || kind < 0 || kind >= _materialsByKind.Length) return;
            _shownKind = kind;
            _renderer.sharedMaterial = _materialsByKind[kind];
        }
    }
}
