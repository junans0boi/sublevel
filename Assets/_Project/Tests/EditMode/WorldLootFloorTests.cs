using Game.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Tests
{
    // ADR-256 (b): in the built World scene (the same colliders a player build has) every loot point has a floor 0.2 to 0.4 m (WorldBuilder.LootLift 0.3) under it,
    // so the pickup, which LootView sets down on the first solid thing below, ends up on the floor and not in it.
    public class WorldLootFloorTests
    {
        [Test]
        public void Every_loot_point_has_its_floor_a_pickup_lift_below()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/World.unity", OpenSceneMode.Single);
            try
            {
                Physics.SyncTransforms();
                var spawns = Object.FindFirstObjectByType<MatchDirector>().LootSpawns;
                Assert.Greater(spawns.Length, 800);
                int bad = 0; string first = null;
                foreach (var t in spawns)
                {
                    var p = t.position;
                    bool hit = Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var h, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    float lift = hit ? p.y - h.point.y : -1f;
                    if (!hit || lift < 0.2f || lift > 0.4f) { bad++; if (first == null) first = $"{t.name} at {p}: floor {(hit ? lift.ToString("F2") + " m below" : "not found")}"; }
                }
                Assert.AreEqual(0, bad, first);
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }
    }
}
