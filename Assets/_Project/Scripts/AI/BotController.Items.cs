using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.AI
{
    // What a bot does with what it carries (#148, ADR-116): bandages, kits and boost items out of sight, grenades,
    // flashes, fire bottles and smoke in a fight, and what the flash and the fire do to it. Server only.
    public partial class BotController
    {
        struct FireSpot
        {
            public Vector3 Centre;
            public float Until;
        }

        static readonly List<FireSpot> Fires = new List<FireSpot>(8);

        CombatantGear _gear;
        CombatantHealing _healing;
        ThrowableUser _throwables;
        float _blindUntil, _deafUntil;
        float _nextThrowAt, _nextBoostAt;
        float _enemySeenAt = -100f;
        public int ThrowsMade { get; private set; }
        public int BoostsUsed { get; private set; }
        public bool Blind => Time.time < _blindUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void HookItems()
        {
            Fires.Clear();
            ThrownItem.ServerFlashed -= OnFlashed;
            ThrownItem.ServerFlashed += OnFlashed;
            ThrownItem.ServerFired -= OnFired;
            ThrownItem.ServerFired += OnFired;
        }

        // A flash went off: every bot works out its own blindness the way the player's screen does (distance, how far
        // it looks away, a clear line), and rings in its ears.
        static void OnFlashed(Vector3 centre, Combatant thrower)
        {
            var rules = FlashRules.Current;
            for (int i = 0; i < All.Count; i++)
            {
                var bot = All[i];
                if (bot == null || bot._self == null || bot._self.Dead.Value) continue;
                var eye = bot._self.EyePosition;
                var to = centre - eye;
                float distance = to.magnitude;
                float angle = distance < 0.05f ? 0f : Vector3.Angle(bot.transform.forward, to);
                bool sight = !Blocked(centre + Vector3.up * 0.15f, eye);
                float blind = Flash.Blind(distance, angle, sight, rules);
                float ring = Flash.Tinnitus(distance, blind, rules);
                float now = Time.time;
                if (blind > 0f) bot._blindUntil = Mathf.Max(bot._blindUntil, now + blind);
                if (ring > 0f) bot._deafUntil = Mathf.Max(bot._deafUntil, now + ring);
            }
        }

        static void OnFired(Vector3 centre, Combatant thrower)
        {
            Fires.Add(new FireSpot { Centre = centre, Until = Time.time + MolotovRules.Current.FireSeconds });
        }

        void InitItems()
        {
            _gear = GetComponent<CombatantGear>();
            _healing = GetComponent<CombatantHealing>();
            _throwables = GetComponent<ThrowableUser>();
        }

        // Bandages and kits count as healing to the brain (it heals below its threshold and retreats at low health).
        int HealCount => _self.Medkits.Value + (_healing != null ? _healing.Bandages.Value + _healing.FirstAids.Value : 0);

        // Before the brain thinks: a blind bot sees nothing, a deafened one hears nothing.
        void ApplyBlindness(float now)
        {
            if (_percept.EnemyVisible) _enemySeenAt = now;
            if (now < _blindUntil)
            {
                _percept.EnemyVisible = false;
                _enemy = null;
                _percept.LootVisible = false;
                _loot = null;
            }
            if (now < _deafUntil) _percept.Heard = false;
        }

        // After the brain thought: items, and the body's reaction to being blind or on fire.
        void ApplyItems(ref BotIntent intent, float now)
        {
            if (now < _blindUntil)
            {
                // Can neither see nor aim: it ducks and waits for its eyes to clear.
                intent.Move = false;
                intent.Sprint = false;
                intent.Fire = false;
                intent.PickUp = false;
                intent.Crouch = true;
                return;
            }
            if (FleeFire(ref intent, now)) return;

            bool fighting = _percept.EnemyVisible && _enemy != null;
            if (fighting) TryThrow(ref intent, now);
            else TryBoost(now, intent);
        }

        // Standing in a fire, or burning: run out of it, away from its centre.
        bool FleeFire(ref BotIntent intent, float now)
        {
            var at = transform.position;
            float radius = MolotovRules.Current.Radius + 1f;
            bool found = false;
            Vector3 centre = default;
            float best = float.MaxValue;
            for (int i = Fires.Count - 1; i >= 0; i--)
            {
                if (now >= Fires[i].Until) { Fires.RemoveAt(i); continue; }
                float d = Flat(Fires[i].Centre - at);
                if (d < radius && d < best) { best = d; centre = Fires[i].Centre; found = true; }
            }
            if (!found) return false;
            var away = at - centre;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            intent.Move = true;
            intent.Sprint = true;
            intent.Crouch = false;
            intent.SeekCover = false;
            intent.PickUp = false;
            intent.Heal = false;
            intent.MoveTarget = ToVec(at + away.normalized * _rules.FireFleeMeters);
            return true;
        }

        void TryBoost(float now, in BotIntent intent)
        {
            if (_healing == null || now < _nextBoostAt || _healing.Using || intent.State == BotState.Heal) return;
            int items = _healing.Drinks.Value + _healing.Pills.Value + _healing.Syringes.Value;
            if (!BotItems.WantsBoost(_percept.HealthFraction, now, _enemySeenAt, items, _self.Reloading.Value, _rules)) return;
            _nextBoostAt = now + 3f;
            var kind = Healing.BestBoost(_healing.Drinks.Value, _healing.Pills.Value, _healing.Syringes.Value);
            if (kind != HealKind.None && _healing.ServerBegin(kind)) BoostsUsed++;
        }

        void TryThrow(ref BotIntent intent, float now)
        {
            if (_throwables == null || now < _nextThrowAt || _self.Reloading.Value) return;
            var enemy = _enemy;
            var target = ChestOf(enemy);
            float distance = Flat(target - transform.position);
            ThrowableKind? kind;
            if (intent.State == BotState.Retreat && _throwables.Smokes.Value > 0 && distance > 5f)
            {
                kind = ThrowableKind.Smoke; // a screen between it and the one it runs from
                distance = Mathf.Min(distance * 0.5f, 6f);
                target = transform.position + Vector3.ProjectOnPlane(target - transform.position, Vector3.up).normalized * distance;
            }
            else
            {
                bool still = enemy.PlanarSpeed < _rules.StillSpeed;
                kind = BotItems.PickThrow(distance, still, _throwables.Grenades.Value, _throwables.Flashes.Value,
                    _throwables.Molotovs.Value, (float)_rng.NextDouble(), _rules);
            }
            if (kind == null) return;
            // A grenade does not go through a wall: only with a clear line to where it lands.
            var eye = _self.EyePosition;
            if (kind != ThrowableKind.Smoke && Blocked(eye, target)) return;
            var flat = target - eye;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.25f) return;
            var rules = ThrowableRules.Current;
            var origin = _throwables.ThrowOrigin(flat.normalized);
            if (!BotItems.TryLaunchPitch(Flat(target - origin), target.y - origin.y, rules.ThrowSpeed, rules.Gravity, out float pitch)) return;
            var direction = flat.normalized;
            var velocity = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, direction)) * direction * rules.ThrowSpeed;
            _nextThrowAt = now + _rules.ThrowIntervalSeconds;
            if (_throwables.ServerThrow(origin, velocity, kind.Value)) ThrowsMade++;
        }
    }
}
