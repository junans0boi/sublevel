using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.AI;

namespace Game.AI
{
    // Cover for the peek cycle (ADR-311, #375): a hiding place with a spot beside it that sees the enemy again. The rays are spread
    // over frames (CoverSearchesPerFrame bots a frame) and the answer is kept while the bot is hiding or peeking. Server only.
    public partial class BotController
    {
        const float CoverKeepSeconds = 8f;
        const float PeekEyeHeight = 1.6f;
        static readonly float[] HideRadii = { 3f, 6f, 10f };
        static readonly float[] PeekOffsets = { 2f, 3.5f };

        static int _coverFrame, _coversThisFrame;

        bool _coverOk, _peekAOk, _peekBOk;
        Vector3 _hidePoint, _peekAPoint, _peekBPoint;
        float _coverFoundAt = -100f, _nextCoverSearch;
        Vector3 _threatAt;
        float _threatSeenAt = -100f;

        readonly List<CoverCandidate> _cands = new List<CoverCandidate>(24);
        readonly List<Vector3> _candA = new List<Vector3>(24), _candB = new List<Vector3>(24), _candHide = new List<Vector3>(24);

        // Before the brain thinks: keeps the cover answer fresh and puts it in the percept.
        void TickCover(float now)
        {
            if (_percept.EnemyVisible) { _threatAt = FromVec(_percept.EnemyPosition); _threatSeenAt = now; }
            bool engaged = _percept.HasWeapon && BotWeapons.UsesCover(_percept.WeaponStyle) && _rules.PeekCycle
                && now - _threatSeenAt < _rules.MemorySeconds;
            _percept.CoverValid = false;
            if (!engaged) { _coverOk = false; return; }
            var phase = _brain.Phase;
            bool holding = phase == PeekPhase.Hide || phase == PeekPhase.Peek;
            if (now >= _nextCoverSearch && (!holding || now - _coverFoundAt > CoverKeepSeconds) && TakeCoverSlot())
            {
                _nextCoverSearch = now + _rules.CoverSearchSeconds;
                BotBrawlStats.CoverSearches++;
                SearchCover(_threatAt);
            }
            if (!_coverOk || now - _coverFoundAt > CoverKeepSeconds + 2f) return;
            _percept.CoverValid = true;
            _percept.CoverHide = ToVec(_hidePoint);
            _percept.CoverPeekAValid = _peekAOk; _percept.CoverPeekA = ToVec(_peekAPoint);
            _percept.CoverPeekBValid = _peekBOk; _percept.CoverPeekB = ToVec(_peekBPoint);
        }

        bool TakeCoverSlot()
        {
            if (_coverFrame != Time.frameCount) { _coverFrame = Time.frameCount; _coversThisFrame = 0; }
            return _coversThisFrame++ < Mathf.Max(1, _rules.CoverSearchesPerFrame);
        }

        static Vector3 FromVec(BotVec v) => new Vector3(v.X, v.Y, v.Z);

        void SearchCover(Vector3 threat)
        {
            var from = transform.position;
            _cands.Clear(); _candA.Clear(); _candB.Clear(); _candHide.Clear();
            for (int r = 0; r < HideRadii.Length; r++)
                for (int d = 0; d < CoverDirections; d++)
                {
                    float angle = d * (Mathf.PI * 2f / CoverDirections);
                    var candidate = from + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * HideRadii[r];
                    if (!NavMesh.SamplePosition(candidate, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                    if (!Physics.Linecast(hit.position + Vector3.up * CrouchedEyeHeight, threat, out var block, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    if (block.collider.GetComponentInParent<Combatant>() != null) continue;
                    var toThreat = threat - hit.position; toThreat.y = 0f;
                    if (toThreat.sqrMagnitude < 1e-4f) continue;
                    var side = Vector3.Cross(Vector3.up, toThreat.normalized);
                    bool okA = FindPeek(hit.position, side, threat, out var a, out float da);
                    bool okB = FindPeek(hit.position, -side, threat, out var b, out float db);
                    if (!okA && !okB) continue;
                    _cands.Add(new CoverCandidate
                    {
                        Hide = ToVec(hit.position), WalkMeters = Vector3.Distance(from, hit.position), PeekA = okA, PeekB = okB,
                        PeekMeters = okA && okB ? (da + db) * 0.5f : (okA ? da : db),
                    });
                    _candHide.Add(hit.position); _candA.Add(a); _candB.Add(b);
                }
            int best = BotCover.Pick(_cands, ToVec(from), ToVec(threat));
            _coverOk = best >= 0;
            if (!_coverOk) return;
            _coverFoundAt = Time.time;
            _hidePoint = _candHide[best];
            _peekAOk = _cands[best].PeekA; _peekBOk = _cands[best].PeekB;
            _peekAPoint = _candA[best]; _peekBPoint = _candB[best];
        }

        // A spot on one side of the hiding place from which the enemy is in sight.
        static bool FindPeek(Vector3 hide, Vector3 side, Vector3 threat, out Vector3 spot, out float meters)
        {
            for (int i = 0; i < PeekOffsets.Length; i++)
            {
                var candidate = hide + side * PeekOffsets[i];
                if (!NavMesh.SamplePosition(candidate, out var hit, 1f, NavMesh.AllAreas)) continue;
                var eye = hit.position + Vector3.up * PeekEyeHeight;
                if (Physics.Linecast(eye, threat, out var block, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && block.collider.GetComponentInParent<Combatant>() == null) continue;
                spot = hit.position; meters = PeekOffsets[i];
                return true;
            }
            spot = default; meters = 0f;
            return false;
        }

        // Cover statistics from the brain's own counters (the difference since the last call).
        int _tallyCycles, _tallyEpisodes, _tallyCut;
        double _tallyExposeSeconds;

        void TallyCover()
        {
            if (_brain == null) return;
            BotBrawlStats.PeekCycles += _brain.PeekCycles - _tallyCycles; _tallyCycles = _brain.PeekCycles;
            BotBrawlStats.ExposeEpisodes += _brain.ExposeEpisodes - _tallyEpisodes; _tallyEpisodes = _brain.ExposeEpisodes;
            BotBrawlStats.ExposeCutShort += _brain.ExposeCutShort - _tallyCut; _tallyCut = _brain.ExposeCutShort;
            BotBrawlStats.ExposeSeconds += (float)(_brain.ExposeSeconds - _tallyExposeSeconds); _tallyExposeSeconds = _brain.ExposeSeconds;
            if (_brain.Phase != PeekPhase.None) BotBrawlStats.EngagedSeconds += Time.deltaTime;
            if (_brain.Phase == PeekPhase.Hide) BotBrawlStats.HideSeconds += Time.deltaTime;
        }
    }
}
