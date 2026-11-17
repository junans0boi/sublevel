using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #399, ADR-300: --simulate-ping / --simulate-jitter. Without the flags nothing changes; bad values are reported and ignored.
    public class SimulatedPingTests
    {
        static SimulatedPing P(params string[] a) => SimulatedPing.Parse(a);

        [Test]
        public void NoFlags_NothingSimulated_NoError()
        {
            foreach (var o in new[] { P(), P("x", "--port", "7770", "--dedicated"), SimulatedPing.Parse(null) })
            {
                Assert.IsFalse(o.Active);
                Assert.IsNull(o.Error);
                Assert.AreEqual(0, o.PingMs);
                Assert.AreEqual("", o.Label);
            }
        }

        [Test]
        public void Ping_AndJitter_AreRead()
        {
            var o = P("game", "--simulate-ping", "100", "--simulate-jitter", "20");
            Assert.IsTrue(o.Active);
            Assert.AreEqual(100, o.PingMs);
            Assert.AreEqual(20, o.JitterMs);
            StringAssert.Contains("100", o.Label);
        }

        [TestCase("--simulate-ping", "abc")]
        [TestCase("--simulate-ping", "-5")]
        [TestCase("--simulate-ping", "501")]
        [TestCase("--simulate-jitter", "201")]
        [TestCase("--simulate-ping", "1.5")]
        public void BadValue_IsAnError_AndNotActive(string flag, string value)
        {
            var o = P(flag, value);
            Assert.IsNotNull(o.Error);
            Assert.IsFalse(o.Active);
        }

        [Test]
        public void MissingValue_IsAnError()
        {
            Assert.IsNotNull(P("--simulate-ping").Error);
            Assert.IsFalse(P("--simulate-ping", "100", "--simulate-jitter").Active);
        }

        [Test]
        public void ZeroPing_IsOff()
        {
            Assert.IsFalse(P("--simulate-ping", "0").Active);
        }

        [Test]
        public void Delay_WandersByJitter_NeverBelowZero()
        {
            var o = P("--simulate-ping", "100", "--simulate-jitter", "20");
            Assert.AreEqual(80, o.DelayMs(0f));
            Assert.AreEqual(100, o.DelayMs(0.5f));
            Assert.AreEqual(120, o.DelayMs(1f));
            Assert.AreEqual(0, P("--simulate-ping", "10", "--simulate-jitter", "50").DelayMs(0f));
        }

        [Test]
        public void ServerFlags_AreNotMistakenForOurs()
        {
            Assert.IsFalse(P("--dedicated", "--port", "7770", "--bots", "5").Active);
        }
    }
}
