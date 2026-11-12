using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Shows the model of the weapon a combatant is holding. Models are indexed by weapon id (0 = none).
    [RequireComponent(typeof(Combatant))]
    public class WeaponVisual : MonoBehaviour
    {
        [SerializeField] GameObject[] _modelsByWeaponId = new GameObject[0];

        Combatant _combatant;
        int _shown = -1;

        public int ShownWeaponId => _shown;

        void Awake() => _combatant = GetComponent<Combatant>();

        void Update()
        {
            int id = _combatant.ActiveWeaponId;
            if (id == _shown) return;
            _shown = id;
            for (int i = 0; i < _modelsByWeaponId.Length; i++)
                if (_modelsByWeaponId[i] != null) _modelsByWeaponId[i].SetActive(i == id);
        }
    }
}
