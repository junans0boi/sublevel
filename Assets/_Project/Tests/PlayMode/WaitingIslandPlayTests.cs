using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #415 #416 #419 (ADR-382/383/386): the waiting phase in the real World scene, as a host with its own client: the person starts on the
    // island, cannot leave it, takes no damage, can bungee, and is sent to the elevator when the countdown ends.
    public class WaitingIslandPlayTests
    {
        sealed class NoPlayerInput : IPlayerInput { public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 }; }

        bool _oldFlag;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _oldFlag = MatchRules.Current.UseWaitingIsland;
            MatchRules.Current.UseWaitingIsland = true;
            WaitingRoom.ForceEnabled = true;
            DedicatedServer.StartSecondsOverride = 12f;     // a short countdown for the test
            PlayerController.InputOverride = new NoPlayerInput();
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 90f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null || WaitingRoom.Current == null)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "world loaded and the waiting room open");
                yield return null;
            }
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MatchRules.Current.UseWaitingIsland = _oldFlag;
            WaitingRoom.ForceEnabled = false;
            DedicatedServer.StartSecondsOverride = -1f;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static bool OnIsland(Vector3 p) => WaitingIsland.Inside(p.x, p.z);

        [UnityTest, Timeout(300000)]
        public IEnumerator StartsOnTheIsland_NotBlocked_AndDeploysWhenTheCountdownEnds()
        {
            var me = Combatant.LocalPlayer;
            Assert.IsTrue(OnIsland(me.transform.position), "spawned on the island: " + me.transform.position);
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(SpawnProtection.InputBlocked, "the shut elevator doors do not hold a person on the island");
            float end = Time.realtimeSinceStartup + 60f;
            while (WaitingRoom.Current != null) { Assert.Less(Time.realtimeSinceStartup, end, "the countdown ended"); yield return null; }
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(OnIsland(me.transform.position), "sent to the elevator: " + me.transform.position);
            Assert.IsTrue(SpawnProtection.InputBlocked, "in the elevator with the doors shut");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CannotLeaveTheIsland_ServerPutsThemBack()
        {
            var me = Combatant.LocalPlayer;
            var p = me.transform.position;
            PlayerController.Local.Teleport(new Vector3(WaitingIsland.CentreX + 70f, p.y, WaitingIsland.CentreZ));
            yield return new WaitForSeconds(1.2f);
            var q = me.transform.position;
            float d = Vector2.Distance(new Vector2(q.x, q.z), new Vector2(WaitingIsland.CentreX, WaitingIsland.CentreZ));
            Assert.LessOrEqual(d, WaitingIsland.WalkRadius + 1f, "pulled back inside the walk circle, now " + d + " m from the centre");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator NothingHurts_OnTheIsland()
        {
            var me = Combatant.LocalPlayer;
            float before = me.Hp.Value;
            me.ServerTakeDamage(40f);
            yield return null;
            Assert.AreEqual(before, me.Hp.Value, 1e-3f, "a hit does nothing while waiting");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Bungee_CarriesThePlayerUp_AndDoesNotHurt()
        {
            var me = Combatant.LocalPlayer;
            float hp = me.Hp.Value;
            float deckY = me.transform.position.y;
            var pad = WaitingIsland.BungeePad;
            PlayerController.Local.Teleport(new Vector3(WaitingIsland.WorldX(pad.x), deckY, WaitingIsland.WorldZ(pad.z)));
            float top = deckY, end = Time.realtimeSinceStartup + 9f;
            while (Time.realtimeSinceStartup < end) { top = Mathf.Max(top, me.transform.position.y); yield return null; }
            Assert.Greater(top - deckY, BungeeCurve.Top - 3f, "carried up to the platform, reached +" + (top - deckY) + " m");
            Assert.AreEqual(hp, me.Hp.Value, 1e-3f, "no damage from the jump");
        }
    }
}
