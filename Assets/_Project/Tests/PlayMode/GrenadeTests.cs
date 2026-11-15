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
    // PUBG_GAP 4-3 batch I in the real TestBox scene as a host: a thrown grenade hurts by distance, walls stop
    // the blast, the thrower is not spared, and the server keeps the count. The throws go through the server API
    // (ThrowableUser.ServerThrow), so only the last test depends on input.
    public class GrenadeTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = false;
                return f;
            }
        }

        const string Wiring = "the Player prefab needs a ThrowableUser with its grenade prefab assigned (batch I wiring notes)";

        ScriptedInput _input;
        ThrowableRules _rulesBefore;
        int _blasts;
        Vector3 _blastAt;
        Combatant _blastBy;
        float _blastTime;

        void OnBlast(Vector3 centre, Combatant thrower)
        {
            _blasts++;
            _blastAt = centre;
            _blastBy = thrower;
            _blastTime = Time.time;
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

        // `fuseSeconds` below zero keeps the rules as they are.
        IEnumerator LoadTestBox(float fuseSeconds)
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            _rulesBefore = ThrowableRules.Current;
            if (fuseSeconds >= 0f) ThrowableRules.Current = new ThrowableRules { FuseSeconds = fuseSeconds };
            _blasts = 0;
            _blastAt = default;
            _blastBy = null;
            ThrownItem.ServerExploded += OnBlast;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ThrownItem.ServerExploded -= OnBlast;
            if (_rulesBefore != null) ThrowableRules.Current = _rulesBefore;
            _rulesBefore = null;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // The first dummy, with every dummy's shooting switched off so only grenades hurt anyone.
        IEnumerator AQuietDummy(Action<Combatant> found)
        {
            Combatant dummy = null;
            yield return WaitUntil(() =>
            {
                foreach (var c in Combatant.All)
                    if (!c.IsPlayer) { dummy = c; return true; }
                return false;
            }, 5f, "a training dummy in the test box");
            yield return WaitUntil(() => dummy.ActiveWeaponId != 0, 5f, "the dummy armed itself");
            foreach (var c in Combatant.All)
            {
                if (c.IsPlayer) continue;
                // By name: the test assembly does not reference the AI assembly.
                foreach (var part in c.GetComponents<MonoBehaviour>())
                    if (part.GetType().Name == "TrainingDummy") part.enabled = false;
            }
            yield return new WaitForSeconds(0.3f);
            found(dummy);
        }

        // Sees every damage offered to a combatant, leaving whatever filter it already had (armour) in charge of
        // the amount. Returns that earlier filter so the test can put it back.
        static Func<DamageInfo, float> Watch(Combatant combatant, Action<DamageInfo> seen)
        {
            var before = combatant.ServerDamageFilter;
            combatant.ServerDamageFilter = d =>
            {
                seen(d);
                return before != null ? before(d) : d.Amount;
            };
            return before;
        }

        static ThrowableUser Thrower(Combatant combatant)
        {
            var user = combatant.GetComponent<ThrowableUser>();
            Assert.IsNotNull(user, Wiring);
            return user;
        }

        [UnityTest]
        public IEnumerator InTheOpen_ThreeMetresFromADummy_HurtsIt_AndNamesTheThrower()
        {
            yield return LoadTestBox(-1f); // the real fuse
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            Combatant dummy = null;
            yield return AQuietDummy(d => dummy = d);
            Assert.AreEqual(100f, dummy.Hp.Value);
            Assert.AreEqual(5f, ThrowableRules.Current.FuseSeconds, 1e-4f);

            int hits = 0;
            DamageInfo seen = default;
            var filterBefore = Watch(dummy, d => { hits++; seen = d; });

            Assert.AreEqual(1, user.ServerAdd(1));
            // Dropped rather than thrown: it falls straight down and lies 5 m to the dummy's side (the medium ring).
            float thrownAt = Time.time;
            Assert.IsTrue(user.ServerThrow(dummy.transform.position + new Vector3(5f, 0.5f, 0f), Vector3.zero), Wiring);
            Assert.AreEqual(0, user.Grenades.Value, "the grenade left the pack");
            Assert.IsNotNull(user.LastThrown);

            yield return new WaitForSeconds(2f);
            Assert.AreEqual(0, _blasts, "the fuse is still burning");
            Assert.AreEqual(100f, dummy.Hp.Value);
            Assert.IsTrue(user.LastThrown.Resting, "it came to rest on the floor");

            yield return WaitUntil(() => _blasts == 1, 7f, "the grenade went off");
            Assert.AreEqual(5f, _blastTime - thrownAt, 0.5f, "five seconds after the throw");
            Assert.AreEqual(me, _blastBy);
            Assert.AreEqual(5f, Vector3.Distance(_blastAt, dummy.transform.position), 0.15f, "it lay where it was dropped");

            Assert.AreEqual(1, hits, "one blast, one damage");
            Assert.AreEqual(me, seen.Attacker);
            Assert.AreEqual(DamageCause.Blast, seen.Cause);
            Assert.AreEqual(0, seen.WeaponId);
            Assert.AreEqual(82f, seen.Amount, 3f, "100 down to 40 between 3.5 and 8.5 m: 82 at 5 m");
            Assert.Less(dummy.Hp.Value, 100f);
            if (filterBefore == null) Assert.AreEqual(100f - seen.Amount, dummy.Hp.Value, 1e-2f);
            Assert.IsFalse(dummy.Dead.Value);
            Assert.AreEqual(100f, me.Hp.Value, "the thrower was far away");

            // The effect is shown by the thrower's view, in the frame the blast happened (the host runs it locally).
            var view = me.GetComponent<ThrowableView>();
            if (view != null)
            {
                Assert.AreEqual(1, view.BlastsShown);
                Assert.AreEqual(_blastAt, view.LastBlastCentre);
            }
            dummy.ServerDamageFilter = filterBefore;

            // The spent grenade is removed from the match.
            yield return WaitUntil(() => user.LastThrown == null, 3f, "the grenade object despawned");
        }

        [UnityTest]
        public IEnumerator CookingThreeSeconds_ThenThrowing_BlastsTwoSecondsLater()
        {
            yield return LoadTestBox(-1f); // the real five second fuse
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            yield return AQuietDummy(d => { });
            Assert.AreEqual(1, user.ServerAdd(1));
            Assert.IsFalse(user.Cooking.Value);
            Assert.IsTrue(user.ServerBeginCook(), "the pin comes out");
            Assert.IsTrue(user.Cooking.Value);
            Assert.AreEqual(1, user.Grenades.Value, "still in the hand: not used up until thrown");
            yield return new WaitForSeconds(3f);
            Assert.AreEqual(2f, user.CookRemaining, 0.3f);
            Assert.AreEqual(0, _blasts);
            float thrownAt = Time.time;
            Assert.IsTrue(user.ServerThrowAimed(me.transform.forward), Wiring);
            Assert.IsFalse(user.Cooking.Value);
            yield return WaitUntil(() => _blasts == 1, 5f, "the cooked grenade went off");
            Assert.AreEqual(2f, _blastTime - thrownAt, 0.4f, "what was left of the fuse, not a fresh five seconds");
        }

        [UnityTest]
        public IEnumerator CookedUntilTheFuseEnds_GoesOffInTheHand()
        {
            yield return LoadTestBox(-1f);
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            yield return AQuietDummy(d => { });
            Assert.AreEqual(1, user.ServerAdd(1));
            float cookedAt = Time.time;
            Assert.IsTrue(user.ServerBeginCook());
            yield return WaitUntil(() => _blasts == 1, 8f, "it went off in the hand");
            Assert.AreEqual(5f, _blastTime - cookedAt, 0.5f);
            Assert.Less(Vector3.Distance(_blastAt, me.transform.position + Vector3.up), 2.5f, "at the hand");
            Assert.AreEqual(me, _blastBy);
            Assert.Less(me.Hp.Value, 100f, "the thrower paid for holding it");
            Assert.AreEqual(0, user.Grenades.Value);
            Assert.IsFalse(user.Cooking.Value);
        }

        [UnityTest]
        public IEnumerator ARoll_LeavesLowAndLevel_AtTheRollSpeed()
        {
            yield return LoadTestBox(0.5f);
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            yield return AQuietDummy(d => { });
            Assert.AreEqual(1, user.ServerAdd(1));
            // Looking a little up: a throw would lob, a roll stays on the ground.
            var look = (me.transform.forward + Vector3.up * 0.4f).normalized;
            Assert.IsTrue(user.ServerThrowAimed(look, ThrowableKind.Frag, true), Wiring);
            var v = user.LastThrown.Velocity;
            Assert.AreEqual(0f, v.y, 1e-3f, "level");
            Assert.AreEqual(ThrowableRules.Current.RollSpeed, new Vector2(v.x, v.z).magnitude, 1e-2f);
            Assert.Less(user.LastThrown.transform.position.y - me.transform.position.y, 0.6f, "from the hip");
        }

        [UnityTest]
        public IEnumerator AVest_CutsTheBlast_WithoutWearing()
        {
            yield return LoadTestBox(0.5f);
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            Combatant dummy = null;
            yield return AQuietDummy(d => dummy = d);
            var gear = dummy.GetComponent<CombatantGear>();
            Assert.IsNotNull(gear, "the dummy needs CombatantGear");
            gear.ServerEquip(ArmorSlot.Vest, 2);
            float worn = gear.VestDurability.Value;
            Assert.Greater(worn, 0f);
            Assert.AreEqual(1, user.ServerAdd(1));
            Assert.IsTrue(user.ServerThrow(dummy.transform.position + new Vector3(1f, 0.5f, 0f), Vector3.zero), Wiring);
            yield return WaitUntil(() => _blasts == 1, 4f, "the grenade went off");
            yield return null;
            Assert.AreEqual(100f - 100f * (1f - ArmorRules.Current.Find(ArmorSlot.Vest, 2).Reduction), dummy.Hp.Value, 1.5f, "the vest's share of 100 was cut");
            Assert.AreEqual(worn, gear.VestDurability.Value, 1e-3f, "a blast does not wear the vest");
        }

        [UnityTest]
        public IEnumerator BehindAWall_TheBlastDoesNothing()
        {
            yield return LoadTestBox(1f);
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            Combatant dummy = null;
            yield return AQuietDummy(d => dummy = d);

            // "BlockSpawn" is a 3 m high, 1 m thick wall along z = -14 from x = -12 to 12. The dummy stands 1 m
            // behind its far face, the grenade lies 1 m in front of its near face: 3 m apart, wall in between.
            dummy.transform.position = new Vector3(3f, 0f, -12.5f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            int hits = 0;
            var filterBefore = Watch(dummy, d => hits++);

            Assert.AreEqual(1, user.ServerAdd(1));
            Assert.IsTrue(user.ServerThrow(new Vector3(3f, 0.5f, -15.5f), Vector3.zero), Wiring);
            yield return WaitUntil(() => _blasts == 1, 5f, "the grenade went off");

            Assert.AreEqual(3f, Vector3.Distance(_blastAt, dummy.transform.position), 0.15f, "close enough to hurt, were it not for the wall");
            Assert.AreEqual(0, hits, "the wall took it");
            Assert.AreEqual(100f, dummy.Hp.Value);
            dummy.ServerDamageFilter = filterBefore;
        }

        [UnityTest]
        public IEnumerator TheThrower_StandingOnTheirOwnGrenade_IsHurt_AndTheDeadThrowNothing()
        {
            yield return LoadTestBox(1f);
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            Combatant dummy = null;
            yield return AQuietDummy(d => dummy = d);
            Assert.AreEqual(100f, me.Hp.Value);

            int hits = 0;
            DamageInfo seen = default;
            var filterBefore = Watch(me, d => { hits++; seen = d; });

            Assert.AreEqual(2, user.ServerAdd(2));
            Assert.IsTrue(user.ServerThrow(me.transform.position + Vector3.up * 0.5f, Vector3.zero), Wiring);
            yield return WaitUntil(() => _blasts == 1, 5f, "the grenade went off");

            Assert.AreEqual(1, hits);
            Assert.AreEqual(me, seen.Attacker, "self-inflicted");
            Assert.AreEqual(DamageCause.Blast, seen.Cause);
            Assert.AreEqual(100f, seen.Amount, 1e-3f, "point blank");
            Assert.Less(me.Hp.Value, 100f);
            Assert.IsTrue(me.Dead.Value);
            Assert.AreEqual(100f, dummy.Hp.Value, "nobody else was near");
            me.ServerDamageFilter = filterBefore;

            yield return new WaitForSeconds(1.2f); // past the throw interval
            // The one left in the hand went into the death crate (ADR-116), so there is nothing to throw.
            Assert.AreEqual(0, user.Grenades.Value);
            Assert.IsFalse(user.ServerThrow(me.transform.position + Vector3.up * 0.5f, Vector3.zero), "the dead throw nothing");
            Assert.AreEqual(0, user.Grenades.Value);
        }

        [UnityTest]
        public IEnumerator TheCount_GoesDownWithEveryThrow_AndAnEmptyHandThrowsNothing()
        {
            yield return LoadTestBox(0.5f);
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            yield return AQuietDummy(d => { });
            // A corner of the box with nobody within the blast radius.
            var corner = new Vector3(25f, 0.5f, -25f);
            float interval = ThrowableRules.Current.MinThrowInterval;
            Assert.AreEqual(1f, interval, 1e-4f);

            Assert.AreEqual(0, user.Grenades.Value, "nobody starts with grenades");
            Assert.IsFalse(user.ServerThrow(corner, Vector3.zero), "nothing to throw");
            Assert.AreEqual(0, user.ServerThrows);

            // Pickups reach the component through the combatant; the carry limit is 3.
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = ThrowableRules.GrenadeLootKind, Amount = 1 }),
                "Combatant.ServerGive must hand grenade loot to ThrowableUser (with several loot receivers on one combatant it has to ask each of them)");
            Assert.AreEqual(1, user.Grenades.Value);
            Assert.AreEqual(2, user.ServerAdd(5), "only two more fit");
            Assert.AreEqual(3, user.Grenades.Value);
            Assert.IsFalse(me.ServerGive(new LootEntry { Kind = ThrowableRules.GrenadeLootKind, Amount = 1 }), "full: the pickup stays on the ground");
            Assert.AreEqual(3, user.Grenades.Value);

            Assert.IsTrue(user.ServerThrow(corner, Vector3.zero), Wiring);
            Assert.AreEqual(2, user.Grenades.Value);
            Assert.IsFalse(user.ServerThrow(corner, Vector3.zero), "too soon after the last throw");
            Assert.AreEqual(2, user.Grenades.Value, "a refused throw costs nothing");

            yield return new WaitForSeconds(interval + 0.1f);
            Assert.IsTrue(user.ServerThrow(corner, Vector3.zero));
            Assert.AreEqual(1, user.Grenades.Value);
            yield return new WaitForSeconds(interval + 0.1f);
            Assert.IsFalse(user.ServerThrow(corner, new Vector3(float.NaN, 0f, 0f)), "nonsense is refused");
            Assert.AreEqual(1, user.Grenades.Value);
            Assert.IsTrue(user.ServerThrow(corner, Vector3.zero));
            Assert.AreEqual(0, user.Grenades.Value);
            yield return new WaitForSeconds(interval + 0.1f);
            Assert.IsFalse(user.ServerThrow(corner, Vector3.zero), "none left");
            Assert.AreEqual(0, user.Grenades.Value, "never below zero");
            Assert.AreEqual(3, user.ServerThrows);

            yield return WaitUntil(() => _blasts == 3, 5f, "all three went off");
            Assert.AreEqual(100f, me.Hp.Value, "out of reach of all of them");
        }

        [UnityTest]
        public IEnumerator HoldingG_ShowsTheArc_AndLettingGo_Throws()
        {
            yield return LoadTestBox(1f);
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            var view = me.GetComponent<ThrowableView>();
            Assert.IsNotNull(view, "the Player prefab needs a ThrowableView (batch I wiring notes)");
            yield return AQuietDummy(d => { });
            // Up and to the side, away from the wall in front of the spawn.
            PlayerController.Local.LookAt(me.EyePosition + new Vector3(-10f, 6f, 2f));
            yield return null;

            // Nothing to throw: the key does nothing.
            _input.Frame.ThrowHeld = _input.Frame.ThrowPressed = true;
            yield return null;
            yield return null;
            Assert.IsFalse(view.PreviewVisible);
            _input.Frame.ThrowHeld = _input.Frame.ThrowPressed = false;
            _input.Frame.ThrowReleased = true;
            yield return null;
            yield return null;
            _input.Frame.ThrowReleased = false;
            Assert.AreEqual(0, view.ThrowsRequested);

            Assert.AreEqual(1, user.ServerAdd(1));
            yield return WaitUntil(() => user.Grenades.Value == 1, 2f, "the grenade count reached the owner");
            _input.Frame.ThrowHeld = _input.Frame.ThrowPressed = true;
            yield return null;
            yield return null;
            _input.Frame.ThrowPressed = false;
            yield return null;
            yield return null;
            Assert.IsTrue(view.PreviewVisible, "the arc shows while G is held (needs the effect materials on ThrowableView)");
            Assert.Greater(view.PreviewPointCount, 2);
            Assert.AreEqual(0, user.ServerThrows, "nothing is thrown before the key is let go");

            _input.Frame.ThrowHeld = false;
            _input.Frame.ThrowReleased = true;
            yield return null;
            yield return null;
            _input.Frame.ThrowReleased = false;
            yield return WaitUntil(() => user.ServerThrows == 1, 2f, "the server threw it");
            Assert.AreEqual(1, view.ThrowsRequested);
            Assert.IsFalse(view.PreviewVisible, "the arc goes away with the throw");
            Assert.AreEqual(0, user.Grenades.Value);
            Assert.IsNotNull(user.LastThrown, Wiring);
            var velocity = user.LastThrown.Velocity;
            Assert.Less(velocity.x, 0f, "thrown the way the player looks");
            Assert.Greater(velocity.y, 0f);
            Assert.LessOrEqual(velocity.magnitude, 18.01f, "no faster than the throwing speed");

            yield return WaitUntil(() => _blasts == 1, 5f, "the grenade went off");
            Assert.AreEqual(me, _blastBy);
        }

        // ADR-144 / #183: PUBG sequence - key 5 takes it out, R pulls the pin, the fire button held shows the arc and
        // letting go throws with the cooked fuse; another slot cancels, and the fire button never punches meanwhile.
        [UnityTest]
        public IEnumerator Key5_Fire_R_Sequence_Throws_AndAnotherSlotCancels()
        {
            yield return LoadTestBox(-1f);
            var me = Combatant.LocalPlayer;
            var user = Thrower(me);
            var view = me.GetComponent<ThrowableView>();
            yield return AQuietDummy(d => { });
            PlayerController.Local.LookAt(me.EyePosition + new Vector3(-10f, 6f, 2f));
            Assert.AreEqual(1, user.ServerAdd(1));
            Assert.AreEqual(1, user.ServerAdd(ThrowableKind.Smoke, 1));
            yield return WaitUntil(() => user.TotalCount == 2, 2f, "counts reached the owner");
            _input.Frame.SelectSlot = Loadout.ThrowableSlot;
            yield return null;
            _input.Frame.SelectSlot = -1;
            yield return WaitUntil(() => me.ActiveSlot.Value == Loadout.ThrowableSlot, 2f, "key 5 took the throwable out");
            Assert.AreEqual(ThrowableKind.Frag, user.SelectedKind);

            _input.Frame.Reload = true; // R
            yield return null;
            _input.Frame.Reload = false;
            yield return WaitUntil(() => user.Cooking.Value, 2f, "R pulled the pin without the arc");
            Assert.IsFalse(view.PreviewVisible);

            _input.Frame.FireHeld = _input.Frame.FirePressed = true;
            yield return null;
            _input.Frame.FirePressed = false;
            yield return null;
            yield return null;
            Assert.IsTrue(view.PreviewVisible, "the arc shows while the fire button is held");
            Assert.AreEqual(0, user.ServerThrows);
            _input.Frame.FireHeld = false;
            _input.Frame.FireReleased = true;
            yield return null;
            yield return null;
            _input.Frame.FireReleased = false;
            yield return WaitUntil(() => user.ServerThrows == 1, 2f, "letting go threw it");
            Assert.IsFalse(user.Cooking.Value);
            Assert.IsFalse(view.PreviewVisible);
            Assert.Greater(user.LastThrown.Velocity.magnitude, 1f);

            // Hold again, then another slot: no throw.
            _input.Frame.FireHeld = _input.Frame.FirePressed = true;
            yield return null;
            _input.Frame.FirePressed = false;
            yield return null;
            _input.Frame.SelectSlot = 0;
            yield return null;
            _input.Frame.SelectSlot = -1;
            yield return null;
            yield return null;
            _input.Frame.FireHeld = false;
            _input.Frame.FireReleased = true;
            yield return null;
            yield return null;
            _input.Frame.FireReleased = false;
            Assert.AreEqual(1, user.ServerThrows, "another slot cancelled the throw");
            Assert.IsFalse(view.PreviewVisible);
        }
    }
}
