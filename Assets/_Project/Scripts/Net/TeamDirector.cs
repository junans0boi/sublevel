using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Server side of team matches (ADR-072). Installs itself next to every match (nothing in a scene refers to it) and,
    // in a team mode only: puts every combatant in a team as it appears (players first, then bots, the order the
    // elevator deployment counts them in, so a team shares its elevator), keeps the names teammates see, ticks
    // bleeding and revives, finishes knocked members of a team with nobody left standing, and tells MatchState about
    // knocks for the feed. In solo it does nothing at all.
    public class TeamDirector : MonoBehaviour
    {
        const float NameRefreshSeconds = 0.5f;

        public static TeamDirector Instance { get; private set; }

        readonly HashSet<int> _assigned = new HashSet<int>();
        readonly List<TeamMember> _members = new List<TeamMember>();
        int _players, _others, _lateJoiners;
        float _nextNames;
        MatchState _state;

        // Server: the team mode this match was started in (fixed for the match).
        public TeamMode Mode { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            MatchState.Started -= Install;
            MatchState.Started += Install;
        }

        static void Install(MatchState state)
        {
            if (Instance != null || state == null) return;
            var director = state.gameObject.AddComponent<TeamDirector>();
            director._state = state;
        }

        void Awake()
        {
            Instance = this;
            Mode = TeamMatch.Mode;
            Combatant.ServerKnocked += OnKnocked;
        }

        void OnDestroy()
        {
            Combatant.ServerKnocked -= OnKnocked;
            if (Instance == this) Instance = null;
        }

        void OnKnocked(Combatant victim, DamageInfo info)
        {
            if (_state != null) _state.ServerPostKnock(victim, info);
        }

        void Update()
        {
            if (Mode == TeamMode.Solo || _state == null || !_state.IsServer) return;
            var all = Combatant.All;
            AssignNewcomers(all);
            if (Time.time >= _nextNames)
            {
                _nextNames = Time.time + NameRefreshSeconds;
                for (int i = 0; i < all.Count; i++)
                {
                    var c = all[i];
                    if (c != null && c.IsServerInitialized && c.HasTeam) c.ServerSetTeamName(_state.ServerNameOf(c));
                }
            }

            float dt = Time.deltaTime;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c != null && c.IsServerInitialized) c.ServerTickTeam(dt);
            }
            FinishStranded(all);
        }

        // Players are participants 0..ExpectedPlayers-1, everyone else follows in the order they appear; a player
        // beyond the expected ones comes after all bots.
        void AssignNewcomers(List<Combatant> all)
        {
            if (_assigned.Count == all.Count) return;
            int size = TeamRules.Current.SizeOf(Mode);
            var director = MatchDirector.Instance;
            int expected = director != null ? director.ExpectedPlayers : 1;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !c.IsServerInitialized || _assigned.Contains(c.ObjectId)) continue;
                _assigned.Add(c.ObjectId);
                int index;
                if (c.IsPlayer && _players < expected) index = _players++;
                else if (c.IsPlayer) index = expected + (director != null ? director.BotCount : _others) + _lateJoiners++;
                else index = expected + _others++;
                c.ServerSetTeam(Teams.TeamOf(index, size), Teams.SlotOf(index, size), _state.ServerNameOf(c));
            }
        }

        // Knocked members of a team with nobody left on its feet die: nobody could revive them.
        void FinishStranded(List<Combatant> all)
        {
            _members.Clear();
            bool anyKnocked = false;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                bool live = c != null && c.IsServerInitialized;
                _members.Add(live ? new TeamMember(c.TeamId.Value, c.Dead.Value, c.Knocked.Value) : new TeamMember(Teams.None, true, false));
                anyKnocked |= live && c.Knocked.Value;
            }
            if (!anyKnocked) return;
            _finish.Clear();
            for (int i = 0; i < _members.Count; i++)
                if (TeamStandings.MustFinish(_members, i)) _finish.Add(all[i]);
            // Collected first: a death runs handlers that may touch the combatant list.
            for (int i = 0; i < _finish.Count; i++) _finish[i].ServerFinishKnocked();
            _finish.Clear();
        }

        readonly List<Combatant> _finish = new List<Combatant>();
    }
}
