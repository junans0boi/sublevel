using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // A knock-down for the kill feed (ADR-072): who knocked whom with what. Placement and kills do not change.
    public struct KnockEntry : IBroadcast
    {
        public int KnockerId;       // -1 when nobody did it
        public int VictimId;
        public string KnockerName;
        public string VictimName;
        public int WeaponId;
        public bool Headshot;
        public byte Cause;          // DamageCause
    }

    // A whole team went out with this placement (ADR-072); its members already dead learn their placement here.
    public struct TeamPlaced : IBroadcast
    {
        public int Team;
        public int Placement;
    }

    // Team side of the score keeping (ADR-072). In a team mode the last team standing wins: a member's death only
    // places it once its whole team is out, and then every member gets the team's placement.
    public partial class MatchState
    {
        // Client side: a knock arrived.
        public static event Action<KnockEntry> KnockPosted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTeamStatics() => KnockPosted = null;

        readonly List<TeamMember> _serverMembers = new List<TeamMember>();
        readonly List<Combatant> _serverMemberOf = new List<Combatant>();
        int _serverWinnerTeam = Teams.None;
        // Replicated: placements of whole teams.
        readonly Dictionary<int, int> _teamPlacements = new Dictionary<int, int>();

        public bool IsServer => ServerActive;
        // The winning team once decided (Teams.None while playing, in solo, or when nobody was left).
        public int WinnerTeam { get; private set; } = Teams.None;
        public int KnocksPosted { get; private set; }

        public int TeamPlacementOf(int team) => team != Teams.None && _teamPlacements.TryGetValue(team, out int placement) ? placement : 0;

        // Teams in the match and still alive, as clients see the combatants (0 and 0 in solo).
        public int TeamsTotal => CountTeams(false);
        public int TeamsAlive => CountTeams(true);

        static readonly HashSet<int> TeamScratch = new HashSet<int>();

        static int CountTeams(bool aliveOnly)
        {
            TeamScratch.Clear();
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !c.HasTeam || (aliveOnly && c.Dead.Value)) continue;
                TeamScratch.Add(c.TeamId.Value);
            }
            return TeamScratch.Count;
        }

        void BindTeam()
        {
            _network.ClientManager.RegisterBroadcast<KnockEntry>(OnKnockEntry);
            _network.ClientManager.RegisterBroadcast<TeamPlaced>(OnTeamPlaced);
        }

        void UnbindTeam()
        {
            _network.ClientManager.UnregisterBroadcast<KnockEntry>(OnKnockEntry);
            _network.ClientManager.UnregisterBroadcast<TeamPlaced>(OnTeamPlaced);
        }

        bool ServerTeamMode => TeamDirector.Instance != null && TeamDirector.Instance.Mode != TeamMode.Solo;

        void ServerFillMembers()
        {
            _serverMembers.Clear();
            _serverMemberOf.Clear();
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !c.IsServerInitialized) continue;
                _serverMembers.Add(new TeamMember(c.TeamId.Value, _serverDead.Contains(c.ObjectId), c.Knocked.Value));
                _serverMemberOf.Add(c);
            }
        }

        // The placement a death gives: solo as before (one behind those alive); in a team mode 0 while the victim's
        // team still has someone alive, else the team's placement, which every member hears about.
        int ServerVictimPlacement(Combatant victim)
        {
            if (!ServerTeamMode || !victim.HasTeam) return _serverAlive + 1;
            ServerFillMembers();
            int team = victim.TeamId.Value;
            if (TeamStandings.TeamAlive(_serverMembers, team)) return 0;
            int placement = TeamStandings.PlacementOnElimination(_serverMembers);
            ServerBroadcast(new TeamPlaced { Team = team, Placement = placement });
            return placement;
        }

        // Solo: one combatant left. Team modes: one team left. `winner` is a living member of the winner (or null).
        bool ServerDecided(out Combatant winner)
        {
            winner = null;
            _serverWinnerTeam = Teams.None;
            if (!ServerTeamMode)
            {
                if (_serverAlive > 1) return false;
                winner = ServerLastAlive();
                return true;
            }
            ServerFillMembers();
            if (!TeamStandings.Decided(_serverMembers, out int team)) return false;
            _serverWinnerTeam = team;
            for (int i = 0; i < _serverMembers.Count && winner == null; i++)
                if (!_serverMembers[i].Dead && _serverMembers[i].Team == team) winner = _serverMemberOf[i];
            return true;
        }

        // "팀 3" for a winning team in a team mode, else the winner's own name.
        string ServerWinnerName(Combatant winner) =>
            _serverWinnerTeam != Teams.None ? "팀 " + _serverWinnerTeam : winner != null ? ServerName(winner) : "";

        // Called by TeamDirector when someone is knocked down.
        public void ServerPostKnock(Combatant victim, DamageInfo info)
        {
            if (victim == null || !ServerActive || _serverOver) return;
            var knocker = info.Attacker != null && info.Attacker != victim ? info.Attacker : null;
            ServerBroadcast(new KnockEntry
            {
                KnockerId = knocker != null ? knocker.ObjectId : -1,
                VictimId = victim.ObjectId,
                KnockerName = knocker != null ? ServerName(knocker) : "",
                VictimName = ServerName(victim),
                WeaponId = info.WeaponId,
                Headshot = info.Cause == DamageCause.Shot && info.Zone == HitZone.Head,
                Cause = (byte)info.Cause,
            });
        }

        void ServerBroadcast<T>(T message) where T : struct, IBroadcast
        {
            if (!ServerActive) return;
            foreach (var connection in _network.ServerManager.Clients.Values)
                if (connection.IsAuthenticated) _network.ServerManager.Broadcast(connection, message);
        }

        void OnKnockEntry(KnockEntry entry, Channel channel)
        {
            KnocksPosted++;
            KnockPosted?.Invoke(entry);
        }

        void OnTeamPlaced(TeamPlaced placed, Channel channel)
        {
            if (placed.Team == Teams.None) return;
            _teamPlacements[placed.Team] = placed.Placement;
            Changed?.Invoke();
        }

        // Client: the winner of a team match places its whole team first.
        void ApplyWinnerTeam(int team)
        {
            WinnerTeam = team;
            if (team != Teams.None) _teamPlacements[team] = 1;
        }

        // A combatant's own placement, or its team's once the team is placed.
        int PlacementWithTeam(Combatant combatant, int own) =>
            own != 0 || combatant == null ? own : TeamPlacementOf(combatant.TeamId.Value);
    }
}
