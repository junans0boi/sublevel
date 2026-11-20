using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Vehicle damage (#90, ADR-067, ADR-074). The server keeps the vehicle's health, the stage (smoke -> fire -> wreck),
    // the flat tyres and the glass panes, and replicates them; every side writes the result into VehicleState (health
    // share for the HUD, Disabled once burning, wheel grip / drop / steering pull / top speed for the core).
    //
    // In: shots (Combatant.ServerFire), grenades (ThrownItem.Explode), collisions (VehicleState.Impact on the
    // simulating side, sent to the server). At zero health the fire burns BurnSeconds, then the vehicle explodes
    // (occupants and anyone near take blast damage, other vehicles too) and stays a wreck.
    //
    // Sits on the vehicle prefab next to Vehicle (VehicleBuilder). Visuals: Game.Gameplay.VehicleDamageVisual.
    [RequireComponent(typeof(Vehicle))]
    public class VehicleDamage : NetworkBehaviour
    {
        public readonly SyncVar<float> Health = new SyncVar<float>(1f);   // 0..1 share of MaxHealth
        public readonly SyncVar<byte> Stage = new SyncVar<byte>(0);       // VehicleDamageStage
        public readonly SyncVar<int> FlatTyres = new SyncVar<int>(0);     // bit i = wheel i flat
        public readonly SyncVar<int> Panes = new SyncVar<int>(0);         // 2 bits per pane: 0 whole, 1 cracked, 2 shattered
        public readonly SyncVar<int> Parts = new SyncVar<int>(0);         // 2 bits per VehiclePart: PartState

        public static readonly List<VehicleDamage> All = new List<VehicleDamage>();

        // Every side: a bullet hole where a shot struck the body (local point and the normal back toward the shooter),
        // with its dimple (radius, depth in metres); and the explosion.
        public event Action<Vector3, Vector3, float, float> Holed;
        public event Action Exploded;
        // Every side: a glass pane hit along a ray (local origin and direction), for the crack's place.
        public event Action<int, Vector3, Vector3> PaneHit;
        // Every side: a dent at a local point, pushed along a local direction (radius, depth in metres).
        public event Action<Vector3, Vector3, float, float> Dented;

        Vehicle _vehicle;
        VehicleState _state;
        float _health;          // server: exact health points
        float[] _tyreHealth;    // server
        int[] _paneHits;        // server
        float _burning;         // server: seconds on fire
        float[] _grip, _drop;
        float[] _partHealth;    // server
        readonly Transform[] _parts = new Transform[VehicleDamageMath.PartCount];
        // Server: the last holes and dents, sent to a client that joins later (OnSpawnServer).
        Vector3[] _holeAt, _holeNormal, _dentAt, _dentDir;
        Vector2[] _holeSize, _dentSize;
        int _holesPlaced, _dentsPlaced;

        public Vehicle Vehicle => _vehicle;
        public VehicleDamageStage CurrentStage => (VehicleDamageStage)Stage.Value;
        VehicleDamageRules Rules => VehicleDamageRules.For(_vehicle.Kind);

        void Awake()
        {
            _vehicle = GetComponent<Vehicle>();
            _state = GetComponent<VehicleState>();
            if (_state == null) _state = gameObject.AddComponent<VehicleState>();
            // Detachable parts: Model/Part_<VehiclePart> (VehicleBuilder), only on models that have them as separate meshes.
            for (int i = 0; i < _parts.Length; i++) _parts[i] = transform.Find("Model/Part_" + (VehiclePart)i);
        }

        // A detachable part's transform (null: this model has none, or it fell and was let go by the visual).
        public Transform Part(int part) => part >= 0 && part < _parts.Length ? _parts[part] : null;
        public void ReleasePart(int part) { if (part >= 0 && part < _parts.Length) _parts[part] = null; }
        public PartState PartStateOf(int part) => VehicleDamageMath.GetPart(Parts.Value, part);

        void OnEnable()
        {
            All.Add(this);
            _state.Impact += OnImpact;
        }

        void OnDisable()
        {
            All.Remove(this);
            _state.Impact -= OnImpact;
        }

        public override void OnStartServer()
        {
            _health = Rules.MaxHealth;
            int wheels = _vehicle.WheelCount;
            _tyreHealth = new float[wheels];
            for (int i = 0; i < wheels; i++) _tyreHealth[i] = Rules.TyreHealth;
            _paneHits = new int[VehicleDamageMath.PaneCount];
            _partHealth = new float[VehicleDamageMath.PartCount];
            for (int i = 0; i < _partHealth.Length; i++) _partHealth[i] = Rules.PartHealth;
            _holeAt = new Vector3[Rules.MaxDecals]; _holeNormal = new Vector3[Rules.MaxDecals]; _holeSize = new Vector2[Rules.MaxDecals];
            _dentAt = new Vector3[Rules.DentHistory]; _dentDir = new Vector3[Rules.DentHistory]; _dentSize = new Vector2[Rules.DentHistory];
        }

        // A client joining after the shooting started gets the holes and dents so far (oldest first); glass, tyres,
        // parts and the stage are SyncVars and arrive by themselves.
        public override void OnSpawnServer(NetworkConnection connection)
        {
            if (connection == null || connection.IsLocalClient || (_holesPlaced == 0 && _dentsPlaced == 0)) return;
            var r = Rules;
            int holes = VehicleDamageMath.HistoryCount(_holesPlaced, r.MaxDecals), dents = VehicleDamageMath.HistoryCount(_dentsPlaced, r.DentHistory);
            var ha = new Vector3[holes]; var hn = new Vector3[holes]; var hs = new Vector2[holes];
            for (int k = 0; k < holes; k++)
            {
                int i = VehicleDamageMath.HistorySlot(_holesPlaced, r.MaxDecals, k);
                ha[k] = _holeAt[i]; hn[k] = _holeNormal[i]; hs[k] = _holeSize[i];
            }
            var da = new Vector3[dents]; var dd = new Vector3[dents]; var ds = new Vector2[dents];
            for (int k = 0; k < dents; k++)
            {
                int i = VehicleDamageMath.HistorySlot(_dentsPlaced, r.DentHistory, k);
                da[k] = _dentAt[i]; dd[k] = _dentDir[i]; ds[k] = _dentSize[i];
            }
            TargetHistory(connection, da, dd, ds, ha, hn, hs);
        }

        [TargetRpc]
        void TargetHistory(NetworkConnection connection, Vector3[] dentAt, Vector3[] dentDir, Vector2[] dentSize, Vector3[] holeAt, Vector3[] holeNormal, Vector2[] holeSize)
        {
            for (int i = 0; i < dentAt.Length; i++) Dented?.Invoke(dentAt[i], dentDir[i], dentSize[i].x, dentSize[i].y);
            for (int i = 0; i < holeAt.Length; i++) Holed?.Invoke(holeAt[i], holeNormal[i], holeSize[i].x, holeSize[i].y);
        }

        void Update()
        {
            if (IsServerInitialized) ServerTick(Time.deltaTime);
            Apply();
        }

        // Every side: the replicated condition into the core's extension point.
        void Apply()
        {
            var r = Rules;
            var stage = CurrentStage;
            int wheels = _vehicle.WheelCount;
            if (_grip == null || _grip.Length != wheels) { _grip = new float[wheels]; _drop = new float[wheels]; }
            int flat = FlatTyres.Value;
            VehicleDamageMath.WheelEffects(flat, wheels, r, _grip, _drop);
            _state.WheelGrip = _grip;
            _state.WheelDrop = _drop;
            _state.SteerPullDeg = SteerPull(flat, r);
            _state.TopSpeedFactor = VehicleDamageMath.TopSpeedFactor(stage, VehicleDamageMath.CountBits(flat), r);
            _state.Health01 = Health.Value;
            _state.Disabled = VehicleDamageMath.Disables(stage);
        }

        // A flat front tyre pulls the steering toward its side, a rear one a little (by where the wheel is, not its index).
        float SteerPull(int flat, VehicleDamageRules r)
        {
            if (_vehicle.IsMotorcycle || flat == 0) return 0f;
            float pull = 0f;
            for (int i = 0; i < _vehicle.WheelCount; i++)
            {
                if ((flat & (1 << i)) == 0) continue;
                var pivot = _vehicle.WheelPivot(i);
                if (pivot == null) continue;
                float side = transform.InverseTransformPoint(pivot.position).x < 0f ? -1f : 1f;
                pull += side * (_vehicle.IsFrontWheel(i) ? r.FlatFrontPullDeg : r.FlatRearPullDeg);
            }
            return pull;
        }

        // ---- Server ----

        void ServerTick(float dt)
        {
            var stage = CurrentStage;
            if (stage == VehicleDamageStage.Wrecked) return;
            var next = VehicleDamageMath.Advance(stage, VehicleDamageMath.StageFor(_health, Rules.MaxHealth, Rules));
            if (next != stage) Stage.Value = (byte)next;
            if (next != VehicleDamageStage.Burning) return;
            _burning += dt;
            if (VehicleDamageMath.BurnedOut(_burning, Rules)) ServerExplode();
        }

        void ServerHurt(float amount)
        {
            if (amount <= 0f || _health <= 0f) return;
            _health = Mathf.Max(0f, _health - amount);
            Health.Value = _health / Rules.MaxHealth;
        }

        // A shot along `dir` from `origin` (`length` = how far it went). `hit` = what stopped it (null if nothing):
        // glass panes on the way crack / shatter; on this vehicle's body it takes health and leaves a hole, on a tyre
        // it wears the tyre down to flat.
        [Server]
        public void ServerShot(Vector3 origin, Vector3 dir, float length, Collider hit, Vector3 point, float weaponDamage)
        {
            if (CurrentStage == VehicleDamageStage.Wrecked || _vehicle.IsRaceCar) return;
            var r = Rules;
            if (!_vehicle.IsMotorcycle && TryGlassBand(out var box, out float belt, out float roof))
            {
                var o = transform.InverseTransformPoint(origin);
                var d = transform.InverseTransformDirection(dir);
                int pane = VehicleDamageMath.PaneHit(o.x, o.y, o.z, d.x, d.y, d.z, box.min.x, box.max.x, box.min.z, box.max.z, belt, roof);
                // Only if the shot got as far as this vehicle.
                if (pane >= 0 && DistanceToBody(origin) <= length + 0.05f)
                {
                    ServerHitPane(pane, 1);
                    RpcPane((byte)pane, o, d);
                }
            }
            // Tyres have no collider (ray cast wheels): the shot's line against each wheel.
            int tyre = TyreAlong(origin, dir, length);
            if (tyre >= 0)
            {
                if (VehicleDamageMath.HitTyre(ref _tyreHealth[tyre], weaponDamage)) FlatTyres.Value |= 1 << tyre;
                return;
            }
            if (hit == null || !BelongsToMe(hit)) return;
            var local = transform.InverseTransformPoint(point);
            var b = LocalBounds();
            float y01 = b.size.y > 0f ? (local.y - b.min.y) / b.size.y : 0.5f;
            float z01 = b.size.z > 0f ? (local.z - b.min.z) / b.size.z : 0.5f;
            float amount = VehicleDamageMath.BulletDamage(weaponDamage, VehicleDamageMath.ZoneAt(y01, z01, r), r);
            ServerHurt(amount);
            int part = PartNear(local, r.PartReach);
            if (part >= 0) ServerHitPart(part, amount);
            VehicleDamageMath.BulletDent(weaponDamage, r, out float radius, out float depth);
            var normal = transform.InverseTransformDirection(-dir);
            if (_holeAt != null)
            {
                int slot = VehicleDamageMath.DecalSlot(_holesPlaced++, r.MaxDecals);
                _holeAt[slot] = local; _holeNormal[slot] = normal; _holeSize[slot] = new Vector2(radius, depth);
            }
            RpcHole(local, normal, radius, depth);
        }

        // ---- Detachable parts ----

        // A part's bounds in the vehicle's local space (it hangs off the Model, which leans on a motorcycle).
        bool PartBounds(int part, out Bounds bounds)
        {
            bounds = default;
            var t = _parts[part];
            if (t == null || PartStateOf(part) == PartState.Fallen) return false;
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            var mb = mf.sharedMesh.bounds;
            bool first = true;
            for (int c = 0; c < 8; c++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                var p = transform.InverseTransformPoint(t.TransformPoint(corner));
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
            }
            return true;
        }

        // The part nearest a local point, within `reach` of its bounds, or -1.
        int PartNear(Vector3 local, float reach)
        {
            int best = -1;
            float bestSqr = reach * reach;
            for (int i = 0; i < _parts.Length; i++)
            {
                if (!PartBounds(i, out var b)) continue;
                float d = b.SqrDistance(local);
                if (d <= bestSqr) { bestSqr = d; best = i; }
            }
            return best;
        }

        void ServerHitPart(int part, float amount)
        {
            if (_partHealth == null || _parts[part] == null) return;
            var state = VehicleDamageMath.HitPart(ref _partHealth[part], amount, Rules);
            int packed = VehicleDamageMath.SetPart(Parts.Value, part, state);
            if (packed != Parts.Value) Parts.Value = packed;
        }

        // A blast at `centre` worth `damageAtDistance(metres)` to a combatant; `factor` scales it for the vehicle.
        // Close enough to the body it wrecks the vehicle at once; it shatters the glass within GlassBlastRadius.
        [Server]
        public void ServerBlast(Vector3 centre, Func<float, float> damageAtDistance, float factor)
        {
            if (CurrentStage == VehicleDamageStage.Wrecked || _vehicle.IsRaceCar) return;
            var r = Rules;
            float distance = DistanceToBody(centre);
            if (distance <= r.InstantWreckRadius) ServerHurt(_health);
            else ServerHurt(damageAtDistance(distance) * factor);
            // The side facing the blast caves in, less with distance.
            float near01 = Mathf.Clamp01(1f - distance / Mathf.Max(0.1f, r.GlassBlastRadius));
            if (near01 > 0f)
            {
                var point = ClosestOnBody(centre);
                var push = (point - centre).sqrMagnitude > 1e-6f ? (point - centre).normalized : (transform.position - centre).normalized;
                ServerDent(transform.InverseTransformPoint(point), transform.InverseTransformDirection(push), r.DentBlastRadius, r.DentBlastDepth * r.DeformScale * near01);
            }
            if (distance <= r.GlassBlastRadius)
                for (int p = 0; p < VehicleDamageMath.PaneCount; p++) ServerHitPane(p, r.GlassHitsToShatter);
            // Each part by its own distance: a blast at the nose takes the bumper and the hood, not the trunk lid.
            var localCentre = transform.InverseTransformPoint(centre);
            for (int i = 0; i < _parts.Length; i++)
            {
                if (!PartBounds(i, out var pb)) continue;
                float d = Mathf.Sqrt(pb.SqrDistance(localCentre));
                ServerHitPart(i, d <= r.InstantWreckRadius ? r.PartHealth : damageAtDistance(d) * factor * r.PartBlastFactor);
            }
        }

        // Every vehicle near a blast (grenades: ThrownItem.Explode).
        [Server]
        public static void ServerBlastAll(Vector3 centre, Func<float, float> damageAtDistance, float factor, VehicleDamage except = null)
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var v = All[i];
                if (v != null && v != except && v.IsServerInitialized) v.ServerBlast(centre, damageAtDistance, factor);
            }
        }

        void ServerHitPane(int pane, int hits)
        {
            if (_paneHits == null || pane < 0 || pane >= _paneHits.Length) return;
            _paneHits[pane] += hits;
            int state = VehicleDamageMath.GlassState(_paneHits[pane], Rules);
            int packed = VehicleDamageMath.SetPane(Panes.Value, pane, state);
            if (packed != Panes.Value) Panes.Value = packed;
        }

        void ServerExplode()
        {
            var r = Rules;
            var centre = LocalBounds().center;
            centre = transform.TransformPoint(centre);
            Stage.Value = (byte)VehicleDamageStage.Wrecked;
            ServerHurt(_health);
            // Every part still on is blown off (models without parts: nothing to show).
            int parts = Parts.Value;
            for (int i = 0; i < _parts.Length; i++) if (_parts[i] != null) parts = VehicleDamageMath.SetPart(parts, i, PartState.Fallen);
            Parts.Value = parts;
            // Straight line from full damage at the inner radius to nothing at the outer one (no medium ring, unlike the grenade, ADR-101).
            var blast = new ThrowableRules { BlastDamage = r.ExplosionDamage, BlastInnerRadius = r.ExplosionInnerRadius, BlastMidRadius = r.ExplosionInnerRadius, BlastMidDamage = r.ExplosionDamage, BlastOuterRadius = r.ExplosionOuterRadius };
            var victims = new List<Combatant>(Combatant.All);
            foreach (var c in victims)
            {
                if (c == null || c.Dead.Value) continue;
                bool inside = VehicleSeat.VehicleOf(c) == _vehicle;
                float amount = inside ? r.OccupantBlastDamage : Throwables.BlastDamage(Vector3.Distance(centre, c.transform.position + Vector3.up), blast);
                if (amount <= 0f) continue;
                c.ServerTakeDamage(new DamageInfo { Amount = amount, Zone = HitZone.Body, Cause = DamageCause.Blast });
            }
            ServerBlastAll(centre, d => Throwables.BlastDamage(d, blast), r.ExplosionToVehicleFactor, this);
            RpcExploded();
        }

        // Collisions: the simulating side (the driver's client, or the server) reports them.
        void OnImpact(float speed, Collider other)
        {
            // Where it struck: the other's surface nearest the vehicle, pushed toward the vehicle's middle.
            var centre = transform.TransformPoint(LocalBounds().center);
            var point = other != null ? ClosestOnBody(other.ClosestPoint(centre)) : centre;
            var local = transform.InverseTransformPoint(point);
            if (IsServerInitialized) ServerImpact(speed, local);
            else if (IsOwner) CmdImpact(speed, local);
        }

        [ServerRpc]
        void CmdImpact(float speed, Vector3 local) => ServerImpact(speed, local);

        void ServerImpact(float speed, Vector3 local)
        {
            if (CurrentStage == VehicleDamageStage.Wrecked || _vehicle.IsRaceCar) return;   // race cars take no damage (ADR-508)
            var r = Rules;
            // Capped: a client cannot claim more than MaxReportedImpact, nor a point far off the body.
            speed = Mathf.Clamp(speed, 0f, r.MaxReportedImpact);
            float amount = VehicleDamageMath.CollisionDamage(speed, r);
            ServerHurt(amount);
            var b = LocalBounds();
            local = b.ClosestPoint(local);
            int part = PartNear(local, r.PartReach);
            if (part >= 0) ServerHitPart(part, amount * r.PartCollisionFactor);
            if (!VehicleDamageMath.ImpactDent(speed, r, out float radius, out float depth)) return;
            var inward = (b.center - local).sqrMagnitude > 1e-6f ? (b.center - local).normalized : Vector3.forward;
            ServerDent(local, inward, radius, depth);
        }

        // A dent everyone sees, kept for late joiners.
        void ServerDent(Vector3 local, Vector3 dir, float radius, float depth)
        {
            if (_dentAt != null)
            {
                var r = Rules;
                int slot = VehicleDamageMath.DecalSlot(_dentsPlaced++, r.DentHistory);
                _dentAt[slot] = local; _dentDir[slot] = dir; _dentSize[slot] = new Vector2(radius, depth);
            }
            RpcDent(local, dir, radius, depth);
        }

        // Tests and captures.
        [Server]
        public void ServerSetHealth01(float share)
        {
            _health = Mathf.Clamp01(share) * Rules.MaxHealth;
            Health.Value = Mathf.Clamp01(share);
        }

        [ObserversRpc(RunLocally = true)]
        void RpcHole(Vector3 local, Vector3 localNormal, float radius, float depth) => Holed?.Invoke(local, localNormal, radius, depth);

        [ObserversRpc(RunLocally = true)]
        void RpcExploded() => Exploded?.Invoke();

        [ObserversRpc(RunLocally = true)]
        void RpcPane(byte pane, Vector3 localOrigin, Vector3 localDir) => PaneHit?.Invoke(pane, localOrigin, localDir);

        [ObserversRpc(RunLocally = true)]
        void RpcDent(Vector3 local, Vector3 localDir, float radius, float depth) => Dented?.Invoke(local, localDir, radius, depth);

        // The point of the body's colliders nearest `point`.
        Vector3 ClosestOnBody(Vector3 point)
        {
            float best = float.PositiveInfinity;
            var at = point;
            var colliders = _vehicle.Colliders;
            for (int i = 0; i < colliders.Count; i++)
            {
                var c = colliders[i];
                if (c == null || !c.enabled) continue;
                var p = c.ClosestPoint(point);
                float d = (p - point).sqrMagnitude;
                if (d < best) { best = d; at = p; }
            }
            return at;
        }

        // ---- Geometry ----

        bool BelongsToMe(Collider c)
        {
            var colliders = _vehicle.Colliders;
            for (int i = 0; i < colliders.Count; i++) if (colliders[i] == c) return true;
            return false;
        }

        // The first wheel the shot's line enters (a sphere of the wheel's radius about its centre) before `length`, or -1.
        int TyreAlong(Vector3 origin, Vector3 dir, float length)
        {
            float radius = _vehicle.Rules.WheelRadius;
            int best = -1;
            float bestT = length + 0.05f;
            for (int i = 0; i < _vehicle.WheelCount; i++)
            {
                var pivot = _vehicle.WheelPivot(i);
                if (pivot == null) continue;
                var c = pivot.position;
                float t = VehicleDamageMath.RaySphere(origin.x, origin.y, origin.z, dir.x, dir.y, dir.z, c.x, c.y, c.z, radius);
                if (t >= 0f && t < bestT) { bestT = t; best = i; }
            }
            return best;
        }

        // Every vehicle the shot passes near (Combatant.ServerFire).
        [Server]
        public static void ServerShotAll(Vector3 origin, Vector3 dir, float length, Collider hit, Vector3 point, float weaponDamage)
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var v = All[i];
                if (v == null || !v.IsServerInitialized) continue;
                // Cheap reject: the vehicle's centre further than 5 m from the shot's segment.
                var c = v.transform.position;
                float along = Mathf.Clamp(Vector3.Dot(c - origin, dir), 0f, length);
                if ((origin + dir * along - c).sqrMagnitude > 25f) continue;
                v.ServerShot(origin, dir, length, hit, point, weaponDamage);
            }
        }

        // The body's colliders' bounds in local space.
        public Bounds LocalBounds()
        {
            var colliders = _vehicle.Colliders;
            bool any = false;
            var b = new Bounds();
            for (int i = 0; i < colliders.Count; i++)
            {
                if (!(colliders[i] is BoxCollider box)) continue;
                var centre = transform.InverseTransformPoint(box.transform.TransformPoint(box.center));
                var size = Vector3.Scale(box.size, box.transform.lossyScale);
                var cb = new Bounds(centre, size);
                if (!any) { b = cb; any = true; } else b.Encapsulate(cb);
            }
            return any ? b : new Bounds(Vector3.up, new Vector3(1.8f, 1.5f, 4f));
        }

        // The cabin's glass band: from the top of the chassis box to the bottom of the roof box (cars only).
        public bool TryGlassBand(out Bounds box, out float belt, out float roof)
        {
            box = LocalBounds();
            belt = roof = 0f;
            BoxCollider chassis = null, top = null;
            foreach (var c in _vehicle.Colliders)
                if (c is BoxCollider bc) { if (bc.name == "Chassis") chassis = bc; else if (bc.name == "Roof") top = bc; }
            if (chassis == null || top == null) return false;
            belt = transform.InverseTransformPoint(chassis.transform.TransformPoint(chassis.center)).y + chassis.size.y * 0.5f;
            roof = transform.InverseTransformPoint(top.transform.TransformPoint(top.center)).y - top.size.y * 0.5f;
            return roof > belt;
        }

        public float DistanceToBody(Vector3 point)
        {
            float best = float.PositiveInfinity;
            var colliders = _vehicle.Colliders;
            for (int i = 0; i < colliders.Count; i++)
            {
                var c = colliders[i];
                if (c == null || !c.enabled) continue;
                best = Mathf.Min(best, (c.ClosestPoint(point) - point).magnitude);
            }
            return float.IsPositiveInfinity(best) ? Vector3.Distance(point, transform.position) : best;
        }
    }
}
