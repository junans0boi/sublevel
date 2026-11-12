using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Tuning data loaded from JSON under Resources/GameData (ADR-032).
    public static class GameData
    {
        static WeaponTable _weapons;
        static LootTable _loot;
        static GameRules _rules;

        public static WeaponTable Weapons => _weapons ??= Load<WeaponTable>("GameData/weapons");
        public static GameRules Rules => _rules ??= Load<GameRules>("GameData/rules");
        public static LootTable Loot => _loot ??= Load<LootTable>("GameData/loot");

        static T Load<T>(string path)
        {
            var text = Resources.Load<TextAsset>(path);
            if (text == null) throw new System.IO.FileNotFoundException("Missing data file: Resources/" + path);
            return JsonUtility.FromJson<T>(text.text);
        }
    }
}
