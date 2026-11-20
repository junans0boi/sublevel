using System.IO;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-098: bullet speed, gravity drop and the zero elevation (decision #126).
    public class BallisticsTests
    {
        static WeaponTable Weapons() => JsonUtility.FromJson<WeaponTable>(File.ReadAllText("Assets/_Project/Data/Resources/GameData/weapons.json"));

        // Drop at 300 m (m) = g/2 * (300 / speed)^2, the table the ADR quotes.
        [TestCase(1, 380f, 3.06f)]
        [TestCase(2, 280f, 5.63f)]
        [TestCase(3, 880f, 0.57f)]
        [TestCase(4, 380f, 3.06f)]
        [TestCase(5, 800f, 0.69f)]
        public void DropAt300m_MatchesTheTable(int weaponId, float speed, float drop)
        {
            var rules = new BallisticsRules();
            var weapon = Weapons().Find(weaponId);
            Assert.AreEqual(speed, Ballistics.MuzzleSpeed(weapon, rules), 1e-3f);
            Assert.AreEqual(drop, Ballistics.Drop(speed, 300f, rules.Gravity), 0.01f);
        }

        [Test]
        public void ZeroedAt300m_TheBulletIsOnTheLineThere_AndAboveItBefore()
        {
            var rules = new BallisticsRules();
            const float speed = 880f;
            float deg = Ballistics.ElevationDeg(speed, 300f, rules.Gravity);
            float t300 = 300f / (speed * Mathf.Cos(deg * Mathf.Deg2Rad));
            Assert.AreEqual(0f, Ballistics.HeightAt(speed, deg, t300, rules.Gravity), 0.02f);
            float t150 = 150f / (speed * Mathf.Cos(deg * Mathf.Deg2Rad));
            Assert.Greater(Ballistics.HeightAt(speed, deg, t150, rules.Gravity), 0.1f);
            Assert.Less(Ballistics.HeightAt(speed, deg, 400f / speed, rules.Gravity), 0f, "falls below the line past the zero");
        }

        [Test]
        public void EveryFirearm_HasAMuzzleSpeed_AndTheSniperNeedsOver0_3sFor300m()
        {
            foreach (var w in Weapons().Weapons)
                if (!w.IsMelee) Assert.Greater(w.MuzzleSpeed, 0f, w.Name);
            Assert.Greater(Ballistics.FlightSeconds(Weapons().Find(5).MuzzleSpeed, 300f), 0.3f);
        }
    }
}
