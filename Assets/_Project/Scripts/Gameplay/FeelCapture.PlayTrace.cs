using System;
using System.Collections;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Gameplay
{
    // `-playtrace` (with `-loadtrace <out.txt> -loadtrace-play`): a scripted bot practice match with no screenshots, so
    // every hitch LoadTrace logs belongs to the game. Each step is a LoadTrace mark (frame number + GC count), so a hitch
    // can be paired with the first thing that happened just before it. Steps: doors, press-to-move latency, run to loot
    // and pick it up, a fight, a car, a swim, a minute with the bots, death, spectating. Development aid like the rest.
    public partial class FeelCapture
    {
        bool _playTrace;

        static bool BootPlayTrace(string[] args)
        {
            if (Array.IndexOf(args, "-playtrace") < 0) return false;
            Application.runInBackground = true;
            var go = new GameObject("FeelCapture");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<FeelCapture>();
            capture._folder = System.IO.Path.GetTempPath();
            capture._playTrace = true;
            PlayerController.InputOverride = capture;
            VehicleInput.InputOverride = capture;
            return true;
        }

        IEnumerator PlayTrace(Combatant me)
        {
            var pc = PlayerController.Local;
            me.ServerDamageFilter = _ => 0f; // alive to the end of the script; death comes last, on purpose
            LoadTrace.Mark("play: spawned in the elevator");
            while (SpawnProtection.InputBlocked) yield return null;
            LoadTrace.Mark("play: input unblocked (doors)");

            // Press -> first movement, five times from standing.
            for (int n = 0; n < 5; n++)
            {
                yield return new WaitForSeconds(0.6f);
                var from = me.transform.position;
                int f0 = Time.frameCount;
                double t0 = Time.realtimeSinceStartupAsDouble;
                _frame.Move = new Vector2(0f, 1f);
                while ((me.transform.position - from).sqrMagnitude < 0.0001f && Time.frameCount - f0 < 300) yield return null;
                LoadTrace.Mark($"play: press {n} -> moved after {Time.frameCount - f0} frames, {(Time.realtimeSinceStartupAsDouble - t0) * 1000.0:F0} ms");
                yield return new WaitForSeconds(0.5f);
                _frame.Move = Vector2.zero;
            }

            // Run to the nearest loot 20-60 m away along the navmesh, then pick it up.
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 120 });
            LoadTrace.Mark("play: rifle given");
            yield return new WaitForSeconds(0.5f);
            NavMeshPath path = null;
            LootItem best = null;
            float bestLen = float.MaxValue;
            NavMesh.SamplePosition(me.transform.position, out var start, 3f, NavMesh.AllAreas);
            foreach (var item in LootItem.All)
            {
                if (item == null || (item.transform.position - me.transform.position).sqrMagnitude > 60f * 60f) continue;
                if (!NavMesh.SamplePosition(item.transform.position, out var target, 2f, NavMesh.AllAreas)) continue;
                var p = new NavMeshPath();
                if (!NavMesh.CalculatePath(start.position, target.position, NavMesh.AllAreas, p) || p.status != NavMeshPathStatus.PathComplete) continue;
                float len = 0f;
                for (int c = 1; c < p.corners.Length; c++) len += Vector3.Distance(p.corners[c - 1], p.corners[c]);
                if (len >= 20f && len < bestLen) { bestLen = len; best = item; path = p; }
            }
            if (best != null)
            {
                LoadTrace.Mark($"play: run {bestLen:F0} m to loot");
                // One change at a time, so a hitch names its cause: turn, walk with the rifle, then sprint.
                var c1 = path.corners[Mathf.Min(1, path.corners.Length - 1)];
                pc.LookAt(new Vector3(c1.x, me.EyePosition.y - 0.2f, c1.z));
                yield return new WaitForSeconds(0.5f);
                LoadTrace.Mark("play: turned");
                _frame.Move = new Vector2(0f, 1f);
                yield return new WaitForSeconds(0.5f);
                LoadTrace.Mark("play: walked with the rifle");
                _frame.Sprint = true;
                yield return new WaitForSeconds(0.5f);
                LoadTrace.Mark("play: sprinted");
                float giveUp = Time.realtimeSinceStartup + 25f;
                for (int corner = 1; corner < path.corners.Length && Time.realtimeSinceStartup < giveUp;)
                {
                    var c = path.corners[corner];
                    var flat = new Vector3(c.x - me.transform.position.x, 0f, c.z - me.transform.position.z);
                    if (flat.magnitude < 0.9f) { corner++; continue; }
                    _frame.Sprint = flat.magnitude > 6f;
                    pc.LookAt(new Vector3(c.x, me.EyePosition.y - 0.2f, c.z));
                    yield return null;
                }
                _frame.Move = Vector2.zero;
                _frame.Sprint = false;
                LoadTrace.Mark($"play: run done, {Vector3.Distance(me.transform.position, best.transform.position):F1} m from the loot");
                pc.LookAt(best.transform.position);
                yield return new WaitForSeconds(0.4f);
                _frame.Interact = true;
                yield return new WaitForSeconds(0.6f);
                LoadTrace.Mark("play: pickup pressed");
            }
            else LoadTrace.Mark("play: no loot in reach");

            // A fight: 28 m from a bot with a clear line, sights up, six bursts.
            Combatant bot = null;
            foreach (var c in Combatant.All)
                if (c != null && !c.IsPlayer && !c.Dead.Value) { bot = c; break; }
            Vector3 spot = default;
            bool found = false;
            for (int a = 0; bot != null && a < 36 && !found; a++)
            {
                float ang = a * 10f * Mathf.Deg2Rad;
                var probe = bot.transform.position + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * 28f;
                if (!NavMesh.SamplePosition(probe, out var hit, 4f, NavMesh.AllAreas)) continue;
                if (Physics.Linecast(hit.position + Vector3.up * 1.6f, bot.transform.position + Vector3.up * 1.3f, out var block) && block.collider.GetComponentInParent<Combatant>() != bot) continue;
                spot = hit.position;
                found = true;
            }
            if (found)
            {
                pc.Teleport(spot + Vector3.up * 0.1f);
                LoadTrace.Mark("play: teleported to the fight (new area)");
                yield return new WaitForSeconds(1.5f);
                _frame.AimHeld = true;
                LoadTrace.Mark("play: aim");
                yield return new WaitForSeconds(0.7f);
                for (int n = 0; n < 6 && !bot.Dead.Value; n++)
                {
                    pc.LookAt(bot.transform.position + Vector3.up * 1.3f);
                    _frame.FirePressed = _frame.FireHeld = true;
                    LoadTrace.Mark($"play: fire burst {n} (bot hp {bot.Hp.Value:F0})");
                    yield return new WaitForSeconds(0.35f);
                    _frame.FireHeld = false;
                    yield return new WaitForSeconds(0.25f);
                }
                _frame.AimHeld = false;
                _frame.Reload = true;
                yield return null;
                _frame.Reload = false;
                LoadTrace.Mark("play: reload");
                yield return new WaitForSeconds(2.5f);
            }
            else LoadTrace.Mark("play: no bot with a clear line");

            // A car: in, throttle, out.
            Vehicle car = null;
            float carDist = float.MaxValue;
            foreach (var v in Vehicle.All)
                if (v != null && !v.IsMotorcycle && (v.transform.position - me.transform.position).sqrMagnitude < carDist) { car = v; carDist = (v.transform.position - me.transform.position).sqrMagnitude; }
            if (car != null)
            {
                pc.Teleport(car.DoorPosition(0) + Vector3.up * 0.05f);
                LoadTrace.Mark("play: teleported to a car (new area)");
                yield return new WaitForSeconds(1f);
                _vehicleFrame.Interact = true;
                float until = Time.realtimeSinceStartup + 3f;
                while (!VehicleSeat.IsAttached(me) && Time.realtimeSinceStartup < until) yield return null;
                LoadTrace.Mark($"play: in the car {VehicleSeat.IsAttached(me)}");
                yield return new WaitForSeconds(car.Rules.EnterSeconds + 0.4f);
                _vehicleFrame.Throttle = 1f;
                LoadTrace.Mark("play: throttle");
                yield return new WaitForSeconds(4f);
                _vehicleFrame.Throttle = 0f;
                LoadTrace.Mark($"play: {car.SpeedKmh:F0} km/h, brake");
                yield return new WaitForSeconds(1.5f);
                _vehicleFrame.Interact = true;
                until = Time.realtimeSinceStartup + 3f;
                while (VehicleSeat.IsAttached(me) && Time.realtimeSinceStartup < until) yield return null;
                LoadTrace.Mark("play: out of the car");
                yield return new WaitForSeconds(1f);
            }

            // A swim: the middle of the first body of water, swimming forward.
            if (WaterVolume.All.Count > 0)
            {
                var w = WaterVolume.All[0];
                var mid = w.Area.center;
                pc.Teleport(new Vector3(mid.x, w.SurfaceY + 0.2f, mid.y));
                LoadTrace.Mark("play: into the water (new area)");
                _frame.Move = new Vector2(0f, 1f);
                float until = Time.realtimeSinceStartup + 3f;
                while (!CombatantSwim.In(me) && Time.realtimeSinceStartup < until) yield return null;
                LoadTrace.Mark($"play: swimming {CombatantSwim.In(me)}");
                yield return new WaitForSeconds(4f);
                _frame.Move = Vector2.zero;
                LoadTrace.Mark("play: swim done");
            }

            LoadTrace.Mark("play: a minute with the bots");
            yield return new WaitForSeconds(60f);
            me.ServerDamageFilter = null;
            me.ServerTakeDamage(new DamageInfo { Amount = 10000f, Cause = DamageCause.Zone, Zone = HitZone.Body });
            LoadTrace.Mark("play: death");
            yield return new WaitForSeconds(10f);
            LoadTrace.Mark("play: quit");
        }
    }
}
