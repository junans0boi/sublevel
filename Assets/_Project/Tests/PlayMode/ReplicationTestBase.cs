using System;
using System.Collections;
using System.IO;
using FishNet.Object;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #163/#164: the host plus a remote client (MultiHarness) in the TestBox, for the replication audits. A remote
    // client's view of the world is what its own NetworkManager spawned; the host's objects are the server's.
    public abstract class ReplicationTestBase
    {
        protected const int Pistol = 1, Rifle = 3, Sniper = 5;
        protected MultiHarness Net;
        protected RemoteClient Remote;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();
        protected virtual string LogDir => "Logs/net-replication";

        protected void Log(string line)
        {
            Debug.Log(line);
            _log.AppendLine(line);
        }

        protected Combatant HostPlayer => Net.HostPlayer;
        // The host's player as the remote client holds it.
        protected Combatant HostOnRemote => MultiHarness.ReplicaOf(Net.Remote0(), HostPlayer);
        // The remote's player on the server (the object the host holds).
        protected Combatant RemoteOnHost => MultiHarness.ReplicaOf(Net.Host, Remote.Player);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _log.Clear();
            Net = new MultiHarness { Log = Log };
            yield return Net.StartHost("TestBox");
            yield return Net.AddClient(c => Remote = c);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            SmokeZones.Clear(); // a cloud still up when the scene went away leaves its entry in the static table
            yield return Net.Teardown();
        }

        protected static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
        }

        protected IEnumerator Until(Func<bool> cond, string what, float timeout = 10f) => NetHarness.WaitUntil(cond, what, Log, timeout);

        // Floor and a back wall far from the TestBox; both players are put on it facing +z. Returns the wall.
        protected GameObject Arena(float wallDistance = 90f)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ReplicationArenaFloor";
            floor.transform.position = new Vector3(3000f, -0.5f, 3060f);
            floor.transform.localScale = new Vector3(80f, 1f, 220f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(3000f, 3f, 2990f + wallDistance);
            wall.transform.localScale = new Vector3(40f, 8f, 1f);
            Physics.SyncTransforms();
            return wall;
        }

        // Host player at z=2990, remote player `gap` metres in front of it, both facing +z.
        protected IEnumerator PlaceInArena(float gap)
        {
            if (GameObject.Find("ReplicationArenaFloor") == null) Arena();
            HostPlayer.GetComponent<PlayerController>().Teleport(new Vector3(3000f, 0.1f, 2990f));
            Remote.Controller.Teleport(new Vector3(3000f, 0.1f, 2990f + gap));
            HostPlayer.GetComponent<PlayerController>().LookAt(new Vector3(3000f, 1.6f, 3200f));
            Remote.Controller.LookAt(new Vector3(3000f, 1.6f, 2900f));
            yield return Seconds(1f);
        }

        // One server-side shot with the spread closed.
        protected static bool ShootAt(Combatant shooter, Vector3 point)
        {
            float before = shooter.StanceSpreadFactor;
            shooter.StanceSpreadFactor = 0f;
            bool fired = shooter.ServerFire(point - shooter.EyePosition);
            shooter.StanceSpreadFactor = before;
            return fired;
        }

        protected IEnumerator GiveAndHold(Combatant onServer, int weapon, int rounds = 30)
        {
            onServer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = weapon, Amount = rounds });
            yield return Until(() => onServer.ActiveWeaponId == weapon, "weapon in hand on the server");
        }

        protected static Combatant On(FishNet.Managing.NetworkManager manager, Combatant who) => MultiHarness.ReplicaOf(manager, who);

        protected static T Part<T>(Combatant c) where T : Component => c.GetComponent<T>();
    }

    public static class MultiHarnessExt
    {
        // The first remote client's manager.
        public static FishNet.Managing.NetworkManager Remote0(this MultiHarness net) => net.Clients[0].Manager;
    }
}
