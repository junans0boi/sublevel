using System.Linq;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Tests
{
    // ADR-257: 20 stations of 4 elevators = 80 deployment slots in the built World scene, one player an elevator, no two cars on the same spot.
    public class WorldElevatorSlotTests
    {
        [Test]
        public void The_world_has_80_elevators_and_80_player_spawns_that_do_not_overlap()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/World.unity", OpenSceneMode.Single);
            try
            {
                var info = Object.FindFirstObjectByType<WorldInfo>();
                Assert.AreEqual(80, info.Elevators.Count);
                var spawns = new SerializedObject(Object.FindFirstObjectByType<MatchDirector>()).FindProperty("_playerSpawns");
                Assert.AreEqual(80, spawns.arraySize, "deployment slots");
                var points = Enumerable.Range(0, spawns.arraySize).Select(i => ((Transform)spawns.GetArrayElementAtIndex(i).objectReferenceValue).position).ToArray();
                for (int i = 0; i < points.Length; i++)
                    for (int j = i + 1; j < points.Length; j++)
                        Assert.Greater(Vector3.Distance(points[i], points[j]), 3.5f, $"slots {i} and {j}");
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }
    }
}
