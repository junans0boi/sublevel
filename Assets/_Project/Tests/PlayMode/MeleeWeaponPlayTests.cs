using System;
using System.Collections;
using System.Reflection;
using FishNet.Managing;
using FishNet.Object;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // ADR-073 in the real scenes as a host: a melee weapon is picked up into the melee slot, the fire button swings
    // it through the same server path as a punch with the weapon's own damage, reach and rate, it is never aimed,
    // the swing shows (third-person bone turns, first-person placement) and is heard, and a bot fights with one.
    public class MeleeWeaponPlayTests
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

        const int Machete = 21, Pan = 24;
        const float Chest = 1.1f;
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly Type BotType = Type.GetType("Game.AI.BotController, Game.AI");

        ScriptedInput _input;
        int _swings;
        Combatant.PunchResult _last;

        void OnPunched(in Combatant.PunchResult punch)
        {
            _swings++;
            _last = punch;
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
            _swings = 0;
            _last = default;
            Combatant.LocalPlayer.Punched += OnPunched;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Combatant.LocalPlayer != null) Combatant.LocalPlayer.Punched -= OnPunched;
            PlayerController.InputOverride = null;
            BotRules.Current = new BotRules();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // The first dummy, quiet; the player in the eye view holding `weaponId`, `metres` in front of it, looking at its chest.
        IEnumerator FaceAQuietDummyWith(int weaponId, float metres, Action<Combatant> found)
        {
            Combatant dummy = null;
            yield return WaitUntil(() =>
            {
                foreach (var c in Combatant.All)
                    if (!c.IsPlayer) { dummy = c; return true; }
                return false;
            }, 5f, "a training dummy in the test box");
            foreach (var c in Combatant.All)
            {
                if (c.IsPlayer) continue;
                foreach (var part in c.GetComponents<MonoBehaviour>())
                    if (part.GetType().Name == "TrainingDummy") part.enabled = false;
            }
            var me = Combatant.LocalPlayer;
            me.ServerStripLoadout();
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = weaponId, Amount = 0 }), "a melee weapon is taken");
            yield return WaitUntil(() => me.ActiveWeaponId == weaponId, 2f, "the melee weapon in hand");
            var pc = PlayerController.Local;
            if (pc.Rig.Mode == ViewMode.ThirdPerson) pc.Rig.ToggleMode();
            pc.Teleport(dummy.transform.position + dummy.transform.forward * metres + Vector3.up * 0.1f);
            pc.LookAt(dummy.transform.position + Vector3.up * Chest);
            yield return new WaitForSeconds(1.0f); // on the floor, standing still, any earlier swing timed out
            pc.LookAt(dummy.transform.position + Vector3.up * Chest);
            yield return null;
            found(dummy);
        }

        IEnumerator Click()
        {
            _input.Frame.FirePressed = true;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Machete_InTheMeleeSlot_SwingsForFifty_AsMelee_WithItsName()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummyWith(Machete, 1.2f, d => dummy = d);
            Assert.AreEqual(Machete, me.WeaponInSlot(Loadout.MeleeSlot));
            Assert.AreEqual(Loadout.MeleeSlot, me.ActiveSlot.Value);

            float before = dummy.Hp.Value;
            DamageInfo seen = default;
            var previous = dummy.ServerDamageFilter;
            dummy.ServerDamageFilter = info => { seen = info; return previous != null ? previous(info) : info.Amount; };
            int shots = 0;
            void OnShot(Vector3 a, Vector3 b) => shots++;
            me.ShotFired += OnShot;
            var anim = me.GetComponent<CombatantAnimation>();
            int shown = anim.SwingsShown;
            var audio = me.GetComponent<MeleeAudio>();
            int heard = audio != null ? audio.ImpactsPlayed : 0;
            var punchAudio = me.GetComponent<CombatantAudio>();
            int punchSounds = punchAudio != null ? punchAudio.PunchesPlayed : 0;

            yield return Click();
            yield return WaitUntil(() => dummy.Hp.Value < before, 0.5f, "the swing landed");
            dummy.ServerDamageFilter = previous;
            me.ShotFired -= OnShot;

            Assert.AreEqual(before - 50f, dummy.Hp.Value, 1e-3f, "50 on the body");
            Assert.AreEqual(DamageCause.Melee, seen.Cause);
            Assert.AreEqual(Machete, seen.WeaponId);
            Assert.AreEqual(1, _swings);
            Assert.AreEqual(Machete, _last.WeaponId);
            Assert.AreEqual(dummy, _last.Victim);
            Assert.AreEqual(0, shots, "nothing was fired");
            Assert.AreEqual(shown + 1, anim.SwingsShown, "the swing is shown");
            Assert.IsNull(anim.UpperState, "no gun pose for a melee weapon");
            if (audio != null) Assert.AreEqual(heard + 1, audio.ImpactsPlayed, "the hit is heard");
            if (punchAudio != null) Assert.AreEqual(punchSounds, punchAudio.PunchesPlayed, "not as a punch");
            Assert.AreEqual("가  [마체테]  나", KillFeed.Format(new KillEntry { KillerId = 1, VictimId = 2, KillerName = "가", VictimName = "나", Cause = (byte)DamageCause.Melee, WeaponId = Machete }));
        }

        [UnityTest]
        public IEnumerator Machete_ReachesFurtherThanAFist_AndKeepsItsOwnRate()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummyWith(Machete, 1.45f, d => dummy = d);
            float before = dummy.Hp.Value;
            var dir = dummy.transform.position + Vector3.up * Chest - me.EyePosition;
            Assert.IsTrue(me.ServerPunch(dir));
            Assert.AreEqual(before - 50f, dummy.Hp.Value, 1e-3f, "1.45 m: out of a fist's reach, inside the machete's");
            yield return new WaitForSeconds(0.4f);
            Assert.IsFalse(me.ServerPunch(dir), "0.4 s later: the machete swings every 0.65 s");
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(me.ServerPunch(dir));
            // CmdFire with a melee weapon in hand swings too (the server never fires it).
            yield return new WaitForSeconds(0.7f);
            int swings = _swings;
            Assert.IsTrue(me.ServerFire(dir));
            Assert.AreEqual(swings + 1, _swings);
        }

        [UnityTest]
        public IEnumerator MeleeWeapon_IsNeverAimed_AndPutAwayTheButtonPunches()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummyWith(Pan, 1.0f, d => dummy = d);
            me.ServerSetStance(AimMode.Sights, 0);
            Assert.AreEqual(AimMode.Hip, me.AimMode, "the server refuses to aim a pan");
            _input.Frame.AimHeld = true;
            yield return new WaitForSeconds(0.4f);
            _input.Frame.AimHeld = false;
            Assert.AreEqual(AimMode.Hip, me.AimMode, "holding the aim button does nothing");

            me.ServerSetHolstered(true);
            yield return new WaitForSeconds(1.0f);
            PlayerController.Local.LookAt(dummy.transform.position + Vector3.up * Chest);
            yield return null;
            float before = dummy.Hp.Value;
            yield return Click();
            yield return WaitUntil(() => dummy.Hp.Value < before, 0.5f, "a punch with the pan put away");
            Assert.AreEqual(before - 12f, dummy.Hp.Value, 1e-3f);
            Assert.AreEqual(0, _last.WeaponId, "a fist");
        }

        [UnityTest]
        public IEnumerator FirstPerson_TheWeaponIsPlacedAtTheEye_AndSwings()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummyWith(Machete, 3f, d => dummy = d);
            var visual = me.GetComponent<MeleeVisual>();
            Assert.IsNotNull(visual, "the combatant prefab has the melee view");
            var model = me.GetComponent<WeaponVisual>().ModelFor(Machete);
            Assert.IsNotNull(model);
            yield return WaitUntil(() => visual.PlacedAtEye, 1f, "the machete in front of the camera");
            Assert.IsTrue(model.activeInHierarchy);
            Assert.IsNotNull(model.GetComponent<MeshFilter>().sharedMesh, "the code-built mesh");
            var cam = PlayerController.Local.Camera.transform;
            var local = cam.InverseTransformPoint(model.transform.position);
            Assert.Greater(local.z, 0.1f, "in front of the camera");
            Assert.Greater(local.x, 0f, "on the right");
            Assert.Less(local.y, 0f, "below the eyes");

            yield return Click();
            float peak = 0f;
            float end = Time.time + 0.5f;
            while (Time.time < end)
            {
                peak = Mathf.Max(peak, visual.Phase.Weight);
                yield return null;
            }
            Assert.Greater(peak, 0.9f, "the swing played at the eye");
        }

        [UnityTest]
        public IEnumerator PickingUpAnotherMeleeWeapon_DropsTheOldOne()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummyWith(Machete, 3f, d => dummy = d);
            int before = CountLoot(Machete);
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Pan, Amount = 0 }));
            yield return null;
            Assert.AreEqual(Pan, me.WeaponInSlot(Loadout.MeleeSlot));
            Assert.AreEqual(before + 1, CountLoot(Machete), "the machete lies on the ground");
        }

        static int CountLoot(int weaponId)
        {
            int n = 0;
            foreach (var item in LootItem.All)
                if (item != null && (LootKind)item.Kind.Value == LootKind.Weapon && item.WeaponId.Value == weaponId) n++;
            return n;
        }

        // ---- A bot with a melee weapon (zone D: it has a navmesh) ----

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator BotWithAMachete_RunsAtAnEnemyNearby_AndHitsItWithTheMachete()
        {
            if (BotType == null) Assert.Ignore("Game.AI.BotController is not in this build");
            BotRules.Current = new BotRules { TargetsPlayers = false, LootRadius = 0f };
            SceneManager.LoadScene("ZoneD");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player in ZoneD");
            yield return WaitUntil(() => Combatant.All.Count > 1 && MatchDirector.Instance != null, 15f, "the scene's training dummies");

            var director = MatchDirector.Instance;
            var prefab = (NetworkObject)typeof(MatchDirector).GetField("_dummyPrefab", Priv).GetValue(director);
            var network = (NetworkManager)typeof(MatchDirector).GetField("_networkManager", Priv).GetValue(director);
            Combatant Spawn(Vector3 at)
            {
                var body = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
                if (body.GetComponent("TrainingDummy") is Behaviour quiet) quiet.enabled = false;
                network.ServerManager.Spawn(body);
                return body.GetComponent<Combatant>();
            }
            var bot = Spawn(new Vector3(0f, 300f, 0f));
            var target = Spawn(new Vector3(3f, 300f, 0f));
            yield return WaitUntil(() => bot.IsServerInitialized && target.IsServerInitialized, 5f, "bodies spawned");
            var all = Combatant.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var c = all[i];
                if (c == null || c.IsPlayer || c == bot || c == target) continue;
                if (c.GetComponent("TrainingDummy") is Behaviour dummy) dummy.enabled = false;
                c.transform.position = new Vector3(3f * i, -200f, 0f);
                c.ServerTakeDamage(100000f);
            }
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(FindOpenLine(6f, out var from, out var to), "zone D should have 6 m of open floor at a convergence point");
            bot.transform.SetPositionAndRotation(from, Quaternion.LookRotation(Flat(to - from)));
            target.transform.SetPositionAndRotation(to, Quaternion.LookRotation(Flat(from - to)));
            Assert.IsTrue(bot.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Machete, Amount = 0 }));
            Assert.AreEqual(Machete, bot.ActiveWeaponId, "empty hands take the machete");
            DamageInfo seen = default;
            target.ServerDamageFilter = info => { seen = info; return info.Amount; };
            yield return new WaitForFixedUpdate();
            bot.gameObject.AddComponent(BotType);

            float hp = target.Hp.Value;
            yield return WaitUntil(() => target.Hp.Value < hp, 40f, "the bot to reach the target and swing");
            Assert.AreEqual(DamageCause.Melee, seen.Cause);
            Assert.AreEqual(Machete, seen.WeaponId, "with the machete, not a fist");
            Assert.AreEqual(bot, seen.Attacker);
            Assert.AreEqual(Machete, bot.ActiveWeaponId, "it kept the machete in hand");
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v : Vector3.forward;
        }

        static bool FindOpenLine(float meters, out Vector3 from, out Vector3 to)
        {
            from = to = default;
            var zone = ZoneInfo.Current;
            if (zone == null) return false;
            foreach (var point in zone.ConvergencePoints)
            {
                if (!NavMesh.SamplePosition(point.position, out var start, 2f, NavMesh.AllAreas)) continue;
                for (int deg = 0; deg < 360; deg += 15)
                {
                    var dir = Quaternion.Euler(0f, deg, 0f) * Vector3.forward;
                    if (!NavMesh.SamplePosition(start.position + dir * meters, out var end, 1f, NavMesh.AllAreas)) continue;
                    if (Mathf.Abs(end.position.y - start.position.y) > 0.5f) continue;
                    if (!Clear(start.position, end.position, 0.5f) || !Clear(start.position, end.position, 1.1f) || !Clear(start.position, end.position, 1.6f)) continue;
                    from = start.position;
                    to = end.position;
                    return true;
                }
            }
            return false;
        }

        static bool Clear(Vector3 a, Vector3 b, float height) =>
            !Physics.Linecast(a + Vector3.up * height, b + Vector3.up * height, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }
}
