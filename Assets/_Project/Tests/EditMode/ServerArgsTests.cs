using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #168: the dedicated server's command line and its start/stop timers.
    public class ServerArgsTests
    {
        static ServerOptions P(params string[] args) => ServerArgs.Parse(args);

        [Test]
        public void NoFlag_IsNotADedicatedServer_AndDefaultsAreTheDocumentedOnes()
        {
            var o = P("Sublevel", "-batchmode");
            Assert.IsFalse(o.Dedicated);
            Assert.AreEqual(7770, o.Port);
            Assert.AreEqual(-1, o.Bots);
            Assert.IsNull(o.Region);
            Assert.AreEqual(0, o.MaxPlayers);
            Assert.AreEqual(60f, o.StartSeconds);
            Assert.IsNull(o.Error);
        }

        [Test]
        public void AllArguments_AreRead()
        {
            var o = P("x", "--dedicated", "--port", "17900", "--bots", "5", "--region", "north", "--max-players", "8", "--start-seconds", "12.5");
            Assert.IsTrue(o.Dedicated);
            Assert.AreEqual(17900, o.Port);
            Assert.AreEqual(5, o.Bots);
            Assert.AreEqual("north", o.Region);
            Assert.AreEqual(8, o.MaxPlayers);
            Assert.AreEqual(12.5f, o.StartSeconds);
            Assert.IsNull(o.Error);
        }

        [TestCase("--port", "0")]
        [TestCase("--port", "65535")]
        [TestCase("--port", "abc")]
        [TestCase("--port", "-5")]
        [TestCase("--bots", "-1")]
        [TestCase("--bots", "80")]
        [TestCase("--bots", "x")]
        [TestCase("--max-players", "0")]
        [TestCase("--max-players", "65")]
        [TestCase("--participants", "1")]
        [TestCase("--participants", "81")]
        [TestCase("--participants", "x")]
        [TestCase("--start-seconds", "-1")]
        [TestCase("--region", " ")]
        public void BadValue_IsReportedAndNothingIsGuessed(string flag, string value)
        {
            var o = P("--dedicated", flag, value);
            Assert.IsNotNull(o.Error, flag + " " + value);
            StringAssert.Contains(flag, o.Error);
        }

        [Test]
        public void MissingValue_IsReported()
        {
            Assert.IsNotNull(P("--dedicated", "--port").Error);
        }

        [Test]
        public void Bots_ZeroIsAllowed_AndMaxIsTheCapMinusOnePerson()
        {
            Assert.AreEqual(0, P("--bots", "0").Bots);
            Assert.AreEqual(23, P("--bots", "23").Bots);
        }

        [Test]
        public void Match_EndsWhenDecidedForLongEnough_OrEveryoneLeft_OrNobodyCame()
        {
            Assert.IsFalse(ServerRules.ShouldEndMatch(100, true, 0, 5));
            Assert.IsTrue(ServerRules.ShouldEndMatch(100, true, 0, ServerRules.ResultSeconds));
            Assert.IsFalse(ServerRules.ShouldEndMatch(100, true, ServerRules.EmptySeconds - 1, 0));
            Assert.IsTrue(ServerRules.ShouldEndMatch(100, true, ServerRules.EmptySeconds, 0));
            Assert.IsFalse(ServerRules.ShouldEndMatch(ServerRules.NobodyCameSeconds - 1, false, 999, 0), "a match nobody reached yet is waited for");
            Assert.IsTrue(ServerRules.ShouldEndMatch(ServerRules.NobodyCameSeconds, false, 0, 0));
        }

        [Test]
        public void AutoStart_NeedsPeople_AndATimer()
        {
            Assert.IsFalse(ServerRules.ShouldAutoStart(60, 59, 2));
            Assert.IsTrue(ServerRules.ShouldAutoStart(60, 60, 1));
            Assert.IsFalse(ServerRules.ShouldAutoStart(60, 600, 0), "nobody there, nothing to start");
            Assert.IsFalse(ServerRules.ShouldAutoStart(0, 600, 3), "timer switched off");
        }
    }
}
