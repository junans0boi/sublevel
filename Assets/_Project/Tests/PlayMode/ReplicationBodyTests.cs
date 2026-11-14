using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #165: swimming, hanging from a ledge, dying (death cam on the killer, the result numbers, spectating) and what a
    // client that joins late sees of all that, for REMOTE players (docs/MP_AUDIT_VEHICLES.md).
    public class ReplicationBodyTests : ReplicationTestBase
    {
        protected override string LogDir => "Logs/net-replication/body";

        SwimRules _swim;

        [UnitySetUp]
        public IEnumerator BodySetUp()
        {
            _swim = SwimRules.Current;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator BodyTearDown()
        {
            if (_swim != null) SwimRules.Current = _swim;
            yield return null;
        }

        static CombatantStance StanceOf(NetworkManager side, Combatant who) => MultiHarness.ReplicaOf(side, who).GetComponent<CombatantStance>();
        static CombatantSwim SwimOf(NetworkManager side, Combatant who) => MultiHarness.ReplicaOf(side, who).GetComponent<CombatantSwim>();

        // ---- Swimming ----

        [UnityTest]
        public IEnumerator Swimming_ARemoteSwimmerLiesOnTheSurfaceForEveryone_AndDrownsByTheServersCount()
        {
            SwimRules.Current = new SwimRules { DiveSeconds = 2f, RecoverSeconds = 1f };
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            var pool = TestPool.Create(new Vector3(3000f, 0f, 3000f), 12f, 2.0f, out _);
            Physics.SyncTransforms();
            var host = Net.Host;
            Remote.Controller.Teleport(new Vector3(3000f, 0.1f, 3000f));
            yield return Until(() => Remote.Controller.Swimming, "A swims on its own screen");
            yield return Seconds(1.5f);
            foreach (var side in new[] { host, b.Manager })
            {
                var swim = SwimOf(side, Remote.Player);
                var stance = StanceOf(side, Remote.Player);
                yield return Until(() => swim.InWater, "swimming seen on " + side.name);
                Assert.IsFalse(swim.HeadUnder, "head up on " + side.name);
                Assert.AreEqual((int)Stance.Prone, stance.State.Value, "the lying swim pose is the replicated stance on " + side.name);
                Assert.AreEqual(Remote.Player.transform.position.y, MultiHarness.ReplicaOf(side, Remote.Player).transform.position.y, 0.3f, "same height on the surface on " + side.name);
            }
            // No shooting from the water: the server refuses the remote swimmer too.
            RemoteOnHost.StanceSpreadFactor = 0f;
            Assert.IsFalse(RemoteOnHost.ServerFire(Vector3.forward), "no shooting while swimming");

            // Ctrl dive: the server's own count of the breath runs out and it hurts the swimmer; both clients see the health fall.
            float hp0 = Remote.Player.Hp.Value;
            var serverSwim = SwimOf(host, Remote.Player);
            Remote.Input.Frame.SlowWalk = true;
            yield return Until(() => serverSwim.HeadUnder && SwimOf(b.Manager, Remote.Player).HeadUnder, "the head is under for the server and for B");
            yield return Until(() => serverSwim.Breath <= 0f, "the server's breath ran out", 6f);
            yield return Seconds(1.5f);
            Assert.Greater(serverSwim.DrownDamage, 3f, "the server dealt drowning damage");
            Assert.Less(Remote.Player.Hp.Value, hp0 - 3f, "A's own health fell");
            Assert.Less(MultiHarness.ReplicaOf(b.Manager, Remote.Player).Hp.Value, hp0 - 3f, "and B sees it fall");

            // Up again: the breath comes back, no more damage.
            Remote.Input.Frame.SlowWalk = false;
            yield return Until(() => !serverSwim.HeadUnder, "head out again");
            yield return Until(() => serverSwim.Breath >= 0.99f, "breath back", 5f);
            float hp1 = Remote.Player.Hp.Value;
            yield return Seconds(0.8f);
            Assert.AreEqual(hp1, Remote.Player.Hp.Value, 1e-3f, "no damage with the head up");
            UnityEngine.Object.Destroy(pool);
        }

        // ---- Ledge ----

        // The server's copy of a player sits where the owner's body is, in the same world: the owner's own probes (ledge
        // search) would hit it. Test-only (in a real client the copy is in another process).
        static void HideServerCopy(Combatant onServer)
        {
            foreach (var c in onServer.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = 2;
        }

        // The jump key held (a jump, then the grab in the air) until `done`.
        IEnumerator HoldJumpUntil(RemoteClient who, Func<bool> done, string what)
        {
            float end = Time.time + 5f, nextLog = 0f;
            while (!done() && Time.time < end)
            {
                who.Input.Frame.Jump = true;
                if (Time.time >= nextLog)
                {
                    nextLog = Time.time + 0.15f;
                    Log($"  jump hold: at {who.Player.transform.position} yaw {who.Player.transform.eulerAngles.y:F0} parkour {who.Controller.ParkourUnderWay} ledge {who.Controller.OnLedge}");
                }
                yield return null;
            }
            who.Input.Frame.Jump = false;
            Assert.IsTrue(done(), what);
        }

        [UnityTest]
        public IEnumerator Ledge_ARemotePlayerHangsAndClimbs_EveryoneSeesTheMoveAndThePose()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            Arena();
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "LedgeBlock";
            block.transform.position = new Vector3(3000f, 1.15f, 3010f);
            block.transform.localScale = new Vector3(3f, 2.3f, 2f);
            Physics.SyncTransforms();
            HideServerCopy(RemoteOnHost);
            Remote.Controller.Teleport(new Vector3(3000f, 0.1f, 3008.4f));
            Remote.Controller.LookAt(new Vector3(3000f, 1.6f, 3100f));
            yield return Seconds(0.6f);
            var host = Net.Host;
            yield return HoldJumpUntil(Remote, () => Remote.Controller.OnLedge, "A catches the edge");
            yield return Seconds(0.6f);
            foreach (var side in new[] { host, b.Manager })
            {
                yield return Until(() => StanceOf(side, Remote.Player).ParkourMove == ParkourMove.Hang, "hang seen on " + side.name);
                var anim = MultiHarness.ReplicaOf(side, Remote.Player).GetComponent<CombatantAnimation>();
                yield return Until(() => anim.LedgePose > 0.95f, "hanging pose on " + side.name);
                Assert.Less(Vector3.Distance(MultiHarness.ReplicaOf(side, Remote.Player).transform.position, Remote.Player.transform.position), 0.4f, "same place on " + side.name);
            }
            // Pull up: B and the server see the climb, then A standing on top.
            Remote.Input.Frame.Jump = true;
            yield return null;
            var seen = new HashSet<ParkourMove>();
            var anims = MultiHarness.ReplicaOf(b.Manager, Remote.Player);
            float end = Time.time + 3f;
            while (Remote.Controller.InParkour && Time.time < end)
            {
                seen.Add(StanceOf(b.Manager, Remote.Player).ParkourMove);
                yield return null;
            }
            yield return Seconds(1f);
            Log("B saw " + string.Join(",", seen));
            Assert.IsTrue(seen.Contains(ParkourMove.LedgeClimb), "B saw the pull-up");
            Assert.AreEqual(ParkourMove.None, StanceOf(b.Manager, Remote.Player).ParkourMove, "and that it ended");
            Assert.AreEqual(ParkourMove.None, StanceOf(host, Remote.Player).ParkourMove);
            Assert.Greater(Remote.Player.transform.position.y, 2f, "A stands on the block");
            foreach (var side in new[] { host, b.Manager })
                Assert.Less(Vector3.Distance(MultiHarness.ReplicaOf(side, Remote.Player).transform.position, Remote.Player.transform.position), 0.5f, "on top on " + side.name);
            Assert.AreEqual(0, MoveGuard.Corrections, "a climb is not a cheat");
        }

        // ---- Death, result, spectating ----

        sealed class Heard
        {
            public readonly List<KillEntry> Kills = new List<KillEntry>();
            public readonly List<PlayerStats> Stats = new List<PlayerStats>();
            public readonly List<MatchStatus> Status = new List<MatchStatus>();
        }

        static Heard Listen(NetworkManager m)
        {
            var h = new Heard();
            m.ClientManager.RegisterBroadcast<KillEntry>((e, c) => h.Kills.Add(e));
            m.ClientManager.RegisterBroadcast<PlayerStats>((e, c) => h.Stats.Add(e));
            m.ClientManager.RegisterBroadcast<MatchStatus>((e, c) => h.Status.Add(e));
            return h;
        }

        [UnityTest]
        public IEnumerator Death_ARemoteVictim_GetsDeathCamOnItsKiller_TheResultNumbers_AndSpectatesKillerThenAnotherRemote()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            LocalSwitch.To(Remote);
            Assert.AreSame(Remote.Player, Combatant.LocalPlayer, "A is this process's local player");
            var heardA = Listen(Remote.Manager);
            var heardB = Listen(b.Manager);

            // A hurts the host first, so the result has a damage number; then the host kills A.
            yield return GiveAndHold(RemoteOnHost, Rifle);
            yield return PlaceInArena(15f);
            yield return Until(() => Remote.Player.ActiveWeaponId == Rifle, "A holds its rifle");
            float hostHp = HostPlayer.Hp.Value;
            Remote.Player.CmdFire((HostPlayer.transform.position + Vector3.up * 1.1f - Remote.Player.EyePosition).normalized);
            yield return Until(() => HostPlayer.Hp.Value < hostHp, "A's shot hurt the host");
            yield return Seconds(1.2f);
            float dealt = hostHp - HostPlayer.Hp.Value;
            var hostOnA = MultiHarness.ReplicaOf(Remote.Manager, HostPlayer);

            RemoteOnHost.ServerTakeDamage(new DamageInfo { Amount = 1000f, Attacker = HostPlayer, Zone = HitZone.Body, Cause = DamageCause.Shot, WeaponId = Rifle });
            yield return Until(() => Remote.Player.Dead.Value, "A is dead on its own screen");
            yield return Until(() => MultiHarness.ReplicaOf(b.Manager, Remote.Player).Dead.Value, "and on B's");

            // The wire: the kill, with killer and weapon, and A's numbers, reach both clients.
            foreach (var heard in new[] { heardA, heardB })
            {
                yield return Until(() => heard.Kills.Exists(k => k.VictimId == Remote.Player.ObjectId) && heard.Stats.Exists(s => s.Id == Remote.Player.ObjectId), "kill entry and stats on the wire");
                var kill = heard.Kills.Find(k => k.VictimId == Remote.Player.ObjectId);
                Assert.AreEqual(HostPlayer.ObjectId, kill.KillerId, "the killer");
                Assert.AreEqual(Rifle, kill.WeaponId, "the weapon");
                var stats = heard.Stats.Find(s => s.Id == Remote.Player.ObjectId);
                Assert.AreEqual(dealt, stats.Damage, 1f, "the damage A dealt");
                Assert.Greater(stats.Seconds, 1.5f, "how long A lasted");
            }

            // The screen of the dead remote player: the death cam names the killer, then the panel shows the numbers.
            var screen = ResultScreen.Instance;
            Assert.IsNotNull(screen);
            yield return Until(() => screen.Current == ResultScreen.Phase.DeathCam, "death cam");
            yield return Until(() => screen.DeathCamText.Length > 0, "death cam names the killer");
            StringAssert.Contains(MatchState.Instance.NameOfId(HostPlayer.ObjectId), screen.DeathCamText);
            yield return Until(() => screen.Current == ResultScreen.Phase.Panel, "the panel after the death cam", ResultScreen.DeathCamSeconds + 3f);
            yield return Seconds(0.3f);
            Log($"panel: {screen.TitleText} | {screen.PlacementText} | kills {screen.KillsText} | damage {screen.DamageText} | time {screen.TimeText} | by {screen.KilledByText}");
            Assert.AreEqual(MatchOutcome.Eliminated, screen.Outcome);
            Assert.AreEqual("탈락", screen.TitleText);
            Assert.AreEqual(Mathf.RoundToInt(dealt), int.Parse(screen.DamageText), 1, "damage number");
            Assert.AreNotEqual("0:00", screen.TimeText, "survival time");
            StringAssert.Contains(MatchState.Instance.NameOfId(HostPlayer.ObjectId), screen.KilledByText);

            // Spectate: the killer first; Next walks on to the other remote player, whose camera follows.
            screen.StartSpectating();
            Assert.AreEqual(ResultScreen.Phase.Spectate, screen.Current);
            Assert.AreEqual(HostPlayer.ObjectId, screen.Spectate.Target.ObjectId, "the killer is watched first");
            bool sawB = false;
            for (int i = 0; i < 12 && !sawB; i++)
            {
                screen.Spectate.Next();
                sawB = screen.Spectate.Target.ObjectId == b.Player.ObjectId;
            }
            Assert.IsTrue(sawB, "the other remote player can be watched");
            screen.Spectate.Place();
            Assert.IsTrue(screen.Spectate.BarShown);
            // The watched remote dies: the view goes to the one who killed it.
            var watched = screen.Spectate.Target;
            MultiHarness.ReplicaOf(Net.Host, b.Player).ServerTakeDamage(new DamageInfo { Amount = 1000f, Attacker = HostPlayer, Zone = HitZone.Body, Cause = DamageCause.Shot, WeaponId = Rifle });
            yield return Until(() => watched.Dead.Value, "the watched player died");
            yield return Until(() => { screen.Spectate.Place(); return screen.Spectate.Target.ObjectId == HostPlayer.ObjectId; }, "on to the killer of the watched one", 4f);

            // A client that joins now: both dead players are dead for it, and the count in the match status is right.
            Heard late = null;
            RemoteClient c3 = null;
            yield return Net.AddClient(c => c3 = c, beforeConnect: nm => late = Listen(nm));
            yield return Until(() => late.Status.Count > 0, "the late client got the match status");
            Assert.IsTrue(MultiHarness.ReplicaOf(c3.Manager, Remote.Player).Dead.Value, "A is dead for the late joiner");
            Assert.IsTrue(MultiHarness.ReplicaOf(c3.Manager, b.Player).Dead.Value, "and B");
            var status = late.Status[late.Status.Count - 1];
            Assert.AreEqual(MatchState.Instance.TotalCount - 2, status.Alive, "the alive count already excludes them");
        }

        // ---- A client that joins late ----

        [UnityTest]
        public IEnumerator LateJoiner_SeesASwimmerAndAHangingPlayer()
        {
            SwimRules.Current = new SwimRules { DiveSeconds = 20f, RecoverSeconds = 9f };
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            var pool = TestPool.Create(new Vector3(3000f, 0f, 3000f), 12f, 2.0f, out _);
            Arena();
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.transform.position = new Vector3(3000f, 1.15f, 3040f);
            block.transform.localScale = new Vector3(3f, 2.3f, 2f);
            Physics.SyncTransforms();
            Remote.Controller.Teleport(new Vector3(3000f, 0.1f, 3000f));
            HideServerCopy(MultiHarness.ReplicaOf(Net.Host, b.Player));
            b.Controller.Teleport(new Vector3(3000f, 0.1f, 3038.4f));
            b.Controller.LookAt(new Vector3(3000f, 1.6f, 3100f));
            yield return Seconds(0.8f);
            yield return HoldJumpUntil(b, () => b.Controller.OnLedge, "B catches the edge");
            yield return Until(() => Remote.Controller.Swimming, "A swims");
            yield return Seconds(0.8f);

            RemoteClient c3 = null;
            yield return Net.AddClient(c => c3 = c);
            yield return Until(() => MultiHarness.ReplicaOf(c3.Manager, Remote.Player) != null && MultiHarness.ReplicaOf(c3.Manager, b.Player) != null, "both players spawned for the late joiner");
            yield return Until(() => SwimOf(c3.Manager, Remote.Player).InWater, "the late joiner sees the swimmer in the water");
            Assert.AreEqual((int)Stance.Prone, StanceOf(c3.Manager, Remote.Player).State.Value);
            yield return Until(() => StanceOf(c3.Manager, b.Player).ParkourMove == ParkourMove.Hang, "and the hanging player");
            var anim = MultiHarness.ReplicaOf(c3.Manager, b.Player).GetComponent<CombatantAnimation>();
            yield return Until(() => anim.LedgePose > 0.95f, "in the hanging pose");
            UnityEngine.Object.Destroy(pool);
        }
    }

    // Test-only: in one process every owned player calls itself "local"; the code under test (VehicleInput, the result
    // screen, the spectate camera) reads these, so a test picks whose view it is.
    public static class LocalSwitch
    {
        public static void To(RemoteClient who) => To(who.Controller, who.Player);

        public static void To(PlayerController controller, Combatant player)
        {
            typeof(PlayerController).GetProperty("Local").GetSetMethod(true).Invoke(null, new object[] { controller });
            typeof(Combatant).GetProperty("LocalPlayer").GetSetMethod(true).Invoke(null, new object[] { player });
        }
    }
}
