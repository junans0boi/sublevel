using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // The waiting island's race hall is drawn in full only for a camera in the pit or the hall (perf, ADR-505, the master's 11.6 ms p99
    // from the island gate): for any other camera the hall's meshes that start at RaceTrack.GateViewX or further east, and its lamps there,
    // or far north or south of the gate, are off. Per camera, just before it renders, so a probe camera in the hall and the player's camera on the island each get theirs.
    public sealed class RaceHallCulling : MonoBehaviour
    {
        readonly List<Renderer> _far = new List<Renderer>();
        readonly List<Light> _farLights = new List<Light>();
        bool _collected, _shown = true;

        void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeCamera;

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            Show(true);
        }

        // Late: the meshes come from the blob (WorldMeshBank) when the scene loads, so their bounds are known only then.
        void Collect()
        {
            _collected = true;
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                var b = r.bounds;
                if (!RaceTrack.SeenFromIsland(b.min.x - WaitingIsland.CentreX, b.min.z - WaitingIsland.CentreZ, b.max.z - WaitingIsland.CentreZ)) _far.Add(r);
            }
            foreach (var l in GetComponentsInChildren<Light>(true))
                if (l.transform.position.x - WaitingIsland.CentreX >= RaceTrack.GateViewX - 20f) _farLights.Add(l);   // all but the pit's lamp
        }

        void BeforeCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!_collected) Collect();
            var p = cam.transform.position;
            bool whole = RaceTrack.SeesWholeHall(p.x, p.z);
            if (whole != _shown) Show(whole);
        }

        void Show(bool on)
        {
            _shown = on;
            foreach (var r in _far) if (r != null) r.enabled = on;
            foreach (var l in _farLights) if (l != null) l.enabled = on;
        }
    }
}
