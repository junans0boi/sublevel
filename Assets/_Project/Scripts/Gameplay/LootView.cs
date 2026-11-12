using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Colours a pickup by what it holds; weapon pickups show the weapon's model instead of the box.
    [RequireComponent(typeof(LootItem))]
    public class LootView : MonoBehaviour
    {
        [SerializeField] Renderer _renderer;
        [SerializeField] Material[] _materialsByKind = new Material[0];
        [SerializeField] GameObject[] _weaponModelsById = new GameObject[0];

        LootItem _item;
        int _shownKind = -1;
        int _shownWeapon = -1;

        void Awake() => _item = GetComponent<LootItem>();

        void Update()
        {
            int kind = _item.Kind.Value;
            int weapon = kind == (int)Game.Core.LootKind.Weapon ? _item.WeaponId.Value : 0;
            if ((kind == _shownKind && weapon == _shownWeapon) || kind < 0 || kind >= _materialsByKind.Length) return;
            _shownKind = kind;
            _shownWeapon = weapon;
            _renderer.sharedMaterial = _materialsByKind[kind];
            bool hasModel = weapon > 0 && weapon < _weaponModelsById.Length && _weaponModelsById[weapon] != null;
            _renderer.enabled = !hasModel;
            for (int i = 0; i < _weaponModelsById.Length; i++)
                if (_weaponModelsById[i] != null) _weaponModelsById[i].SetActive(hasModel && i == weapon);
        }
    }
}
