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
        public static GameRules Rules => _rules ??= LoadRules();

        // Rule sets that live in their own static `Current` (so pure code runs without a data file) follow the data.
        static GameRules LoadRules()
        {
            var rules = Load<GameRules>("GameData/rules");
            StanceRules.Current = rules.Stance;
            FallRules.Current = rules.Fall;
            FreeLookRules.Current = rules.FreeLook;
            ArmorRules.Current = rules.Armor;
            CrateRules.Current = rules.Crate;
            ThrowableRules.Current = rules.Throwables;
            BotRules.Current = rules.Bot;
            return rules;
        }

        // Before anything reads a `Current`: components may look at one without touching Rules first.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LoadAtStart()
        {
            _rules = null;
            _ = Rules;
        }
        public static LootTable Loot => _loot ??= Load<LootTable>("GameData/loot");

        static T Load<T>(string path)
        {
            var text = Resources.Load<TextAsset>(path);
            if (text == null) throw new System.IO.FileNotFoundException("Missing data file: Resources/" + path);
            return JsonUtility.FromJson<T>(text.text);
        }
    }
}
