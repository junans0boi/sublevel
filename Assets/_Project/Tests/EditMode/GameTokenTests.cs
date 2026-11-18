using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class GameTokenTests
    {
        const string Secret = "test-secret";
        const long Now = 1_800_000_000;

        [Test]
        public void ValidToken_GivesAccountAndNickname()
        {
            var t = GameToken.Sign(Secret, 7, "해무\"1", Now);
            Assert.AreEqual(TokenResult.Ok, GameToken.Verify(Secret, t, Now + 10, out var c));
            Assert.AreEqual(7, c.AccountId);
            Assert.AreEqual("해무\"1", c.Nickname);
        }

        [Test]
        public void NullNickname_IsEmpty()
        {
            Assert.AreEqual(TokenResult.Ok, GameToken.Verify(Secret, GameToken.Sign(Secret, 1, null, Now), Now, out var c));
            Assert.AreEqual("", c.Nickname);
        }

        [Test] public void Expired_IsRejected() => Assert.AreEqual(TokenResult.Expired, GameToken.Verify(Secret, GameToken.Sign(Secret, 1, "a", Now), Now + 301, out _));
        [Test] public void WrongSecret_IsForged() => Assert.AreEqual(TokenResult.BadSignature, GameToken.Verify("other", GameToken.Sign(Secret, 1, "a", Now), Now, out _));
        [Test] public void EmptyToken_IsMissing() => Assert.AreEqual(TokenResult.Missing, GameToken.Verify(Secret, "", Now, out _));

        [Test]
        public void TamperedBody_IsForged()
        {
            var parts = GameToken.Sign(Secret, 1, "a", Now).Split('.');
            var other = GameToken.Sign(Secret, 2, "a", Now).Split('.');
            Assert.AreEqual(TokenResult.BadSignature, GameToken.Verify(Secret, other[0] + "." + parts[1], Now, out _));
        }

        [TestCase("garbage")] [TestCase("a.b.c")] [TestCase("!!.@@")]
        public void Garbage_IsMalformed(string t) => Assert.AreEqual(TokenResult.Malformed, GameToken.Verify(Secret, t, Now, out _));

        // Produced by the backend's signGameToken (node) so both sides agree on the format.
        [Test]
        public void BackendToken_Verifies()
        {
            const string t = "eyJzdWIiOjcsIm5pY2siOiLtlbTrrLQiLCJpYXQiOjE4MDAwMDAwMDAsImV4cCI6MTgwMDAwMDMwMH0.5gfq1rRIMiXuJs1f9_jeX2R_lICM9hPewzDEmBMY7bU";
            Assert.AreEqual(TokenResult.Ok, GameToken.Verify(Secret, t, Now, out var c));
            Assert.AreEqual("해무", c.Nickname);
        }

        [Test]
        public void Peek_ReadsNicknameWithoutSecret_AndRejectsExpired()
        {
            var t = GameToken.Sign(Secret, 3, "서아", Now);
            Assert.IsTrue(GameToken.Peek(t, Now + 1, out var c));
            Assert.AreEqual("서아", c.Nickname);
            Assert.IsFalse(GameToken.Peek(t, Now + 400, out _));
            Assert.IsFalse(GameToken.Peek("junk", Now, out _));
        }

        [Test]
        public void CheckLogin_OpenWithoutSecret_DemandsTokenWithSecret()
        {
            var t = GameToken.Sign(Secret, 3, "서아", Now);
            Assert.AreEqual(JoinResult.Ok, LobbyRules.CheckLogin(null, "", Now, out _));
            Assert.AreEqual(JoinResult.Ok, LobbyRules.CheckLogin(Secret, t, Now, out var c));
            Assert.AreEqual(3, c.AccountId);
            Assert.AreEqual(JoinResult.LoginRequired, LobbyRules.CheckLogin(Secret, "", Now, out _));
            Assert.AreEqual(JoinResult.LoginRequired, LobbyRules.CheckLogin("x", t, Now, out _));
            Assert.AreEqual(JoinResult.LoginExpired, LobbyRules.CheckLogin(Secret, t, Now + 999, out _));
        }
    }
}
