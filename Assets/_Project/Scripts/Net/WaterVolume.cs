using System.Collections.Generic;
using UnityEngine;

namespace Game.Net
{
    // A body of water: a rectangle (x, z) with its surface at one height (ADR-112). The floor under it is whatever the
    // ground is: the depth at a point is the surface minus the highest solid below it. Registered while enabled.
    public class WaterVolume : MonoBehaviour
    {
        public static readonly List<WaterVolume> All = new List<WaterVolume>();
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        public Rect Area { get; private set; }
        public float SurfaceY { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        // `centre`: x, z of the middle; `size`: x, z extent.
        public static WaterVolume Create(Transform parent, Vector2 centre, Vector2 size, float surfaceY, bool showSurface)
        {
            var go = new GameObject("Water");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(centre.x, surfaceY, centre.y);
            var water = go.AddComponent<WaterVolume>();
            water.Area = new Rect(centre - size * 0.5f, size);
            water.SurfaceY = surfaceY;
            if (showSurface) water.AddSurface(size);
            return water;
        }

        // A translucent sheet at the surface (no collider: bodies, shots and sight lines go through water for now).
        void AddSurface(Vector2 size)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Surface";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;
            var material = new Material(shader) { name = "WaterSurface" };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(0.10f, 0.34f, 0.45f, 0.55f));
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        // Water depth at a point (feet); 0 outside every volume. `surface` and `ground` describe the volume it is in.
        public static float DepthAt(Vector3 position, out float surface, out float ground)
        {
            surface = ground = position.y;
            for (int i = 0; i < All.Count; i++)
            {
                var w = All[i];
                if (w == null || !w.Area.Contains(new Vector2(position.x, position.z))) continue;
                float top = w.SurfaceY;
                if (position.y > top + 3f) continue;
                // The floor: the highest solid below the surface that is not a body.
                int n = Physics.RaycastNonAlloc(new Vector3(position.x, top + 0.5f, position.z), Vector3.down, Hits, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float floor = top - 60f;
                for (int k = 0; k < n; k++)
                {
                    if (Hits[k].collider.GetComponentInParent<Combatant>() != null || Hits[k].collider.GetComponentInParent<WaterVolume>() != null) continue;
                    floor = Mathf.Max(floor, Hits[k].point.y);
                }
                surface = top;
                ground = floor;
                return Core.Swim.Depth(top, floor);
            }
            return 0f;
        }
    }
}
