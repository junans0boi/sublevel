using System.IO;
using FishNet.Object;
using Game.Net;
using UnityEditor;
using UnityEngine;

public static class NetSmokeFixtures
{
    const string Dir = "Assets/_Project/Tests/PlayMode/Resources/NetSmoke";
    const string PrefabPath = Dir + "/SmokeNetObject.prefab";

    // Run headless: tools/unity.sh run NetSmokeFixtures.Create
    public static void Create()
    {
        Directory.CreateDirectory(Dir);
        var go = new GameObject("SmokeNetObject");
        go.AddComponent<NetworkObject>();
        go.AddComponent<SmokeNetObject>();
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
