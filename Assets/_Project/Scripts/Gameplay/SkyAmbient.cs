using UnityEngine;

namespace Game.Gameplay
{
    // Scenes are built without a lighting bake, so the sky's ambient light has to be computed when the scene starts.
    // The generated World also gets a time of day and weather (EnvironmentView, ADR-119).
    public class SkyAmbient : MonoBehaviour
    {
        void Start()
        {
            DynamicGI.UpdateEnvironment();
            if (WorldInfo.Current != null && GetComponent<EnvironmentView>() == null) gameObject.AddComponent<EnvironmentView>();
            if (WorldInfo.Current != null && GetComponent<AmbientBeds>() == null) gameObject.AddComponent<AmbientBeds>();
            // Ground reaction, wildlife and the night sky (#205-#208, ADR-163).
            if (WorldInfo.Current == null) return;
            if (GetComponent<GroundFx>() == null) gameObject.AddComponent<GroundFx>();
            if (GetComponent<Wildlife>() == null) gameObject.AddComponent<Wildlife>();
            if (GetComponent<NightFx>() == null) gameObject.AddComponent<NightFx>();
        }
    }
}
