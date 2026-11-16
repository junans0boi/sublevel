using System.Collections;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // ADR-255: the saved PlayerAppearance decides the spawned player's model and colour variant, and a remote client sees the same model.
    // No sound. The host's saved look is set before the match starts; the remote client (MultiHarness) holds no look of its own.
    public class PlayerLookPlayTests
    {
        MultiHarness _net;
        RemoteClient _remote;
        string _saved;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _saved = PlayerPrefs.GetString(PlayerAppearanceStore.Key, "");
            PlayerAppearanceStore.Save(new PlayerAppearance { ModelId = "UbcFemale", Variant = 2 });
            _net = new MultiHarness { Log = Debug.Log };
            yield return _net.StartHost("TestBox");
            yield return _net.AddClient(c => _remote = c);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (string.IsNullOrEmpty(_saved)) PlayerAppearanceStore.Clear(); else PlayerPrefs.SetString(PlayerAppearanceStore.Key, _saved);
            yield return _net.Teardown();
        }

        static Color Tint(CharacterDef def, int variant) => def.Variants[variant].Materials[0].GetColor("_BaseColor");

        [UnityTest]
        public IEnumerator SavedLook_DecidesTheHostsModelAndVariant_AndTheRemoteSeesTheSame()
        {
            var host = _net.HostPlayer;
            var hostLook = host.GetComponent<CombatantLookView>();
            Assert.IsNotNull(hostLook, "Player prefabs carry CombatantLookView");
            Assert.AreEqual("UbcFemale", hostLook.ModelId, "the spawned prefab is Player_UbcFemale");
            Assert.AreEqual(2, hostLook.VariantApplied, "variant index from the saved look");
            Assert.AreEqual("UbcFemale:2:0:0:0", host.GetComponent<CombatantLook>().Look.Value, "the server holds the look text it spawned with");
            var def = host.GetComponentInChildren<CharacterDef>();
            Assert.AreEqual(Tint(def, 2), def.Renderers.SelectMany(r => r.sharedMaterials).First().GetColor("_BaseColor"), "the renderer wears variant 2");

            var replica = MultiHarness.ReplicaOf(_net.Remote0(), host);
            yield return NetHarness.WaitUntil(() => replica != null || (replica = MultiHarness.ReplicaOf(_net.Remote0(), host)) != null, "the host's body on the remote client");
            var seen = replica.GetComponent<CombatantLookView>();
            yield return NetHarness.WaitUntil(() => seen.VariantApplied == 2, "the remote applies the variant");
            Assert.AreEqual("UbcFemale", seen.ModelId, "the remote client sees the same model");
            var remoteDef = replica.GetComponentInChildren<CharacterDef>();
            Assert.AreEqual(Tint(def, 2), remoteDef.Renderers.SelectMany(r => r.sharedMaterials).First().GetColor("_BaseColor"), "and the same colour");
            Assert.AreEqual("MixamoSwat", _remote.Player.GetComponentInChildren<CharacterDef>().Id, "a client that sent no look gets the default model");
        }
    }
}
