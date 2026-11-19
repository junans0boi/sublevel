using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.AI;

namespace Game.AI
{
    // Empty-handed and after the fight (ADR-310, #374): fist fights, running out of sight (BotFlee), going to where somebody fell.
    // Server only.
    public partial class BotController
    {
        struct DeathSpot
        {
            public Vector3 Position;
            public float At;
            public int Id;
        }

        static int _deathId;

        static readonly List<DeathSpot> Deaths = new List<DeathSpot>(32);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetBrawl() { Deaths.Clear(); _deathId = 0; }

        // Combatant.ServerDied is cleared when a play session starts, so it is hooked again by every bot's Init (idempotent).
        static void HookDeaths()
        {
            Combatant.ServerDied -= OnSomebodyDied;
            Combatant.ServerDied += OnSomebodyDied;
            Combatant.ServerDamaged -= OnSomebodyHurt;
            Combatant.ServerDamaged += OnSomebodyHurt;
        }

        // A bot was hurt by somebody: in the peace it goes after that one only (ADR-315).
        static void OnSomebodyHurt(Combatant who, DamageInfo info, float amount)
        {
            if (who == null || info.Attacker == null || info.Attacker == who || !who.TryGetComponent(out BotController bot)) return;
            bot._hurtAt = Time.time;
            bot._attackerId = info.Attacker.ObjectId;
        }

        static void OnSomebodyDied(Combatant who, DamageInfo info)
        {
            if (who == null) return;
            Deaths.Add(new DeathSpot { Position = who.transform.position, At = Time.time, Id = ++_deathId });
            if (Deaths.Count > 64) Deaths.RemoveAt(0);
        }

        // Last gunshot heard within earshot (Sense): where and when.
        Vector3 _shotAt;
        float _shotTime = -1000f;
        float _lastEnemyAt = -1000f;

        // Corpse trip.
        bool _corpseTrip;
        Vector3 _corpseSpot;
        float _corpseSince;
        readonly HashSet<int> _corpsesDone = new HashSet<int>();
        int _corpseIndex = -1;

        // Flight statistics: one episode from the first run until the line of sight breaks for 2 s (success) or FleeSeconds pass.
        bool _fleeing;
        float _fleeStart, _unseenSince = -1f;
        BotState _prevState = BotState.Roam;
        bool _fistFighting;

        // Before the brain: the percept bits for this (ADR-310).
        bool _heardCounted, _investigatedCounted, _everEngaged, _inEngagement;
        float _engageStart, _engageLast;

        void TickBrawl(float now)
        {
            if (_percept.Heard && !_heardCounted) { _heardCounted = true; BotBrawlStats.NoiseHeardBots++; }
            if (_percept.EnemyVisible) _lastEnemyAt = now;
            _percept.CorpseActive = false;
            if (_farmStarted && !_farmDone) { _corpseTrip = false; return; }
            if (!_farmStarted) return;
            // Still on a trip: arrived, too long or something else came up ends it.
            if (_corpseTrip)
            {
                var at = transform.position;
                bool arrived = Flat(_corpseSpot - at) <= _rules.FarmArriveMeters + 1f;
                if (arrived) { BotBrawlStats.CorpseArrived++; EndTrip(); }
                else if (now - _corpseSince > _rules.CorpseGiveUpSeconds || _percept.EnemyVisible || now - _shotTime < _rules.CorpseQuietSeconds && Flat(_shotAt - _corpseSpot) < 50f)
                {
                    BotBrawlStats.CorpseGaveUp++;
                    EndTrip();
                }
            }
            if (!_corpseTrip && !_percept.EnemyVisible) TryStartTrip(now);
            if (!_corpseTrip) return;
            _percept.CorpseActive = true;
            _percept.CorpseTarget = ToVec(_corpseSpot);
        }

        void EndTrip()
        {
            _corpseTrip = false;
            if (_corpseIndex > 0) _corpsesDone.Add(_corpseIndex);
            _corpseIndex = -1;
        }

