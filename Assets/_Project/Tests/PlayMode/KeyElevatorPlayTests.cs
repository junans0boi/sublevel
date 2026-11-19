using System;
using System.Collections;
using System.Reflection;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // ADR-360 / #268 in the generated World scene as a host, with the pollution running (keys only):
    // the keys lie on the map (ceil(teams x 0.65)), the old free ride is refused, the player picks a key up with F,
    // inserts it with F, closes the doors with F, holds F to ride and comes out in the chosen region; a team rides
    // twice at most. A bot picks a key up and rides a key elevator out of a warned region.
    //   KEY_SHOT_DIR=<dir> saves the steps as PNGs (pickup, doors open, choosing, riding, arrival, limit).
    public class KeyElevatorPlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.Interact = Frame.FirePressed = Frame.Jump = false;
                return f;
            }
        }

        ScriptedInput _input;
        DeploymentRules _savedRules;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what) =>
            PollutionDirectorTests.WaitUntil(condition, seconds, what);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            BotRules.Current = new BotRules { TargetsPlayers = false };
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "local player in World");
            yield return WaitUntil(() => PollutionDirector.Instance != null && PollutionDirector.Instance.ServerReady, 30f, "the pollution to start");
            _savedRules = DeploymentRules.Current;
            // Short rides; no per-rider cooldown so the second key ride follows at once (the team cooldown is set below).
            DeploymentRules.Current = new DeploymentRules { TravelSeconds = 6f, TravelHoldSeconds = 1f, TravelCooldownSeconds = 0f };
            yield return WaitUntil(() => !SpawnProtection.InputBlocked, 10f, "the deployment doors to open");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            PollutionDirector.RulesOverride = null;
            if (_savedRules != null) DeploymentRules.Current = _savedRules;
            BotRules.Current = new BotRules();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static Vector3 Pos => Combatant.LocalPlayer.transform.position;

        // Nothing is warned for a long time (the first confirmation is 1000 s away); 4 s doors, no team cooldown.
        static PollutionRules CalmRules()
        {
            var r = PollutionDirectorTests.Timed(1000f, 5f);
            r.DoorOpenSeconds = 4f;
            r.TeamCooldownSeconds = 0f;
            return r;
        }

        static int KeysOnTheGround()
        {
            int n = 0;
            foreach (var item in LootItem.All) if (item != null && item.IsSpawned && item.Kind.Value == (int)LootKind.Key) n++;
            return n;
        }

        IEnumerator Shot(string name)
        {
            string dir = Environment.GetEnvironmentVariable("KEY_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) yield break;
            yield return UiShot.Save(System.IO.Path.Combine(dir, name + ".png"), 1600, 900);
        }

        IEnumerator Press()
        {
            _input.Frame.Interact = true;
            yield return null;
            yield return null;
        }

        IEnumerator HoldF(float seconds)
        {
            _input.Frame.Interact = true;
            _input.Frame.InteractHeld = true;
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
            _input.Frame.InteractHeld = false;
            yield return null;
        }

        static KeyStationStatus Status(int station)
        {
            KeyElevatorNet.LastStatus.TryGetValue(station, out var s);
            return s;
        }

        // Insert -> close -> hold to ride, all through F; returns after the arrival with the rider standing in a station.
        IEnumerator KeyRide(int station, string shotPrefix)
        {
            var rides = ElevatorRides.Instance;
            var keys = KeyHolder.Local;
            int before = keys.Keys.Value;
            yield return Press();
            yield return WaitUntil(() => (KeyPhase)Status(station).Phase == KeyPhase.DoorOpen, 3f, "the doors to open on the key");
            Assert.AreEqual(Combatant.LocalPlayer.ObjectId, Status(station).Holder);
            yield return WaitUntil(() => keys.Keys.Value == before - 1, 2f, "the key to be used up");
            yield return null;
            if (shotPrefix != null) yield return Shot(shotPrefix + "_2_doors_open");
            yield return Press();
            yield return WaitUntil(() => (KeyPhase)Status(station).Phase == KeyPhase.Selecting, 3f, "F to close the doors");
            yield return null;
            yield return null;
            int chosen = ElevatorPanel.Selected;
            Assert.GreaterOrEqual(chosen, 0, "a destination is offered");
            if (shotPrefix != null) yield return Shot(shotPrefix + "_3_choosing");
            yield return HoldF(1.4f);
            yield return WaitUntil(() => rides.LocalRiding, 3f, "the key ride to start");
            Assert.AreEqual(chosen, rides.LocalDestination);
            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(Hud.Instance.Elevator.TransitShown, "the ride band shows");
            if (shotPrefix != null)
            {
                // The follow camera sits against the shut car (it showed only a wall): the shot looks from the door
                // side of the car back at the rider instead.
                var cam = Camera.main.transform;
                var me = Combatant.LocalPlayer.transform;
                Debug.Log($"[KeyPlay] riding camera {cam.position} rider {me.position} distance {Vector3.Distance(cam.position, me.position + Vector3.up * 1.6f):0.00}");
                var fwd = Vector3.ProjectOnPlane(me.forward, Vector3.up).normalized;
                cam.position = me.position + fwd * 0.75f + Vector3.up * 1.95f;
                cam.rotation = Quaternion.LookRotation(me.position + Vector3.up * 1.0f - cam.position);
                yield return Shot(shotPrefix + "_4_riding");
            }
            if (shotPrefix != null)
            {
                yield return WaitUntil(() => !rides.LocalRiding || rides.LocalSecondsLeft < 2.5f, 15f, "near the arrival");
                if (rides.LocalRiding) yield return Shot(shotPrefix + "_4b_rising");
            }
            yield return WaitUntil(() => !rides.LocalRiding, 15f, "the arrival");
            yield return null;
            yield return null;
            int arrived = rides.StationAt(Pos);
            Assert.GreaterOrEqual(arrived, 0, "out in a station, at " + Pos);
            Assert.AreEqual(chosen, rides.RegionOfStation(arrived), "in the chosen region");
        }

        [UnityTest]
        public IEnumerator Player_PicksUpAKey_RidesTwice_ThirdUseRefused_FreeRideBlocked()
        {
            var zones = PollutionDirector.Instance;
            var rides = ElevatorRides.Instance;
            var me = Combatant.LocalPlayer;
            var keys = KeyHolder.Local;
            Assert.IsNotNull(keys, "the local player carries keys");
            Assert.IsTrue(PollutionDirector.FreeRidesBlocked, "the pollution runs: keys only");

            // The match's keys: ceil(teams x 0.65), 40 at most (ADR-360); 1-2 wait in supply crates, the rest lie on the map.
            var layout = MatchDirector.Instance.Layout;
            Assert.IsNotNull(layout);
            int expected = KeyPlan.CountFor(layout.TeamCount, zones.Rules);
            int lying = KeysOnTheGround();
            Debug.Log($"[KeyPlay] teams {layout.TeamCount} keys expected {expected} on the ground {lying} in crates {zones.ServerCrateKeys}");
            Assert.That(zones.ServerCrateKeys, Is.InRange(1, 2), "keys kept for the supply crates");
            Assert.That(lying, Is.InRange(expected - zones.ServerCrateKeys - 2, expected - zones.ServerCrateKeys), "the other keys lie on the map (a few may be picked up already)");

            zones.ServerConfigure(CalmRules(), 3);
            Assert.IsNull(KeyElevatorNet.Instance.System, "nobody used a key yet: the system takes these rules");

            int start = rides.StationAt(Pos);
            Assert.GreaterOrEqual(start, 0, "the player still stands in its deployment station");
            int startRegion = rides.RegionOfStation(start);

            // The old free ride: the request comes back refused, the server refuses it directly too.
            yield return null;
            yield return null;
            Assert.IsTrue(rides.RequestRide(start, (startRegion + 1) % zones.RegionCount));
            yield return WaitUntil(() => !rides.LocalPending, 3f, "the server's answer");
            Assert.IsFalse(rides.LocalRiding, "no free ride while the pollution runs");
            Assert.IsFalse(rides.ServerTryStart(me, start, (startRegion + 1) % zones.RegionCount));
            // F without a key does nothing to the elevator.
            yield return Press();
            Assert.AreEqual(KeyPhase.Idle, (KeyPhase)Status(start).Phase);

            // A key on the floor in front of the player: F takes it.
            var fwd = Quaternion.Euler(0f, ElevatorDoors.Instance.Sites[start].YawDeg, 0f) * Vector3.forward;
            var spot = Pos + fwd * 0.9f + Vector3.up * 0.3f;
            int count = LootItem.All.Count;
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Key, Amount = 1 }, spot);
            var key = LootItem.All[LootItem.All.Count - 1];
            Assert.Greater(LootItem.All.Count, count);
            PlayerController.Local.LookAt(spot);
            yield return WaitUntil(() => PlayerController.Local.NearbyLoot == key, 3f, "the key to be offered");
            yield return Shot("key_1_pickup");
            yield return Press();
            yield return WaitUntil(() => keys.Keys.Value == 1, 3f, "the key picked up");
            // Two more for the second ride and the refused third.
            keys.ServerAdd(2);
            yield return null;

            yield return KeyRide(start, "key");
            yield return Shot("key_5_arrival");
            int second = rides.StationAt(Pos);
            yield return WaitUntil(() => (KeyPhase)Status(start).Phase == KeyPhase.Idle, 3f, "the first station back to idle");
            Assert.AreEqual(1, KeyElevatorNet.Instance.System.RidesOf(KeyElevatorNet.TeamKey(me)));

            yield return KeyRide(second, null);
            yield return WaitUntil(() => KeyElevatorNet.Instance.System.RidesOf(KeyElevatorNet.TeamKey(me)) == 2, 3f, "the second ride counted");

            // The third use: refused, the key stays, the owner is told.
            int third = rides.StationAt(Pos);
            yield return WaitUntil(() => keys.TeamKeyRides.Value == 2, 3f, "the team told it used both rides");
            int hits = keys.RideLimitHits.Value;
            Assert.AreEqual(1, keys.Keys.Value);
            yield return Press();
            yield return WaitUntil(() => keys.RideLimitHits.Value == hits + 1, 3f, "the ride limit notice");
            Assert.AreEqual(KeyPhase.Idle, (KeyPhase)Status(third).Phase, "no doors open on a third use");
            Assert.AreEqual(1, keys.Keys.Value, "the key is not used up");
            yield return new WaitForSeconds(0.3f);
            yield return Shot("key_6_limit");
        }

        // The state machine with real stations (#268): a use without a choice is void and the key comes back; an enemy
        // standing in the car rides along to the same station; the team cooldown refuses the next key; a station in a
        // contaminated region recharges after a use.
        [UnityTest]
        public IEnumerator Void_EnemyRidesAlong_TeamCooldown_ContaminatedStationRecharges()
        {
            var zones = PollutionDirector.Instance;
            var rides = ElevatorRides.Instance;
            var me = Combatant.LocalPlayer;
            var keys = KeyHolder.Local;
            var net = KeyElevatorNet.Instance;
            var rules = CalmRules();
            rules.SelectSeconds = 2f;
            rules.TeamCooldownSeconds = 25f; // longer than the ride (it starts at the choice)
            rules.RechargeSeconds = 4f;
            int start = rides.StationAt(Pos);
            int startRegion = rides.RegionOfStation(start);
            // The first region to close is not the player's; it is confirmed after 20 s (fast front), the rest far later.
            var timed = PollutionDirectorTests.Timed(20f, 2f);
            rules.ConfirmMinutes = timed.ConfirmMinutes;
            PollutionDirectorTests.ConfigureFirst(zones, rules, startRegion, false);
            int doomed = zones.Match.Timeline.Order[0];
            Assert.IsNull(net.System);
            keys.ServerAdd(3);
            yield return null;

            // Void: inserted, doors closed, no choice in 2 s -> the key comes back.
            Assert.IsTrue(net.ServerInsert(me, start));
            Assert.AreEqual(2, keys.Keys.Value);
            Assert.IsTrue(net.ServerClose(me));
            yield return WaitUntil(() => keys.Keys.Value == 3, 5f, "the void use to give the key back");
            yield return WaitUntil(() => net.System.Station(start).Phase == KeyPhase.Idle, 2f, "the safe station idle again");
            Assert.AreEqual(0, net.System.RidesOf(KeyElevatorNet.TeamKey(me)), "a void use is no ride");

            // An enemy bot stands in the car (brain off): it boards while the doors are open and rides along.
            Combatant enemy = null;
            foreach (var c in Combatant.All)
                if (c != null && !c.IsPlayer && !c.Dead.Value && !c.IsTeammateOf(me)) { enemy = c; break; }
            Assert.IsNotNull(enemy);
            var enemyAgent = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (enemyAgent != null) enemyAgent.enabled = false;
            ((Behaviour)enemy.GetComponent("BotController")).enabled = false;
            var site = ElevatorDoors.Instance.Sites[start];
            enemy.transform.position = site.Spawn + Quaternion.Euler(0f, site.YawDeg, 0f) * Vector3.right * 0.7f + Vector3.up * 0.05f;
            Assert.IsTrue(net.ServerInsert(me, start));
            yield return WaitUntil(() => net.System.Station(start).IsAboard(enemy.ObjectId), 3f, "the enemy to board");
            Assert.IsTrue(net.ServerClose(me));
            int to = -1;
            for (int r = 0; r < zones.RegionCount && to < 0; r++) if (r != startRegion && r != doomed) to = r;
            Assert.IsTrue(net.ServerSelect(me, to));
            Assert.IsTrue(rides.ServerRiding(enemy), "the enemy rides with the holder");
            yield return WaitUntil(() => !rides.ServerRiding(enemy) && !rides.LocalRiding, 20f, "both arrive");
            yield return null;
            yield return null;
            int arrived = rides.StationAt(Pos);
            Assert.AreEqual(to, rides.RegionOfStation(arrived));
            Assert.AreEqual(arrived, rides.StationAt(enemy.transform.position), "the same station for every rider of the car");

            // Team cooldown: the next key is refused (not used up) until it runs out.
            int keysNow = keys.Keys.Value;
            Assert.IsFalse(net.ServerInsert(me, arrived), "the team cooldown refuses the key");
            Assert.AreEqual(keysNow, keys.Keys.Value);
            Assert.Greater(net.System.Cooldowns.Left(KeyElevatorNet.TeamKey(me), zones.Now), 0f);
            yield return WaitUntil(() => net.System.Cooldowns.CanUse(KeyElevatorNet.TeamKey(me), zones.Now), 30f, "the team cooldown to run out");

            // A station in the contaminated region: after a (void) use it is dead for RechargeSeconds.
            yield return WaitUntil(() => zones.StateOf(doomed) == ZoneState.Closed, 25f, "the first region confirmed");
            int dead = -1;
            var sites = ElevatorDoors.Instance.Sites;
            for (int i = 0; i < sites.Count && dead < 0; i++) if (sites[i].Region == doomed && net.System.Station(i).Phase == KeyPhase.Idle) dead = i;
            Assert.GreaterOrEqual(dead, 0);
            PlayerController.Local.Teleport(sites[dead].Spawn + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            Assert.IsTrue(net.ServerInsert(me, dead));
            Assert.IsTrue(net.ServerClose(me));
            yield return WaitUntil(() => net.System.Station(dead).Phase == KeyPhase.Recharging, 5f, "the void use in the gas");
            Assert.Greater(net.System.Station(dead).PhaseLeft(zones.Now), 2f, "recharging for RechargeSeconds");
            Assert.IsFalse(net.ServerInsert(me, dead), "a recharging station takes no key");
            yield return WaitUntil(() => net.System.Station(dead).Phase == KeyPhase.Idle, 8f, "recharged");
        }

        // A key waits in a supply crate (SPEC 5-2): opening the crate puts it out with the items.
        [UnityTest]
        public IEnumerator SupplyCrate_HandsOutAKey()
        {
            var zones = PollutionDirector.Instance;
            var supply = SupplyDirector.Instance;
            Assert.IsNotNull(supply);
            int waiting = zones.ServerCrateKeys;
            Assert.GreaterOrEqual(waiting, 1);
            var crate = supply.ServerDrop(2);
            Assert.IsNotNull(crate);
            yield return WaitUntil(() => Time.time >= crate.LandsAt, 10f, "the crate to land");
            PlayerController.Local.Teleport(crate.Position + new Vector3(1f, 0.1f, 0f));
            yield return null;
            int before = KeysOnTheGround();
            Assert.IsTrue(supply.ServerTryOpen(crate, Combatant.LocalPlayer));
            yield return null;
            Assert.AreEqual(waiting - 1, zones.ServerCrateKeys);
            Assert.AreEqual(before + 1, KeysOnTheGround(), "a key lies by the opened crate");
        }

        [UnityTest]
        public IEnumerator Bot_PicksUpAKey_AndRidesItOutOfAWarnedRegion()
        {
            var zones = PollutionDirector.Instance;
            var rides = ElevatorRides.Instance;
            int playerRegion = rides.RegionOfStation(rides.StationAt(Pos));
            // The bot's region is warned at once (confirmed in 60 s), nobody else's.
            var rules = PollutionDirectorTests.Timed(60f, 59f);
            rules.TeamCooldownSeconds = 0f;
            PollutionDirectorTests.ConfigureFirst(zones, rules, playerRegion, false);
            int doomed = zones.Match.Timeline.Order[0];

            int station = -1;
            var sites = ElevatorDoors.Instance.Sites;
            for (int i = 0; i < sites.Count && station < 0; i++) if (sites[i].Region == doomed && !rides.StationBusy(i)) station = i;
            Assert.GreaterOrEqual(station, 0, "a station in the warned region");

            Combatant bot = null;
            foreach (var c in Combatant.All)
                if (c != null && !c.IsPlayer && !c.Dead.Value && c.GetComponent<KeyHolder>() != null) { bot = c; break; }
            Assert.IsNotNull(bot, "World has bots with key pockets");
            var holder = bot.GetComponent<KeyHolder>();
            var brain = bot.GetComponent("BotController");
            Assert.IsNotNull(brain);
            var brainType = brain.GetType();
            // This bot decides to use its key (the per-bot roll is BotPollutionPlan.WantsKey).
            brainType.GetField("_keyDecision", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(brain, 1);
            Func<string, int> stat = n => (int)brainType.GetField(n, BindingFlags.Public | BindingFlags.Static).GetValue(null);

            // Into the station; a key on the floor a step away.
            Assert.IsTrue(UnityEngine.AI.NavMesh.SamplePosition(sites[station].Spawn, out var onMesh, 2.5f, UnityEngine.AI.NavMesh.AllAreas));
            var agent = bot.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.enabled) Assert.IsTrue(agent.Warp(onMesh.position));
            else bot.transform.position = onMesh.position;
            var fwd = Quaternion.Euler(0f, sites[station].YawDeg, 0f) * Vector3.forward;
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Key, Amount = 1 }, onMesh.position + fwd * 1.2f + Vector3.up * 0.3f);
            int insertsBefore = stat("KeyInsertOk"), ridesBefore = stat("KeyRides");

            yield return WaitUntil(() => holder.Keys.Value > 0 || stat("KeyInsertOk") > insertsBefore, 20f, "the bot to pick the key up");
            yield return WaitUntil(() => stat("KeyInsertOk") > insertsBefore, 30f, "the bot to insert the key (at " + bot.transform.position + ", region " + zones.RegionIndexAt(bot.transform.position) + ")");
            yield return WaitUntil(() => stat("KeyRides") > ridesBefore, 30f, "the bot to choose a destination");
            Assert.IsTrue(rides.ServerRiding(bot), "the bot rides");
            yield return WaitUntil(() => !rides.ServerRiding(bot), 15f, "the bot's arrival");
            yield return null;
            int arrived = rides.StationAt(bot.transform.position);
            Assert.GreaterOrEqual(arrived, 0, "the bot comes out in a station");
            Assert.AreNotEqual(doomed, rides.RegionOfStation(arrived), "out of the warned region");
        }
    }
}
