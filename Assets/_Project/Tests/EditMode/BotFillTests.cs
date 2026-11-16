using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-241: 50 participants, people at most Participants - MinBots, bots fill the rest and never fewer than MinBots.
    public class BotFillTests
    {
        [TestCase(0, 80, 16, 80)]
        [TestCase(1, 80, 16, 79)]
        [TestCase(64, 80, 16, 16)]
        [TestCase(70, 80, 16, 16)]   // past the cap people do not push the bots under the minimum
        [TestCase(80, 80, 16, 16)]
        [TestCase(1, 100, 16, 79)]   // participants clamp to the limit
        [TestCase(1, 8, 20, 7)]      // minBots clamp to participants - 1
        [TestCase(-3, 80, 16, 80)]
        public void Plan(int humans, int participants, int minBots, int expected) =>
            Assert.AreEqual(expected, BotFill.Plan(humans, participants, minBots));

        [Test]
        public void Defaults_Are80Participants_64People_79BotsAtMost()
        {
            var r = new MatchRules();
            Assert.AreEqual(80, r.Participants);
            Assert.AreEqual(16, r.MinBots);
            Assert.AreEqual(64, r.MaxHumans);
            Assert.AreEqual(79, MatchRules.MaxBotsLimit);
            Assert.AreEqual(80, LobbyRules.MaxParticipants);
        }

        [Test]
        public void SliceDefault_BotCount15_IsStillAllowed_And79Fits()
        {
            var r = new MatchRules();
            Assert.AreEqual(15, r.ClampedBotCount);
            r.BotCount = 99;
            Assert.AreEqual(79, r.ClampedBotCount);
        }

        [Test]
        public void ParticipantsArg_IsRead_AndRangeChecked()
        {
            Assert.AreEqual(80, ServerArgs.Parse(new[] { "--dedicated", "--participants", "80" }).Participants);
            Assert.AreEqual(0, ServerArgs.Parse(new[] { "--dedicated" }).Participants);
            Assert.IsNotNull(ServerArgs.Parse(new[] { "--participants", "81" }).Error);
            Assert.IsNull(ServerArgs.Parse(new[] { "--bots", "79", "--max-players", "64" }).Error);
        }
    }
}
