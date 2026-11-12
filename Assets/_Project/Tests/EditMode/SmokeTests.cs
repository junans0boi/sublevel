using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class SmokeTests
    {
        [Test]
        public void Add_ReturnsSum() => Assert.AreEqual(5, Smoke.Add(2, 3));
    }
}
