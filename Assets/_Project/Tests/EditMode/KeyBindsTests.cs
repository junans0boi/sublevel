using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // Key rebinding (#140): defaults, change + save, reload, conflicts, reset.
    public class KeyBindsTests
    {
        int[] _saved;

        [SetUp]
        public void Clean()
        {
            _saved = KeyBinds.SnapshotPrefs();
            KeyBinds.ResetAll();
        }

        [TearDown]
        public void Restore() => KeyBinds.RestorePrefs(_saved);

        [Test]
        public void Defaults_MatchTheOldFixedLayout()
        {
            Assert.AreEqual(KeyBinds.Letter('W'), KeyBinds.Get(BindAction.MoveForward));
            Assert.AreEqual(KeyBinds.DefaultOf(BindAction.Jump), KeyBinds.Get(BindAction.Jump));
            Assert.AreEqual(KeyBinds.MouseBase + 1, KeyBinds.Get(BindAction.Aim));
            Assert.AreEqual("우클릭", KeyBinds.Name(BindAction.Aim));
        }

        [Test]
        public void ChangedKey_IsSaved_AndSurvivesAReload()
        {
            KeyBinds.Set(BindAction.Reload, KeyBinds.Letter('Y'));
            Assert.AreEqual(1, PlayerPrefs.GetInt("keys.Reload", -1) > 0 ? 1 : 0, "written to PlayerPrefs");
            KeyBinds.Load();
            Assert.AreEqual(KeyBinds.Letter('Y'), KeyBinds.Get(BindAction.Reload));
            Assert.AreEqual("Y", KeyBinds.Name(BindAction.Reload));
        }

        [Test]
        public void SharedKey_IsReportedAsAConflict_ResetClearsIt()
        {
            Assert.IsNull(KeyBinds.ConflictOf(BindAction.Reload));
            var other = KeyBinds.Set(BindAction.Reload, KeyBinds.Letter('F'));
            Assert.AreEqual(BindAction.Interact, other);
            Assert.AreEqual(BindAction.Reload, KeyBinds.ConflictOf(BindAction.Interact));
            KeyBinds.ResetAll();
            Assert.IsNull(KeyBinds.ConflictOf(BindAction.Reload));
            Assert.AreEqual(KeyBinds.Letter('R'), KeyBinds.Get(BindAction.Reload));
            Assert.IsFalse(PlayerPrefs.HasKey("keys.Reload"), "saved value removed");
        }

        [Test]
        public void NonsenseCode_IsIgnored()
        {
            Assert.IsNull(KeyBinds.Set(BindAction.Jump, 99999));
            Assert.AreEqual(KeyBinds.DefaultOf(BindAction.Jump), KeyBinds.Get(BindAction.Jump));
            PlayerPrefs.SetInt("keys.Jump", -5);
            KeyBinds.Load();
            Assert.AreEqual(KeyBinds.DefaultOf(BindAction.Jump), KeyBinds.Get(BindAction.Jump), "a bad saved value falls back to the default");
        }
    }
}
