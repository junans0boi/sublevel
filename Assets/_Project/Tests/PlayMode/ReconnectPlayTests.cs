using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #166: a dropped client's combatant waits for the same session and is then given to a bot.
    public class ReconnectPlayTests
    {
        const string LogDir = "Logs/net-reconnect";
        MultiHarness _net;
        float _reconnectSeconds;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

        void Log(string line) { Debug.Log(line); _log.AppendLine(line); }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _log.Clear();
            _reconnectSeconds = MatchRules.Current.ReconnectSeconds;
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            MatchRules.Current.ReconnectSeconds = _reconnectSeconds;
            yield return _net.Teardown();
        }

        static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

        [UnityTest]
        public IEnumerator DropAndRejoin_WithinTheTime_GivesTheSameCombatantBack_StateIntact()
        {
            MatchRules.Current.ReconnectSeconds = 30f;
            RemoteClient remote = null;
            yield return _net.AddClient(c => remote = c, "token-a");
            var onHost = MultiHarness.ReplicaOf(_net.Host, remote.Player);
            int id = onHost.ObjectId;
            remote.Input.Frame.Move = new Vector2(0, 1);
            yield return Seconds(1f);
            remote.Input.Frame.Move = Vector2.zero;
            yield return Seconds(0.5f);
            onHost.ServerTakeDamage(30f);
            yield return Seconds(0.3f);
            float hp = onHost.Hp.Value;
            int slot0 = onHost.Slot0.Value, mag0 = onHost.Mag0.Value, active = onHost.ActiveSlot.Value;
            var where = onHost.transform.position;
            Assert.Less(hp, 100f);

            var keeper = MatchDirector.Instance.Sessions;
            yield return NetHarness.WaitUntil(() => keeper.SessionCount == 1, "server knows the client's token", Log);
            yield return _net.DropClient(remote);
            yield return NetHarness.WaitUntil(() => keeper.HeldCount == 1, "combatant held after the drop", Log);
            yield return Seconds(0.5f);
            Assert.IsTrue(onHost != null && onHost.NetworkObject.IsSpawned, "the combatant stays in the match while held");
            Assert.IsFalse(onHost.IsOwner);

            RemoteClient back = null;
            yield return _net.AddClient(c => back = c, "token-a");
            Assert.AreEqual(id, back.Player.ObjectId, "same combatant, not a new one");
            Assert.IsTrue(back.Player.IsOwner);
            Assert.AreEqual(0, keeper.HeldCount);
            Assert.AreEqual(hp, back.Player.Hp.Value, 0.01f);
            Assert.AreEqual(slot0, back.Player.Slot0.Value);
            Assert.AreEqual(mag0, back.Player.Mag0.Value);
            Assert.AreEqual(active, back.Player.ActiveSlot.Value);
            Assert.Less(Vector3.Distance(where, back.Player.transform.position), 0.5f, "it did not move while the player was away");
            Assert.AreEqual(2, MultiHarness.PlayersSeenBy(_net.Host).Count, "no extra player was spawned");

            var from = onHost.transform.position;
            back.Input.Frame.Move = new Vector2(0, 1);
            yield return Seconds(1f);
            back.Input.Frame.Move = Vector2.zero;
            yield return Seconds(0.5f);
            Assert.Greater(Vector3.Distance(from, onHost.transform.position), 2f, "the returning player controls it again");
        }

        [UnityTest]
        public IEnumerator DropAndStayAway_AfterTheTime_ABotTakesTheCombatantOver_AndOthersStillSeeIt()
        {
            MatchRules.Current.ReconnectSeconds = 4f;
            RemoteClient other = null, dropper = null;
            yield return _net.AddClient(c => other = c, "token-other");
            yield return _net.AddClient(c => dropper = c, "token-drop");
            var onHost = MultiHarness.ReplicaOf(_net.Host, dropper.Player);
            int id = onHost.ObjectId;
            var botType = Type.GetType("Game.AI.BotController, Game.AI");
            Assert.IsNotNull(botType);
            Assert.IsNull(onHost.GetComponent(botType));

            var keeper = MatchDirector.Instance.Sessions;
            yield return NetHarness.WaitUntil(() => keeper.SessionCount == 2, "server knows both tokens", Log);
            yield return _net.DropClient(dropper);
            yield return NetHarness.WaitUntil(() => keeper.HeldCount == 1, "combatant held after the drop", Log);
            Assert.IsNull(onHost.GetComponent(botType), "still the player's during the hold");
            yield return NetHarness.WaitUntil(() => keeper.HeldCount == 0, "hold over", Log, 15f);
            Assert.IsNotNull(onHost.GetComponent(botType), "a bot took the combatant over");

            var onOther = MultiHarness.ReplicaOf(other.Manager, onHost);
            Assert.IsNotNull(onOther, "the other client still sees the combatant");
            Assert.AreEqual(id, onOther.ObjectId);
            Assert.IsFalse(onOther.Dead.Value);

            // The same session after the hand-over is a newcomer: a fresh combatant, the bot keeps its own.
            RemoteClient late = null;
            yield return _net.AddClient(c => late = c, "token-drop");
            Assert.AreNotEqual(id, late.Player.ObjectId);
            Assert.IsNotNull(onHost.GetComponent(botType));
        }
    }
}
