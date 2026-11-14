using System;
using System.Collections;
using System.Collections.Generic;
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
    // #148 (ADR-116): the boost items, flash and fire bottle as inventory rows, in the death crate, and in bots' hands.
    // Bots are the real Bot prefab the ZoneD match spawns; the thrower is the (ignored) player.
    public class ItemFinishPlayTests
    {
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Smg = 2;
        static readonly Type BotType = Type.GetType("Game.AI.BotController, Game.AI");

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        static T Prop<T>(Component bot, string name) => (T)BotType.GetProperty(name).GetValue(bot);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (BotType == null) Assert.Ignore("Game.AI.BotController is not in this build");
            BotRules.Current = new BotRules { TargetsPlayers = false };
            SceneManager.LoadScene("ZoneD");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player in ZoneD");
            yield return WaitUntil(() => Bots().Count == 3, 15f, "three bots");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            BotRules.Current = new BotRules();
            UiFocus.Close();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static List<Combatant> Bots()
        {
            var list = new List<Combatant>();
            foreach (var c in Combatant.All)
                if (c != null && !c.IsPlayer && c.GetComponent(BotType) != null) list.Add(c);
            return list;
        }

        // One bot stays; the others leave the match (moved far below the floor and killed).
        static Combatant OneBot()
        {
            var bots = Bots();
            for (int i = 1; i < bots.Count; i++)
            {
                bots[i].transform.position = new Vector3(3f * i, -200f, 0f);
                bots[i].ServerTakeDamage(100000f);
            }
            return bots[0];
        }

        static void Arm(Combatant body)
        {
            var weapon = GameData.Weapons.Find(Smg);
            body.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Smg, Amount = weapon.MagSize });
            body.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = weapon.Ammo, Amount = 60 });
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

        static void Place(Combatant body, Vector3 position, Vector3 lookAt)
        {
            var facing = lookAt - position;
            facing.y = 0f;
            body.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing));
        }

        // A stationary dummy body to be the bot's target (the scene has no dummies, only bots).
        static IEnumerator SpawnDummy(Vector3 at, Action<Combatant> done)
        {
            var director = MatchDirector.Instance;
            var prefab = (NetworkObject)typeof(MatchDirector).GetField("_dummyPrefab", Priv).GetValue(director);
            var network = (NetworkManager)typeof(MatchDirector).GetField("_networkManager", Priv).GetValue(director);
            var body = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
            if (body.GetComponent("TrainingDummy") is Behaviour dummy) dummy.enabled = false;
            network.ServerManager.Spawn(body);
            var c = body.GetComponent<Combatant>();
            yield return WaitUntil(() => c.IsServerInitialized, 5f, "dummy spawned");
            done(c);
        }

        // ---- Inventory ----

        [UnityTest]
        public IEnumerator BoostFlashAndFireRows_ShowCounts_UseWithRightClick_AndDrop()
        {
            var me = Combatant.LocalPlayer;
            yield return WaitUntil(() => InventoryScreen.Instance != null, 5f, "the inventory screen");
            var screen = InventoryScreen.Instance;
            var healing = me.GetComponent<CombatantHealing>();
            var user = me.GetComponent<ThrowableUser>();
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Drink, Amount = 2 }));
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Pill, Amount = 1 }));
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Syringe, Amount = 1 }));
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Flash, Amount = 2 }));
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Molotov, Amount = 1 }));
            yield return null;
            Assert.AreEqual(2, InventoryBackend.BoostCount(me, HealKind.Drink));
            Assert.AreEqual(2, InventoryBackend.Flashes(me));
            Assert.AreEqual(1, InventoryBackend.Molotovs(me));
            UiFocus.Set(UiScreen.Inventory);
            yield return null;
            StringAssert.Contains("2", InventoryBackend.Describe(screen.ExtraSource(InventorySourceKind.Drink), me));
            StringAssert.Contains("섬광탄", InventoryBackend.Describe(screen.ExtraSource(InventorySourceKind.Flash), me));

            // Right click on a boost row drinks it: the gauge fills and the count drops once the use is done.
            screen.QuickDrop(screen.ExtraSource(InventorySourceKind.Drink));
            yield return WaitUntil(() => healing.Using, 2f, "the drink to start");
            yield return WaitUntil(() => healing.Gauge.Value > 0f && healing.Drinks.Value == 1, 8f, "the drink to finish");

            // #179: right click on a throwable row sets it as the throwable slot's kind (nothing is dropped); a drag to
            // the ground drops one.
            int loot = LootItem.All.Count;
            screen.QuickDrop(screen.ExtraSource(InventorySourceKind.Flash));
            yield return WaitUntil(() => user.SelectedKind == ThrowableKind.Flash, 2f, "flash set as the throwable");
            Assert.AreEqual(loot, LootItem.All.Count, "a right click does not drop it");
            Assert.IsTrue(screen.DropOnGround(screen.ExtraSource(InventorySourceKind.Flash)));
            Assert.IsTrue(screen.DropOnGround(screen.ExtraSource(InventorySourceKind.Molotov)));
            Assert.IsTrue(screen.DropOnGround(screen.ExtraSource(InventorySourceKind.Syringe)));
            yield return WaitUntil(() => LootItem.All.Count == loot + 3, 3f, "three drops");
            Assert.AreEqual(1, user.Flashes.Value);
            Assert.AreEqual(0, user.Molotovs.Value);
            Assert.AreEqual(0, healing.Syringes.Value);
            int kinds = 0;
            for (int i = loot; i < LootItem.All.Count; i++)
            {
                var k = (LootKind)LootItem.All[i].Kind.Value;
                if (k == LootKind.Flash || k == LootKind.Molotov || k == LootKind.Syringe) kinds++;
            }
            Assert.AreEqual(3, kinds, "a flash, a bottle and a syringe on the ground");
        }

        // ---- Death crate ----

        [UnityTest]
        public IEnumerator DeadBot_LeavesBoostItemsFlashesFireBottlesBandagesAndGrenades()
        {
            var bot = OneBot();
            yield return null;
            Assert.IsNotNull(bot.GetComponent<CombatantHealing>(), "the Bot prefab needs CombatantHealing (run BotPrefabPatch.Apply)");
            bot.transform.position = new Vector3(0f, -300f, 0f); // far from anything it could want
            var kinds = new[] { LootKind.Drink, LootKind.Pill, LootKind.Syringe, LootKind.Flash, LootKind.Molotov, LootKind.Bandage, LootKind.FirstAidKit, LootKind.Grenade, LootKind.Smoke };
            foreach (var k in kinds) Assert.IsTrue(bot.ServerGive(new LootEntry { Kind = k, Amount = 1 }), "gave " + k);
            int before = LootItem.All.Count;
            bot.ServerTakeDamage(new DamageInfo { Amount = 5000f, Cause = DamageCause.Zone });
            Assert.IsTrue(bot.Dead.Value);
            yield return WaitUntil(() => LootItem.All.Count >= before + kinds.Length, 3f, "the crate");
            var found = new HashSet<LootKind>();
            for (int i = before; i < LootItem.All.Count; i++) found.Add((LootKind)LootItem.All[i].Kind.Value);
            foreach (var k in kinds) Assert.IsTrue(found.Contains(k), "the crate holds " + k);
            Assert.AreEqual(0, bot.GetComponent<ThrowableUser>().TotalCount);
            Assert.AreEqual(0, bot.GetComponent<CombatantHealing>().Bandages.Value);
        }

        // ---- Bots ----

        [UnityTest, Timeout(120000)]
        public IEnumerator HurtBot_UsesABandage()
        {
            var bot = OneBot();
            yield return null;
            Arm(bot);
            var healing = bot.GetComponent<CombatantHealing>();
            Assert.IsNotNull(healing);
            Assert.IsTrue(bot.ServerGive(new LootEntry { Kind = LootKind.Bandage, Amount = 3 }));
            bot.ServerTakeDamage(45f);
            Assert.AreEqual(55f, bot.Hp.Value, 1e-3f);
            yield return WaitUntil(() => bot.Hp.Value >= 70f, 40f, "the bot to bandage itself");
            // (It may pick up more bandages from the floor meanwhile, so the count is not the proof.)
            Assert.Greater(healing.ServerCompleted, 0);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator HurtBot_DrinksABoostItem()
        {
            var bot = OneBot();
            yield return null;
            Arm(bot);
            var healing = bot.GetComponent<CombatantHealing>();
            Assert.IsTrue(bot.ServerGive(new LootEntry { Kind = LootKind.Drink, Amount = 2 }));
            bot.ServerTakeDamage(20f); // 80%: above the heal threshold, under the boost one
            yield return WaitUntil(() => healing.Drinks.Value < 2 || healing.Gauge.Value > 0f, 30f, "the bot to drink");
            yield return WaitUntil(() => healing.Gauge.Value > 0f, 10f, "the gauge to fill");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator BotWithAGrenade_ThrowsItAtAnEnemyInRange()
        {
            var bot = OneBot();
            yield return null;
            Assert.IsTrue(FindOpenLine(18f, out var from, out var to), "an open 18 m line");
            Place(bot, from, to);
            Arm(bot);
            Assert.IsTrue(bot.ServerGive(new LootEntry { Kind = LootKind.Grenade, Amount = 2 }));
            Combatant dummy = null;
            yield return SpawnDummy(to, c => dummy = c);
            dummy.ServerTakeDamage(0f);
            var user = bot.GetComponent<ThrowableUser>();
            yield return WaitUntil(() => Prop<int>(bot.GetComponent(BotType), "ThrowsMade") > 0 || dummy.Dead.Value, 30f, "the bot to throw or finish the dummy");
            Assert.Greater(Prop<int>(bot.GetComponent(BotType), "ThrowsMade"), 0, "it threw a grenade");
            Assert.Less(user.Grenades.Value, 2);
        }

        // The player (ignored by the bot) throws `kind` onto the bot's feet from `meters` away in front of it.
        static IEnumerator ThrowAtBot(Combatant bot, ThrowableKind kind, float meters)
        {
            var me = Combatant.LocalPlayer;
            var user = me.GetComponent<ThrowableUser>();
            user.ServerAdd(kind, 1);
            var at = bot.transform.position;
            var spot = at + bot.transform.forward * meters;
            if (NavMesh.SamplePosition(spot, out var hit, 2f, NavMesh.AllAreas)) spot = hit.position;
            PlayerController.Local.Teleport(spot);
            yield return new WaitForFixedUpdate();
            var rules = ThrowableRules.Current;
            var origin = spot + Vector3.up * 1.5f;
            var flat = at - spot;
            flat.y = 0f;
            Assert.IsTrue(BotItems.TryLaunchPitch(flat.magnitude, at.y - origin.y, rules.ThrowSpeed, rules.Gravity, out float pitch));
            var dir = flat.normalized;
            var velocity = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, dir)) * dir * rules.ThrowSpeed;
            Assert.IsTrue(user.ServerThrow(origin, velocity, kind), "the player threw");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator FlashNextToABot_BlindsItAndItSeesNothing()
        {
            var bot = OneBot();
            yield return null;
            Assert.IsTrue(FindOpenLine(10f, out var from, out var to), "an open line");
            Place(bot, from, to);
            Arm(bot);
            var botComponent = bot.GetComponent(BotType);
            Combatant dummy = null;
            yield return SpawnDummy(to + Vector3.right * 40f, c => dummy = c); // far and out of the way
            yield return ThrowAtBot(bot, ThrowableKind.Flash, 5f);
            yield return WaitUntil(() => Prop<bool>(botComponent, "Blind"), 5f, "the bot to be blinded");
            Assert.IsNull(Prop<Combatant>(botComponent, "Target"), "blind: no target");
            yield return WaitUntil(() => !Prop<bool>(botComponent, "Blind"), 12f, "the blindness to end");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator BotInAFire_RunsOutOfIt()
        {
            var bot = OneBot();
            yield return null;
            Assert.IsTrue(FindOpenLine(10f, out var from, out var to), "an open line");
            Place(bot, from, to);
            Arm(bot);
            var start = bot.transform.position;
            yield return ThrowAtBot(bot, ThrowableKind.Molotov, 5f);
            yield return WaitUntil(() => bot.Burning.Value || bot.Hp.Value < 100f, 8f, "the bot to catch fire");
            float radius = MolotovRules.Current.Radius;
            yield return new WaitForSeconds(4f);
            Assert.IsFalse(bot.Dead.Value, "it got out before the fire killed it");
            Assert.Greater(Vector3.Distance(bot.transform.position, start), radius * 0.8f, "it left the fire's ring");
            Assert.Greater(bot.Hp.Value, 100f - MolotovRules.Current.FireDps * 5f, "it did not stand in it");
        }
    }
}
