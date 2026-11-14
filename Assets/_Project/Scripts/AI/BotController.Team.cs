using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.AI;

namespace Game.AI
{
    // A bot in a team (ADR-072), kept minimal: it never targets teammates, crawls toward a teammate while knocked
    // down, revives a knocked teammate once no enemy has been in sight for a moment, and while only roaming,
    // looting or looking into a noise it walks back to its leader (the team's player, else the lowest-numbered
    // member on its feet) when it has strayed too far. Fighting and the lockdown still come first.
    public partial class BotController
    {
        // A knocked enemy counts as this many times farther away when choosing whom to shoot.
        const float KnockedTargetFactor = 4f;
        const float CrawlArriveMeters = 1.5f;
        const float ReviveSprintMeters = 6f;

        float _lastEnemySeenAt = -100f;
        bool _wasKnocked;
        float _nextCrawlPath;

        // While knocked: crawl to the nearest teammate on its feet (it can revive sooner), facing the way it goes.
        void TickKnocked(float now)
        {
            _wasKnocked = true;
            _hasDestination = false;
            if (!OnNavMesh) return;
            // Carried on a teammate's back (ADR-129): rides along instead of crawling.
            if (_self.TryCarryPose(out var carriedAt, out float carrierYaw))
            {
                _agent.Warp(carriedAt);
                transform.rotation = Quaternion.Euler(0f, carrierYaw, 0f);
                return;
            }
            var mate = NearestTeammate(true, float.MaxValue, out float distance);
            float speed = GameData.Rules.Locomotion.WalkSpeed * TeamRules.Current.KnockedMoveFactor;
            if (_stance != null) speed *= _stance.MoveFactor;
            _agent.speed = speed;
            if (mate == null || distance <= CrawlArriveMeters)
            {
                if (!_agent.isStopped)
                {
                    _agent.isStopped = true;
                    _agent.velocity = Vector3.zero;
                }
                return;
            }
            if (now >= _nextCrawlPath)
            {
                _nextCrawlPath = now + RepathSeconds;
                if (NavMesh.SamplePosition(mate.transform.position, out var hit, DestinationSnapMeters, NavMesh.AllAreas)) _agent.SetDestination(hit.position);
            }
            if (_agent.isStopped) _agent.isStopped = false;
            var facing = _agent.velocity;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), _rules.TurnDegPerSecond * Time.deltaTime);
        }

        void ApplyTeam(ref BotIntent intent, float now)
        {
            if (!_self.HasTeam) return;
            if (_percept.EnemyVisible) _lastEnemySeenAt = now;
            var rules = TeamRules.Current;

            // Just got up: the weapon was put away when it went down.
            if (_wasKnocked)
            {
                _wasKnocked = false;
                _hasDestination = false;
                if (_self.Holstered.Value && _self.HasAnyWeapon) _self.ServerSelectSlot(_self.ActiveSlot.Value);
            }

            if (_self.Reviving)
            {
                // An enemy shows up: fight first.
                if (_percept.EnemyVisible)
                {
                    _self.ServerCancelRevive();
                    return;
                }
                var target = FindCombatant(_self.ReviveTargetId.Value);
                Hold(ref intent, target);
                return;
            }

            if (now - _lastEnemySeenAt >= rules.BotReviveCalmSeconds)
            {
                var down = NearestTeammate(false, rules.BotReviveSearchMeters, out float distance);
                if (down != null)
                {
                    if (distance <= rules.ReviveRangeMeters * 0.8f && _self.ServerBeginRevive(down))
                    {
                        Hold(ref intent, down);
                        return;
                    }
                    intent.Move = true;
                    intent.MoveTarget = ToVec(down.transform.position);
                    intent.Sprint = distance > ReviveSprintMeters;
                    intent.SeekCover = false;
                    intent.PickUp = false;
                    intent.Crouch = false;
                    intent.Heal = false;
                    return;
                }
            }

            if (intent.State != BotState.Roam && intent.State != BotState.Investigate && intent.State != BotState.Loot) return;
            var leader = Leader();
            if (leader == null) return;
            float away = Flat(leader.transform.position - transform.position);
            bool far = away > rules.BotRegroupMeters;
            if (away <= rules.BotFollowMeters || (intent.State == BotState.Loot && !far)) return;
            intent.Move = true;
            intent.MoveTarget = ToVec(leader.transform.position);
            intent.Sprint = far;
            intent.PickUp = false;
        }

        // Kneels by the knocked teammate: no walking or shooting, facing it.
        static void Hold(ref BotIntent intent, Combatant target)
        {
            intent.Move = false;
            intent.Sprint = false;
            intent.SeekCover = false;
            intent.Fire = false;
            intent.Reload = false;
            intent.Heal = false;
            intent.PickUp = false;
            intent.SwitchWeapon = false;
            intent.Crouch = true;
            intent.Aim = false;
            if (target == null) return;
            intent.Look = true;
            intent.LookTarget = ToVec(target.transform.position);
        }

        // The nearest teammate on its feet (standing) or knocked down and not being revived (!standing), within `range`.
        Combatant NearestTeammate(bool standing, float range, out float distance)
        {
            distance = float.MaxValue;
            Combatant best = null;
            float bestSqr = range * range;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !c.IsServerInitialized || !_self.IsTeammateOf(c) || c.Dead.Value) continue;
                if (standing ? !c.Standing : !c.Knocked.Value || c.BeingRevived) continue;
                float sqr = (c.transform.position - transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = c;
            }
            if (best != null) distance = Mathf.Sqrt(bestSqr);
            return best;
        }

        // Whom a roaming bot keeps near: the team's player while on its feet, else the lowest-numbered member on its
        // feet. Null when that is this bot itself.
        Combatant Leader()
        {
            Combatant player = null, first = null;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !c.IsServerInitialized || !c.Standing || (c != _self && !_self.IsTeammateOf(c))) continue;
                if (c.IsPlayer && player == null) player = c;
                if (first == null || c.TeamSlot.Value < first.TeamSlot.Value) first = c;
            }
            var leader = player ?? first;
            return leader == _self ? null : leader;
        }

        static Combatant FindCombatant(int objectId)
        {
            if (objectId < 0) return null;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ObjectId == objectId) return all[i];
            return null;
        }
    }
}
