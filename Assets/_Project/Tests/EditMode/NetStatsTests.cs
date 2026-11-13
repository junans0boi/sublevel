using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #171: packet loss from numbered pulses, the grade and the text of the network status line.
    public class NetStatsTests
    {
        [Test]
        public void NoGaps_NoLoss()
        {
            var loss = new PulseLoss();
            for (uint i = 0; i < 50; i++) loss.Add(i, i * 0.1f);
            Assert.AreEqual(0f, loss.Loss, 1e-6f);
            Assert.IsTrue(loss.HasData);
        }

        [Test]
        public void MissingNumbers_AreTheLoss()
        {
            var loss = new PulseLoss();
            for (uint i = 0; i < 100; i++) if (i % 10 != 5) loss.Add(i, i * 0.1f * 0.9f); // 10 % never arrive (inside the window)
            Assert.AreEqual(0.10f, loss.Loss, 0.02f);
        }

        [Test]
        public void LateAndRepeatedNumbers_AreIgnored()
        {
            var loss = new PulseLoss();
            loss.Add(1, 0f); loss.Add(2, 0.1f); loss.Add(2, 0.15f); loss.Add(1, 0.2f); loss.Add(3, 0.3f);
            Assert.AreEqual(3, loss.Received);
            Assert.AreEqual(0f, loss.Loss, 1e-6f);
        }

        [Test]
        public void OldPulses_LeaveTheWindow_SoAnEarlyGapIsForgotten()
        {
            var loss = new PulseLoss();
            loss.Add(0, 0f);
            loss.Add(50, 5f);    // 49 lost early on
            Assert.Greater(loss.Loss, 0.9f);
            for (uint i = 51; i < 300; i++) loss.Add(i, 5f + (i - 50) * 0.1f);
            Assert.Less(loss.Loss, 0.01f, "a gap older than the window no longer counts");
        }

        [Test]
        public void TooFewPulses_ReportNoLossAndNoData()
        {
            var loss = new PulseLoss();
            Assert.IsFalse(loss.HasData);
            loss.Add(7, 0f);
            Assert.IsFalse(loss.HasData);
            Assert.AreEqual(0f, loss.Loss);
        }

        [TestCase(20, 0f, NetGrade.Good)]
        [TestCase(60, 0f, NetGrade.Fair)]
        [TestCase(119, 0.005f, NetGrade.Fair)]
        [TestCase(120, 0f, NetGrade.Bad)]
        [TestCase(20, 0.01f, NetGrade.Fair)]
        [TestCase(20, 0.04f, NetGrade.Bad)]
        [TestCase(100, 0.05f, NetGrade.Bad)]
        public void Grade_IsTheWorseOfPingAndLoss(int ping, float loss, NetGrade expected)
            => Assert.AreEqual(expected, NetStatusRules.Grade(ping, loss));

        [Test]
        public void Format_ShowsPingLossAndTick_AndDashesBeforeTheFirstPulses()
        {
            Assert.AreEqual("핑 42 ms   손실 1.5%   틱 12345 (60/s)", NetStatusRules.Format(42, 0.015f, true, 12345, 60));
            Assert.AreEqual("핑 0 ms   손실 -   틱 -", NetStatusRules.Format(-3, 0f, false, 0, 60));
        }
    }
}
