using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class BackendMatchRulesTests
    {
        static AssignedMatch M(string id, params long[] a) => new AssignedMatch { Id = id, Accounts = a };

        [Test]
        public void Allows_OnlyAssignedAccounts()
        {
            var active = new List<AssignedMatch> { M("m-1", 7, 9) };
            Assert.IsTrue(BackendMatchRules.Allows(active, 9));
            Assert.IsFalse(BackendMatchRules.Allows(active, 5));
            Assert.IsFalse(BackendMatchRules.Allows(new List<AssignedMatch>(), 7));
        }

        [Test]
        public void Pick_TakesTheMatchMostPeopleBelongTo()
        {
            var active = new List<AssignedMatch> { M("m-1", 1, 2), M("m-2", 3, 4, 5) };
            Assert.AreEqual("m-2", BackendMatchRules.Pick(active, new HashSet<long> { 3, 5, 1 }).Id);
            Assert.IsNull(BackendMatchRules.Pick(active, new HashSet<long> { 99 }));
        }

        [Test] public void NotAssigned_HasAReason() => StringAssert.Contains("배정", LobbyRules.Reason(JoinResult.NotAssigned));
    }
}
