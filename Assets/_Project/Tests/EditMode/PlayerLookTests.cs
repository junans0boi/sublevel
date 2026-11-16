using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    // ADR-235: every roster model has a Player_<id> prefab with the look components (needs TestBoxBuilder.Build).
    public class PlayerLookTests
    {
        [Test]
        public void EveryAppearanceModel_HasAPlayerPrefab_WithItsCharacterAndLookComponents()
        {
            foreach (var id in PlayerAppearance.ModelIds)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/M1/Player_{id}.prefab");
                Assert.IsNotNull(prefab, $"Player_{id}.prefab (run TestBoxBuilder.Build)");
                var def = prefab.GetComponentInChildren<CharacterDef>(true);
                Assert.IsNotNull(def, id + " CharacterDef");
                Assert.AreEqual(id, def.Id, "the prefab wears the model its name says");
                Assert.IsNotNull(prefab.GetComponent<CombatantLook>(), id + " CombatantLook");
                Assert.IsNotNull(prefab.GetComponent<CombatantLookView>(), id + " CombatantLookView");
                Assert.Greater(def.Variants.Length, 0, id + " has colour variants");
            }
        }

        [Test]
        public void Look_RoundTripsTheVariant_AndUnknownModelsReadAsTheDefault()
        {
            var look = PlayerAppearance.Parse(new PlayerAppearance { ModelId = "MixamoKate", Variant = 3 }.Serialize());
            Assert.AreEqual("MixamoKate", look.ModelId);
            Assert.AreEqual(3, look.Variant);
            Assert.AreEqual(PlayerAppearance.DefaultModelId, PlayerAppearance.Parse("Nobody:1:0:0:0").ModelId);
        }
    }
}
