using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #169 / ADR-133: an emote reaches everyone's screen over a real connection, ends by itself, by moving, by a hit, and
    // is refused for armed hands and too-quick requests. The poses themselves are judged in the capture shots.
    public class EmotePlayTests
    {
        const string LogDir = "Logs/net-team";
        MultiHarness _net;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

        void Log(string line) { Debug.Log(line); _log.AppendLine(line); }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _log.Clear();
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            yield return _net.Teardown();
        }

        static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

        [UnityTest]
        public IEnumerator Emote_ShowsOnTheOtherClient_EndsByItself_AndByMoving()
        {
            RemoteClient remote = null;
            yield return _net.AddClient(c => remote = c);
            var host = _net.HostPlayer;
            var hostOnClient = MultiHarness.ReplicaOf(remote.Manager, host);
            var remoteOnServer = MultiHarness.ReplicaOf(_net.Host, remote.Player);

            // The host waves (weapon away first): the remote client sees the id, and its copy of the body runs the pose clock.
            host.ServerSetHolstered(true);
            host.CmdEmote((byte)Emote.Wave);
            yield return NetHarness.WaitUntil(() => hostOnClient.EmoteId.Value == (byte)Emote.Wave, "the other client sees the emote", Log);
            yield return null;
            yield return null; // a LateUpdate sees the new id
            var anim = hostOnClient.GetComponent<CombatantAnimation>();
            if (anim != null) Assert.GreaterOrEqual(anim.EmoteTime, 0f, "the pose clock runs on the observer");
            yield return Seconds(Emotes.Duration(Emote.Wave) + 0.6f);
            Assert.AreEqual(0, host.EmoteId.Value, "ended by itself on the server");
            yield return NetHarness.WaitUntil(() => hostOnClient.EmoteId.Value == 0, "and on the other client", Log);

            // The remote player dances through its own client (which puts the weapon away), then walks: the emote ends.
            yield return Seconds(0.5f);
            remote.Controller.StartEmote(Emote.Dance);
            yield return NetHarness.WaitUntil(() => remoteOnServer.EmoteId.Value == (byte)Emote.Dance, "the server plays the remote player's emote", Log);
            yield return NetHarness.WaitUntil(() => MultiHarness.ReplicaOf(_net.Host, remote.Player).EmoteId.Value == (byte)Emote.Dance && remote.Player.EmoteId.Value == (byte)Emote.Dance, "the owner sees it too", Log);
            remote.Input.Frame.Move = new Vector2(0f, 1f);
            yield return NetHarness.WaitUntil(() => remoteOnServer.EmoteId.Value == 0, "walking ends it", Log);
            remote.Input.Frame.Move = Vector2.zero;
        }

        [UnityTest]
        public IEnumerator Emote_IsRefused_ForArmedHands_ForBadIds_AndTooQuickRequests_AndEndsWhenHurt()
        {
            yield return Seconds(0.3f);
            var host = _net.HostPlayer;
            if (host.WeaponInSlot(host.ActiveSlot.Value) != 0)
            {
                host.ServerSetHolstered(false);
                Assert.IsFalse(host.ServerEmote(Emote.Wave), "no emote with a weapon in the hands");
            }
            host.ServerSetHolstered(true);
            Assert.IsFalse(host.ServerEmote((Emote)99), "an id outside the wheel");
            Assert.IsTrue(host.ServerEmote(Emote.Cheer));
            Assert.AreEqual((byte)Emote.Cheer, host.EmoteId.Value);
            Assert.IsFalse(host.ServerEmote(Emote.Clap), "a second request inside the minimum interval is dropped");
            Assert.AreEqual((byte)Emote.Cheer, host.EmoteId.Value);

            yield return Seconds(0.2f);
            host.ServerTakeDamage(10f);
            yield return null;
            yield return null;
            Assert.AreEqual(0, host.EmoteId.Value, "a hit ends the emote");

            yield return Seconds(0.5f);
            Assert.IsTrue(host.ServerEmote(Emote.Point));
            host.ServerEndEmote();
            Assert.AreEqual(0, host.EmoteId.Value);
        }
    }
}
