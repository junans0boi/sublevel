using System.Collections;
using FishNet.Object;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #170, ADR-142: the server validates what a client asks for. A remote client at 100 ms / 2 % loss plays every kind of
    // action a player has and is never refused; a modified client's requests are refused, counted, and change nothing.
    public class ServerGuardNetTests : ReplicationVehicleBase
    {
        protected override string LogDir => "Logs/net-replication/guard";

        NetworkObject SpawnLoot(LootEntry entry, Vector3 at)
        {
            MatchDirector.Instance.ServerSpawnLoot(entry, at);
            NetworkObject found = null;
            foreach (var item in Object.FindObjectsByType<LootItem>(FindObjectsSortMode.None))
                if (item.NetworkObject.IsServerInitialized && (item.transform.position - at).sqrMagnitude < 1f) found = item.NetworkObject;
            Assert.IsNotNull(found, "the loot spawned on the server");
            return found;
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator LegitPlay_At100ms_IsNeverRefused()
        {
            var car = FourSeatCar;
            yield return GetIn(Remote, car, 0, 0); // the harness teleports to the door (trusted)
            Net.Simulate(Remote);
            var p = Remote.Player;
            var server = RemoteOnHost;
            server.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            server.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Pistol, Amount = 12 });
            server.ServerGive(new LootEntry { Kind = ThrowableRules.SmokeLootKind, Amount = 2 });
            yield return Until(() => server.ActiveWeaponId != 0, "armed");
            yield return SeatedEverywhere(car, p, 0, Net.Host, Remote.Manager);

            MoveGuard.TrustTeleports = false; // from here on only what a real player can do
            MoveGuard.ResetStats();
            ServerGuard.ResetStats();

            // Out of the car with F: the owner moves itself beside the door and tells the server (a real teleport).
            SetLocal(Remote);
            KeyFrames.Frame.Interact = true;
            yield return Until(() => car.OccupantId(0) == -1, "out of the car");
            yield return Seconds(1f);

            // Running.
            Remote.Input.Frame.Move = new Vector2(0f, 1f);
            Remote.Input.Frame.Sprint = true;
            yield return Seconds(2f);
            Remote.Input.Frame.Move = Vector2.zero;
            Remote.Input.Frame.Sprint = false;

            // Slots, stance, aim, look.
            var stance = p.GetComponent<CombatantStance>();
            foreach (int slot in new[] { 0, 1, 2, 0 }) { p.CmdSelectSlot(slot); yield return Seconds(0.15f); }
            p.CmdSwapSlots(0, 1); // there and back again
            yield return Seconds(0.2f);
            p.CmdSwapSlots(0, 1);
            yield return Seconds(0.2f);
            p.CmdSelectSlot(0);
            p.CmdSetStance((int)AimMode.Sights, 1);
            stance.CmdSetStance((int)Stance.Crouch);
            stance.CmdSetLookPitch(-45f);
            yield return Seconds(0.3f);
            p.CmdSetStance((int)AimMode.Hip, 0);
            stance.CmdSetStance((int)Stance.Stand);
            yield return Seconds(0.3f);

            // Shooting at the weapon's own rate (a little slower), a reload, punches.
            float interval = GameData.Weapons.Find(server.ActiveWeaponId).FireInterval;
            var aim = (p.transform.forward + Vector3.up * 0.3f).normalized;
            for (int i = 0; i < 15; i++) { p.CmdFire(aim); yield return Seconds(interval * 1.1f); }
            p.CmdReload();
            yield return Seconds(0.3f);
            for (int i = 0; i < 3; i++) { p.CmdPunch(aim); yield return Seconds(0.5f); }

            // Picking things up and dropping one.
            var medkit = SpawnLoot(new LootEntry { Kind = LootKind.Medkit, Amount = 1 }, server.transform.position + server.transform.forward * 1.5f);
            yield return Seconds(0.3f);
            p.CmdPickup(medkit);
            yield return Seconds(0.3f);
            p.CmdDropMedkit();
            yield return Seconds(0.3f);

            // A throw, a ping, an emote.
            var thrower = p.GetComponent<ThrowableUser>();
            thrower.CmdSetThrowable((int)ThrowableKind.Smoke, true);
            yield return Seconds(0.3f);
            thrower.CmdThrow(aim);
            yield return Seconds(1.2f);
            p.CmdPing((byte)PingKind.Here, p.EyePosition + p.transform.forward * 20f);
            yield return Seconds(0.5f);
            p.CmdPing((byte)PingKind.Enemy, p.EyePosition + p.transform.forward * 30f);
            yield return Seconds(0.5f);
            p.CmdEmote((byte)Emote.Wave);
            yield return Seconds(0.6f);
            p.CmdEmote((byte)Emote.Cheer);
            yield return Seconds(1f);

            Log($"refused: {ServerGuard.Total} ({ServerGuard.Summary()}), move corrections {MoveGuard.Corrections}");
            Assert.AreEqual(0, ServerGuard.Total, "a legitimate player is never refused: " + ServerGuard.Summary());
            Assert.AreEqual(0, MoveGuard.Corrections);
        }

        // r5 playtest: the island countdown moves everybody into the elevators (WaitingRoom -> MatchDirector.ServerDeployFromIsland:
        // ServerRelocate + ServerCorrect). The owner's body follows and echoes CmdTeleported for the 2.5 km jump; that echo is no cheat.
        [UnityTest, Timeout(240000)]
        public IEnumerator ServerMove_FarAway_IsNotRefusedAsATeleport()
        {
            yield return PlaceInArena(10f);
            Net.Simulate(Remote);
            var server = RemoteOnHost;
            MoveGuard.TrustTeleports = false;
            MoveGuard.ResetStats();
            ServerGuard.ResetStats();
            var to = server.transform.position + new Vector3(2500f, 0f, 0f);
            server.transform.position = to;
            server.GetComponent<MoveGuard>().ServerRelocate(to);
            server.GetComponent<CombatantStance>().ServerCorrect(to);
            yield return Until(() => Flat(Remote.Player.transform.position - to) < 2f, "the owner's body followed the server");
            yield return Seconds(2f);
            Log($"after the server's move: {ServerGuard.Summary()}");
            Assert.AreEqual(0, ServerGuard.Count(GuardKind.Teleport), "the owner's echo of the server's own move is not refused");
            Assert.Less(Flat(server.transform.position - to), 2f, "the server's body stays where the server put it");
        }

        static float Flat(Vector3 d) => new Vector2(d.x, d.z).magnitude;

        [UnityTest, Timeout(240000)]
        public IEnumerator CheatingRequests_AreRefused_Counted_AndChangeNothing()
        {
            var car = FourSeatCar;
            yield return PlaceInArena(10f); // trusted: the harness puts both on the arena floor, 3 km from the car
            Net.Simulate(Remote);
            var p = Remote.Player;
            var server = RemoteOnHost;
            server.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return Until(() => server.ActiveWeaponId == Rifle, "armed");
            MoveGuard.TrustTeleports = false;
            MoveGuard.ResetStats();
            ServerGuard.ResetStats();
            var stance = p.GetComponent<CombatantStance>();
            var thrower = p.GetComponent<ThrowableUser>();

            // Teleport across the map (and a body that really jumped): refused, then put back.
            var home = server.transform.position;
            Remote.Controller.Teleport(home + new Vector3(0f, 0f, 100f));
            yield return Until(() => ServerGuard.Count(GuardKind.Teleport) == 1, "the 100 m teleport refused");
            yield return Until(() => MoveGuard.Corrections >= 1, "and the body corrected");
            yield return Seconds(2.5f);
            Assert.Less((server.transform.position - home).magnitude, 3f, "the server's body is back where it was");
            Log($"after teleport: {ServerGuard.Summary()}");

            // Fire faster than the weapon can: most of 30 requests in one instant are refused as a flood.
            for (int i = 0; i < 30; i++) p.CmdFire(p.transform.forward);
            yield return Until(() => ServerGuard.Count(GuardKind.Fire) >= 20, "fire flood counted");
            Assert.LessOrEqual(ServerGuard.Count(GuardKind.Fire), 26, "the burst allowance still passes");

            // Arguments that are not slots, modes, numbers.
            int slots = ServerGuard.Count(GuardKind.Slot);
            p.CmdSelectSlot(99);
            p.CmdSwapSlots(-1, 7);
            p.CmdDropWeapon(500);
            p.CmdPickupToSlot(null, 9);
            yield return Until(() => ServerGuard.Count(GuardKind.Slot) >= slots + 3, "bad slots counted");
            p.CmdSetStance(9, 0);
            p.CmdDropAmmo(999);
            stance.CmdSetLookPitch(float.NaN);
            thrower.CmdThrow(new Vector3(float.NaN, 0f, 1f));
            yield return Until(() => ServerGuard.Count(GuardKind.Arg) >= 4, "bad arguments counted");

            // Picking up what lies 50 m away.
            var far = SpawnLoot(new LootEntry { Kind = LootKind.Medkit, Amount = 1 }, server.transform.position + new Vector3(0f, 0.3f, 50f));
            p.CmdPickup(far);
            yield return Until(() => ServerGuard.Count(GuardKind.Distance) >= 1, "far pickup counted");
            Assert.IsTrue(far.IsSpawned, "the medkit is still lying there");

            // Pings and emotes pressed 12 times in a frame; entering a car 3 km away.
            for (int i = 0; i < 12; i++) { p.CmdPing((byte)PingKind.Here, p.EyePosition + p.transform.forward * 10f); p.CmdEmote((byte)Emote.Wave); }
            yield return Until(() => ServerGuard.Count(GuardKind.Rate) >= 10, "ping and emote floods counted");
            Replica(Remote.Manager, car).RequestEnter(p);
            yield return Until(() => ServerGuard.Count(GuardKind.Vehicle) >= 1, "far enter counted");
            Assert.AreEqual(-1, car.OccupantId(0), "nobody got in");

            Log($"refused: {ServerGuard.Total} ({ServerGuard.Summary()})");
        }
    }
}
