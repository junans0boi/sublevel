using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class PresentationTests
    {
        [Test]
        public void ShotGrouper_CollapsesPelletsButKeepsSeparateShots()
        {
            var grouper = new ShotGrouper(0.03);
            Assert.IsTrue(grouper.IsNewShot(1.000));
            Assert.IsFalse(grouper.IsNewShot(1.000));
            Assert.IsFalse(grouper.IsNewShot(1.010));
            Assert.IsTrue(grouper.IsNewShot(1.075));
        }
    }
}
