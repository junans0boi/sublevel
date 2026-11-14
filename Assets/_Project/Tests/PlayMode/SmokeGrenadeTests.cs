using System;
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
    // PUBG_GAP 4-3 batch I, the smoke grenade, in the real TestBox scene as a host: it is counted and picked up like
    // a grenade, selected with the cycle request, opens a cloud after its fuse that hides lines of sight (the
    // check the bots use) and hurts nobody, and the cloud goes away after its time.
    public class SmokeGrenadeTests
    {
        const string Wiring = "the Player prefab needs a ThrowableUser with its thrown-item prefab assigned (batch I wiring notes)";

        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() => Frame;
        }

        SmokeRules _smokeBefore;
        int _smokes;
        Vector3 _smokeAt;

        void OnSmoked(Vector3 centre, Combatant thrower)
        {
            _smokes++;
            _smokeAt = centre;
        }

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator LoadTestBox(SmokeRules rules)
        {
            PlayerController.InputOverride = new ScriptedInput();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            _smokeBefore = SmokeRules.Current;
            if (rules != null) SmokeRules.Current = rules;
            _smokes = 0;
            ThrownItem.ServerSmoked += OnSmoked;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ThrownItem.ServerSmoked -= OnSmoked;
            if (_smokeBefore != null) SmokeRules.Current = _smokeBefore;
            _smokeBefore = null;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static ThrowableUser Thrower(Combatant combatant)
        {
            var user = combatant.GetComponent<ThrowableUser>();
            Assert.IsNotNull(user, Wiring);
            return user;
        }

        [UnityTest]
        public IEnumerator ASmokeGrenade_OpensACloud_ThatHidesTheWay_AndGoesAway()
        {
            yield return LoadTestBox(new SmokeRules { FuseSeconds = 0.6f, Seconds = 2.5f, Radius = 5f });
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            Assert.AreEqual(1, user.ServerAdd(ThrowableKind.Smoke, 1));
            Assert.AreEqual(1, user.Smokes.Value);
            Assert.AreEqual(0, user.Grenades.Value, "grenades are counted apart");

            int hits = 0;
            var before = me.ServerDamageFilter;
            me.ServerDamageFilter = d => { hits++; return before != null ? before(d) : d.Amount; };

            // Dropped rather than thrown: it falls and lies where it was let go, well away from the player.
            var spot = me.transform.position + new Vector3(12f, 0.5f, 0f);
            Assert.IsTrue(user.ServerThrow(spot, Vector3.zero, ThrowableKind.Smoke), Wiring);
            Assert.AreEqual(0, user.Smokes.Value, "the smoke grenade left the pack");
            Assert.AreEqual(0, SmokeZones.Count, "no cloud before the fuse is out");
            Assert.IsFalse(SmokeSight.Blocks(spot + Vector3.left * 8f, spot + Vector3.right * 8f));

            yield return WaitUntil(() => _smokes == 1, 5f, "the cloud opened");
            Assert.AreEqual(1, SmokeZones.Count);
            Assert.AreEqual(spot.x, _smokeAt.x, 0.5f, "where it came to rest");

            var centre = _smokeAt + Vector3.up * 1.2f;
            Assert.IsTrue(SmokeSight.Blocks(centre + Vector3.left * 8f, centre + Vector3.right * 8f), "through the middle");
            Assert.IsFalse(SmokeSight.Blocks(centre + Vector3.left * 8f + Vector3.forward * 9f, centre + Vector3.right * 8f + Vector3.forward * 9f), "past it");
            Assert.AreEqual(0, hits, "smoke hurts nobody");
            var view = me.GetComponent<ThrowableView>();
            if (view != null) Assert.AreEqual(1, view.SmokesShown);

            yield return WaitUntil(() => SmokeZones.Count == 0, 6f, "the cloud was gone");
            Assert.IsFalse(SmokeSight.Blocks(centre + Vector3.left * 8f, centre + Vector3.right * 8f));
            me.ServerDamageFilter = before;
        }

        [UnityTest]
        public IEnumerator Smoke_IsPickedUp_Counted_AndSelectedLikeAGrenade()
        {
            yield return LoadTestBox(null);
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            Assert.AreEqual(ThrowableKind.Frag, user.SelectedKind, "frag is selected to begin with");

            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = ThrowableRules.SmokeLootKind, Amount = 2 }), "ServerGive hands smoke to ThrowableUser");
            Assert.AreEqual(2, user.Smokes.Value);
            Assert.AreEqual(0, user.Grenades.Value);
            user.ServerCycle();
            Assert.AreEqual(ThrowableKind.Smoke, user.SelectedKind);
            Assert.AreEqual(2, user.SelectedCount);
            Assert.AreEqual("연막탄", user.SelectedName);
            user.ServerCycle();
            Assert.AreEqual(ThrowableKind.Flash, user.SelectedKind, "nothing else carried: the next in the list");
            user.ServerSelect(ThrowableKind.Frag);
            Assert.AreEqual(0, user.SelectedCount, "a kind with none can still be selected: the HUD shows 0");

            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = ThrowableRules.SmokeLootKind, Amount = 1 }));
            Assert.AreEqual(3, user.Smokes.Value);
            Assert.IsFalse(me.ServerGive(new LootEntry { Kind = ThrowableRules.SmokeLootKind, Amount = 1 }), "three at most");
            Assert.AreEqual(3, user.Smokes.Value);
        }

        // #176: the throwable slot key (5) readies what is carried, and pressing it again cycles the carried kinds;
        // each kind then throws with G. T skips the kinds that are not carried.
        [UnityTest]
        public IEnumerator SlotFive_ReadiesSmoke_CyclesEveryCarriedKind_AndEachThrows()
        {
            yield return LoadTestBox(null);
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            var input = (ScriptedInput)PlayerController.InputOverride;
            Assert.AreEqual(ThrowableKind.Frag, user.SelectedKind);
            Assert.AreEqual(1, user.ServerAdd(ThrowableKind.Smoke, 1));
            yield return WaitUntil(() => user.Smokes.Value == 1, 2f, "count reached the owner");

            IEnumerator PressFive()
            {
                input.Frame.SelectSlot = Loadout.ThrowableSlot;
                yield return null; // one frame: one key press
                input.Frame.SelectSlot = -1;
                yield return null;
                yield return null;
            }
            IEnumerator ThrowWithG(ThrowableKind kind)
            {
                int before = user.ServerThrows;
                input.Frame.ThrowHeld = input.Frame.ThrowPressed = true;
                yield return null;
                yield return null;
                input.Frame.ThrowPressed = false;
                yield return null;
                input.Frame.ThrowHeld = false;
                input.Frame.ThrowReleased = true;
                yield return null;
                yield return null;
                input.Frame.ThrowReleased = false;
                yield return WaitUntil(() => user.ServerThrows == before + 1, 2f, "thrown: " + kind);
                Assert.AreEqual(kind, user.LastThrown.ThrowKind);
                yield return new WaitForSeconds(ThrowableRules.Current.MinThrowInterval + 0.1f);
            }

            yield return PressFive();
            yield return WaitUntil(() => me.ActiveSlot.Value == Loadout.ThrowableSlot, 2f, "slot 5 in hand");
            Assert.AreEqual(ThrowableKind.Smoke, user.SelectedKind, "frag is not carried: slot 5 readies the smoke grenade");
            yield return ThrowWithG(ThrowableKind.Smoke);

            foreach (var k in new[] { ThrowableKind.Frag, ThrowableKind.Smoke, ThrowableKind.Flash, ThrowableKind.Molotov })
                Assert.AreEqual(1, user.ServerAdd(k, 1), "carry " + k);
            yield return WaitUntil(() => user.TotalCount == 4, 2f, "counts reached the owner");
            me.ServerSelectSlot(0); // another slot in hand: 5 takes the throwable out and keeps the kind, then cycles
            var order = new[] { ThrowableKind.Smoke, ThrowableKind.Flash, ThrowableKind.Molotov, ThrowableKind.Frag };
            foreach (var k in order)
            {
                yield return PressFive();
                Assert.AreEqual(k, user.SelectedKind, "slot 5 selects " + k);
                yield return ThrowWithG(k);
            }

            // Only one kind left in the bag: T and 5 both stay on it instead of landing on an empty one.
            Assert.AreEqual(0, user.TotalCount);
            Assert.AreEqual(1, user.ServerAdd(ThrowableKind.Flash, 1));
            user.ServerCycle();
            Assert.AreEqual(ThrowableKind.Flash, user.SelectedKind, "T skips kinds that are not carried");
        }
    }
}
