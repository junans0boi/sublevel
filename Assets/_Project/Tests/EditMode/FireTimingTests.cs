using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // Fire-rate timing: a steady cadence for held fire, tolerant of jitter, never faster than the weapon.
    public class FireTimingTests
    {
        const double Interval = 0.1;
        const double Early = 0.04;

        // Requests as a server sees them from a client firing every 0.1 s: a few milliseconds early or late each time.
        static readonly double[] Jittered = { 0, 0.097, 0.21, 0.295, 0.405, 0.49, 0.61, 0.70, 0.795, 0.91 };

        [Test]
        public void JitteredRequests_AllFire()
        {
            var cadence = new FireCadence();
            int fired = 0;
            foreach (double t in Jittered)
                if (cadence.TryFire(t, Interval, Early)) fired++;
            Assert.AreEqual(10, fired);
        }

        [Test]
        public void WithoutTolerance_ARequestAHairEarlyIsDropped()
        {
            // What the server did before: this is the gap in the burst.
            var cadence = new FireCadence();
            Assert.IsTrue(cadence.TryFire(0, Interval));
            Assert.IsFalse(cadence.TryFire(0.097, Interval));
        }

        [Test]
        public void RequestAtHalfTheInterval_IsRefused()
        {
            var cadence = new FireCadence();
            Assert.IsTrue(cadence.TryFire(0, Interval, Early));
            Assert.IsFalse(cadence.TryFire(0.05, Interval, Early));
            Assert.IsTrue(cadence.TryFire(0.065, Interval, Early), "within the tolerance");
            // The early shot did not move the schedule: the next one is due at 0.2, not at 0.165.
            Assert.IsFalse(cadence.TryFire(0.15, Interval, Early));
            Assert.IsTrue(cadence.TryFire(0.165, Interval, Early));
        }

        [Test]
        public void Spamming_NeverBeatsTheWeaponsRate()
        {
            var cadence = new FireCadence();
            var times = new List<double>();
            for (int i = 0; i < 10000; i++) // a request every millisecond for 10 s
            {
                double t = i * 0.001;
                if (cadence.TryFire(t, Interval, Early)) times.Add(t);
            }
            Assert.That(times.Count, Is.InRange(100, 101), "10 s at 0.1 s per shot, plus the first");
            for (int k = 1; k < times.Count; k++)
                Assert.GreaterOrEqual(times[k] - times[0], k * Interval - Early - 1e-9, "shot " + k + " came too soon");
        }

        [Test]
        public void HeldFire_KeepsTheWeaponsRate_WhateverTheFrameRate()
        {
            // 60 fps, 0.11 s per shot (not a whole number of frames), held for 3 s: 28 shots are due.
            Assert.AreEqual(28, ShotsInThreeSeconds(1.0 / 60.0, catchUp: true));
            Assert.AreEqual(28, ShotsInThreeSeconds(1.0 / 144.0, catchUp: true));
            Assert.AreEqual(28, ShotsInThreeSeconds(1.0 / 37.0, catchUp: true));
            // Scheduling from the frame the shot happened in (the old way) rounds every interval up to whole frames.
            Assert.AreEqual(26, ShotsInThreeSeconds(1.0 / 60.0, catchUp: false));
        }

        static int ShotsInThreeSeconds(double frame, bool catchUp)
        {
            var cadence = new FireCadence();
            int shots = 0;
            for (int i = 0; i * frame < 3.0; i++)
            {
                double now = i * frame;
                if (!cadence.Ready(now)) continue;
                cadence.Fired(now, 0.11, catchUp ? frame : 0);
                shots++;
            }
            return shots;
        }

        [Test]
        public void ClientCadence_IsAcceptedByTheServer_ShotForShot()
        {
            // The client fires on 60 fps frames with catch-up; the server checks the same moments with its tolerance.
            var rules = new FireRules();
            const double frame = 1.0 / 60.0, interval = 0.075;
            var client = new FireCadence();
            var server = new FireCadence();
            int sent = 0, accepted = 0;
            for (int i = 0; i < 600; i++)
            {
                double now = i * frame;
                if (!client.Ready(now)) continue;
                client.Fired(now, interval, rules.ClientCatchUp(interval, frame));
                sent++;
                if (server.TryFire(now, interval, rules.ServerEarly(interval))) accepted++;
            }
            Assert.Greater(sent, 100);
            Assert.AreEqual(sent, accepted);
        }

        [Test]
        public void Rules_CapTheToleranceByTheInterval()
        {
            var rules = new FireRules { ServerEarlySeconds = 0.05f, MaxEarlyFraction = 0.4f, ClientCatchUpSeconds = 0.035f };
            Assert.AreEqual(0.044, rules.ServerEarly(0.11), 1e-6);
            Assert.AreEqual(0.05, rules.ServerEarly(0.85), 1e-6);
            Assert.AreEqual(0.03, rules.ClientCatchUp(0.075, 0.1), 1e-6, "never more than the server tolerates");
            Assert.AreEqual(0.01, rules.ClientCatchUp(0.11, 0.01), 1e-6, "never more than a frame");
        }

        [Test]
        public void Loadout_AcceptsAnEarlyShot_WithinTheTolerance()
        {
            var table = new WeaponTable { Weapons = new[] { new WeaponDef { Id = 1, Auto = true, FireInterval = 0.1f, MagSize = 30 } } };
            var loadout = new Loadout(table);
            loadout.AddWeapon(1, 30);
            Assert.IsTrue(loadout.TryFire(0, Early));
            Assert.IsTrue(loadout.TryFire(0.097, Early), "a hair early");
            Assert.IsFalse(loadout.TryFire(0.12, Early), "far too early: the next shot is due at 0.2");
            Assert.IsTrue(loadout.TryFire(0.2, Early));
            Assert.AreEqual(27, loadout.MagInSlot(0), "a refused shot costs no ammo");
        }
    }
}
