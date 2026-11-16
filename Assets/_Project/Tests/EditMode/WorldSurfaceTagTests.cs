using System.Linq;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Tests
{
    // #246 T5: every group of building colliders in World.unity carries a SurfaceIdTag (no name parsing needed to tell the floor).
    public class WorldSurfaceTagTests
    {
        [Test]
        public void Every_building_collider_group_is_tagged_and_floors_keep_their_footsteps()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/World.unity", OpenSceneMode.Single);
            try
            {
                var groups = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(t => t.name == "Colliders" && t.parent != null && (t.parent.name.StartsWith("Lot") || t.parent.name.StartsWith("Station"))).ToArray();
                Assert.Greater(groups.Length, 60, "building collider groups");
                foreach (var g in groups)
                {
                    var tag = g.GetComponent<SurfaceIdTag>();
                    Assert.NotNull(tag, g.parent.name);
                    Assert.Greater(g.GetComponents<BoxCollider>().Length, 0, g.parent.name + " has an empty collider group");
                }
                // a wooden house has a wooden group, a hall a concrete one (SoundRules.BuildingSurface, as before)
                var house = groups.First(g => g.parent.name.EndsWith("_House"));
                Assert.IsTrue(house.parent.GetComponentsInChildren<SurfaceIdTag>().Any(t => t.Id == SurfaceId.Wood), "House floors are wood");
                var hall = groups.First(g => g.parent.name.EndsWith("_Warehouse"));
                Assert.IsTrue(hall.parent.GetComponentsInChildren<SurfaceIdTag>().Any(t => t.Id == SurfaceId.Concrete), "Warehouse floors are concrete");
                Assert.IsTrue(hall.parent.GetComponentsInChildren<SurfaceIdTag>().Any(t => t.Id == SurfaceId.Metal), "Warehouse stairs are metal");
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }
    }
}
