using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Timed healing (PUBG_GAP 4-3 batch H): bandages, first-aid kits and the medkit. Using one takes seconds; the
    // item is used up and the health given only when the time is out. The server cancels a use (the item stays)
    // when the user takes damage, fires, throws, reloads, switches weapon or dies. The medkit's count lives in the
    // loadout (Combatant.Medkits); bandages and first-aid kits are counted here. Clients read the SyncVars.
    //
    // HUD: show the ring while `Local != null && Local.Using`, filled by `Local.Progress01`, labelled `Local.ItemName`.
    [RequireComponent(typeof(Combatant))]
    public class CombatantHealing : NetworkBehaviour, ILootReceiver
    {
        public readonly SyncVar<int> Bandages = new SyncVar<int>();
        public readonly SyncVar<int> FirstAids = new SyncVar<int>();
        // Boost items (ADR-099) and the boost gauge they fill (0..BoostRules.MaxGauge, drains by itself).
        public readonly SyncVar<int> Drinks = new SyncVar<int>();
        public readonly SyncVar<int> Pills = new SyncVar<int>();
        public readonly SyncVar<int> Syringes = new SyncVar<int>();
        public readonly SyncVar<float> Gauge = new SyncVar<float>();
        // The HealKind being used; 0 = nothing.
        public readonly SyncVar<int> UsingKind = new SyncVar<int>();
        // Server clock (Time.timeAsDouble) when the use began, and how long it takes.
        public readonly SyncVar<double> StartTime = new SyncVar<double>();
        public readonly SyncVar<float> Duration = new SyncVar<float>();

        // The local player's own, for the HUD. Null when there is none.
        public static CombatantHealing Local { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Local = null;

        Combatant _combatant;
        ThrowableUser _throwables;
        CombatantCarry _carry;
        // This side's clock when it learned that a use began (clients do not share the server's clock).
        double _localStart;

        // Server snapshot taken when a use begins: what counts as "something happened" afterwards.
        float _baseHp;
        // Server truth of the gauge (the SyncVar follows it in coarse steps) and the health owed to the next whole point.
        float _gauge, _owedHeal;
        int _baseSlot, _baseMag0, _baseMag1, _baseMag2, _baseThrows;

        // Server counters, for tests.
        public int ServerCompleted { get; private set; }
        public int ServerCancelled { get; private set; }

        // ---- Read-only, every side (HUD, inventory) ----

        public bool Using => UsingKind.Value != 0;
        public HealKind Kind => (HealKind)UsingKind.Value;
        public string ItemName => Healing.NameOf(Kind);
        public float Seconds => Duration.Value;
        // 0..1 through the use; 0 when nothing is being used.
        public float Progress01 => Using ? Healing.Progress01(Time.timeAsDouble, _localStart, Duration.Value) : 0f;
        public float RemainingSeconds => Using ? Mathf.Max(0f, Duration.Value * (1f - Progress01)) : 0f;

        public int CountOf(HealKind kind)
        {
            switch (kind)
            {
                case HealKind.Bandage: return Bandages.Value;
                case HealKind.FirstAid: return FirstAids.Value;
                case HealKind.Medkit: return _combatant.Medkits.Value;
                case HealKind.Drink: return Drinks.Value;
                case HealKind.Pill: return Pills.Value;
                case HealKind.Syringe: return Syringes.Value;
                default: return 0;
            }
        }

        // What H would use right now (None when nothing fits).
        public HealKind BestFit => Healing.BestFit(_combatant.Hp.Value, Bandages.Value, FirstAids.Value, _combatant.Medkits.Value, HealRules.Current);

        // Bag room of the carried boost items.
        public int BoostTenths => Drinks.Value * Carry.Tenths(CarryRules.Current.Drink) + Pills.Value * Carry.Tenths(CarryRules.Current.Pill)
            + Syringes.Value * Carry.Tenths(CarryRules.Current.Syringe);

        public int BoostStep => Boost.Step(Gauge.Value, BoostRules.Current);
        public float Gauge01 => BoostRules.Current.MaxGauge > 0f ? Mathf.Clamp01(Gauge.Value / BoostRules.Current.MaxGauge) : 0f;

        public static bool IsUsing(Combatant c) => c != null && c.TryGetComponent(out CombatantHealing healing) && healing.Using;

        // Speed bonus of the boost gauge's upper steps (1 when there is none).
        public static float BoostSpeedOf(Combatant c) =>
            c != null && c.TryGetComponent(out CombatantHealing healing) ? Boost.SpeedFactor(healing.Gauge.Value, BoostRules.Current) : 1f;

        // The walking-speed factor of a combatant: below 1 while it heals. PlayerController multiplies its speed by it.
        public static float MoveFactorOf(Combatant c)
        {
            if (c == null || !c.TryGetComponent(out CombatantHealing healing) || !healing.Using) return 1f;
            return HealRules.Current.MoveFactor;
        }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _throwables = GetComponent<ThrowableUser>();
            _carry = GetComponent<CombatantCarry>();
            UsingKind.OnChange += OnKindChanged;
        }

        void OnKindChanged(int previous, int next, bool asServer)
        {
            if (next != 0) _localStart = Time.timeAsDouble;
        }

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

        // ---- Server ----

        void Update()
        {
            if (!IsServerInitialized) return;
            if (UsingKind.Value != 0) ServerTick(Time.timeAsDouble);
            ServerBoostTick(Time.deltaTime);
        }

        // The gauge drains and heals a little; health is given in whole points.
        public void ServerBoostTick(float dt)
        {
            if (_gauge <= 0f) return;
            if (_combatant.Dead.Value) { _gauge = _owedHeal = 0f; Gauge.Value = 0f; return; }
            _owedHeal += Boost.Tick(ref _gauge, dt, BoostRules.Current);
            if (_owedHeal >= 1f)
            {
                _combatant.ServerHeal(Mathf.Floor(_owedHeal));
                _owedHeal -= Mathf.Floor(_owedHeal);
            }
            // The replicated value moves in coarse steps, not every frame.
            if (_gauge <= 0f || Mathf.Abs(_gauge - Gauge.Value) >= 0.5f) Gauge.Value = _gauge;
        }

        // Test and bot hook: fills the gauge as a finished boost item would.
        public void ServerAddGauge(float amount)
        {
            _gauge = Boost.Add(_gauge, amount, BoostRules.Current);
            Gauge.Value = _gauge;
        }

        // Cancels the use when something got in the way, or finishes it when its time is out.
        public void ServerTick(double now)
        {
            if (!IsServerInitialized || UsingKind.Value == 0) return;
            var kind = (HealKind)UsingKind.Value;
            if (ShouldCancel(kind))
            {
                ServerCancel();
                return;
            }
            if (now < StartTime.Value + Duration.Value) return;
            Finish(kind);
        }

        bool ShouldCancel(HealKind kind)
        {
            var c = _combatant;
            if (c.Dead.Value || c.TeamBlocksAction || c.Reloading.Value) return true;
            float hp = c.Hp.Value;
            if (hp < _baseHp - 1e-4f) return true; // damage
            _baseHp = hp;
            if (c.ActiveSlot.Value != _baseSlot) return true;
            if (c.Mag0.Value < _baseMag0 || c.Mag1.Value < _baseMag1 || c.Mag2.Value < _baseMag2) return true; // a shot
            _baseMag0 = c.Mag0.Value;
            _baseMag1 = c.Mag1.Value;
            _baseMag2 = c.Mag2.Value;
            if (_throwables != null && _throwables.ServerThrows != _baseThrows) return true;
            return CountOf(kind) <= 0; // dropped meanwhile
        }

        void Finish(HealKind kind)
        {
            var rules = HealRules.Current;
            float amount = Healing.Amount(kind, _combatant.Hp.Value, rules);
            bool used;
            switch (kind)
            {
                case HealKind.Bandage: used = Bandages.Value > 0; if (used) Bandages.Value--; break;
                case HealKind.FirstAid: used = FirstAids.Value > 0; if (used) FirstAids.Value--; break;
                case HealKind.Drink: used = Drinks.Value > 0; if (used) Drinks.Value--; break;
                case HealKind.Pill: used = Pills.Value > 0; if (used) Pills.Value--; break;
                case HealKind.Syringe: used = Syringes.Value > 0; if (used) Syringes.Value--; break;
                default: used = _combatant.ServerConsumeMedkit(); break;
            }
            UsingKind.Value = 0;
            if (!used) { ServerCancelled++; return; }
            if (Healing.IsBoost(kind)) ServerAddGauge(Healing.For(kind, rules).Boost);
            else _combatant.ServerHeal(amount);
            ServerCompleted++;
            // A bandage goes on by itself while health is under its ceiling and there are more (ADR-099).
            if (kind == HealKind.Bandage) ServerBegin(HealKind.Bandage);
        }

        // Stops the use; the item stays. Safe to call when nothing is being used.
        public void ServerCancel()
        {
            if (!IsServerInitialized || UsingKind.Value == 0) return;
            UsingKind.Value = 0;
            ServerCancelled++;
        }

        // Starts the best-fitting item (what the H key asks for).
        public bool ServerUse() => ServerBegin(BestFit);

        // Starts one chosen item. Refused when dead, already using something, reloading, none of it is left, or
        // it could not heal at this health.
        public bool ServerBegin(HealKind kind)
        {
            if (!IsServerInitialized || kind == HealKind.None || UsingKind.Value != 0) return false;
            var c = _combatant;
            if (c.Dead.Value || c.TeamBlocksAction || c.Reloading.Value || CountOf(kind) <= 0) return false;
            var rules = HealRules.Current;
            if (Healing.IsBoost(kind) ? !Healing.CanUseBoost(kind, c.Hp.Value, _gauge, rules, BoostRules.Current) : !Healing.CanUse(kind, c.Hp.Value, rules)) return false;
            _baseHp = c.Hp.Value;
            _baseSlot = c.ActiveSlot.Value;
            _baseMag0 = c.Mag0.Value;
            _baseMag1 = c.Mag1.Value;
            _baseMag2 = c.Mag2.Value;
            _baseThrows = _throwables != null ? _throwables.ServerThrows : 0;
            double now = Time.timeAsDouble;
            _localStart = now;
            StartTime.Value = now;
            Duration.Value = Healing.SecondsOf(kind, rules);
            UsingKind.Value = (int)kind;
            return true;
        }

        // Returns how many were taken (the bag may refuse some or all).
        public int ServerAdd(HealKind kind, int amount)
        {
            if (!IsServerInitialized || amount <= 0 || kind == HealKind.None || kind == HealKind.Medkit) return 0;
            int taken = _carry != null ? _carry.FitCount(Healing.LootOf(kind), AmmoType.Pistol9, amount) : amount;
            if (taken <= 0) return 0;
            switch (kind)
            {
                case HealKind.Bandage: Bandages.Value += taken; break;
                case HealKind.FirstAid: FirstAids.Value += taken; break;
                case HealKind.Drink: Drinks.Value += taken; break;
                case HealKind.Pill: Pills.Value += taken; break;
                default: Syringes.Value += taken; break;
            }
            return taken;
        }

        // Bandage and first-aid pickups, handed over by Combatant.ServerGive. What does not fit goes back on the
        // ground at the feet; a pickup is refused (and stays where it lies) only when nothing of it fits.
        public bool ServerTryTake(LootEntry loot)
        {
            if (!IsServerInitialized || loot == null || _combatant.Dead.Value) return false;
            var kind = Healing.KindOfLoot(loot.Kind);
            if (kind == HealKind.None || kind == HealKind.Medkit) return false;
            int wanted = Mathf.Max(1, loot.Amount);
            int taken = ServerAdd(kind, wanted);
            if (taken <= 0) return false;
            if (taken < wanted && MatchDirector.Instance != null)
                MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = loot.Kind, Amount = wanted - taken }, transform.position + Vector3.up * 0.3f);
            return true;
        }

        // Death crate (ADR-116): every bandage, kit and boost item carried goes into `into` and the counts clear.
        public void ServerStrip(System.Collections.Generic.List<LootEntry> into)
        {
            if (!IsServerInitialized) return;
            ServerCancel();
            for (var kind = HealKind.Bandage; kind <= HealKind.Syringe; kind++)
            {
                if (kind == HealKind.Medkit) continue; // the loadout's own count, stripped with it
                int n = CountOf(kind);
                if (n <= 0) continue;
                switch (kind)
                {
                    case HealKind.Bandage: Bandages.Value = 0; break;
                    case HealKind.FirstAid: FirstAids.Value = 0; break;
                    case HealKind.Drink: Drinks.Value = 0; break;
                    case HealKind.Pill: Pills.Value = 0; break;
                    default: Syringes.Value = 0; break;
                }
                into?.Add(new LootEntry { Kind = Healing.LootOf(kind), Amount = n });
            }
        }

        // ---- Owner requests ----

        // The owner's call: the host is the server, so it does not wait for the next network tick.
        public void RequestUse()
        {
            if (IsServerInitialized) ServerUse(); else CmdUse();
        }

        // J: the smallest boost item carried.
        public void RequestBoost()
        {
            var kind = Healing.BestBoost(Drinks.Value, Pills.Value, Syringes.Value);
            if (kind == HealKind.None) return;
            if (IsServerInitialized) ServerBegin(kind); else CmdUseKind((int)kind);
        }

        public void RequestCancel()
        {
            if (IsServerInitialized) ServerCancel(); else CmdCancel();
        }

        [ServerRpc]
        public void CmdUse() => ServerUse();

        [ServerRpc]
        public void CmdCancel() => ServerCancel();

        // The inventory's double click on a bandage or kit row: that item, not the best fit.
        [ServerRpc]
        public void CmdUseKind(int kind) => ServerBegin((HealKind)kind);

        // Dropping from the inventory screen: one item lands on the ground in front.
        [ServerRpc]
        public void CmdDrop(int kind)
        {
            if (_combatant.Dead.Value || MatchDirector.Instance == null) return;
            var heal = (HealKind)kind;
            if (heal == HealKind.None || heal == HealKind.Medkit || CountOf(heal) <= 0) return;
            switch (heal)
            {
                case HealKind.Bandage: Bandages.Value--; break;
                case HealKind.FirstAid: FirstAids.Value--; break;
                case HealKind.Drink: Drinks.Value--; break;
                case HealKind.Pill: Pills.Value--; break;
                default: Syringes.Value--; break;
            }
            var loot = Healing.LootOf(heal);
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = loot, Amount = 1 },
                transform.position + transform.forward * 1.0f + Vector3.up * 0.3f);
        }
    }
}
