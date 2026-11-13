using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The grenades a combatant carries and the throwing of them (PUBG_GAP 4-3 batch I): fragmentation grenades and
    // smoke grenades, one of them selected at a time (the owner cycles the selection). The server owns the counts
    // and spawns the thrown item; the owner only asks, with the direction it is looking in (ADR-029).
    [RequireComponent(typeof(Combatant))]
    public class ThrowableUser : NetworkBehaviour, ILootReceiver
    {
        // Where the throwing hand is, seen from the eyes along the throw: metres right, up and forward.
        const float HandRight = 0.25f;
        const float HandUp = 0.1f;
        const float HandForward = 0.45f;

        [SerializeField] NetworkObject _thrownPrefab; // the grenade in flight (a spawnable prefab with a ThrownItem)

        public readonly SyncVar<int> Grenades = new SyncVar<int>();
        public readonly SyncVar<int> Smokes = new SyncVar<int>();
        // The ThrowableKind the next throw uses.
        public readonly SyncVar<int> Selected = new SyncVar<int>();

        // The local player's own, for the HUD and the inventory. Null when there is none.
        public static ThrowableUser Local { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Local = null;

        Combatant _combatant;
        CombatantCarry _carry;
        double _nextThrowAt;
        bool _missingPrefabLogged;

        // Server: how many grenades this combatant has thrown, and the last one (null once it is gone).
        public int ServerThrows { get; private set; }
        public ThrownItem LastThrown { get; private set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _carry = GetComponent<CombatantCarry>();
        }

        // ---- Read-only, every side (HUD) ----

        public ThrowableKind SelectedKind => (ThrowableKind)Selected.Value;
        public int CountOf(ThrowableKind kind) => kind == ThrowableKind.Smoke ? Smokes.Value : Grenades.Value;
        public int SelectedCount => CountOf(SelectedKind);
        public string SelectedName => Throwables.NameOf(SelectedKind);

        public override void OnStartClient()
        {
            if (IsOwner && _combatant.IsPlayer) Local = this;
        }

        public override void OnStopClient()
        {
            if (Local == this) Local = null;
        }

        // Scene unloads destroy objects without the network callbacks.
        void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        // ---- The same on every side, so the owner's preview shows what the server will throw ----

        // Where a throw along `direction` leaves the hand. Derived from the eyes the server knows, never from a
        // position a client sent. Falls back to the eyes when the hand would be behind a wall.
        public Vector3 ThrowOrigin(Vector3 direction)
        {
            var eye = _combatant.EyePosition;
            var right = Vector3.Cross(Vector3.up, direction);
            if (right.sqrMagnitude < 1e-6f) right = transform.right; // straight up or down
            right.Normalize();
            var hand = eye + right * HandRight + Vector3.up * HandUp + direction * HandForward;
            var reach = hand - eye;
            float length = reach.magnitude;
            // A little further than the hand, so the grenade does not start touching what is in front of it.
            if (Physics.SphereCast(eye, ThrowableRules.Current.Radius, reach / length, out var hit, length + ThrowableRules.Current.Radius,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
                return eye;
            return hand;
        }

        // The velocity of a throw along a unit direction: a little above it, at the throwing speed.
        public static Vector3 ThrowVelocity(Vector3 direction, ThrowableRules rules)
        {
            var right = Vector3.Cross(Vector3.up, direction);
            if (right.sqrMagnitude > 1e-6f)
                direction = Quaternion.AngleAxis(-rules.LoftDeg, right.normalized) * direction;
            return direction * rules.ThrowSpeed;
        }

        // ---- Server API ----

        // Returns how many were taken (the carry limit and the bag may refuse some or all).
        [Server]
        public int ServerAdd(int amount) => ServerAdd(ThrowableKind.Frag, amount);

        [Server]
        public int ServerAdd(ThrowableKind kind, int amount)
        {
            int max = kind == ThrowableKind.Smoke ? SmokeRules.Current.MaxCarried : ThrowableRules.Current.MaxCarried;
            int taken = Mathf.Max(0, Mathf.Min(amount, max - CountOf(kind)));
            if (taken > 0 && _carry != null) taken = _carry.FitCount(Throwables.LootKindOf(kind), AmmoType.Pistol9, taken);
            if (taken <= 0) return 0;
            if (kind == ThrowableKind.Smoke) Smokes.Value += taken; else Grenades.Value += taken;
            return taken;
        }

        // Grenade and smoke pickups, handed over by Combatant.ServerGive. What does not fit goes back on the ground
        // at the feet; a pickup is refused (and stays where it lies) only when nothing of it fits.
        public bool ServerTryTake(LootEntry loot)
        {
            if (!IsServerInitialized || loot == null) return false;
            ThrowableKind kind;
            if (loot.Kind == ThrowableRules.GrenadeLootKind) kind = ThrowableKind.Frag;
            else if (loot.Kind == ThrowableRules.SmokeLootKind) kind = ThrowableKind.Smoke;
            else return false;
            int wanted = Mathf.Max(1, loot.Amount);
            int taken = ServerAdd(kind, wanted);
            if (taken <= 0) return false;
            if (taken < wanted && MatchDirector.Instance != null)
                MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = loot.Kind, Amount = wanted - taken }, transform.position + Vector3.up * 0.3f);
            return true;
        }

        // Picks what the next throw uses (a kind there is none of can still be selected: the HUD shows 0).
        [Server]
        public void ServerSelect(ThrowableKind kind) => Selected.Value = (int)kind;

        [Server]
        public void ServerCycle() => ServerSelect(Throwables.Next(SelectedKind));

        // The owner's call (the T key): the host is the server, so it does not wait for the next network tick.
        public void RequestCycle()
        {
            if (IsServerInitialized) ServerCycle(); else CmdCycle();
        }

        // A throw the way a player makes it: from the hand, along where they look.
        [Server]
        public bool ServerThrowAimed(Vector3 direction) => ServerThrowAimed(direction, SelectedKind);

        [Server]
        public bool ServerThrowAimed(Vector3 direction, ThrowableKind kind)
        {
            float sqr = direction.sqrMagnitude;
            if (float.IsNaN(sqr) || float.IsInfinity(sqr) || sqr < 1e-6f) return false;
            direction.Normalize();
            return ServerThrow(ThrowOrigin(direction), ThrowVelocity(direction, ThrowableRules.Current), kind);
        }

        // Throws one of the selected kind from a point with a velocity. Refused when dead, out of them, too soon after
        // the last throw, or when the thrown-item prefab is not wired. AI and tests call this directly.
        [Server]
        public bool ServerThrow(Vector3 origin, Vector3 velocity) => ServerThrow(origin, velocity, SelectedKind);

        [Server]
        public bool ServerThrow(Vector3 origin, Vector3 velocity, ThrowableKind kind)
        {
            if (_combatant.Dead.Value || CountOf(kind) <= 0) return false;
            if (!IsFinite(origin) || !IsFinite(velocity)) return false;
            double now = Time.timeAsDouble;
            if (now < _nextThrowAt) return false;
            if (_thrownPrefab == null || _thrownPrefab.GetComponent<ThrownItem>() == null)
            {
                if (!_missingPrefabLogged)
                    Debug.LogWarning($"[ThrowableUser] {name}: no thrown grenade prefab (with a ThrownItem) is assigned; throws are refused");
                _missingPrefabLogged = true;
                return false;
            }

            var rules = ThrowableRules.Current;
            // Never faster than a throw can be, whoever asks.
            velocity = Vector3.ClampMagnitude(velocity, rules.ThrowSpeed);
            _nextThrowAt = now + rules.MinThrowInterval;
            if (kind == ThrowableKind.Smoke) Smokes.Value--; else Grenades.Value--;
            var thrown = Instantiate(_thrownPrefab, origin, Quaternion.identity);
            var item = thrown.GetComponent<ThrownItem>();
            item.Init(_combatant, velocity, rules, kind);
            ServerManager.Spawn(thrown);
            LastThrown = item;
            ServerThrows++;
            return true;
        }

        static bool IsFinite(Vector3 v)
        {
            float sum = v.x + v.y + v.z;
            return !float.IsNaN(sum) && !float.IsInfinity(sum);
        }

        // ---- Owner requests ----

        [ServerRpc]
        public void CmdThrow(Vector3 direction) => ServerThrowAimed(direction);

        [ServerRpc]
        public void CmdCycle() => ServerCycle();

        // Dropping one from the inventory screen: it lands on the ground in front as a pickup.
        [ServerRpc]
        public void CmdDropGrenade()
        {
            if (_combatant.Dead.Value || Grenades.Value <= 0 || MatchDirector.Instance == null) return;
            Grenades.Value--;
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = ThrowableRules.GrenadeLootKind, Amount = 1 },
                transform.position + transform.forward * 1.0f + Vector3.up * 0.3f);
        }

        [ServerRpc]
        public void CmdDropSmoke()
        {
            if (_combatant.Dead.Value || Smokes.Value <= 0 || MatchDirector.Instance == null) return;
            Smokes.Value--;
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = ThrowableRules.SmokeLootKind, Amount = 1 },
                transform.position + transform.forward * 1.0f + Vector3.up * 0.3f);
        }
    }
}
