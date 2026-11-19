using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class AutoRunRulesTests
    {
        [Test]
        public void CanSprint_OnlyWhenNothingStopsIt()
        {
            Assert.IsTrue(AutoRunRules.CanSprint(true, false, false, false, false, false));
            Assert.IsFalse(AutoRunRules.CanSprint(false, false, false, false, false, false), "aiming");
            Assert.IsFalse(AutoRunRules.CanSprint(true, true, false, false, false, false), "aim key down");
            Assert.IsFalse(AutoRunRules.CanSprint(true, false, true, false, false, false), "firing");
            Assert.IsFalse(AutoRunRules.CanSprint(true, false, false, true, false, false), "slow walk");
            Assert.IsFalse(AutoRunRules.CanSprint(true, false, false, false, true, false), "external control");
            Assert.IsFalse(AutoRunRules.CanSprint(true, false, false, false, false, true), "healing");
        }
    }
}