        void TryStartTrip(float now)
        {
            bool lacksGear = !BotFarm.Ready(_percept.HasWeapon, _percept.Mag + _percept.Reserve, _percept.MagSize, ArmorWorn(), HealCount, _rules);
            if (!lacksGear) return;
            var at = transform.position;
            int best = -1;
            float bestRank = float.MaxValue;
            for (int i = 0; i < Deaths.Count; i++)
            {
                if (_corpsesDone.Contains(Deaths[i].Id)) continue;
                float meters = Flat(Deaths[i].Position - at);
                double age = now - Deaths[i].At;
                double sinceShot = Flat(_shotAt - Deaths[i].Position) < 50f ? now - _shotTime : 1e9;
                bool enemyRecent = now - _lastEnemyAt < _rules.MemorySeconds * 0.5f;
                if (!BotCorpse.Approach(true, meters, age, sinceShot, enemyRecent, _rules)) continue;
                float rank = BotCorpse.Rank(meters, age);
                if (rank < bestRank) { bestRank = rank; best = i; }
            }
            if (best < 0) return;
            _corpseTrip = true;
            _corpseSince = now;
            _corpseSpot = Deaths[best].Position;
            _corpseIndex = Deaths[best].Id;
            BotBrawlStats.CorpseTrips++;
        }

        // After the brain thought: the statistics of what it decided.
        void TallyBrawl(float now)
        {
            // An engagement: Engage with an enemy in sight; it ends when that has not been so for 3 s.
            bool engaged = _intent.State == BotState.Engage && _percept.EnemyVisible;
            if (engaged)
            {
                if (!_inEngagement)
                {
                    _inEngagement = true;
                    _engageStart = now;
                    if (!_everEngaged) { _everEngaged = true; BotMatchStats.FirstEngages++; if (_percept.HasWeapon) BotMatchStats.FirstEngagesArmed++; }
                }
                _engageLast = now;
            }
            else if (_inEngagement && now - _engageLast > 3f)
            {
                _inEngagement = false;
                BotMatchStats.Engagements++;
                BotMatchStats.EngageSeconds += _engageLast - _engageStart;
            }
            if (!_investigatedCounted && _brain.NoiseInvestigations > 0) { _investigatedCounted = true; BotBrawlStats.NoiseInvestigatedBots++; }
            if (_farmDone) BotBrawlStats.StateSeconds[(int)_intent.State] += Time.deltaTime;
            bool unarmed = !_percept.HasWeapon && !_percept.OtherSlotUsable;
            if (_intent.State == BotState.Engage && unarmed && _prevState != BotState.Engage) BotBrawlStats.UnarmedEngages++;
            bool fist = _intent.State == BotState.Engage && unarmed && _percept.EnemyVisible;
            if (fist && !_fistFighting && !_percept.EnemyArmed) BotBrawlStats.FistFights++;
            _fistFighting = fist;
            _prevState = _intent.State;

            bool running = _intent.State == BotState.Retreat && unarmed && _percept.EnemyArmed;
            if (running && !_fleeing)
            {
                _fleeing = true;
                _fleeStart = now;
                _unseenSince = -1f;
                BotBrawlStats.FleeStarts++;
            }
            else if (_fleeing)
            {
                if (_percept.EnemyVisible) _unseenSince = -1f;
                else if (_unseenSince < 0f) _unseenSince = now;
                if (_unseenSince >= 0f && now - _unseenSince >= 2f) { BotBrawlStats.FleeBroke++; _fleeing = false; }
                else if (now - _fleeStart > _rules.FleeSeconds && _percept.EnemyVisible) { BotBrawlStats.FleeCaught++; _fleeing = false; }
            }
        }

        // Died while running away.
        void TallyDeath()
        {
            if (_fleeing) { BotBrawlStats.FleeCaught++; _fleeing = false; }
        }

        // Where to hide from an armed enemy when empty-handed: BotFlee over the same ring of places FindCover looks at.
        readonly List<FleeCandidate> _flee = new List<FleeCandidate>(CoverDirections * 3);

        bool FindFleeCover(Vector3 threat, out Vector3 cover)
        {
            cover = default;
            var from = transform.position;
            _flee.Clear();
            for (int r = 0; r < CoverRadii.Length; r++)
                for (int d = 0; d < CoverDirections; d++)
                {
                    float angle = d * (Mathf.PI * 2f / CoverDirections);
                    var candidate = from + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * CoverRadii[r];
                    if (!NavMesh.SamplePosition(candidate, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                    bool hidden = Physics.Linecast(hit.position + Vector3.up * CrouchedEyeHeight, threat, out var block, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        && block.collider.GetComponentInParent<Combatant>() == null;
                    _flee.Add(new FleeCandidate { Point = ToVec(hit.position), Hidden = hidden, WalkMeters = Vector3.Distance(from, hit.position) });
                }
            int best = BotFlee.Pick(_flee, ToVec(from), ToVec(threat));
            if (best < 0) return false;
            var p = _flee[best].Point;
            cover = new Vector3(p.X, p.Y, p.Z);
            return true;
        }
    }
}
