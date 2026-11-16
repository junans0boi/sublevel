using System;
using System.Collections;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // The menu shows exactly one screen at a time: every pair of screens, in both orders, with the 3D lobby shared behind.
    // (A tab switch that skipped hiding the old screen left two on top of each other in the player.) No sound is played.
    public class MenuScreensTests
    {
        static IEnumerator Load()
        {
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null, "main menu", null, 20f);
            yield return null;
        }

        [TearDown]
        public void TearDown() => PlayerPrefs.DeleteKey(UiFx.ReducedKey);

        [UnityTest, Timeout(120000)]
        public IEnumerator EveryScreenPair_LeavesExactlyOneRoot()
        {
            yield return Load();
            var menu = MainMenu.Instance;
            var screens = new (string name, Action open, Func<bool> shown)[]
            {
                ("lobby", menu.Back, () => menu.LobbyHomeShown),
                ("training", menu.ShowTrainingEntry, () => menu.TrainingEntryShown),
                ("character", menu.ShowCharacter, () => menu.CharacterShown),
                ("settings", menu.ShowSettings, () => menu.SettingsShown),
                ("credits", menu.ShowCredits, () => menu.CreditsShown),
                ("custom", menu.ShowCustom, () => menu.CustomShown),
            };
            foreach (var a in screens)
                foreach (var b in screens)
                {
                    a.open();
                    Assert.AreEqual(1, menu.ActiveScreenRoots, a.name + " alone");
                    b.open();
                    Assert.AreEqual(1, menu.ActiveScreenRoots, a.name + " -> " + b.name + ": one root");
                    Assert.IsTrue(b.shown(), a.name + " -> " + b.name + ": the target is the one shown");
                    Assert.IsNotNull(menu.Stage, "the 3D lobby stays");
                    Assert.IsFalse(menu.ModalShown);
                    yield return null;
                }
            // A modal opened over a screen closes when the screen changes.
            menu.ShowQuit();
            menu.ShowSettings();
            Assert.IsFalse(menu.ModalShown);
            Assert.AreEqual(1, menu.ActiveScreenRoots);
            menu.Back();
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TopBarTabs_SwitchScreens_WithoutOverlap()
        {
            yield return Load();
            var menu = MainMenu.Instance;
            foreach (var act in new[] { "char", "tutorial", "settings", "credits", "lobby", "char", "credits", "tutorial", "lobby" })
            {
                menu.OnMock(new MockScreen.Item { go = act });
                Assert.AreEqual(1, menu.ActiveScreenRoots, "tab " + act);
                yield return null;
            }
        }
    }
}
