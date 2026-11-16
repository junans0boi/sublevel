using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // The layer between the pure pollution core and the server/HUD (S6 track): terrain -> grid, snapshot + hash,
    // damage steps, HUD model, chip board, key elevator system.
    public class PollutionWiringTests
    {
        static readonly PollutionRules R = new PollutionRules();

        // 240 x 120 m: region 0 west, region 1 east, a river line at x ~ 120, sea on the north edge row.
        sealed class Terrain : IPollutionTerrain
        {
            public float OriginX => 1000f;
            public float OriginZ => -500f;
            public float WidthMeters => 240f;
            public float DepthMeters => 120f;
            public int RegionCount => 2;
            public byte RegionAt(float x, float z) => z < -500f + 12f ? PollutionGrid.Sea : (byte)(x < 1120f ? 0 : 1);
            public float SlopeDegAt(float x, float z) => 5f;
            public byte RoadAt(float x, float z) => 0;
            public bool RiverAt(float x, float z) => x >= 1114f && x < 1126f;
        }

        static PollutionMatch Match(int seed = 3)
        {
            var g = PollutionGridBuilder.Build(new Terrain());
            return new PollutionMatch(g, PollutionGridBuilder.Adjacency(g, 2), 2, seed, R);
        }

        [Test]
        public void Builder_SamplesTerrain_WithOriginOffset()
        {
            var g = PollutionGridBuilder.Build(new Terrain());
            Assert.AreEqual(20, g.Width);
            Assert.AreEqual(10, g.Height);
            Assert.AreEqual(0, g.Region[g.CellAt(1030f, -400f)]);
            Assert.AreEqual(1, g.Region[g.CellAt(1200f, -400f)]);
            Assert.AreEqual(PollutionGrid.Sea, g.Region[g.CellAt(1100f, -495f)]);
            var river = g.CellAt(1117f, -400f);
            Assert.IsTrue(g.River[river]);
            Assert.AreEqual(0f, g.SlopeDeg[river]);
            Assert.AreEqual(5f, g.SlopeDeg[g.CellAt(1030f, -400f)]);
        }

        [Test]
        public void Adjacency_RegionsTouchAcrossTheRiver_AndAreSorted()
        {
            var g = PollutionGridBuilder.Build(new Terrain());
            var adj = PollutionGridBuilder.Adjacency(g, 2);
            Assert.AreEqual(new[] { 1 }, adj[0]);
            Assert.AreEqual(new[] { 0 }, adj[1]);
        }

        [Test]
        public void Snapshot_ClientRebuildsTheSameFront_AndNoticesADifferentOne()
        {
            var server = Match(7);
            var g = PollutionGridBuilder.Build(new Terrain());
            var client = PollutionMatch.FromSnapshot(g, server.Snapshot, R, out bool ok);
            Assert.IsTrue(ok);
            Assert.AreEqual(server.GridHash, client.GridHash);
            var slow = new PollutionRules { FlatSpeed = 40f };
            PollutionMatch.FromSnapshot(g, server.Snapshot, slow, out bool differs);
            Assert.IsFalse(differs, "another rule set gives another front, so the hash differs");
            var s = server.Snapshot; s.Order = new[] { s.Order[1], s.Order[0] };
            PollutionMatch.FromSnapshot(g, s, R, out bool wrongOrder);
            Assert.IsFalse(wrongOrder);
        }

        [Test]
        public void Match_States_FollowTheTable()
        {
            var m = Match();
            int first = m.Timeline.Order[0];
            Assert.AreEqual(PollutionState.Safe, m.States(100)[first]);
            Assert.AreEqual(PollutionState.Warning, m.States(4 * 60 - 105 + 1)[first]);
            Assert.AreEqual(PollutionState.Contaminated, m.States(4 * 60)[first]);
        }

        [Test]
        public void Exposure_DamageInHalfSecondSteps_NothingOutsideTheGas()
        {
            var m = Match();
            int first = m.Timeline.Order[0];
            float x = first == 0 ? 1006f : 1234f, z = -400f;
            var e = new PollutionExposure();
            Assert.AreEqual(0f, e.Step(m, x, z, 100), "front not there yet");
            double t = 4 * 60 + 1;
            Assert.AreEqual(0f, e.Step(m, x, z, t), "entering starts the step clock");
            Assert.AreEqual(1f, e.Step(m, x, z, t + 0.5), 1e-4, "2 hp/s x 0.5 s");
            Assert.AreEqual(2f, e.Step(m, x, z, t + 1.6), 1e-4, "two steps owed");
            Assert.AreEqual(0f, e.Step(m, x, z, t + 1.7));
        }

        [Test]
        public void Exposure_FilterHalves_AntidoteTenth_AndNeitherHelpsInTheFinalRegion()
        {
            var m = Match();
            int first = m.Timeline.Order[0], fin = m.FinalRegion;
            float x = first == 0 ? 1006f : 1234f, z = -400f;
            double t = 4 * 60 + 1;
            var f = new PollutionExposure(); f.UseFilter(t, R);
            Assert.AreEqual(1f, m.DamagePerSecondAt(x, z, t, f.Cover), 1e-4);
            var a = new PollutionExposure(); a.UseFilter(t, R); a.UseAntidote(t, R);
            Assert.AreEqual(0.2f, m.DamagePerSecondAt(x, z, t, a.Cover), 1e-4, "stronger one wins");
            Assert.AreEqual(2f, m.DamagePerSecondAt(x, z, t + 61, f.Cover), 1e-4, "filter ran out");
            // final region: the second confirmation, all contaminated, factor 1 whatever the cover
            double late = 7.5 * 60 + 60;
            float fx = fin == 0 ? 1006f : 1234f;
            Assert.AreEqual(3.2f, m.DamagePerSecondAt(fx, z, late, a.Cover), 1e-4);
        }

        [Test]
        public void Hud_BannerCountsDown_VignetteAndCoverShow()
        {
            var m = Match();
            int first = m.Timeline.Order[0];
            float x = first == 0 ? 1006f : 1234f, z = -400f;
            var hud = PollutionHud.Build(m, 4 * 60 - 105 + 15, x, z, null);
            Assert.IsTrue(hud.BannerVisible);
            Assert.AreEqual(first, hud.BannerRegion);
            Assert.AreEqual(90f, hud.BannerSeconds, 1e-3);
            Assert.AreEqual("오염 경보 · 북령 1:30 후 확정", PollutionHud.BannerText(hud, "북령"));
            Assert.IsFalse(hud.InGas);
            var e = new PollutionExposure(); e.UseAntidote(250, R);
            hud = PollutionHud.Build(m, 255, x, z, e);
            Assert.IsTrue(hud.InGas);
            Assert.IsFalse(hud.BannerVisible == false && m.Timeline.StateOf(1 - first, 255) == PollutionState.Warning);
            Assert.AreEqual(0.2f, hud.Dps, 1e-4);
            Assert.Greater(hud.Vignette01, 0f);
            Assert.IsTrue(hud.CoverIsAntidote);
            Assert.AreEqual(20f, hud.CoverSeconds, 1e-3);
        }

        [Test]
        public void ChipBoard_SquadDeathDropsAChip_ThroughDepositAndRevive()
        {
            var b = new ChipBoard();
            Assert.IsNull(b.OnDeath(1, 5, TeamMode.Solo, 0, 10));
            Assert.IsNull(b.OnDeath(1, 5, TeamMode.Squad, 0, 10), "last of the team");
            Assert.IsNotNull(b.OnDeath(1, 5, TeamMode.Squad, 2, 100));
            Assert.AreEqual(ChipResult.Ok, b.PickUp(1, 110, 2, 5, R));
            b.OnCarrierDown(2);
            Assert.AreEqual(ChipState.Dropped, b.Of(1).State);
            Assert.AreEqual(ChipResult.Ok, b.PickUp(1, 120, 3, 5, R));
            var calm = new[] { PollutionState.Safe, PollutionState.Safe, PollutionState.Safe };
            var done = new List<int>();
            Assert.AreEqual(ChipResult.Ok, b.DepositAll(3, 130, 1, calm, R, done));
            Assert.AreEqual(new[] { 1 }, done.ToArray());
            var st = b.StatusOf(5, 130, R);
            Assert.AreEqual(1, st.Stored);
            var owners = new List<int>(); b.Revivable(5, owners);
            Assert.AreEqual(new[] { 1 }, owners.ToArray());
            Assert.AreEqual(ChipResult.BadDestination, b.Revive(5, 2, new[] { PollutionState.Safe, PollutionState.Safe, PollutionState.Contaminated }, out _));
            Assert.AreEqual(ChipResult.Ok, b.Revive(5, 2, calm, out int who));
            Assert.AreEqual(1, who);
            Assert.AreEqual(ChipResult.WrongState, b.Revive(5, 2, calm, out _), "used once");
        }

        [Test]
        public void ChipBoard_ExpiresLooseChips_AndBoxInAContaminatedRegionLosesItsChips()
        {
            var b = new ChipBoard();
            b.OnDeath(1, 5, TeamMode.Squad, 2, 0);
            b.OnDeath(2, 5, TeamMode.Squad, 1, 0);
            var calm = new[] { PollutionState.Safe, PollutionState.Safe };
            b.PickUp(2, 10, 3, 5, R); b.DepositAll(3, 20, 0, calm, R);
            Assert.AreEqual(90f - 50f, b.StatusOf(5, 50, R).SoonestLeft, 1e-3);
            b.Tick(95, new[] { PollutionState.Contaminated, PollutionState.Safe }, R);
            Assert.AreEqual(ChipState.Expired, b.Of(1).State);
            Assert.AreEqual(ChipState.Destroyed, b.Of(2).State);
        }

        [Test]
        public void KeySystem_RideEnds_ArrivalRemapped_CooldownIsPerTeam_VoidRefunds()
        {
            var sys = new KeyElevatorSystem(new[] { 0, 1, 2 }, 8f, R);
            var states = new[] { PollutionState.Safe, PollutionState.Safe, PollutionState.Safe };
            var events = new List<KeyStationEvent>();
            Assert.AreEqual(KeyResult.Ok, sys.Insert(0, 0, 7, 70, true, states));
            Assert.AreEqual(KeyResult.Ok, sys.Board(0, 7));
            Assert.AreEqual(KeyResult.Ok, sys.Board(0, 8));
            Assert.AreEqual(KeyResult.Ok, sys.CloseDoors(0, 5, 7));
            Assert.AreEqual(KeyResult.Ok, sys.Select(0, 6, 7, 1, states));
            // the destination gets confirmed during the ride
            states[1] = PollutionState.Contaminated;
            sys.Tick(14.5, states, events);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(KeyEvent.RideEnded, events[0].Event);
            Assert.AreEqual(2, events[0].Arrival, "first non-contaminated region other than the start");
            Assert.AreEqual(new[] { 7, 8 }, events[0].Riders);
            // same team is on cooldown, another team is not
            Assert.AreEqual(KeyResult.TeamCooldown, sys.Insert(2, 20, 7, 70, true, states));
            Assert.AreEqual(KeyResult.Ok, sys.Insert(2, 20, 9, 90, true, states));
            // nobody picks within 15 s of the doors: void, key back, no cooldown for that team
            sys.Board(2, 9); sys.CloseDoors(2, 21, 9);
            sys.Tick(40, states, events);
            Assert.AreEqual(KeyEvent.Voided, events[0].Event);
            Assert.IsTrue(events[0].Refund);
            Assert.AreEqual(9, events[0].Holder);
            Assert.IsTrue(sys.Cooldowns.CanUse(90, 40));
        }

        [Test]
        public void KeySystem_ContaminatedStationRechargesAfterUse()
        {
            var sys = new KeyElevatorSystem(new[] { 0, 1 }, 8f, R);
            var states = new[] { PollutionState.Contaminated, PollutionState.Safe };
            var events = new List<KeyStationEvent>();
            sys.Insert(0, 0, 1, 10, true, states); sys.Board(0, 1); sys.CloseDoors(0, 1, 1);
            sys.Select(0, 2, 1, 1, states);
            sys.Tick(11, states, events);
            Assert.AreEqual(KeyPhase.Recharging, sys.Station(0).Phase);
            Assert.AreEqual(KeyResult.Busy, sys.Insert(0, 50, 2, 20, true, states));
            sys.Tick(11 + 91, states, events);
            Assert.AreEqual(KeyPhase.Idle, sys.Station(0).Phase);
        }
    }
}
