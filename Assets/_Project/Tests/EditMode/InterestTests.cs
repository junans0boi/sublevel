using Game.Core;
using System.IO;
using UnityEngine;
using NUnit.Framework;

namespace Game.Tests
{
    // #167: the distance rules of interest management (radius, hysteresis, what is always seen).
    public class InterestTests
    {
        static readonly InterestRules R = new InterestRules();
        static InterestObserver Body(float meters, bool dead = false) => new InterestObserver { HasBody = true, NearestMeters = meters, Dead = dead };

        [Test]
        public void Radius_DecidesWhatAClientGets_PerKind()
        {
            Assert.IsTrue(Interest.Observes(R, InterestKind.Combatant, false, Body(299f), false));
            Assert.IsFalse(Interest.Observes(R, InterestKind.Combatant, false, Body(301f), false));
            Assert.IsTrue(Interest.Observes(R, InterestKind.Loot, false, Body(99f), false));
            Assert.IsFalse(Interest.Observes(R, InterestKind.Loot, false, Body(101f), false), "loot is only sent near");
            Assert.IsFalse(Interest.Observes(R, InterestKind.Thrown, false, Body(201f), false));
            Assert.IsFalse(Interest.Observes(R, InterestKind.Vehicle, false, Body(351f), false));
            Assert.IsTrue(Interest.Observes(R, InterestKind.Vehicle, false, Body(349f), false));
        }

        [Test]
        public void Hysteresis_KeepsASeenObject_UntilTheHideRadius_ThenNeedsTheShortRadiusAgain()
        {
            Assert.IsFalse(Interest.Observes(R, InterestKind.Combatant, false, Body(330f), false), "not seen yet: the short radius");
            Assert.IsTrue(Interest.Observes(R, InterestKind.Combatant, true, Body(330f), false), "already seen: stays");
            Assert.IsTrue(Interest.Observes(R, InterestKind.Combatant, true, Body(359f), false));
            Assert.IsFalse(Interest.Observes(R, InterestKind.Combatant, true, Body(361f), false), "300 * 1.2 = 360 m: gone");
            Assert.IsFalse(Interest.Observes(R, InterestKind.Combatant, false, Body(330f), false), "and back in only inside 300 m");
        }

        [Test]
        public void NoFlicker_OnTheEdge_ForAnyWalkAcrossIt()
        {
            // Walking back and forth over 290..310 m never toggles once the object is seen.
            bool seen = false;
            int changes = 0;
            for (int i = 0; i < 200; i++)
            {
                float meters = 280f + 40f * Mathf01(i);
                bool now = Interest.Observes(R, InterestKind.Combatant, seen, Body(meters), false);
                if (now != seen) changes++;
                seen = now;
            }
            Assert.LessOrEqual(changes, 1, "appears once, never flickers");
        }

        static float Mathf01(int i) => 0.5f + 0.5f * (float)System.Math.Sin(i * 0.7);

        [Test]
        public void Teammates_AreAlwaysSeen_AtAnyDistance()
        {
            Assert.IsTrue(Interest.Observes(R, InterestKind.Combatant, false, Body(5000f), true));
            Assert.IsTrue(Interest.Observes(R, InterestKind.Vehicle, false, Body(5000f), true), "a vehicle carrying a teammate");
        }

        [Test]
        public void ADeadClient_Spectates_SoKeepsEveryCombatantVehicleAndThrownItem_ButLootStaysNear()
        {
            Assert.IsTrue(Interest.Observes(R, InterestKind.Combatant, false, Body(2000f, dead: true), false));
            Assert.IsTrue(Interest.Observes(R, InterestKind.Vehicle, false, Body(2000f, dead: true), false));
            Assert.IsTrue(Interest.Observes(R, InterestKind.Thrown, false, Body(2000f, dead: true), false));
            Assert.IsFalse(Interest.Observes(R, InterestKind.Loot, false, Body(2000f, dead: true), false));
        }

        [Test]
        public void Global_Things_AndTheOffSwitch_AlwaysPass_AClientWithoutABodySeesNothing()
        {
            Assert.IsTrue(Interest.Observes(R, InterestKind.Always, false, Body(99999f), false), "match, zone, sky, supply state");
            var off = new InterestRules { Enabled = false };
            Assert.IsTrue(Interest.Observes(off, InterestKind.Loot, false, Body(99999f), false));
            Assert.IsFalse(Interest.Observes(R, InterestKind.Combatant, false, new InterestObserver { HasBody = false }, false));
        }

        [Test]
        public void Data_HasAHysteresis_AndRadiiInTheRightOrder()
        {
            var data = JsonUtility.FromJson<GameRules>(File.ReadAllText("Assets/_Project/Data/Resources/GameData/rules.json")).Interest;
            Assert.IsNotNull(data, "rules.json has an Interest section");
            Assert.IsTrue(data.Enabled);
            Assert.Greater(data.HideExtraFraction, 0f);
            Assert.Greater(data.CombatantMeters, SoundRules.FarLayerFull, "a shot is heard to FarLayerFull: the combatant carrying it must be sent that far");
            Assert.Greater(data.ThrownMeters, data.LootMeters);
        }
    }
}
