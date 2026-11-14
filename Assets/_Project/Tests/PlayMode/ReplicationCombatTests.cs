using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #163: the combat systems of the single-player tickets seen from a REMOTE client (docs/MP_AUDIT_COMBAT.md).
    // The host plays the server; `Remote` is a second NetworkManager in the same process with its own replicas.
    public class ReplicationCombatTests : ReplicationTestBase
    {
        protected override string LogDir => "Logs/net-replication/combat";

        // ---- shots, hit zones, impacts ----

        [UnityTest]
        public IEnumerator HostShot_ReachesTheRemote_WithZoneVictimAndImpact()
        {
            yield return GiveAndHold(HostPlayer, Rifle);
            yield return PlaceInArena(12f);
            var shooterOnRemote = HostOnRemote;
            var victimOnRemote = Remote.Player; // the remote's own player is the victim
            yield return Until(() => shooterOnRemote.ActiveWeaponId == Rifle, "remote sees the rifle in the host's hands");
            var seen = new List<Combatant.ShotResult>();
            int fired = 0;
            shooterOnRemote.ShotFired += (a, b) => fired++;
            shooterOnRemote.ShotResolved += (in Combatant.ShotResult s) => seen.Add(s);
            // The server's copy of a remote player is a body bullets can hit (its capsule is on although it is not the owner's).
            Assert.IsTrue(RemoteOnHost.GetComponent<CharacterController>().enabled, "hit target on the server");

            // A bullet flying on shows its muzzle first (Launch) and its hit later; a near one is judged at once (Whole).
            Combatant.ShotResult Landed() => seen.Find(s => s.Phase != Combatant.ShotPhase.Launch);
            float hp0 = victimOnRemote.Hp.Value;
            var feet = RemoteOnHost.transform.position;
            // (the capsule is about 1.62 m tall, the head band starts at 1.5 m)
            Assert.IsTrue(ShootAt(HostPlayer, feet + Vector3.up * 1.55f), "head shot fired");
            yield return Until(() => seen.Exists(s => s.Phase != Combatant.ShotPhase.Launch), "the shot's result on the remote");
            var head = Landed();
            Assert.AreEqual(1, fired, "muzzle flash / sound event once");
            Assert.AreEqual(HitZone.Head, head.Zone, "head zone for the hit marker");
            Assert.AreSame(victimOnRemote, head.Victim, "the victim is the remote's own body");
            Assert.IsTrue(head.Hit == ShotHit.Combatant || head.Hit == ShotHit.Kill);
            Assert.AreEqual(BulletSurface.Body, head.Surface);
            yield return Until(() => victimOnRemote.Hp.Value < hp0 || victimOnRemote.Dead.Value, "victim health replicated");

            // A leg shot is a limb zone (pale blue marker) and its health comes through too.
            yield return Seconds(1f);
            seen.Clear();
            if (!RemoteOnHost.Dead.Value)
            {
                float hp1 = victimOnRemote.Hp.Value;
                Assert.IsTrue(ShootAt(HostPlayer, RemoteOnHost.transform.position + Vector3.up * 0.35f));
                yield return Until(() => seen.Exists(s => s.Phase != Combatant.ShotPhase.Launch), "leg shot result");
                Assert.AreEqual(HitZone.Limb, Landed().Zone);
                yield return Until(() => victimOnRemote.Hp.Value < hp1 || victimOnRemote.Dead.Value, "leg damage replicated");
            }
        }

        [UnityTest]
        public IEnumerator RemoteShot_ReachesTheHost_AndTheShooterSeesItsOwn()
        {
            yield return GiveAndHold(RemoteOnHost, Rifle);
            yield return PlaceInArena(15f);
            yield return Until(() => Remote.Player.ActiveWeaponId == Rifle, "remote sees its own rifle");
            int onHost = 0, onOwn = 0;
            Combatant.ShotResult own = default;
            RemoteOnHost.ShotFired += (a, b) => onHost++;
            Remote.Player.ShotResolved += (in Combatant.ShotResult s) => { onOwn++; own = s; };
            float hp0 = HostPlayer.Hp.Value;
            // The server judges from the owner's replicated eyes and the direction the owner asked for.
            Remote.Player.CmdFire((HostPlayer.transform.position + Vector3.up * 1.1f - Remote.Player.EyePosition).normalized);
            yield return Until(() => onHost >= 1 && onOwn >= 1, "both sides get the shot");
            Assert.IsNotNull(own.Victim, "the remote's hit marker has the victim");
            Assert.AreEqual(HostPlayer.ObjectId, own.Victim.ObjectId, "it is the host's body (the remote's copy of it)");
            Assert.Less(HostPlayer.Hp.Value, hp0, "the host took the damage");
        }

        [UnityTest]
        public IEnumerator BallisticsProjectile_ShowsMuzzleThenImpact_OnTheRemote()
        {
            yield return GiveAndHold(HostPlayer, Sniper, 5);
            var wall = Arena(160f);
            yield return PlaceInArena(40f);
            Remote.Controller.Teleport(new Vector3(3020f, 0.1f, 2990f)); // out of the line of fire
            yield return Seconds(0.5f);
            var shooterOnRemote = HostOnRemote;
            var phases = new List<Combatant.ShotPhase>();
            var hits = new List<ShotHit>();
            shooterOnRemote.ShotResolved += (in Combatant.ShotResult s) => { phases.Add(s.Phase); hits.Add(s.Hit); };
            Assert.IsTrue(ShootAt(HostPlayer, new Vector3(3000f, 1.6f, 2990f + 160f)));
            Assert.Greater(BulletWorld.InFlight, 0, "far target: the bullet flies on the server");
            yield return Until(() => phases.Contains(Combatant.ShotPhase.Impact), "impact message on the remote after the flight", 15f);
            Assert.AreEqual(Combatant.ShotPhase.Launch, phases[0], "muzzle part first");
            Assert.AreEqual(ShotHit.World, hits[phases.IndexOf(Combatant.ShotPhase.Impact)]);
            Object.Destroy(wall);
        }

        [UnityTest]
        public IEnumerator Penetration_BothMarksOfAThinWall_ReachTheRemote()
        {
            yield return GiveAndHold(HostPlayer, Rifle);
            Arena(120f);
            yield return PlaceInArena(30f);
            Remote.Controller.Teleport(new Vector3(3020f, 0.1f, 2990f));
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.transform.position = new Vector3(3000f, 1.5f, 3000f);
            slab.transform.localScale = new Vector3(4f, 4f, 0.2f);
            slab.AddComponent<BulletSurfaceTag>().Surface = BulletSurface.Wood;
            Physics.SyncTransforms();
            yield return Seconds(0.5f);
            int impacts = 0, wholes = 0;
            HostOnRemote.ShotResolved += (in Combatant.ShotResult s) =>
            {
                if (s.Phase == Combatant.ShotPhase.Impact && s.Hit == ShotHit.World) impacts++;
                if (s.Phase == Combatant.ShotPhase.Whole) wholes++;
            };
            Assert.IsTrue(ShootAt(HostPlayer, new Vector3(3000f, 1.5f, 3000f)));
            yield return Until(() => impacts >= 2, "entry and exit marks of the wood slab on the remote");
            Assert.GreaterOrEqual(impacts, 2);
            Object.Destroy(slab);
        }

        [UnityTest]
        public IEnumerator Punch_ReachesTheRemote()
        {
            yield return PlaceInArena(0.9f);
            Remote.Controller.LookAt(HostPlayer.transform.position + Vector3.up * 1.2f);
            HostPlayer.GetComponent<PlayerController>().LookAt(Remote.Player.transform.position + Vector3.up * 1.2f);
            yield return Until(() => HostOnRemote.ActiveWeaponId == 0 || true, "ready");
            int punches = 0;
            Combatant.PunchResult last = default;
            HostOnRemote.Punched += (in Combatant.PunchResult p) => { punches++; last = p; };
            float hp0 = Remote.Player.Hp.Value;
            Assert.IsTrue(HostPlayer.ServerPunch((Remote.Player.transform.position + Vector3.up * 1.2f - HostPlayer.EyePosition).normalized));
            yield return Until(() => punches >= 1, "punch message on the remote");
            Assert.AreEqual(ShotHit.Combatant, last.Hit);
            Assert.AreSame(Remote.Player, last.Victim);
            yield return Until(() => Remote.Player.Hp.Value < hp0, "punch damage replicated");
        }

        // ---- attachments, suppressor, recoil inputs ----

        [UnityTest]
        public IEnumerator Attachments_FittedOnTheServer_ShowOnTheRemoteReplica_WithMods_Model_AndMuffledSound()
        {
            yield return GiveAndHold(HostPlayer, Rifle);
            var onRemote = HostOnRemote;
            var gear = HostPlayer.GetComponent<CombatantOptics>();
            foreach (int id in new[] { 11, 12, 15, 16, 5 }) Assert.IsTrue(HostPlayer.ServerGive(Optics.ToLoot(id)), "fits: " + id);
            var remoteGear = onRemote.GetComponent<CombatantOptics>();
            int slot = HostPlayer.ActiveSlot.Value;
            yield return Until(() => remoteGear.AttachmentInSlot(slot, AttachSlot.Muzzle) == 11 && remoteGear.AttachmentInSlot(slot, AttachSlot.Grip) == 12
                && remoteGear.AttachmentInSlot(slot, AttachSlot.Stock) == 15 && remoteGear.AttachmentInSlot(slot, AttachSlot.Mag) == 16 && remoteGear.ActiveOptic == 5, "all five parts replicated");
            Assert.IsTrue(remoteGear.ActiveMods.Suppressed, "the suppressor flag is known to the remote");
            Assert.AreEqual(gear.ActiveMods.Pitch, remoteGear.ActiveMods.Pitch, 1e-5f, "recoil modifiers are the same on both sides");
            // The magazine size the server grants shows in the replicated magazine and reserve.
            Assert.AreEqual(HostPlayer.ActiveMag, onRemote.ActiveMag);
            // The model on the gun, as the remote draws it.
            var view = onRemote.GetComponent<AttachmentVisual>();
            yield return Until(() => view != null && view.Part(AttachSlot.Muzzle) != null && view.Part(AttachSlot.Mag) != null, "the remote draws the attachments on the held gun");
            // The audio hook that makes the shot sound far away follows the replicated suppressor.
            var audio = onRemote.GetComponent<CombatantAudio>();
            yield return Until(() => audio != null && audio.Suppressed, "the remote's audio treats the shot as suppressed");
            // Bots (server) hear it from a quarter of the distance.
            float plain = BotRules.Current.GunshotHearingRadius;
            Assert.AreEqual(plain / SoundRules.SuppressedDistanceFactor, HostPlayer.ShotHearingRadius(plain), 1e-3f);
            // And taking it off is seen as well.
            gear.ServerRemove(Attachments.GearIndex(slot, AttachSlot.Muzzle));
            yield return Until(() => !remoteGear.ActiveMods.Suppressed && audio.Suppressed == false, "suppressor gone on the remote");
        }

        [UnityTest]
        public IEnumerator Zeroing_TheRemoteOwnerSetsItsOwnDistanceOnTheServer()
        {
            yield return GiveAndHold(RemoteOnHost, Sniper, 5);
            RemoteOnHost.ServerGive(Optics.ToLoot(6)); // a 4x/8x scope the sniper takes
            var gear = Remote.Player.GetComponent<CombatantOptics>();
            yield return Until(() => gear.ActiveOptic != 0, "scope fitted");
            var def = gear.ActiveOpticDef;
            var rules = GameData.Rules.Ballistics;
            float meters = Zeroing.Distance(def, rules, Zeroing.IndexOf(def, rules, 300));
            gear.CmdSetZero(300);
            yield return Until(() => Mathf.Approximately(RemoteOnHost.ZeroMeters, meters), "the server holds the remote owner's zero");
        }

        // ---- throwables ----

        sealed class Counts
        {
            public int Blast, Smoke, Flash, Fire;
            public float SmokeSeconds = -1f;
        }

        Counts Watch()
        {
            var c = new Counts();
            ThrownItem.ClientExploded += (p, t) => c.Blast++;
            ThrownItem.ClientSmoked += (p, r, s, t) => { c.Smoke++; c.SmokeSeconds = s; };
            ThrownItem.ClientFlashed += (p, t) => c.Flash++;
            ThrownItem.ClientFired += (p, r, s, t) => c.Fire++;
            return c;
        }

        // Two clients (the host's own and the remote) in this process: each effect arrives twice.
        const int Clients = 2;

        [UnityTest]
        public IEnumerator Throwables_AllKinds_ReachTheRemote_FragDamagesRemote_FireBurnsRemote_CookingIsSeen()
        {
            var before = ThrowableRules.Current;
            ThrowableRules.Current = new ThrowableRules { FuseSeconds = 1.2f };
            var counts = Watch();
            try
            {
                var user = HostPlayer.GetComponent<ThrowableUser>();
                var userOnRemote = HostOnRemote.GetComponent<ThrowableUser>();
                foreach (var kind in new[] { LootKind.Grenade, LootKind.Smoke, LootKind.Flash, LootKind.Molotov })
                    Assert.IsTrue(HostPlayer.ServerGive(new LootEntry { Kind = kind, Amount = 2 }), "pickup " + kind);
                yield return Until(() => userOnRemote.Grenades.Value == 2 && userOnRemote.Smokes.Value == 2 && userOnRemote.Flashes.Value == 2 && userOnRemote.Molotovs.Value == 2, "carried counts replicated");
                yield return PlaceInArena(8f);
                // Cooking: the pin out is a replicated flag.
                Assert.IsTrue(user.ServerBeginCook());
                yield return Until(() => userOnRemote.Cooking.Value, "pin pulled seen on the remote");
                Assert.Greater(userOnRemote.CookRemaining, 0f);
                var at = RemoteOnHost.transform.position;

                // A frag grenade lands at the remote player's feet.
                float hp0 = Remote.Player.Hp.Value;
                // 7 m behind the remote body (mid-range damage); the host stands well outside the blast.
                Assert.IsTrue(user.ServerThrow(at + new Vector3(0f, 0.4f, 7f), Vector3.zero, ThrowableKind.Frag));
                yield return Until(() => counts.Blast >= Clients, "blast shown on both clients", 12f);
                yield return Until(() => Remote.Player.Hp.Value < hp0 || Remote.Player.Dead.Value, "blast damage replicated to the victim");
                Assert.AreEqual(1, userOnRemote.Grenades.Value, "the count went down on the remote (cooked one was thrown)");
                Assert.IsFalse(RemoteOnHost.Dead.Value, "a mid-range blast hurts, it does not kill");

                // Smoke, flash, fire bottle.
                // (one throw a second at most)
                yield return Seconds(1.1f);
                Assert.IsTrue(user.ServerThrow(at + new Vector3(0f, 0.4f, -6f), Vector3.zero, ThrowableKind.Smoke));
                yield return Seconds(1.1f);
                Assert.IsTrue(user.ServerThrow(at + new Vector3(8f, 0.4f, 0f), Vector3.zero, ThrowableKind.Flash));
                yield return Seconds(1.1f);
                Assert.IsTrue(user.ServerThrow(at + new Vector3(0f, 0.4f, 0f), Vector3.down * 2f, ThrowableKind.Molotov));
                yield return Until(() => counts.Smoke >= Clients && counts.Flash >= Clients && counts.Fire >= Clients, "smoke, flash and fire shown on both clients", 12f);
                yield return Until(() => Remote.Player.Burning.Value, "the remote player burns (replicated flag)");
            }
            finally
            {
                ThrowableRules.Current = before;
            }
        }

        // ---- boost / heal, armour, backpack ----

        [UnityTest]
        public IEnumerator HealAndBoost_Progress_Gauge_AndItemCounts_ReachTheRemote()
        {
            var healing = HostPlayer.GetComponent<CombatantHealing>();
            var onRemote = HostOnRemote.GetComponent<CombatantHealing>();
            HostPlayer.ServerTakeDamage(55f);
            Assert.AreEqual(2, healing.ServerAdd(HealKind.Bandage, 2));
            Assert.AreEqual(1, healing.ServerAdd(HealKind.Pill, 1));
            yield return Until(() => onRemote.Bandages.Value == 2 && onRemote.Pills.Value == 1, "counts replicated");
            Assert.IsTrue(healing.ServerBegin(HealKind.Bandage));
            yield return Until(() => onRemote.Using && onRemote.Kind == HealKind.Bandage, "the remote sees the bandage being applied");
            yield return Seconds(onRemote.Seconds * 0.5f);
            Assert.That(onRemote.Progress01, Is.InRange(0.2f, 0.9f), "progress runs on the remote's own clock");
            // (the next bandage goes on by itself, so the count is what shows the first one was used up)
            yield return Until(() => onRemote.Bandages.Value == 1, "bandage used up, seen on the remote", 15f);
            healing.ServerCancel();
            yield return Until(() => !onRemote.Using, "the remote sees the use stop");
            Assert.IsTrue(healing.ServerBegin(HealKind.Pill));
            yield return Until(() => onRemote.Gauge.Value > 0f, "boost gauge replicated", 15f);
            Assert.Greater(onRemote.BoostStep, 0);
            float hp = HostOnRemote.Hp.Value;
            yield return Seconds(3f);
            Assert.GreaterOrEqual(HostOnRemote.Hp.Value, hp, "the boost keeps healing and the remote sees the health");
        }

        [UnityTest]
        public IEnumerator ArmourAndBackpack_Tiers_Durability_AndCapacity_ReachTheRemote_AndWearDown()
        {
            var gear = HostPlayer.GetComponent<CombatantGear>();
            var carry = HostPlayer.GetComponent<CombatantCarry>();
            gear.ServerEquip(ArmorSlot.Helmet, 2);
            gear.ServerEquip(ArmorSlot.Vest, 3);
            carry.ServerEquipBackpack(2);
            var gearR = HostOnRemote.GetComponent<CombatantGear>();
            var carryR = HostOnRemote.GetComponent<CombatantCarry>();
            yield return Until(() => gearR.HelmetTier.Value == 2 && gearR.VestTier.Value == 3 && carryR.BackpackTier.Value == 2, "tiers replicated");
            Assert.AreEqual(carry.CapacityTenths, carryR.CapacityTenths, "same bag size on both sides");
            Assert.IsNotNull(HostOnRemote.GetComponent<BackpackVisual>(), "the pack is drawn on the replica");
            float full = gearR.VestDurability.Value;
            yield return GiveAndHold(RemoteOnHost, Rifle);
            yield return PlaceInArena(10f);
            yield return Until(() => HostOnRemote != null, "ready");
            Assert.IsTrue(ShootAt(RemoteOnHost, HostPlayer.transform.position + Vector3.up * 1.1f), "remote shoots the armoured host");
            yield return Until(() => gearR.VestDurability.Value < full, "the vest wear is seen on the remote");
        }

        // ---- loot, pickup, death crate ----

        [UnityTest]
        public IEnumerator Loot_PickedUpByTheRemote_GoesToItsLoadout_AndDeathCrateReachesTheRemote()
        {
            // Pickup: a rifle lies next to the remote's player; the remote asks, the server hands it over.
            var remoteSpawned = Remote.Manager.ClientManager.Objects.Spawned;
            RemoteOnHost.transform.position = RemoteOnHost.transform.position; // (position is the owner's)
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 20 }, Remote.Player.transform.position + Vector3.up * 0.3f + Vector3.forward * 0.5f);
            LootItem onRemote = null;
            yield return Until(() =>
            {
                foreach (var nob in remoteSpawned.Values)
                {
                    var item = nob != null ? nob.GetComponent<LootItem>() : null;
                    if (item != null && item.Kind.Value == (int)LootKind.Weapon && item.WeaponId.Value == Rifle && (item.transform.position - Remote.Player.transform.position).sqrMagnitude < 4f) { onRemote = item; return true; }
                }
                return false;
            }, "the remote sees the loot item");
            Remote.Player.CmdPickup(onRemote.NetworkObject);
            yield return Until(() => Remote.Player.WeaponInSlot(0) == Rifle || Remote.Player.WeaponInSlot(1) == Rifle, "pickup arrived in the remote's loadout");
            yield return Until(() => !onRemote.IsSpawned, "the item is gone on the remote");

            // Death crate: the host's player dies next to the remote; its things lie around and the marker stands.
            var weaponHost = HostPlayer;
            yield return GiveAndHold(weaponHost, Pistol);
            weaponHost.GetComponent<CombatantGear>().ServerEquip(ArmorSlot.Vest, 1);
            weaponHost.GetComponent<CombatantHealing>().ServerAdd(HealKind.Bandage, 3);
            int before = CountLoot(remoteSpawned);
            weaponHost.ServerTakeDamage(500f);
            yield return Until(() => HostOnRemote.Dead.Value, "death replicated");
            var crate = HostOnRemote.GetComponent<DeathCrate>();
            yield return Until(() => CountLoot(remoteSpawned) >= before + 3, "the dropped contents (pistol, vest, bandages ...) appear on the remote");
            yield return Until(() => crate.MarkerVisible, "the crate marker stands over the body on the remote");
        }

        static int CountLoot(IReadOnlyDictionary<int, NetworkObject> spawned)
        {
            int n = 0;
            foreach (var nob in spawned.Values) if (nob != null && nob.GetComponent<LootItem>() != null) n++;
            return n;
        }

        // ---- a client joining while a cloud is up ----

        [UnityTest]
        public IEnumerator LateJoiner_GetsTheSmokeCloudAndTheFireStillBurning()
        {
            var counts = Watch();
            HostPlayer.ServerGive(new LootEntry { Kind = LootKind.Smoke, Amount = 1 });
            HostPlayer.ServerGive(new LootEntry { Kind = LootKind.Molotov, Amount = 1 });
            yield return PlaceInArena(8f);
            var user = HostPlayer.GetComponent<ThrowableUser>();
            var at = HostPlayer.transform.position + new Vector3(0f, 0.4f, 6f);
            Assert.IsTrue(user.ServerThrow(at, Vector3.zero, ThrowableKind.Smoke));
            yield return Seconds(1.1f);
            Assert.IsTrue(user.ServerThrow(at + Vector3.right * 12f, Vector3.down * 2f, ThrowableKind.Molotov));
            yield return Until(() => counts.Smoke >= Clients && counts.Fire >= Clients, "both effects open for the two clients", 12f);
            yield return Seconds(2f);
            int smokes = counts.Smoke, fires = counts.Fire;
            counts.SmokeSeconds = -1f;
            RemoteClient late = null;
            yield return Net.AddClient(c => late = c);
            yield return Until(() => counts.Smoke > smokes && counts.Fire > fires, "the third client is told of the cloud and the fire that are already up", 8f);
            Assert.Less(counts.SmokeSeconds, SmokeRules.Current.Seconds - 1f, "it is shown with what is left of its time, not from the start");
            Assert.Greater(counts.SmokeSeconds, 0f);
        }
    }
}
