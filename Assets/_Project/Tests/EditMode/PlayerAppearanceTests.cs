using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class PlayerAppearanceTests
    {
        [SetUp] public void Setup() { PlayerPrefs.DeleteKey(PlayerAppearanceStore.Key); PlayerPrefs.DeleteKey("ui.char.index"); }
        [TearDown] public void Tear() { PlayerPrefs.DeleteKey(PlayerAppearanceStore.Key); PlayerPrefs.DeleteKey("ui.char.index"); }

        [Test]
        public void Roster_HasFifteenModelsWithNames()
        {
            Assert.AreEqual(15, PlayerAppearance.ModelIds.Length);
            Assert.AreEqual(PlayerAppearance.ModelIds.Length, PlayerAppearance.ModelNames.Length);
            CollectionAssert.AllItemsAreUnique(PlayerAppearance.ModelIds);
        }

        [Test]
        public void Default_IsTheDefaultModel()
        {
            Assert.AreEqual(PlayerAppearance.DefaultModelId, PlayerAppearance.Default.ModelId);
            Assert.AreEqual(0, PlayerAppearance.Default.ModelIndex);
        }

        [Test]
        public void Serialize_Parse_RoundTrips()
        {
            var a = new PlayerAppearance { ModelId = "UbcFemale", Variant = 5, Helmet = 2, Vest = 3, Pack = 1 };
            var b = PlayerAppearance.Parse(a.Serialize());
            Assert.AreEqual(a, b);
            Assert.AreEqual("UbcFemale:5:2:3:1", a.Serialize());
        }

        [Test]
        public void Parse_BadInput_GivesAValidLook()
        {
            Assert.AreEqual(PlayerAppearance.Default, PlayerAppearance.Parse(null));
            Assert.AreEqual(PlayerAppearance.Default, PlayerAppearance.Parse("garbage"));
            var a = PlayerAppearance.Parse("MixamoKate:-4:9:x");
            Assert.AreEqual("MixamoKate", a.ModelId);
            Assert.AreEqual(0, a.Variant);
            Assert.AreEqual(3, a.Helmet, "gear tier clamps to 3");
            Assert.AreEqual(0, a.Vest);
        }

        [Test]
        public void Store_SavesAndLoads_AcrossLoads()
        {
            Assert.AreEqual(PlayerAppearance.Default, PlayerAppearanceStore.Load(), "nothing saved: the default model");
            var a = new PlayerAppearance { ModelId = "MixamoBrian", Variant = 2, Helmet = 1 };
            PlayerAppearanceStore.Save(a);
            Assert.AreEqual(a, PlayerAppearanceStore.Load());
            PlayerAppearanceStore.Clear();
            Assert.AreEqual(PlayerAppearance.Default, PlayerAppearanceStore.Load());
        }

        [Test]
        public void Store_ReadsTheOldCharacterScreenKey()
        {
            PlayerPrefs.SetInt("ui.char.index", 4);
            Assert.AreEqual("MixamoBrian", PlayerAppearanceStore.Load().ModelId);
        }

        [Test]
        public void PartyMember_CarriesItsAppearance()
        {
            var mine = PlayerAppearance.FromIndex(3, 1);
            var theirs = PlayerAppearance.FromIndex(9, 0);
            var party = new PartyLobby(0, "나", mine);
            party.Join(2, "파티원", theirs);
            Assert.AreEqual(mine, party.Members[0].Appearance);
            Assert.AreEqual(theirs, party.Members[1].Appearance);
            party.SetAppearance(2, PlayerAppearance.FromIndex(14, 2));
            Assert.AreEqual("UbcFemale", party.Members[1].Appearance.ModelId);
            Assert.AreEqual(2, party.Members[1].Appearance.Variant);
        }
    }
}
