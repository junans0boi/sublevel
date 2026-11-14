using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #165: vehicle damage, fire and explosion for REMOTE clients, and what a client that joins late sees of a vehicle
    // (docs/MP_AUDIT_VEHICLES.md). The shooter is the remote client; the server judges every shot.
    public class ReplicationVehicleDamageTests : ReplicationVehicleBase
    {
        IEnumerator RemoteShoots(Func<Vector3> target, Func<bool> done, int shots)
        {
            for (int fired = 0; fired < shots && !done(); fired++)
            {
                Remote.Player.CmdFire((target() - Remote.Player.EyePosition).normalized);
                yield return Seconds(0.2f);
            }
            yield return Seconds(0.3f);
        }

        [UnityTest]
        public IEnumerator RemoteShots_FlatTyre_Glass_Holes_ReachEveryClient_AndALateJoinerGetsTheHoles()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            var car = FourSeatCar;
            var damage = car.GetComponent<VehicleDamage>();
            var onA = Replica(Remote.Manager, car).GetComponent<VehicleDamage>();
            var onB = Replica(b.Manager, car).GetComponent<VehicleDamage>();
            int holesA = 0, holesB = 0, panesB = 0;
            onA.Holed += (_, __, ___, ____) => holesA++;
            onB.Holed += (_, __, ___, ____) => holesB++;
            onB.PaneHit += (_, __, ___) => panesB++;
            yield return GiveAndHold(RemoteOnHost, Rifle, 120);
            yield return Until(() => Remote.Player.ActiveWeaponId == Rifle, "A holds its rifle");

            var t = car.transform;
            Remote.Controller.Teleport(t.position - t.right * 5f + Vector3.up * 0.1f);
            yield return Seconds(0.6f);
            Remote.Controller.LookAt(t.position + Vector3.up * 0.8f);

            // The nearest tyre goes flat for everyone: grip, the dropped corner and the squashed wheel on the other client.
            int wheel = 0;
            float best = float.MaxValue;
            for (int i = 0; i < car.WheelCount; i++)
            {
                float d = (car.WheelPivot(i).position - Remote.Player.transform.position).sqrMagnitude;
                if (d < best) { best = d; wheel = i; }
            }
            yield return RemoteShoots(() => car.WheelPivot(wheel).position, () => (damage.FlatTyres.Value & (1 << wheel)) != 0, 16);
            Assert.AreNotEqual(0, damage.FlatTyres.Value & (1 << wheel), "the server flattened the tyre");
            var stateB = Replica(b.Manager, car).GetComponent<VehicleState>();
            yield return Until(() => (onB.FlatTyres.Value & (1 << wheel)) != 0 && (onA.FlatTyres.Value & (1 << wheel)) != 0, "flat tyre on both clients");
            yield return Until(() => stateB.GripOf(wheel) < 1f && stateB.DropOf(wheel) > 0f, "less grip and a lower corner on B");
            yield return Until(() => Replica(b.Manager, car).WheelPivot(wheel).localScale.y < 1f, "the wheel is squashed on B's screen");

            // The window cracks; the body takes holes (the shooter's own screen too) and loses health.
            var bounds = damage.LocalBounds();
            Vector3 Window() => t.TransformPoint(new Vector3(0f, bounds.min.y + bounds.size.y * 0.75f, bounds.center.z));
            yield return RemoteShoots(Window, () => damage.Panes.Value != 0, 6);
            Assert.AreNotEqual(0, damage.Panes.Value, "a pane cracked on the server");
            yield return Until(() => onB.Panes.Value == damage.Panes.Value && panesB > 0, "the crack reaches B (state and the hit that drew it)");
            float before = damage.Health.Value;
            Vector3 Door() => t.TransformPoint(new Vector3(0f, bounds.min.y + bounds.size.y * 0.32f, bounds.min.z + bounds.size.z * 0.5f));
            yield return RemoteShoots(Door, () => holesB >= 3, 8);
            Assert.Less(damage.Health.Value, before, "the body lost health");
            yield return Until(() => holesA > 0 && holesB > 0, "bullet holes on both clients");
            yield return Until(() => onB.Health.Value < before && Mathf.Abs(onB.Health.Value - damage.Health.Value) < 0.01f, "B reads the same health");
            Log($"holes: A {holesA}, B {holesB}; health {before:F2} -> {damage.Health.Value:F2}");

            // A client that joins now gets the holes shot so far (kept by the server), the glass and the tyre.
            RemoteClient late = null;
            yield return Net.AddClient(c => late = c);
            var lateCar = Replica(late.Manager, car);
            var lateVisual = lateCar.GetComponent<VehicleDamageVisual>();
            yield return Until(() => lateVisual.HolesShown > 0, "the late joiner sees the bullet holes");
            Assert.AreEqual(damage.Panes.Value, lateCar.GetComponent<VehicleDamage>().Panes.Value, "and the glass");
            Assert.AreNotEqual(0, lateCar.GetComponent<VehicleDamage>().FlatTyres.Value & (1 << wheel), "and the flat tyre");
            yield return Until(() => lateCar.WheelPivot(wheel).localScale.y < 1f, "drawn squashed");
        }

        [UnityTest]
        public IEnumerator Fire_SmokesBurnsDisablesAndExplodes_ForEveryClient_HurtingWhoStandsBeside_AndKillingThePassenger()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            var car = FourSeatCar;
            var damage = car.GetComponent<VehicleDamage>();
            yield return GetIn(b, car, 0, 0); // B sits in the driver's seat (nobody drives) and stays in the car
            var onA = Replica(Remote.Manager, car);
            var onB = Replica(b.Manager, car);
            var dmgA = onA.GetComponent<VehicleDamage>();
            var dmgB = onB.GetComponent<VehicleDamage>();
            int boomA = 0, boomB = 0;
            dmgA.Exploded += () => boomA++;
            dmgB.Exploded += () => boomB++;
            // A stands beside the car, outside it.
            var t = car.transform;
            Remote.Controller.Teleport(t.position - t.right * 3.2f + Vector3.up * 0.1f);
            yield return Seconds(0.8f);
            float hpA = Remote.Player.Hp.Value;

            damage.ServerSetHealth01(0.2f);
            yield return Until(() => dmgB.CurrentStage == VehicleDamageStage.BlackSmoke && dmgA.CurrentStage == VehicleDamageStage.BlackSmoke, "black smoke on both clients");
            Assert.IsFalse(onB.GetComponent<VehicleState>().Disabled, "a smoking car still drives");
            damage.ServerSetHealth01(0f);
            yield return Until(() => dmgB.CurrentStage == VehicleDamageStage.Burning && dmgA.CurrentStage == VehicleDamageStage.Burning, "burning on both clients");
            Assert.IsTrue(onB.GetComponent<VehicleState>().Disabled, "a burning car cannot be driven on B's side either");
            Assert.AreNotEqual(onA, Vehicle.NearestEnterable(onA.DoorPosition(0), 3f, out _), "and cannot be entered");
            yield return Until(() => dmgB.CurrentStage == VehicleDamageStage.Wrecked && dmgA.CurrentStage == VehicleDamageStage.Wrecked, "the explosion", VehicleDamageRules.For(car.Kind).BurnSeconds + 5f);
            yield return Seconds(0.4f);
            Assert.AreEqual(1, boomA, "the explosion event on A");
            Assert.AreEqual(1, boomB, "the explosion event on B");
            var visualB = onB.GetComponent<VehicleDamageVisual>();
            Assert.Greater(visualB.ExplosionsShown, 0, "shown on B");
            Assert.IsNotNull(visualB.BangSource);
            Assert.IsTrue(visualB.BangSource.isPlaying, "and heard on B");
            Assert.IsTrue(Remote.Player.Dead.Value || Remote.Player.Hp.Value < hpA, "the blast hurt A beside the car");
            Assert.Less(MultiHarness.ReplicaOf(b.Manager, Remote.Player).Hp.Value, hpA + 0.01f, "and B sees A's health");
            // The occupant was inside: out of the seat on every side (dead).
            yield return Until(() => b.Player.Dead.Value, "the occupant died in the blast");
            yield return Until(() => car.OccupantId(0) == -1 && dmgA.Vehicle.OccupantId(0) == -1 && onB.OccupantId(0) == -1, "the seat is free everywhere");
            Assert.IsTrue(onB.GetComponent<VehicleState>().Disabled, "the wreck stays disabled");

            RemoteClient late = null;
            yield return Net.AddClient(c => late = c);
            var lateCar = Replica(late.Manager, car);
            yield return Until(() => lateCar.GetComponent<VehicleDamage>().CurrentStage == VehicleDamageStage.Wrecked && lateCar.GetComponent<VehicleState>().Disabled, "the late joiner sees a wreck");
        }

        [UnityTest]
        public IEnumerator LateJoiner_SeesTheDriverAndPassengerSeated_TheEngineTheTankAndTheHorn()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            var car = FourSeatCar;
            yield return GetIn(Remote, car, 0, 0);
            yield return GetIn(b, car, 1, 1);
            SetLocal(Remote);
            car.GetComponent<VehicleFuel>().ServerSetFuel(0.42f);
            KeyFrames.Frame.Horn = true;
            yield return Until(() => car.GetComponent<VehicleHorn>().Sounding, "the horn sounds");

            RemoteClient late = null;
            yield return Net.AddClient(c => late = c);
            var sides = new[] { late.Manager };
            yield return SeatedEverywhere(car, Remote.Player, 0, sides);
            yield return SeatedEverywhere(car, b.Player, 1, sides);
            var lateCar = Replica(late.Manager, car);
            Assert.IsTrue(lateCar.EngineOn.Value, "the engine is on");
            Assert.AreEqual(0.42f, lateCar.GetComponent<VehicleFuel>().Fuel01, 0.02f, "the tank");
            yield return Until(() => lateCar.GetComponent<VehicleHorn>().Sounding && lateCar.GetComponent<VehicleAudio>().HornPlaying, "and the horn is heard by the late joiner");
            yield return Until(() => lateCar.GetComponent<VehicleAudio>().EnginePlaying, "with the engine running");
            KeyFrames.Frame.Horn = false;
        }
    }
}
