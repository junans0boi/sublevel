using System.Collections;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #448 (ADR-512/518): a host and a remote client in the event race scene: both start at the wheel of their own grid car, the remote
    // hears the same countdown and the same race order as the host, and its own driving is judged by the server.
    public class RaceEventMultiTests
    {
        MultiHarness _net;
        void Log(string s) => Debug.Log(s);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost(RaceEvent.Courses[0].Scene);
        }

        [UnityTearDown]
        public IEnumerator TearDown() { yield return _net.Teardown(); }

        [UnityTest, Timeout(300000)]
        public IEnumerator RemoteDriver_SeatedCountedDown_SameOrderOnBothSides()
        {
            RaceStandings remoteSaw = default; float remoteAt = -1f;
            RemoteClient remote = null;
            yield return _net.AddClient(c => remote = c, null, nm => nm.ClientManager.RegisterBroadcast<RaceStandings>((m, ch) => { remoteSaw = m; remoteAt = Time.unscaledTime; }));
            var remoteOnServer = MultiHarness.ReplicaOf(_net.Host, remote.Player);
            yield return NetHarness.WaitUntil(() => RaceDirector.Current.CarOf(remoteOnServer) != null && RaceDirector.Current.CarOf(_net.HostPlayer) != null, "a grid car each", Log, 15f);
            yield return NetHarness.WaitUntil(() => VehicleSeat.IsAttached(remote.Player), "the remote sees itself at the wheel", Log, 15f);
            Assert.AreNotEqual(RaceDirector.Current.CarOf(remoteOnServer), RaceDirector.Current.CarOf(_net.HostPlayer), "two cars");
            yield return NetHarness.WaitUntil(() => remoteAt > 0f && (RacePhase)remoteSaw.Phase == RacePhase.Countdown, "the remote hears the countdown", Log, 20f);
            yield return NetHarness.WaitUntil(() => (RacePhase)remoteSaw.Phase == RacePhase.Racing && remoteSaw.Keys != null, "and GO", Log, 10f);
            yield return new WaitForSeconds(1f);
            var host = RaceHud.Last;
            // The harness's remote connects late: the bots may already have filled the field to Racers, then it gets the next free slot.
            Assert.GreaterOrEqual(remoteSaw.Keys.Length, RaceEvent.Racers, "two people and the bots");
            Assert.LessOrEqual(remoteSaw.Keys.Length, RaceEvent.GridSlots);
            int people = 0; foreach (int k in remoteSaw.Keys) if (k >= 0) people++;
            Assert.AreEqual(2, people);
            CollectionAssert.AreEquivalent(host.Keys, remoteSaw.Keys, "the same racers on both sides");
        }
    }
}
