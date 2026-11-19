using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.AI
{
    // Farming (ADR-294, #373): right after the drop the bot picks a building near it, walks the loot points of that building
    // one after the other (items are found by sight on the way, SenseLoot), and stops when it is kitted out (BotFarm.Ready)
    // or its time is up. Out of time (or FarmHintSeconds after the drop) and unarmed, a bot is told where the nearest weapon lies. Server only.
    public partial class BotController
    {
        // Which bots chose which building (a soft reservation: BotRules.FarmBuildingCap others at most, teammates not counted).
        static readonly Dictionary<int, List<BotController>> Chosen = new Dictionary<int, List<BotController>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetFarm() => Chosen.Clear();

        int OthersIn(int building)
        {
            if (!Chosen.TryGetValue(building, out var list)) return 0;
            int n = 0;
            foreach (var bot in list) if (bot != this && bot._self != null && !_self.IsTeammateOf(bot._self)) n++;
            return n;
        }

        void Choose(int building)
        {
            Unchoose();
            _farmBuilding = building;
            if (building < 0) return;
            if (!Chosen.TryGetValue(building, out var list)) Chosen[building] = list = new List<BotController>(3);
            list.Add(this);
        }

        void Unchoose()
        {
            if (_farmBuilding >= 0 && Chosen.TryGetValue(_farmBuilding, out var list)) list.Remove(this);
            _farmBuilding = -1;
        }

        bool _farmStarted, _farmDone, _gunNoted;
        float _farmStartAt;
        int _farmBuilding = -1, _farmSpawn = -1;
        readonly HashSet<int> _farmVisitedBuildings = new HashSet<int>();
        readonly HashSet<int> _farmVisitedSpawns = new HashSet<int>();
        LootItem _hint;
        float _nextHintAt;

        public bool Farming => _farmStarted && !_farmDone;

        // Called before the brain thinks: says whether the bot is farming and where to, and keeps the weapon hint.
        void TickFarm(float now)
        {
            _percept.FarmActive = false;
            // First time it holds a gun: the farming statistic (seconds since the drop).
            if (_farmStarted && !_gunNoted && _self.HasAnyWeapon) { _gunNoted = true; BotFarmStats.FirstGunSeconds.Add(now - _farmStartAt); BotMatchStats.FirstGun[(int)_level].Add(now - _farmStartAt); }
            if (_farmStarted) TickHint(now);
            if (_farmDone) return;
            var index = BuildingLoot.Index;
            if (index == null || _rules.FarmSeconds <= 0f || !OnNavMesh) return;
            if (!_farmStarted)
            {
                _farmStarted = true;
                _farmStartAt = now;
                if (BotFarmStats.FirstStartAt < 0f) BotFarmStats.FirstStartAt = now;
                BotFarmStats.Bots++;
                BotFarmStats.Farming++;
            }
            if (BotFarm.Ready(_percept.HasWeapon, _percept.Mag + _percept.Reserve, _percept.MagSize, ArmorWorn(), HealCount, _rules)) { EndFarm(); return; }
            if (now - _farmStartAt >= _rules.FarmSeconds) { EndFarm(); return; }

            var at = transform.position;
            if (_farmSpawn >= 0)
            {
                index.SpawnAt(_farmSpawn, out float sx, out float sy, out float sz);
                if (BotFarm.Reached(at.x, at.y, at.z, sx, sy, sz, _rules.FarmArriveMeters) || _percept.Stuck) { _farmVisitedSpawns.Add(_farmSpawn); _farmSpawn = -1; }
            }
            // The next loot point of the building; none left: the next building.
            for (int guard = 0; guard < 3 && _farmSpawn < 0; guard++)
            {
                if (_farmBuilding >= 0)
                {
                    _farmSpawn = BotFarm.NextSpawn(index, _farmBuilding, at.x, at.z, _farmVisitedSpawns);
                    if (_farmSpawn >= 0) break;
                    _farmVisitedBuildings.Add(_farmBuilding);
                    Unchoose();
                    _farmVisitedSpawns.Clear();
                }
                Choose(BotFarm.PickBuilding(index, at.x, at.z, OthersIn, _farmVisitedBuildings, _rules, _rng));
                if (_farmBuilding < 0) { EndFarm(); return; }
            }
            if (_farmSpawn < 0) return;
            index.SpawnAt(_farmSpawn, out float tx, out float ty, out float tz);
            _percept.FarmActive = true;
            _percept.FarmTarget = new BotVec(tx, ty, tz);
        }

        int ArmorWorn() => _gear == null ? 0 : (_gear.Has(ArmorSlot.Helmet) ? 1 : 0) + (_gear.Has(ArmorSlot.Vest) ? 1 : 0);

        void EndFarm()
        {
            if (_farmStarted && !_farmDone) BotFarmStats.Farming--;
            _farmDone = true;
            Unchoose();
            _percept.FarmActive = false;
        }

        // Out of farming time, or FarmHintSeconds after the drop, without a gun: the nearest weapon on the ground is pointed out (BotFarm.HintDue).
        void TickHint(float now)
        {
            if (!BotFarm.HintDue(_self.HasAnyWeapon, _farmDone, now - _farmStartAt, _rules)) { _hint = null; return; }
            if (_hint != null && _hint.IsSpawned) return;
            if (now < _nextHintAt) return;
            _nextHintAt = now + 3f;
            _hint = null;
            float best = float.MaxValue;
            var here = transform.position;
            var all = LootItem.All;
            for (int i = 0; i < all.Count; i++)
            {
                var item = all[i];
                if (item == null || !item.IsSpawned || item.Kind.Value != (int)LootKind.Weapon) continue;
                if (GameData.Weapons.Find(item.WeaponId.Value)?.IsMelee == true) continue;
                float d = (item.transform.position - here).sqrMagnitude;
                if (d < best) { best = d; _hint = item; }
            }
        }

        // SenseLoot's last word: a pickup it keeps after the line of sight broke for a moment, and the hinted weapon.
        void AdoptRemembered(float now)
        {
            if (_loot != null) { _lootKept = _loot; _lootKeptAt = now; return; }
            if (_lootKept != null && _lootKept.IsSpawned && now - _lootKeptAt < LootKeepSeconds) { _loot = _lootKept; return; }
            _lootKept = null;
            if (_hint != null && _hint.IsSpawned && _hint.ObjectId != _ignoredLootId) _loot = _hint;
        }

        LootItem _lootKept;
        float _lootKeptAt;
        const float LootKeepSeconds = 2f;
    }
}
