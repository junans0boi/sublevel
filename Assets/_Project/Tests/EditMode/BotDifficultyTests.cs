using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #149 (ADR-117): the three difficulties.
    public class BotDifficultyTests
    {
        [Test]
        public void Normal_IsTheDataFilesOwnObject()
        {
            var data = new BotRules();
            Assert.AreSame(data, BotLevels.For(data, BotDifficulty.Normal));
        }

        [Test]
        public void ReactionAimAndSight_GoEasyThenNormalThenHard()
        {
            var data = new BotRules();
            var easy = BotLevels.For(data, BotDifficulty.Easy);
            var hard = BotLevels.For(data, BotDifficulty.Hard);
            Assert.Greater(easy.ReactionSeconds, data.ReactionSeconds);
            Assert.Less(hard.ReactionSeconds, data.ReactionSeconds);
            Assert.Greater(easy.AimErrorFarDeg, data.AimErrorFarDeg);
            Assert.Less(hard.AimErrorFarDeg, data.AimErrorFarDeg);
            Assert.Less(easy.ViewDistance, data.ViewDistance);
            Assert.Greater(hard.ViewDistance, data.ViewDistance);
            Assert.Less(easy.FieldOfViewDeg, data.FieldOfViewDeg);
            Assert.Greater(hard.FieldOfViewDeg, data.FieldOfViewDeg);
            Assert.Less(easy.TurnDegPerSecond, data.TurnDegPerSecond);
            Assert.Greater(hard.TurnDegPerSecond, data.TurnDegPerSecond);
            Assert.Less(easy.FootstepHearingRadius, data.FootstepHearingRadius);
            Assert.Greater(hard.FootstepHearingRadius, data.FootstepHearingRadius);
        }

        [Test]
        public void ScalingACopy_LeavesTheDataUntouched()
        {
            var data = new BotRules();
            float reaction = data.ReactionSeconds;
            BotLevels.For(data, BotDifficulty.Easy);
            BotLevels.For(data, BotDifficulty.Hard);
            Assert.AreEqual(reaction, data.ReactionSeconds);
        }

        [Test]
        public void EasyAimIsWorseAtEveryRange_ThanHard()
        {
            var data = new BotRules();
            var easy = BotLevels.For(data, BotDifficulty.Easy);
            var hard = BotLevels.For(data, BotDifficulty.Hard);
            foreach (float d in new[] { 5f, 30f, 80f })
                Assert.Greater(BotAim.ErrorDeg(easy, d, 0.5f), BotAim.ErrorDeg(hard, d, 0.5f));
        }
    }
}
