using Game.Net;
using UnityEditor;
using UnityEngine;

// #148 (ADR-116): bots carry and use bandages, kits and boost items, so the Bot prefab needs CombatantHealing like the
// player's. Patches the saved prefab in place (no full test box rebuild). Idempotent.
// Run headless: tools/unity.sh run BotPrefabPatch.Apply
public static class BotPrefabPatch
{
    const string BotPrefab = "Assets/_Project/Prefabs/M1/Bot.prefab";

    public static void Apply()
    {
        var root = PrefabUtility.LoadPrefabContents(BotPrefab);
        try
        {
            if (root.GetComponent<CombatantHealing>() == null) root.AddComponent<CombatantHealing>();
            PrefabUtility.SaveAsPrefabAsset(root, BotPrefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[BotPrefabPatch] Bot prefab has CombatantHealing");
    }
}
