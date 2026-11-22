using UnityEngine;
using UnityEngine.AI;

namespace Game.Gameplay
{
    // Registers a zone's baked navmesh while the zone is loaded.
    public class ZoneNavMesh : MonoBehaviour
    {
        [SerializeField] NavMeshData _data;

        NavMeshDataInstance _instance;

        void OnEnable()
        {
            if (_data != null) using (LoadTrace.Step("NavMesh.AddNavMeshData")) _instance = NavMesh.AddNavMeshData(_data);
        }

        void OnDisable() => _instance.Remove();
    }
}
