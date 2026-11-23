using System.Collections;
using System.Reflection;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #500-#503 (ADR-560): what the server no longer takes on a client's word. A modified remote client at 100 ms / 2 % loss
    // teleports through a wall, leaves its fall report out, claims a crash and impacts on a standing car, and floods the inventory.
    public class ServerTrustNetTests : ReplicationVehicleBase
    {
        protected override string LogDir => "Logs/net-replication/trust";

        // #500: 10 m forward through a wall is refused and put back; the same 10 m with nothing in the way is taken.
        [UnityTest, Timeout(240000)]
        public IEnumerator Teleport_ThroughAWall_IsRefused_AClearOneIsTaken()
        {
            yield return PlaceInArena(10f);
            Net.Simulate(Remote);
            var server = RemoteOnHost;
            var home = server.transform.position;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = home + new Vector3(0f, 2f, 5f);
            wall.transform.localScale = new Vector3(10f, 4f, 0.5f);
            Physics.SyncTransforms();
            yield return Seconds(0.5f);
            MoveGuard.TrustTeleports = false;
            MoveGuard.ResetStats();
            ServerGuard.ResetStats();

            Remote.Controller.Teleport(home + new Vector3(0f, 0f, 10f));
            yield return Until(() => ServerGuard.Count(GuardKind.Teleport) == 1, "the teleport through the wall refused");
            yield return Until(() => MoveGuard.Corrections >= 1, "and the body corrected");
            yield return Seconds(2.5f);
            Assert.Less((server.transform.position - home).magnitude, 3f, "the server's body is back on its side of the wall");

            Object.Destroy(wall);
            Physics.SyncTransforms();
            yield return Seconds(1f);
            ServerGuard.ResetStats();
            var to = server.transform.position + new Vector3(0f, 0f, 10f);
            Remote.Controller.Teleport(to);
            yield return Seconds(2f);
            Log($"after the clear teleport: {ServerGuard.Summary()}");
            Assert.AreEqual(0, ServerGuard.Count(GuardKind.Teleport), "nothing in the way: taken (leaving a vehicle is this)");
            Assert.Less((server.transform.position - to).magnitude, 3f);
        }

        // #501: an owner that never reports its fall is hurt by the drop the server watched (less the tolerance).
        [UnityTest, Timeout(240000)]
        public IEnumerator AFallWithNoReport_StillHurts()
        {
            yield return PlaceInArena(10f);
            Net.Simulate(Remote);
            var server = RemoteOnHost;
            var stance = server.GetComponent<CombatantStance>();
            MoveGuard.TrustTeleports = false;
            float hp = server.Hp.Value;
            int hurt = stance.FallsHurt;

            // A modified client: its own fall tracking (and so its report) is off; it moves the body itself.
            var body = Remote.Player.transform;
            Remote.Controller.enabled = false;
            var cc = Remote.Player.GetComponent<CharacterController>();
            cc.enabled = false;
            var ground = body.position;
            var top = ground + Vector3.up * 10f;
            body.position = top;
            yield return Seconds(1f);
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                body.position = Vector3.Lerp(top, ground, t / 0.6f);
                yield return null;
            }
            body.position = ground;
            yield return Until(() => stance.FallsHurt > hurt, "the server hurt the unreported fall", 5f);
            Log($"unreported fall: {stance.LastFallMeters:F1} m, hp {hp} -> {server.Hp.Value}");
            Assert.AreEqual(9f, stance.LastFallMeters, 1f, "the watched 10 m less the 1 m tolerance");
            Assert.AreEqual(DamageCause.Fall, stance.LastFall.Cause);
            Assert.Less(server.Hp.Value, hp);
            cc.enabled = true;
            Remote.Controller.enabled = true;
        }

        static void Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);

        // #502: the driver of a standing car claims a 40 m/s impact and a 999 m/s crash: no damage, nobody thrown out.
        [UnityTest, Timeout(240000)]
        public IEnumerator ACrashOrImpactClaimedOnAStandingCar_ChangesNothing()
        {
            var car = FourSeatCar;
            yield return GetIn(Remote, car, 0, 0);
            Net.Simulate(Remote);
            yield return Seconds(3f);   // standing: the server's recent speed settles
            var damage = car.GetComponent<VehicleDamage>();
            float health = damage.Health.Value;
            var mine = Replica(Remote.Manager, car);
            ServerGuard.ResetStats();
            Call(mine.GetComponent<VehicleDamage>(), "CmdImpact", 40f, Vector3.zero);
            Call(mine, "CmdCrash", 999f, null);
            yield return Seconds(1.5f);
            Log($"after the claims: health {health} -> {damage.Health.Value}, cap {damage.ServerReportCap():F1}, {ServerGuard.Summary()}");
            Assert.Less(damage.ServerReportCap(), 10f, "the server saw a standing car");
            Assert.AreEqual(health, damage.Health.Value, 1e-4f, "a 40 m/s impact from standing is cut below the damage floor");
            Assert.AreEqual(Remote.Player.ObjectId, car.OccupantId(0), "a car does not crash; the driver stays in");
        }

        // #503: a hundred drop/pickup requests in a frame: most refused as a flood; the clicks of a person are not.
        [UnityTest, Timeout(240000)]
        public IEnumerator InventoryFlood_IsRefused_NormalClicksAreNot()
        {
            yield return PlaceInArena(10f);
            Net.Simulate(Remote);
            var p = Remote.Player;
            var server = RemoteOnHost;
            server.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 3 });
            yield return Seconds(0.5f);
            ServerGuard.ResetStats();
            for (int i = 0; i < 10; i++) { p.CmdSwapWeaponSlots(); yield return Seconds(0.15f); }
            Assert.AreEqual(0, ServerGuard.Count(GuardKind.Rate), "ten clicks in 1.5 s pass");

            for (int i = 0; i < 100; i++) p.CmdSwapWeaponSlots();
            yield return Until(() => ServerGuard.Count(GuardKind.Rate) >= 70, "the flood refused");
            Log($"after the flood: {ServerGuard.Summary()}");
            Assert.LessOrEqual(ServerGuard.Count(GuardKind.Rate), 85, "the burst allowance still passes");
        }
    }
}
