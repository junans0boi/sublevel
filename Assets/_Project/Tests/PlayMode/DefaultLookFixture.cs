using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

// The saved look (PlayerPrefs "player.appearance", also read by the lobby) decides which model the local player spawns with (ADR-255), and
// PlayerPrefs are editor-wide: a look left there by anything else would change every PlayMode test's character. Pin the default model for the
// whole PlayMode run and put the stored value back afterwards. Tests that need another look set it themselves.
[SetUpFixture]
public class DefaultLookFixture
{
    string _saved;

    [OneTimeSetUp]
    public void Pin()
    {
        _saved = PlayerPrefs.GetString(PlayerAppearanceStore.Key, "");
        PlayerAppearanceStore.Save(PlayerAppearance.Default);
    }

    [OneTimeTearDown]
    public void Restore()
    {
        if (string.IsNullOrEmpty(_saved)) PlayerAppearanceStore.Clear(); else PlayerPrefs.SetString(PlayerAppearanceStore.Key, _saved);
        PlayerPrefs.Save();
    }
}
