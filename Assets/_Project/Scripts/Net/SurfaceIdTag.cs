using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // What the colliders on this GameObject are made of (#246, docs/research/12_surface_tag_design.md). WorldBuilder puts one on
    // every group of building colliders, so footsteps and bullet impacts read the surface instead of parsing the building's name.
    public class SurfaceIdTag : MonoBehaviour
    {
        public SurfaceId Id = SurfaceId.Concrete;
    }
}
