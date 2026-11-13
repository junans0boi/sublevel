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
    // #171: the measured numbers of the network status (ping, packet loss, server tick) with a remote client, and the
    // on-screen line with its setting.
    public class NetStatusPlayTests
    {
        const string LogDir = "Logs/net-status";
        MultiHarness _net;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

        void Log(string line) { Debug.Log(line); _log.AppendLine(line); }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _log.Clear();
            GameSettings.SetAndSave(SettingId.NetStats, 0f);
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            GameSettings.SetAndSave(SettingId.NetStats, 0f);
            yield return _net.Teardown();
        }

        // The rect a UI element covers on the screen, pixels (overlay canvases use screen coordinates as world positions).
        static Rect Screen(RectTransform rect)
        {
            var c = new Vector3[4];
            rect.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        static IEnumerator Seconds(float s) { float end = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < end) yield return null; }

        [UnityTest, Timeout(120000)]
        public IEnumerator RemoteClient_ReadsPingLossAndServerTick_FromTheServersPulses()
        {
            RemoteClient remote = null;
            yield return _net.AddClient(c => remote = c);
            var status = NetStatusClient.Attach(remote.Manager);
            yield return NetHarness.WaitUntil(() => status.HasPulses, "pulses arriving", Log, 10f);
            yield return Seconds(2f);
            Log($"clean: ping {status.PingMs} ms, loss {status.Loss:P1}, tick {status.ServerTick}, rate {status.TickRate}");
            Assert.AreEqual(0f, status.Loss, 0.005f, "no loss on localhost");
            Assert.Less(status.PingMs, 60);
            Assert.Greater(status.TickRate, 0);
            uint tick = status.ServerTick;
            Assert.Greater(tick, 0u);
            yield return Seconds(1f);
            Assert.Greater(status.ServerTick, tick, "the server tick moves on");

            // 100 ms round trip and a fifth of the unreliable packets lost: the window (10 s) fills with the new conditions.
            _net.Simulate(remote, 100, 0.2);
            yield return Seconds(PulseLoss.WindowSeconds + 3f);
            Log($"simulated: ping {status.PingMs} ms, loss {status.Loss:P1}, tick {status.ServerTick}");
            Assert.GreaterOrEqual(status.PingMs, 60, "the 100 ms round trip shows as ping");
            Assert.Less(status.PingMs, 250);
            Assert.Greater(status.Loss, 0.05f, "20 % loss is seen");
            Assert.Less(status.Loss, 0.40f);
            Assert.AreEqual(NetGrade.Bad, NetStatusRules.Grade(status.PingMs, status.Loss));
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Line_IsHiddenByDefault_ShowsWithTheSetting_AndReadsTheLiveNumbers()
        {
            yield return NetHarness.WaitUntil(() => NetStatusView.Instance != null, "net status view installed", Log, 10f);
            var view = NetStatusView.Instance;
            yield return Seconds(0.5f);
            Assert.IsFalse(view.Visible, "off by default");

            GameSettings.SetAndSave(SettingId.NetStats, 1f);
            yield return NetHarness.WaitUntil(() => view.Visible && view.Text.StartsWith("핑 "), "line shown with numbers", Log, 5f);
            yield return NetHarness.WaitUntil(() => view.Text.Contains("%"), "loss and tick read from the pulses", Log, 10f);
            Log("line: " + view.Text);
            StringAssert.Contains("ms", view.Text);
            StringAssert.Contains("틱", view.Text);
            Assert.AreEqual(NetGrade.Good, view.Grade);
            // Strict about the screen: not on top of the kill feed's block or the alive/kill cards above it.
            var line = Screen(view.Panel);
            Assert.IsFalse(line.Overlaps(Screen(KillFeed.Instance.Area)), "clear of the kill feed block");
            Assert.Less(line.yMax, Screen(KillFeed.Instance.Area).yMin + 0.5f, "under the kill feed block");
            Assert.GreaterOrEqual(line.xMin, 0f);
            Assert.LessOrEqual(line.xMax, UnityEngine.Screen.width + 0.5f, "on screen");
            Assert.GreaterOrEqual(line.yMin, 0f);

            GameSettings.SetAndSave(SettingId.NetStats, 0f);
            yield return Seconds(0.3f);
            Assert.IsFalse(view.Visible, "off again");
        }
    }
}
