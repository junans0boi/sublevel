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
    // #421 #422 (ADR-501/502): the island's range and board with the host and a remote client in the real World scene. The remote steps
    // into a booth, gets the booth's gun from the server, scores on a target; a person in the lane takes nothing; a gun outside a booth
    // does not fire; stepping out takes the gun back; both clients turn the board together; the departure takes every loan back, clears
    // the records and leaves the match inventory as it was.
    public class WaitingRangePlayTests
    {
        MultiHarness _net;
        RemoteClient _remote;
        bool _oldFlag;
        int _lootAtStart;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _oldFlag = MatchRules.Current.UseWaitingIsland;
            MatchRules.Current.UseWaitingIsland = true;
            WaitingRoom.ForceEnabled = true;
            DedicatedServer.StartSecondsOverride = 75f;
            _net = new MultiHarness { Log = Debug.Log };
            yield return _net.StartHost("World");
            yield return NetHarness.WaitUntil(() => WaitingRoom.Current != null, "the waiting room open", Debug.Log, 60f);
            yield return _net.AddClient(c => _remote = c, beforeConnect: WaitingHud.Attach);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MatchRules.Current.UseWaitingIsland = _oldFlag;
            WaitingRoom.ForceEnabled = false;
            DedicatedServer.StartSecondsOverride = -1f;
            yield return _net.Teardown();
        }

        static Vector3 World(float lx, float y, float lz) => new Vector3(WaitingIsland.WorldX(lx), y, WaitingIsland.WorldZ(lz));
        static float Deck => GameObject.Find("WaitingIsland").transform.position.y;
        IEnumerator Until(System.Func<bool> cond, string what, float timeout = 10f) => NetHarness.WaitUntil(cond, what, Debug.Log, timeout);

        static string Inventory(Combatant c)
        {
            var s = new System.Text.StringBuilder();
            for (int i = 0; i < Loadout.SlotCount; i++) s.Append(c.WeaponInSlot(i)).Append(',');
            for (int t = 0; t < AmmoNames.Count; t++) s.Append(c.ReserveFor((AmmoType)t)).Append(',');
            return s.ToString();
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator Booth_LendsAGun_ScoresOnTargets_HurtsNobody_TakesItBack_AndTheBoardTurnsTogether()
        {
            var room = WaitingRoom.Current;
            var host = _net.HostPlayer;
            var remoteOnHost = MultiHarness.ReplicaOf(_net.Host, _remote.Player);
            var hostHud = _net.Host.GetComponent<WaitingHud>();
            var remoteHud = _remote.Manager.GetComponent<WaitingHud>();
            Assert.IsNotNull(hostHud, "the host's board"); Assert.IsNotNull(remoteHud, "the remote's board");
            string inventoryBefore = Inventory(remoteOnHost);
            _lootAtStart = Object.FindObjectsByType<LootItem>(FindObjectsSortMode.None).Length;
            float deck = Deck;
            int weapon = IslandRules.Current.RangeWeaponId;

            // Into booth 0: the server lends the gun, the remote sees it in its own hands.
            var stand = WaitingRange.Stand(0);
            _remote.Controller.Teleport(World(stand.x, deck + 0.1f, stand.z));
            yield return Until(() => room.HasLoan(remoteOnHost), "the booth lent its gun");
            yield return Until(() => _remote.Player.ActiveWeaponId == weapon && _remote.Player.ReserveFor(GameData.Weapons.Find(weapon).Ammo) > 0, "the remote holds the booth gun with rounds");

            // The loan cannot be dropped (and so not picked up outside or carried into the match): the server refuses both drops.
            var ammo = GameData.Weapons.Find(weapon).Ammo;
            int reserve = remoteOnHost.ReserveFor(ammo);
            _remote.Player.CmdDropWeapon(Loadout.SidearmSlot);
            _remote.Player.CmdDropAmmo((int)ammo);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(weapon, remoteOnHost.WeaponInSlot(Loadout.SidearmSlot), "the booth gun stays in hand");
            Assert.GreaterOrEqual(remoteOnHost.ReserveFor(ammo), reserve, "no rounds left the booth");
            Assert.AreEqual(0, Object.FindObjectsByType<LootItem>(FindObjectsSortMode.None).Length - _lootAtStart, "nothing was dropped on the island");

            // The remote shoots its target (the server judges): the score goes up and comes back in the records broadcast.
            var t = WaitingRange.Target(0, 0);
            var chest = World(t.x, deck + 1.15f, t.z);
            int key = room.KeyOf(remoteOnHost);
            float end = Time.realtimeSinceStartup + 15f;
            while (room.RangeRecords.ScoreOf(key) < 2)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "two target hits from the booth");
                _remote.Player.CmdFire((chest - _remote.Player.EyePosition).normalized);
                yield return new WaitForSeconds(0.35f);
            }
            yield return Until(() => remoteHud.RangeTop.Count > 0 && remoteHud.RangeTop[0].Score >= 2, "the remote's board has the range TOP3");

            // A person in the lane: the booth gun's bullets stop on them harmlessly, and nothing scores.
            var hp = host.Hp.Value;
            host.GetComponent<PlayerController>().Teleport(World(t.x + 3f, deck + 0.1f, t.z));
            yield return new WaitForSeconds(1f);
            int scoreBefore = room.RangeRecords.ScoreOf(key);
            for (int i = 0; i < 6; i++)
            {
                _remote.Player.CmdFire((host.transform.position + Vector3.up * 1.2f - _remote.Player.EyePosition).normalized);
                yield return new WaitForSeconds(0.35f);
            }
            Assert.AreEqual(hp, host.Hp.Value, 1e-3f, "a person takes nothing from the booth gun");
            Assert.AreEqual(scoreBefore, room.RangeRecords.ScoreOf(key), "a person is not a target");

            // A gun outside a booth does not fire at all (the server's gate), whoever holds it.
            host.GetComponent<PlayerController>().Teleport(World(0f, deck + 0.1f, 0f));
            yield return new WaitForSeconds(0.5f);
            host.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = weapon, Amount = 12 });
            host.ServerSelectSlot(Loadout.SidearmSlot);
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(weapon, host.ActiveWeaponId);
            Assert.IsFalse(host.ServerFire(remoteOnHost.transform.position - host.EyePosition), "no shot outside a booth");
            host.ServerStripLoadout();

            // Both clients turn the board together (the countdown's seconds drive it).
            int agree = 0, samples = 0;
            var pages = new HashSet<BoardPage>();
            end = Time.realtimeSinceStartup + 14f;
            while (Time.realtimeSinceStartup < end)
            {
                samples++;
                if (hostHud.PageShown == remoteHud.PageShown) agree++;
                pages.Add(remoteHud.PageShown);
                yield return new WaitForSeconds(0.5f);
            }
            Debug.Log($"[WaitingRangePlayTests] board pages agree {agree}/{samples}, seen {string.Join(",", pages)}");
            Assert.GreaterOrEqual(agree, samples - 2, "both clients show the same page (a frame of delay at a turn at most)");
            Assert.GreaterOrEqual(pages.Count, 2, "the board turned");

            // Out of the booth: the gun and its rounds go back to the booth.
            _remote.Controller.Teleport(World(-10f, deck + 0.1f, 0f));
            yield return Until(() => !room.HasLoan(remoteOnHost), "taken back on the way out");
            yield return Until(() => Inventory(_remote.Player) == inventoryBefore, "the remote's own copy has nothing left");
            Assert.AreEqual(inventoryBefore, Inventory(remoteOnHost));

            // In again, and the countdown ends while the gun is in hand: the departure takes it back and the records go with the island.
            _remote.Controller.Teleport(World(stand.x, deck + 0.1f, stand.z));
            yield return Until(() => room.HasLoan(remoteOnHost), "lent again");
            yield return Until(() => WaitingRoom.Current == null, "the countdown ended", 90f);
            Assert.AreEqual(inventoryBefore, Inventory(remoteOnHost), "the match starts with the inventory it had before the island");
            Assert.AreEqual(0, room.RangeRecords.Top(3).Count, "no records outlive the island");
            yield return Until(() => remoteHud.RangeTop.Count == 0, "the remote's board forgot the records");
            yield return Until(() => Inventory(_remote.Player) == inventoryBefore, "the remote's own copy agrees");
        }
    }
}
