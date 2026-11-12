using System.Collections;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public class NetSmokeTests
    {
        NetHarness _net;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var prefab = Resources.Load<NetworkObject>("NetSmoke/SmokeNetObject");
            Assert.IsNotNull(prefab, "Run `tools/unity.sh run NetSmokeFixtures.Create` to build the fixture prefab");
            _net = new NetHarness(prefab);
            yield return _net.Connect(Debug.Log);
        }

        [UnityTearDown]
        public IEnumerator TearDown() => _net.Teardown();

        [UnityTest]
        public IEnumerator ServerAndClient_Connect()
        {
            Assert.IsTrue(_net.Server.IsServerStarted);
            Assert.IsTrue(_net.Client.IsClientStarted);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ServerAndClient_ConnectAgainAfterTeardown()
        {
            Assert.IsTrue(_net.Client.IsClientStarted);
            yield return null;
        }
    }
}
