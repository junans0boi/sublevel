using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-241: 50 participants, people at most Participants - MinBots, bots fill the rest and never fewer than MinBots.
    public class BotFillTests
    {
        [TestCase(0, 50, 10, 50)]
        [TestCase(1, 50, 10, 49)]
        [TestCase(40, 50, 10, 10)]
        [TestCase(45, 50, 10, 10)]   // past the cap people do not push the bots under the minimum
        [TestCase(50, 50, 10, 10)]
        [TestCase(1, 100, 10, 49)]   // participants clamp to the limit
        [TestCase(1, 8, 20, 7)]      // minBots clamp to participants - 1
        [TestCase(-3, 50, 10, 50)]
        public void Plan(int humans, int participants, int minBots, int expected) =>
            Assert.AreEqual(expected, BotFill.Plan(humans, participants, minBots));

        [Test]
        public void Defaults_Are50Participants_40People_49BotsAtMost()
        {
            var r = new MatchRules();
            Assert.AreEqual(50, r.Participants);
            Assert.AreEqual(10, r.MinBots);
            Assert.AreEqual(40, r.MaxHumans);
            Assert.AreEqual(49, MatchRules.MaxBotsLimit);
            Assert.AreEqual(50, LobbyRules.MaxParticipants);
        }

        [Test]
        public void SliceDefault_BotCount15_IsStillAllowed_And49Fits()
        {
            var r = new MatchRules();
            Assert.AreEqual(15, r.ClampedBotCount);
            r.BotCount = 99;
            Assert.AreEqual(49, r.ClampedBotCount);
        }

        [Test]
        public void ParticipantsArg_IsRead_AndRangeChecked()
        {
            Assert.AreEqual(50, ServerArgs.Parse(new[] { "--dedicated", "--participants", "50" }).Participants);
            Assert.AreEqual(0, ServerArgs.Parse(new[] { "--dedicated" }).Participants);
            Assert.IsNotNull(ServerArgs.Parse(new[] { "--participants", "51" }).Error);
            Assert.IsNull(ServerArgs.Parse(new[] { "--bots", "49", "--max-players", "40" }).Error);
        }
    }
}
