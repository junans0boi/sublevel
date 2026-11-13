using UnityEngine;

namespace Game.Gameplay
{
    // Shows one code-built melee weapon (MeleeMeshes, ADR-073) on this object. The mesh is not an asset: it is
    // made when the object first wakes (the builders call Build themselves to measure it, then Clear before saving).
    public class MeleeModel : MonoBehaviour
    {
        [SerializeField] int _weaponId;
        // Metal, grip, paint: MeleeMeshes' sub-mesh order.
        [SerializeField] Material[] _materials = new Material[0];

        public int WeaponId => _weaponId;

        void Awake() => Build();

        public void Setup(int weaponId, Material[] materials)
        {
            _weaponId = weaponId;
            _materials = materials;
        }

        public void Build()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = MeleeMeshes.Model(_weaponId);
            if (_materials != null && _materials.Length > 0) renderer.sharedMaterials = _materials;
        }

        // Drops the mesh reference so a prefab is not saved pointing at a mesh that only exists in memory.
        public void Clear()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter != null) filter.sharedMesh = null;
        }
    }
}
