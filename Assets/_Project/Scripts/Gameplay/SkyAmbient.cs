using UnityEngine;

namespace Game.Gameplay
{
    // Scenes are built without a lighting bake, so the sky's ambient light has to be computed when the scene starts.
    public class SkyAmbient : MonoBehaviour
    {
        void Start() => DynamicGI.UpdateEnvironment();
    }
}
