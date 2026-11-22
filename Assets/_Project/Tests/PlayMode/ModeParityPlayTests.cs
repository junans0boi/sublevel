using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // docs/MODE_PARITY_AUDIT.md: bot practice runs as a host, live solo/squad on a dedicated server with remote clients.
    // Each interaction here runs in both topologies through a remote client's own PlayerController and must behave the same.
    // In one process the peers share scene objects, so what reaches the remote client is read off the wire (a listener on its
    // own NetworkManager); DedicatedServerDoorPlayTests covers the separate-process build.
    public class ModeParityPlayTests
    {
        static readonly Vector3 Spot = new Vector3(3000f, 0f, 3000f);
        MultiHarness _net;

        HealRules _healBefore;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_healBefore != null) HealRules.Current = _healBefore;
            _healBefore = null;
            if (_net != null) yield return _net.Teardown();
            _net = null;
        }

        static Combatant ServerCopy(MultiHarness net, Combatant player) =>
            net.Host.ServerManager.Objects.Spawned.TryGetValue(player.ObjectId, out var nob) ? nob.GetComponent<Combatant>() : null;

        static IEnumerator Seconds(float s)
        {
            float end = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        // A remote client on a floor far from the scene's own things, the server holding its body there too.
        IEnumerator RemoteAtSpot(MultiHarness.Topology topology, List<DoorChanged> heard, System.Action<RemoteClient> done)
        {
            _net = new MultiHarness { Log = Debug.Log };
            yield return _net.Start(topology, "TestBox");
            RemoteClient remote = null;
            yield return _net.AddClient(c => remote = c, beforeConnect: nm => nm.ClientManager.RegisterBroadcast<DoorChanged>((m, _) => heard.Add(m)));
            Assert.IsFalse(remote.Player.IsServerInitialized, "the remote player is a client object in both topologies");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = Spot + Vector3.down * 0.5f;
            floor.transform.localScale = new Vector3(60f, 1f, 60f);
            remote.Controller.Teleport(Spot + Vector3.up * 0.1f);
            yield return NetHarness.WaitUntil(() => ServerCopy(_net, remote.Player) != null && (ServerCopy(_net, remote.Player).transform.position - Spot).sqrMagnitude < 1f, "the server holds the remote body at the spot", Debug.Log, 10f);
            done(remote);
        }

        // ADR-104 / r5-r8 playtests: F on a door from a remote client opens and shuts it, and every client is told, whatever the topology.
        [UnityTest]
        public IEnumerator RemoteClient_FOpensAndShutsADoor_InBothTopologies([Values] MultiHarness.Topology topology)
        {
            var heard = new List<DoorChanged>();
            RemoteClient remote = null;
            yield return RemoteAtSpot(topology, heard, c => remote = c);
            var system = DoorSystem.Instance;
            Assert.IsNotNull(system, "the match director adds the door system on the server");
            var door = system.Add(Door.Create(system.transform, system.Count, Spot + Vector3.forward * 2f, Quaternion.LookRotation(Vector3.back), 1.3f, 2.3f));
            Physics.SyncTransforms();
            remote.Controller.LookAt(door.transform.position + Vector3.up * 1.4f);
            yield return Seconds(0.3f);

            remote.Input.Frame.Interact = true;
            yield return NetHarness.WaitUntil(() => door.State != DoorState.Closed, $"[{topology}] the server opened the door for the remote client", Debug.Log, 5f);
            yield return NetHarness.WaitUntil(() => heard.Count > 0, $"[{topology}] the remote client was told", Debug.Log, 5f);
            Assert.AreEqual((byte)door.State, heard[heard.Count - 1].State);
            Assert.Less((heard[heard.Count - 1].At - door.transform.position).magnitude, 0.01f, "the message names the door by its place");

            yield return Seconds(0.5f);
            remote.Input.Frame.Interact = true;
            yield return NetHarness.WaitUntil(() => door.State == DoorState.Closed, $"[{topology}] F again shut it", Debug.Log, 5f);
            yield return NetHarness.WaitUntil(() => heard[heard.Count - 1].State == (byte)DoorState.Closed, $"[{topology}] the remote client was told it shut", Debug.Log, 5f);
        }

        // A dead remote player's restart reloaded the dedicated server's whole match for everyone (CmdRequestRestart / result screen).
        [UnityTest]
        public IEnumerator DeadPlayersRestart_DoesNotReloadADedicatedServersMatch()
        {
            var heard = new List<DoorChanged>();
            RemoteClient remote = null;
            yield return RemoteAtSpot(MultiHarness.Topology.Dedicated, heard, c => remote = c);
            var director = MatchDirector.Instance;
            LogAssert.Expect(LogType.Log, "[MatchDirector] restart refused: a dedicated server's match ends on its own");
            director.Restart();
            yield return Seconds(1f);
            Assert.AreSame(director, MatchDirector.Instance, "the match scene was not reloaded");
            Assert.IsTrue(remote.Manager.ClientManager.Started, "the remote client is still in the match");
        }

        // One server check for everyone: a body out of reach is refused the same way for the host's player path and a remote one.
        [UnityTest]
        public IEnumerator DoorRequest_OutOfReach_IsRefused_InBothTopologies([Values] MultiHarness.Topology topology)
        {
            var heard = new List<DoorChanged>();
            RemoteClient remote = null;
            yield return RemoteAtSpot(topology, heard, c => remote = c);
            var system = DoorSystem.Instance;
            var door = system.Add(Door.Create(system.transform, system.Count, Spot + Vector3.forward * 12f, Quaternion.LookRotation(Vector3.back), 1.3f, 2.3f));
            int index = system.Count - 1;
            var body = ServerCopy(_net, remote.Player);
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Doors\] refused F from .*m from door"));
            Assert.IsFalse(system.ServerRequest(body, index, door.transform.position), "12 m away: refused");
            Assert.AreEqual(DoorState.Closed, door.State);
            body.transform.position = Spot + Vector3.forward * 10.5f;
            Assert.IsTrue(system.ServerRequest(body, index, door.transform.position), "1.5 m away: opened");
            Assert.AreNotEqual(DoorState.Closed, door.State);
            yield return null;
        }

        // #482 (v0.3.0 "구급상자 사용안됨"): every healing item is used by the path a player takes — the inventory row (double or
        // right click), H and J — from a remote client in both topologies and from the host's own player. The medkit row used
        // H's best fit (a bandage under 75) and its right click dropped it.
        [UnityTest]
        public IEnumerator EveryHealingItem_IsUsedFromItsInputPath_InBothTopologies([Values] MultiHarness.Topology topology)
        {
            _healBefore = HealRules.Current;
            HealRules.Current = new HealRules
            {
                Bandage = new HealItemRules(0.3f, 10f, 75f), FirstAid = new HealItemRules(0.3f, 75f, 75f), Medkit = new HealItemRules(0.3f, 100f, 100f),
                Drink = new HealItemRules(0.3f, 0f, 0f) { Boost = 40f }, Pill = new HealItemRules(0.3f, 0f, 0f) { Boost = 60f },
                Syringe = new HealItemRules(0.3f, 0f, 0f) { Boost = 100f },
            };
            RemoteClient remote = null;
            yield return RemoteAtSpot(topology, new List<DoorChanged>(), c => remote = c);
            yield return HealEveryItem($"[{topology} remote]", remote.Player, ServerCopy(_net, remote.Player), remote.Input);
            if (topology == MultiHarness.Topology.Host)
                yield return HealEveryItem("[host's own player]", _net.HostPlayer, _net.HostPlayer, _net.HostInput);
        }

        IEnumerator HealEveryItem(string who, Combatant me, Combatant body, HarnessInput input)
        {
            Assert.IsNotNull(me, who + " player");
            Assert.IsNotNull(body, who + " server body");
            var heal = body.GetComponent<CombatantHealing>();
            Assert.IsNotNull(heal, "the player prefab carries CombatantHealing");
            body.ServerHeal(1000f);

            // Medkit from its inventory row, with bandages in the bag (H's best fit would be a bandage).
            Assert.AreEqual(2, heal.ServerAdd(HealKind.Bandage, 2));
            Assert.IsTrue(body.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 2 }));
            body.ServerTakeDamage(50f);
            Assert.IsTrue(InventoryBackend.RightClickHeal(InventorySourceKind.Medkit, out var kind), "the medkit row's right click uses it");
            InventoryBackend.UseHealing(me, kind);
            yield return NetHarness.WaitUntil(() => body.Hp.Value >= 99.9f, $"{who} the medkit row healed to full", Debug.Log, 5f);
            Assert.AreEqual(1, body.Medkits.Value, who + " one medkit used");
            Assert.AreEqual(2, heal.Bandages.Value, who + " no bandage used");
            body.ServerTakeDamage(50f);
            InventoryBackend.UseMedkit(me); // the row's double click
            yield return NetHarness.WaitUntil(() => body.Hp.Value >= 99.9f && body.Medkits.Value == 0, $"{who} double click used the other medkit", Debug.Log, 5f);
            Assert.AreEqual(2, heal.Bandages.Value, who + " still no bandage used");

            // First-aid kit from its row, bandages still in the bag.
            Assert.AreEqual(1, heal.ServerAdd(HealKind.FirstAid, 1));
            body.ServerTakeDamage(60f);
            InventoryBackend.UseHealing(me, HealKind.FirstAid);
            yield return NetHarness.WaitUntil(() => heal.FirstAids.Value == 0 && !heal.Using, $"{who} the first-aid kit was used", Debug.Log, 5f);
            Assert.AreEqual(75f, body.Hp.Value, 0.01f, who + " first aid heals to 75");

            // H: bandages (best fit under 75) repeat until they run out.
            body.ServerTakeDamage(30f); // 45
            input.Frame.Heal = true;
            yield return NetHarness.WaitUntil(() => heal.Bandages.Value == 0 && !heal.Using, $"{who} H used both bandages", Debug.Log, 5f);
            Assert.AreEqual(65f, body.Hp.Value, 0.01f, who + " two bandages: +20");

            // H with only a medkit left: the best fit is the medkit.
            Assert.IsTrue(body.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 1 }));
            input.Frame.Heal = true;
            yield return NetHarness.WaitUntil(() => body.Medkits.Value == 0 && !heal.Using, $"{who} H used the medkit", Debug.Log, 5f);
            Assert.AreEqual(100f, body.Hp.Value, 0.01f);

            // Boost items: J takes the smallest, the rows the others.
            Assert.AreEqual(1, heal.ServerAdd(HealKind.Drink, 1));
            Assert.AreEqual(1, heal.ServerAdd(HealKind.Pill, 1));
            Assert.AreEqual(1, heal.ServerAdd(HealKind.Syringe, 1));
            int done = heal.ServerCompleted;
            var mine = me.GetComponent<CombatantHealing>();
            // J picks the smallest boost from the owner's own counts, so they must have reached it.
            yield return NetHarness.WaitUntil(() => mine.Drinks.Value == 1 && mine.Pills.Value == 1 && mine.Syringes.Value == 1, $"{who} the boost counts reached the owner", Debug.Log, 5f);
            input.Frame.Boost = true;
            yield return NetHarness.WaitUntil(() => heal.Drinks.Value == 0 && !heal.Using, $"{who} J used the drink", Debug.Log, 5f);
            Assert.IsTrue(InventoryBackend.RightClickHeal(InventorySourceKind.Pill, out kind));
            InventoryBackend.UseHealing(me, kind);
            yield return NetHarness.WaitUntil(() => heal.Pills.Value == 0 && !heal.Using, $"{who} the pill row was used", Debug.Log, 5f);
            InventoryBackend.UseHealing(me, HealKind.Syringe);
            yield return NetHarness.WaitUntil(() => heal.Syringes.Value == 0 && !heal.Using, $"{who} the syringe row was used", Debug.Log, 5f);
            Assert.AreEqual(done + 3, heal.ServerCompleted, who + " three boosts finished");
            Assert.Greater(heal.Gauge.Value, 90f, who + " the syringe filled the gauge");
        }
    }
}
