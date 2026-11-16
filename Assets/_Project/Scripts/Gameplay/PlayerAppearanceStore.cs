using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // The local player's saved look (PlayerPrefs "player.appearance"), read by the lobby stage and by the match spawn.
    public static class PlayerAppearanceStore
    {
        public const string Key = "player.appearance";

        // The saved look, or the default model when nothing was saved. Old 외형 keys (ui.char.index/color) are read once.
        public static PlayerAppearance Load()
        {
            string s = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(s)) return PlayerAppearance.Parse(s);
            if (PlayerPrefs.HasKey("ui.char.index")) return PlayerAppearance.FromIndex(PlayerPrefs.GetInt("ui.char.index"), 0);
            return PlayerAppearance.Default;
        }

        public static void Save(PlayerAppearance appearance)
        {
            PlayerPrefs.SetString(Key, appearance.Serialize());
            PlayerPrefs.Save();
        }

        public static void Clear() => PlayerPrefs.DeleteKey(Key);
    }
}
