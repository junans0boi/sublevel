using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Team play on a combatant (ADR-072): its team and number, being knocked down (기절) instead of dying while a
    // teammate stands, bleeding out, reviving a teammate (F held), and the ping its owner shares with the team.
    // The server decides everything here; TeamDirector ticks it every frame. Solo matches never set a team, and a
    // combatant without a team is never knocked down, so nothing here changes a solo match.
    public partial class Combatant
    {
        // Team number (Teams.None = 0 outside team matches) and the member's number in it, 1..4.
        public readonly SyncVar<int> TeamId = new SyncVar<int>();
        public readonly SyncVar<int> TeamSlot = new SyncVar<int>();
        // The name the match uses for this combatant ("플레이어", "봇 3"), for teammates' tags and lists.
        public readonly SyncVar<string> TeamName = new SyncVar<string>("");
        public readonly SyncVar<bool> Knocked = new SyncVar<bool>();
        // Knocks in this life (1 on the first): every further one bleeds faster.
        public readonly SyncVar<int> KnockCount = new SyncVar<int>();
        // On the reviver: who it is reviving (-1 none). On the knocked: who revives it (-1 none). Both carry the progress.
        public readonly SyncVar<int> ReviveTargetId = new SyncVar<int>(-1);
        public readonly SyncVar<int> RevivedById = new SyncVar<int>(-1);
        public readonly SyncVar<float> ReviveProgress = new SyncVar<float>();
        // The owner's map marker, shared with the team (ADR-065 (6) marker, ADR-072).
        public readonly SyncVar<bool> PingOn = new SyncVar<bool>();
        public readonly SyncVar<Vector3> PingAt = new SyncVar<Vector3>();

        // Server only: someone was knocked down (victim, the damage that did it) / revived (revived, reviver).
        // Static like ServerDied: subscribers unsubscribe themselves; cleared when a play session starts.
        public static event Action<Combatant, DamageInfo> ServerKnocked;
        public static event Action<Combatant, Combatant> ServerRevived;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTeamStatics()
        {
            ServerKnocked = null;
            ServerRevived = null;
        }

        // Progress is replicated in these steps, not every frame.
        const float ReviveProgressStep = 0.02f;

        // Server: who knocked this one down (credited if it bleeds out or its team is wiped) and with what.
        Combatant _knockedBy;
        DamageInfo _knockInfo;
        float _bleedOwed;
        readonly ReviveTimer _revive = new ReviveTimer();
        Combatant _reviveTarget, _revivedBy;

        // Alive and not knocked down.
        public bool Standing => !Dead.Value && !Knocked.Value;
        public bool Reviving => ReviveTargetId.Value >= 0;
        public bool BeingRevived => RevivedById.Value >= 0;
        // Knocked down or busy reviving: no shooting, punching, picking up or healing.
        public bool TeamBlocksAction => Knocked.Value || Reviving;
        public bool HasTeam => TeamId.Value != Teams.None;

        public bool IsTeammateOf(Combatant other) => other != null && other != this && Teams.Same(TeamId.Value, other.TeamId.Value);

        // Seconds until a knocked combatant bleeds out at the current rate (paused while being revived); every side.
        public float BleedSecondsLeft => Knocked.Value ? Knocks.SecondsLeft(Hp.Value, Mathf.Max(1, KnockCount.Value), TeamRules.Current) : 0f;

        // ---- Server ----

        [Server]
        public void ServerSetTeam(int team, int slot, string name)
        {
            if (TeamId.Value != team) TeamId.Value = team;
            if (TeamSlot.Value != slot) TeamSlot.Value = slot;
            ServerSetTeamName(name);
        }

        [Server]
        public void ServerSetTeamName(string name)
        {
            name ??= "";
            if (TeamName.Value != name) TeamName.Value = name;
        }

        // A teammate (other than this one) is on its feet.
        bool ServerTeammateStanding()
        {
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (c != null && c.IsServerInitialized && IsTeammateOf(c) && c.Standing) return true;
            }
            return false;
        }

        // Friendly fire and the knocked damage factor (ADR-072). False when nothing is left to apply.
        bool ServerTeamAdjust(ref DamageInfo dealt)
        {
            var attacker = dealt.Attacker;
            float amount = Knocks.DamageBetween(dealt.Amount, attacker != null ? attacker.TeamId.Value : Teams.None, TeamId.Value,
                attacker == this, Knocked.Value, TeamRules.Current);
            if (!(amount > 0f)) return false;
            dealt.Amount = amount;
            return true;
        }

        // The hit that just emptied the health knocks down instead of killing, when the rules allow (ADR-072): the
        // knock bar replaces the health, the weapon is put away, the body lies down and leaves any vehicle.
        bool ServerTryKnock(in DamageInfo dealt)
        {
            var mode = HasTeam ? TeamMatch.Mode : TeamMode.Solo;
            if (!Knocks.KnocksDown(mode, Knocked.Value, ServerTeammateStanding())) return false;
            var rules = TeamRules.Current;
            _health = new Health(_maxHealth);
            _health.ApplyDamage(_maxHealth - Mathf.Clamp(rules.KnockedHp, 1f, _maxHealth));
            _knockedBy = dealt.Attacker != this ? dealt.Attacker : null;
            _knockInfo = dealt;
            _bleedOwed = 0f;
            KnockCount.Value = KnockCount.Value + 1;
            Knocked.Value = true;
            ServerCancelRevive();
            _loadout.CancelReload();
            Holstered.Value = true;
            Aim.Value = 0;
            Lean.Value = 0;
            var stance = GetComponent<CombatantStance>();
            if (stance != null)
            {
                stance.ServerStopParkour();
                stance.SlowWalk.Value = false;
                stance.State.Value = (int)Stance.Prone;
            }
            Push();
            ServerKnocked?.Invoke(this, dealt);
            // Out of a seat last: getting out at speed hurts, and that now lands on the knock bar.
            var vehicle = VehicleSeat.VehicleOf(this);
            if (vehicle != null) vehicle.ServerExit(this);
            return true;
        }

        // Damage that skips armour and the knock rule (bleeding, a wiped team's knocked members).
        void ServerApplyDirect(in DamageInfo info)
        {
            if (!IsServerInitialized || _health == null || _health.IsDead) return;
            if (_health.ApplyDamage(info.Amount) <= 0f) return;
            Push();
            if (_health.IsDead) ServerDied?.Invoke(this, info);
        }

        // A knocked combatant whose team has nobody left standing dies at once, credited to whoever knocked it.
        [Server]
        public void ServerFinishKnocked()
        {
            if (!Knocked.Value || _health == null || _health.IsDead) return;
            var info = _knockInfo;
            info.Attacker = _knockedBy;
            info.Amount = _health.Current;
            ServerApplyDirect(info);
        }

        // Called by TeamDirector once a frame on the server: bleeding and the revive under way.
        [Server]
        public void ServerTickTeam(float dt)
        {
            if (_health == null) return;
            if (_health.IsDead)
            {
                if (Reviving) ServerCancelRevive();
                if (Knocked.Value) Knocked.Value = false;
                if (BeingRevived) ClearRevivedBy();
                return;
            }
            TickRevive(dt);
            // The reviver went away (died, left) without saying so.
            if (BeingRevived && (_revivedBy == null || !_revivedBy.IsSpawned || _revivedBy._reviveTarget != this)) ClearRevivedBy();
            if (!Knocked.Value || BeingRevived) return;
            // Bleeding is paid in whole points so the health is not replicated every frame.
            _bleedOwed += TeamRules.Current.BleedFor(Mathf.Max(1, KnockCount.Value)) * dt;
            if (_bleedOwed < 1f) return;
            float amount = Mathf.Floor(_bleedOwed);
            _bleedOwed -= amount;
            ServerApplyDirect(new DamageInfo { Amount = amount, Attacker = _knockedBy, Zone = HitZone.Body, WeaponId = 0, Cause = DamageCause.Bleed });
        }

        // Starts reviving a knocked teammate within reach. Bots call it directly; players through CmdBeginRevive.
        [Server]
        public bool ServerBeginRevive(Combatant target)
        {
            if (_health == null || target == null || target == this || !Standing || Reviving || VehicleSeat.IsAttached(this)) return false;
            if (!target.IsServerInitialized || target.Dead.Value || target.BeingRevived) return false;
            float distance = Vector3.Distance(transform.position, target.transform.position);
            if (!_revive.CanStart(distance, Standing, target.Knocked.Value, IsTeammateOf(target), TeamRules.Current)) return false;
            _revive.Start();
            _reviveTarget = target;
            target._revivedBy = this;
            ReviveTargetId.Value = target.ObjectId;
            target.RevivedById.Value = ObjectId;
            ReviveProgress.Value = 0f;
            target.ReviveProgress.Value = 0f;
            _loadout.CancelReload();
            Aim.Value = 0;
            Lean.Value = 0;
            Push();
            return true;
        }

        [Server]
        public void ServerCancelRevive()
        {
            var target = _reviveTarget;
            _reviveTarget = null;
            _revive.Cancel();
            if (ReviveTargetId.Value != -1) ReviveTargetId.Value = -1;
            if (ReviveProgress.Value != 0f) ReviveProgress.Value = 0f;
            if (target != null && target._revivedBy == this) target.ClearRevivedBy();
        }

        void ClearRevivedBy()
        {
            _revivedBy = null;
            if (RevivedById.Value != -1) RevivedById.Value = -1;
            if (ReviveProgress.Value != 0f) ReviveProgress.Value = 0f;
        }

        void TickRevive(float dt)
        {
            if (_reviveTarget == null)
            {
                if (Reviving) ServerCancelRevive();
                return;
            }
            var target = _reviveTarget;
            bool there = target.IsSpawned;
            float distance = there ? Vector3.Distance(transform.position, target.transform.position) : float.MaxValue;
            var step = _revive.Tick(dt, distance, Standing && !VehicleSeat.IsAttached(this), there && target.Knocked.Value && !target.Dead.Value, TeamRules.Current);
            switch (step)
            {
                case ReviveStep.Cancelled:
                    ServerCancelRevive();
                    break;
                case ReviveStep.Going:
                    float progress = _revive.Progress01(TeamRules.Current);
                    if (progress - ReviveProgress.Value >= ReviveProgressStep)
                    {
                        ReviveProgress.Value = progress;
                        target.ReviveProgress.Value = progress;
                    }
                    break;
                case ReviveStep.Done:
                    ServerCancelRevive();
                    target.ServerGetUp(this);
                    break;
            }
        }

        // Back on its feet (crouched) with ReviveHp; the weapon stays put away until a slot is chosen.
        void ServerGetUp(Combatant by)
        {
            if (_health == null || _health.IsDead || !Knocked.Value) return;
            var rules = TeamRules.Current;
            _health = new Health(_maxHealth);
            _health.ApplyDamage(_maxHealth - Mathf.Clamp(rules.ReviveHp, 1f, _maxHealth));
            Knocked.Value = false;
            _knockedBy = null;
            _bleedOwed = 0f;
            var stance = GetComponent<CombatantStance>();
            if (stance != null) stance.State.Value = (int)Stance.Crouch;
            Push();
            ServerRevived?.Invoke(this, by);
        }

        [Server]
        public void ServerSetPing(bool on, Vector3 at)
        {
            float sqr = at.sqrMagnitude;
            if (float.IsNaN(sqr) || float.IsInfinity(sqr)) return;
            if (PingOn.Value != on) PingOn.Value = on;
            if (on && PingAt.Value != at) PingAt.Value = at;
        }

        // ---- Owner requests ----

        [ServerRpc]
        public void CmdBeginRevive(NetworkObject target)
        {
            if (target == null) return;
            ServerBeginRevive(target.GetComponent<Combatant>());
        }

        [ServerRpc]
        public void CmdCancelRevive() => ServerCancelRevive();

        [ServerRpc]
        public void CmdSetPing(bool on, Vector3 at) => ServerSetPing(on, at);
    }
}
