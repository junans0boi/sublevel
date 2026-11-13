using System.Collections.Generic;
using FishNet.Object;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Bullets (ADR-098, decision #126): the first InstantMeters of a shot are judged at once, the rest of the flight is
    // simulated on the server with the weapon's muzzle speed and gravity. Bots and the dummy use the same path.
    public partial class Combatant
    {
        // A bullet in flight. Position at time t: Origin + Velocity * t + gravity * t^2 / 2.
        internal struct Bullet
        {
            public Combatant Shooter;
            public WeaponDef Weapon;
            public Vector3 Origin, Velocity;
            public float T, TEnd, Speed;
            public float Travelled;   // metres flown before the last rebase (a penetrated piece)
            public float DamageScale; // share of damage left after the pieces gone through
            public int Pieces;
            public Vector3 Muzzle; // where the shot started (for the effects)
        }

        // Where the sights and the bullet cross, metres (zeroing, G10b). Server side.
        public float ZeroMeters { get; set; } = -1f;
        public float EffectiveZero => ZeroMeters > 0f ? ZeroMeters : GameData.Rules.Ballistics.DefaultZeroMeters;

        // Launches one projectile. `zero` is the distance at which the bullet crosses the line of sight.
        void ServerLaunch(Vector3 origin, Vector3 dir, WeaponDef weapon, float zero)
        {
            var r = GameData.Rules.Ballistics;
            float speed = Ballistics.MuzzleSpeed(weapon, r);
            float elevation = Ballistics.ElevationDeg(speed, zero, r.Gravity);
            var right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 1e-6f) right = transform.right;
            var up = Quaternion.AngleAxis(-elevation, right.normalized) * dir;
            var bullet = new Bullet
            {
                Shooter = this,
                Weapon = weapon,
                Origin = origin,
                Velocity = up * speed,
                Speed = speed,
                TEnd = Mathf.Min(weapon.Range / speed, r.MaxFlightSeconds),
                Muzzle = origin,
                DamageScale = 1f,
            };
            float instant = Mathf.Min(r.InstantMeters / speed, bullet.TEnd);
            bool done = ServerAdvance(ref bullet, instant, whole: true);
            if (done) return;
            // Still flying: the muzzle effects now, the impact when it lands.
            RpcShotFx(origin, origin + dir * weapon.Range, -dir, ShotLaunchBit, null, 0);
            BulletWorld.Add(bullet);
        }

        Vector3 BulletAt(in Bullet b, float t) => b.Origin + b.Velocity * t + 0.5f * GameData.Rules.Ballistics.Gravity * t * t * Vector3.down;

        // Moves the bullet to time `to` and judges the segment. True when it ended (hit something or ran out of range).
        // `whole`: the shot's only message (instant hit), otherwise the impact message of a bullet already announced.
        // A thin wall, door or car on the way is gone through with less damage and speed (ADR-114): the bullet is
        // then rebased at the far side and flies on from there.
        internal bool ServerAdvance(ref Bullet b, float to, bool whole)
        {
            float t0 = b.T;
            var a = BulletAt(b, b.T);
            var c = BulletAt(b, to);
            var seg = c - a;
            float length = seg.magnitude;
            var dir = length > 1e-5f ? seg / length : b.Velocity.normalized;
            float travelled = b.Travelled + b.Speed * b.T;
            var weapon = b.Weapon;
            var rules = GameData.Rules.Penetration;
            var end = c;
            var normal = -dir;
            var result = ShotHit.None;
            var zone = HitZone.Body;
            var surface = BulletSurface.Concrete;
            NetworkObject victim = null;
            Collider stoppedBy = null;
            bool stopped = false;
            var from = a;                 // start of the part of the segment not judged yet
            float scale = b.DamageScale, speedKeep = 1f, passed = 0f; // passed: metres along the segment up to the last exit
            int pieces = b.Pieces;
            Vector3 lastExit = a;
            while (true)
            {
                float left = length - Vector3.Distance(a, from);
                // [VEHICLE HOOK] same as Physics.Raycast on foot; from a seat it passes the shooter's own car and its occupants.
                if (left <= 1e-4f || !VehicleSeat.Raycast(this, from, dir, left, out var hit)) break;
                stopped = true;
                end = hit.point;
                normal = hit.normal;
                stoppedBy = hit.collider;
                result = ShotHit.World;
                var target = hit.collider.GetComponentInParent<Combatant>();
                // A body that is already dead stops the shot like any other obstacle.
                if (target != null && target != this && !target._health.IsDead)
                {
                    surface = BulletSurface.Body;
                    // The zone comes from how high on the victim the shot landed, in the victim's current stance.
                    zone = HitZones.At(hit.point.y - target.transform.position.y, target.EyeHeightOffset, GameData.Rules.Fire.Zones);
                    target.ServerTakeDamage(new DamageInfo
                    {
                        Amount = DamageRules.ForZone(weapon, travelled + Vector3.Distance(a, hit.point), zone) * scale,
                        Attacker = this,
                        Zone = zone,
                        WeaponId = weapon.Id,
                        Cause = DamageCause.Shot,
                    });
                    result = target._health.IsDead ? ShotHit.Kill : ShotHit.Combatant;
                    victim = target.NetworkObject;
                    break;
                }
                surface = BulletSurfaces.Of(hit.collider);
                int s = (int)surface;
                if (pieces >= rules.MaxPieces || rules.MaxThickness[s] <= 0f) break;
                float thick = BulletSurfaces.Thickness(hit, dir, rules.MaxThickness[s], out var exit);
                if (!Penetration.TryPass(surface, thick, b.Speed * speedKeep, rules, out float dk, out float sk)) break;
                // Through: a mark on each side (an impact message, the shot itself is announced once), the car takes its hit.
                float dealt = DamageRules.ForZone(weapon, travelled + Vector3.Distance(a, hit.point), HitZone.Body) * scale;
                VehicleDamage.ServerShotAll(from, dir, hit.distance, hit.collider, hit.point, dealt);
                RpcShotFx(b.Muzzle, hit.point, hit.normal, (byte)((int)ShotHit.World | ShotImpactBit), null, (byte)surface);
                RpcShotFx(b.Muzzle, exit, dir, (byte)((int)ShotHit.World | ShotImpactBit), null, (byte)surface);
                scale *= dk;
                speedKeep *= sk;
                pieces++;
                stopped = false;
                stoppedBy = null;
                lastExit = exit;
                passed = Vector3.Distance(a, exit);
                from = exit + dir * 0.01f;
            }
            float used = (end - a).magnitude;
            // Window glass on the way breaks; the shot goes on through it (ADR-065 (4)).
            GlassPanes.ServerBreakAlong(a, dir, used, ObjectId);
            // [VEHICLE HOOK] #90: vehicle glass, tyres and body on the shot's line (after the last piece it went through).
            float lastFrom = Vector3.Distance(a, from);
            VehicleDamage.ServerShotAll(from, dir, Mathf.Max(0f, used - lastFrom), stoppedBy, end, DamageRules.ForZone(weapon, travelled + used, HitZone.Body) * scale);
            b.T = to;
            if (pieces > b.Pieces && !stopped)
            {
                // Rebase at the last exit: the speed it lost, the damage it lost, the range it has left.
                float t = length > 1e-5f ? Mathf.Lerp(t0, to, passed / length) : to;
                var v = b.Velocity + Vector3.down * (GameData.Rules.Ballistics.Gravity * t);
                b.Travelled = travelled + passed;
                b.Origin = lastExit;
                b.Velocity = v * speedKeep;
                b.Speed = b.Velocity.magnitude;
                b.TEnd = Mathf.Min(Mathf.Max(0f, weapon.Range - b.Travelled) / b.Speed, GameData.Rules.Ballistics.MaxFlightSeconds);
                b.T = to - t;
            }
            b.DamageScale = scale;
            b.Pieces = pieces;
            bool done = stopped || b.T >= b.TEnd - 1e-4f;
            if (!done) return false;
            if (whole) RpcShotFx(b.Muzzle, end, normal, (byte)((int)result | ((int)zone << ShotZoneShift)), victim, (byte)surface);
            else if (stopped) RpcShotFx(b.Muzzle, end, normal, (byte)((int)result | ((int)zone << ShotZoneShift) | ShotImpactBit), victim, (byte)surface);
            return true;
        }
    }

    // Ticks the bullets in flight (server only). Created on the first shot that outlives the instant range.
    public class BulletWorld : MonoBehaviour
    {
        static BulletWorld _instance;
        readonly List<Combatant.Bullet> _bullets = new List<Combatant.Bullet>();

        public static int InFlight => _instance != null ? _instance._bullets.Count : 0;

        internal static void Add(Combatant.Bullet bullet)
        {
            if (_instance == null) _instance = new GameObject("BulletWorld").AddComponent<BulletWorld>();
            _instance._bullets.Add(bullet);
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void FixedUpdate()
        {
            for (int i = _bullets.Count - 1; i >= 0; i--)
            {
                var b = _bullets[i];
                if (b.Shooter == null) { _bullets.RemoveAt(i); continue; }
                float to = Mathf.Min(b.T + Time.fixedDeltaTime, b.TEnd);
                bool done = b.Shooter.ServerAdvance(ref b, to, whole: false);
                if (done) _bullets.RemoveAt(i); else _bullets[i] = b;
            }
        }
    }
}
