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
    // ADR-053 (3)(4) in the real TestBox scene as a host: zone damage, who did it, one death event per death,
    // the armour hook, stance values and loot the loadout does not know.
    public class DamagePipelineTests
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

        // Takes every loot kind it is offered and remembers the last one.
        sealed class TakesAnything : MonoBehaviour, ILootReceiver
        {
            public int Taken;
            public bool Accept = true;

            public bool ServerTryTake(LootEntry loot)
            {
                if (!Accept) return false;
                Taken++;
                return true;
            }
        }

        const int Rifle = 3; // 40 per projectile, head x2.35, limb x0.9 (weapons.json, ADR-089)

        ScriptedInput _input;
        int _deaths;
        Combatant _lastVictim;
        DamageInfo _lastDamage;

        void OnDied(Combatant victim, DamageInfo damage)
        {
            _deaths++;
            _lastVictim = victim;
            _lastDamage = damage;
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

        IEnumerator LoadTestBox()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            _deaths = 0;
            _lastVictim = null;
            _lastDamage = default;
            Combatant.ServerDied += OnDied;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Combatant.ServerDied -= OnDied;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        IEnumerator GiveRifle()
        {
            Combatant.LocalPlayer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => Combatant.LocalPlayer.ActiveWeaponId == Rifle, 2f, "rifle in hand");
        }

        // The first dummy, armed but no longer shooting on its own, with the player standing 5 m in front of it.
        IEnumerator FaceAQuietDummy(Action<Combatant> found)
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
            var pc = PlayerController.Local;
            pc.Teleport(dummy.transform.position + dummy.transform.forward * 5f + Vector3.up * 0.1f);
            pc.LookAt(dummy.transform.position + Vector3.up * 1.1f);
            yield return new WaitForSeconds(0.5f); // on the floor, standing still, any weapon ready again
            found(dummy);
        }

        // One server-side shot from the shooter's eyes at a point, with the spread cone closed so that the
        // projectile goes exactly there.
        static bool ShootAt(Combatant shooter, Vector3 point)
        {
            float before = shooter.StanceSpreadFactor;
            shooter.StanceSpreadFactor = 0f;
            bool fired = shooter.ServerFire(point - shooter.EyePosition);
            shooter.StanceSpreadFactor = before;
            return fired;
        }

        [UnityTest]
        public IEnumerator HeadShot_DealsBaseTimesTheHeadFactor_ABodyShotDoesNot()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var view = me.GetComponent<CombatantView>();
            yield return GiveRifle();
            Combatant dummy = null;
            yield return FaceAQuietDummy(d => dummy = d);
            var weapon = GameData.Weapons.Find(Rifle);
            Assert.AreEqual(40f, weapon.Damage, 1e-4f);
            Assert.AreEqual(2.35f, weapon.HeadFactor, 1e-4f);
            Assert.AreEqual(0.9f, weapon.LimbFactor, 1e-4f);
            Assert.AreEqual(100f, dummy.Hp.Value);

            Combatant.ShotResult seen = default;
            int resolved = 0, headMarkers = 0;
            void OnResolved(in Combatant.ShotResult shot) { seen = shot; resolved++; }
            void OnHead(bool kill) => headMarkers++;
            me.ShotResolved += OnResolved;
            view.HeadshotConfirmed += OnHead;

            Assert.IsTrue(ShootAt(me, dummy.transform.position + Vector3.up * 1.65f), "head shot fired");
            Assert.AreEqual(6f, dummy.Hp.Value, 1e-3f, "40 x 2.35 on the head");
            Assert.AreEqual(1, resolved);
            Assert.AreEqual(ShotHit.Combatant, seen.Hit, "the hit kind is unchanged by the zone");
            Assert.AreEqual(HitZone.Head, seen.Zone);
            Assert.IsTrue(seen.Head);
            Assert.AreEqual(dummy, seen.Victim);
            Assert.AreEqual(1, view.HitConfirms);
            Assert.AreEqual(1, view.HeadshotConfirms);
            Assert.AreEqual(1, headMarkers);

            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(ShootAt(me, dummy.transform.position + Vector3.up * 1.1f), "body shot fired");
            Assert.AreEqual(0f, dummy.Hp.Value, 1e-3f, "40 x 1.0 on the body: the 6 left are gone");
            Assert.AreEqual(HitZone.Body, seen.Zone);
            Assert.IsFalse(seen.Head);
            Assert.AreEqual(2, view.HitConfirms);
            Assert.AreEqual(1, view.HeadshotConfirms, "a body hit is not a headshot");
            Assert.AreEqual(1, _deaths);

            me.ShotResolved -= OnResolved;
            view.HeadshotConfirmed -= OnHead;
        }

        [UnityTest]
        public IEnumerator LegShot_DealsLess_AndAKill_RaisesServerDiedOnce_WithTheAttacker()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var view = me.GetComponent<CombatantView>();
            yield return GiveRifle();
            Combatant dummy = null;
            yield return FaceAQuietDummy(d => dummy = d);

            Assert.IsTrue(ShootAt(me, dummy.transform.position + Vector3.up * 0.4f), "leg shot fired");
            Assert.AreEqual(64f, dummy.Hp.Value, 1e-3f, "40 x 0.9 on a leg");
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(ShootAt(me, dummy.transform.position + Vector3.up * 0.4f));
            Assert.AreEqual(28f, dummy.Hp.Value, 1e-3f);
            Assert.AreEqual(0, _deaths, "still alive: nothing announced");
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(ShootAt(me, dummy.transform.position + Vector3.up * 1.65f));

            Assert.AreEqual(0f, dummy.Hp.Value);
            Assert.IsTrue(dummy.Dead.Value);
            Assert.AreEqual(1, _deaths, "one death, one event");
            Assert.AreEqual(dummy, _lastVictim);
            Assert.AreEqual(me, _lastDamage.Attacker);
            Assert.AreEqual(HitZone.Head, _lastDamage.Zone);
            Assert.AreEqual(Rifle, _lastDamage.WeaponId);
            Assert.AreEqual(DamageCause.Shot, _lastDamage.Cause);
            Assert.AreEqual(94f, _lastDamage.Amount, 1e-3f, "the killing damage as dealt, not what was left of the health");
            Assert.AreEqual(1, view.KillConfirms);
            Assert.AreEqual(1, view.HeadshotConfirms);

            // The dead take no more damage and die only once.
            yield return new WaitForSeconds(0.3f);
            ShootAt(me, dummy.transform.position + Vector3.up * 1.1f);
            dummy.ServerTakeDamage(50);
            dummy.ServerTakeDamage(new DamageInfo { Amount = 50, Cause = DamageCause.Blast, Attacker = me });
            Assert.AreEqual(1, _deaths);
        }

        [UnityTest]
        public IEnumerator ADummysShot_NamesTheDummyAsTheAttacker()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummy(d => dummy = d);
            int dummyWeapon = dummy.ActiveWeaponId;

            me.ServerTakeDamage(95);
            Assert.AreEqual(5f, me.Hp.Value, 1e-3f);
            Assert.AreEqual(0, _deaths);
            Assert.IsTrue(ShootAt(dummy, me.transform.position + Vector3.up * 1.1f), "the dummy fired");

            Assert.IsTrue(me.Dead.Value, "killed by the dummy's shot");
            Assert.AreEqual(1, _deaths);
            Assert.AreEqual(me, _lastVictim);
            Assert.AreEqual(dummy, _lastDamage.Attacker);
            Assert.AreEqual(dummyWeapon, _lastDamage.WeaponId);
            Assert.AreEqual(HitZone.Body, _lastDamage.Zone);
            Assert.AreEqual(DamageCause.Shot, _lastDamage.Cause);
        }

        [UnityTest]
        public IEnumerator DamageInfo_GoesThroughTheFilter_AndKeepsItsCause()
        {
            yield return LoadTestBox();
            Combatant dummy = null;
            yield return FaceAQuietDummy(d => dummy = d);

            dummy.ServerTakeDamage(30);
            Assert.AreEqual(70f, dummy.Hp.Value, 1e-3f, "the plain overload still works");

            int asked = 0;
            DamageInfo offered = default;
            dummy.ServerDamageFilter = d => { asked++; offered = d; return d.Amount * 0.5f; };
            dummy.ServerTakeDamage(new DamageInfo { Amount = 40, Cause = DamageCause.Fall, Zone = HitZone.Limb });
            Assert.AreEqual(50f, dummy.Hp.Value, 1e-3f, "the filter halved 40");
            Assert.AreEqual(1, asked);
            Assert.AreEqual(40f, offered.Amount, 1e-3f);
            Assert.AreEqual(DamageCause.Fall, offered.Cause);
            Assert.AreEqual(HitZone.Limb, offered.Zone);

            dummy.ServerDamageFilter = d => 0f;
            dummy.ServerTakeDamage(new DamageInfo { Amount = 500, Cause = DamageCause.Blast });
            Assert.AreEqual(50f, dummy.Hp.Value, 1e-3f, "fully absorbed");
            Assert.AreEqual(0, _deaths);

            dummy.ServerDamageFilter = null;
            dummy.ServerTakeDamage(new DamageInfo { Amount = 500, Cause = DamageCause.Zone });
            Assert.IsTrue(dummy.Dead.Value);
            Assert.AreEqual(1, _deaths);
            Assert.AreEqual(dummy, _lastVictim);
            Assert.IsNull(_lastDamage.Attacker, "nobody did it");
            Assert.AreEqual(DamageCause.Zone, _lastDamage.Cause);
            Assert.AreEqual(0, _lastDamage.WeaponId);
        }

        [UnityTest]
        public IEnumerator StanceValues_MoveTheEyes_AndTheHeadBandOfTheVictim()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            yield return GiveRifle();
            Combatant dummy = null;
            yield return FaceAQuietDummy(d => dummy = d);
            Assert.AreEqual(0f, me.EyeHeightOffset);
            Assert.AreEqual(1f, me.StanceSpreadFactor);

            // Set and read inside one frame: a stance component may own these values from the next frame on.
            float upright = me.EyePosition.y;
            me.EyeHeightOffset = -0.5f;
            Assert.AreEqual(upright - 0.5f, me.EyePosition.y, 1e-4f, "the eyes follow the stance offset");
            Vector3 origin = default;
            void OnShot(Vector3 from, Vector3 to) => origin = from;
            me.ShotFired += OnShot;
            // Chest height of a standing dummy is its head once it is half a metre lower.
            dummy.EyeHeightOffset = -0.5f;
            bool fired = ShootAt(me, dummy.transform.position + Vector3.up * 1.1f);
            me.ShotFired -= OnShot;
            me.EyeHeightOffset = 0f;
            dummy.EyeHeightOffset = 0f;

            Assert.IsTrue(fired);
            Assert.AreEqual(upright - 0.5f, origin.y, 1e-3f, "the shot starts at the lowered eyes");
            Assert.AreEqual(6f, dummy.Hp.Value, 1e-3f, "40 x 2.35: a head shot on the lowered dummy");
        }

        // ADR-089: the reference rifle's body damage puts an unarmoured target down in three shots, not two.
        [UnityTest]
        public IEnumerator UnarmouredTarget_DiesToThreeRifleBodyShots()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            yield return GiveRifle();
            Combatant dummy = null;
            yield return FaceAQuietDummy(d => dummy = d);
            Assert.AreEqual(100f, dummy.Hp.Value);
            for (int i = 0; i < 2; i++)
            {
                Assert.IsTrue(ShootAt(me, dummy.transform.position + Vector3.up * 1.1f), "body shot " + (i + 1));
                yield return new WaitForSeconds(0.2f);
            }
            Assert.AreEqual(20f, dummy.Hp.Value, 1e-3f, "two body shots: 40 + 40");
            Assert.IsFalse(dummy.Dead.Value);
            Assert.IsTrue(ShootAt(me, dummy.transform.position + Vector3.up * 1.1f), "body shot 3");
            Assert.IsTrue(dummy.Dead.Value, "the third body shot kills");
            Assert.AreEqual(HitZone.Body, _lastDamage.Zone);
        }

        // ADR-089: a weapon just taken in hand cannot fire until its draw time has passed.
        [UnityTest]
        public IEnumerator FiringIsRefused_WhileTheWeaponIsDrawn()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            yield return GiveRifle();
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 12 }), "pistol in the sidearm slot");
            Combatant dummy = null;
            yield return FaceAQuietDummy(d => dummy = d);
            var target = dummy.transform.position + Vector3.up * 1.1f;
            Assert.IsTrue(ShootAt(me, target), "the rifle in hand fires");
            yield return new WaitForSeconds(0.3f);

            Assert.IsTrue(me.ServerSelectSlot(Loadout.SidearmSlot));
            Assert.IsFalse(ShootAt(me, target), "drawing the pistol: no shot");
            yield return new WaitForSeconds(GameData.Weapons.Find(1).DrawSeconds * 0.5f);
            Assert.IsFalse(ShootAt(me, target), "still drawing");
            yield return new WaitForSeconds(GameData.Weapons.Find(1).DrawSeconds * 0.5f + 0.1f);
            Assert.IsTrue(ShootAt(me, target), "drawn: it fires");

            // Putting the weapon away and taking it out again draws it again.
            me.ServerSetHolstered(true);
            me.ServerSetHolstered(false);
            Assert.IsFalse(ShootAt(me, target), "taken out again: drawing");
        }

        [UnityTest]
        public IEnumerator MovingAndJumping_WidenTheSpread()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            yield return GiveRifle();
            yield return new WaitForSeconds(0.4f); // settled on the floor
            Assert.AreEqual(0f, me.PlanarSpeed, 0.1f);
            Assert.IsFalse(me.Airborne);
            Assert.AreEqual(1f, me.SpreadFactor, 1e-3f, "standing still from the hip");

            me.StanceSpreadFactor = 0.5f;
            Assert.AreEqual(0.5f, me.SpreadFactor, 1e-3f, "the stance factor scales it");
            me.StanceSpreadFactor = 1f;

            // Running is 4.7 m/s times the rifle's 0.954 (ADR-095): the full movement factor of the rifle (x5, ADR-089).
            _input.Frame.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(4.7f * GameData.Rules.Locomotion.RifleMoveFactor, me.PlanarSpeed, 0.5f);
            Assert.AreEqual(5f, me.SpreadFactor, 0.5f, "running widens the cone");
            _input.Frame.Move = Vector2.zero;
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(1f, me.SpreadFactor, 0.05f, "and it closes again when standing");

            _input.Frame.Jump = true;
            yield return WaitUntil(() => me.Airborne, 1f, "off the ground");
            Assert.AreEqual(15f, me.SpreadFactor, 1e-3f, "in the air: the rifle's air factor");
            yield return WaitUntil(() => !me.Airborne, 2f, "down again");
        }

        [UnityTest]
        public IEnumerator UnknownLoot_GoesToTheLootReceiver_OrIsRefused()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var unknown = new LootEntry { Kind = (LootKind)99, Amount = 1 };

            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 1 }), "known kinds are taken as before");
            Assert.IsFalse(me.ServerGive(unknown), "nobody takes it: refused");

            var receiver = me.gameObject.AddComponent<TakesAnything>();
            // The combatant caches its receivers when it spawns (the gear and throwable components are now among them),
            // so a component added afterwards has to be picked up again.
            typeof(Combatant).GetField("_receivers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(me, null);
            Assert.IsTrue(me.ServerGive(unknown));
            Assert.AreEqual(1, receiver.Taken);
            receiver.Accept = false;
            Assert.IsFalse(me.ServerGive(unknown), "the receiver may refuse (full, wrong kind)");
            Assert.AreEqual(1, receiver.Taken);
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 1 }));
            Assert.AreEqual(1, receiver.Taken, "kinds the loadout knows never reach the receiver");
            UnityEngine.Object.Destroy(receiver);
        }
    }
}
