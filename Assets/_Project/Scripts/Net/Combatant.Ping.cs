using FishNet.Connection;
using FishNet.Object;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Team pings (ADR-130). A player asks the server to put a marker at a point; the server checks it and tells every
    // teammate (the pinger too) through that teammate's own combatant, which is always theirs to hear, whatever the
    // interest management shows them of the pinger. Players outside the team never get it: it is not a SyncVar.
    // Each client keeps what it was told in PingsHeard, a PingBoard that expires them (PingLifeSeconds).
    public partial class Combatant
    {
        // What this combatant's owner has been told by its team (filled on the owner's own side only).
        public readonly PingBoard PingsHeard = new PingBoard();

        // Server: per pinger, so a held button cannot flood the team.
        static readonly PingLimiter PingGate = new PingLimiter();

        [Server]
        public bool ServerPing(PingKind kind, Vector3 at)
        {
            var rules = TeamRules.Current;
            if (!Pings.IsValid((int)kind) || !HasTeam || Dead.Value || float.IsNaN(at.x + at.y + at.z) || float.IsInfinity(at.x + at.y + at.z)) return false;
            // A ping is a point in view: farther than the reach (plus a little for lag) is not one.
            if (Vector3.Distance(EyePosition, at) > rules.PingMaxMeters + 15f) return false;
            if (!PingGate.TryAccept(ObjectId, Time.time, rules)) return false;
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (c != null && c.IsServerInitialized && c.IsPlayer && (c == this || IsTeammateOf(c))) c.ServerTellPing(this, kind, at);
            }
            return true;
        }

        void ServerTellPing(Combatant from, PingKind kind, Vector3 at)
        {
            if (Owner == null || !Owner.IsValid || Owner.IsLocalClient) HearPing(from.ObjectId, from.TeamSlot.Value, (byte)kind, at);
            else TargetPing(Owner, from.ObjectId, from.TeamSlot.Value, (byte)kind, at);
        }

        [TargetRpc]
        void TargetPing(NetworkConnection connection, int owner, int slot, byte kind, Vector3 at) => HearPing(owner, slot, kind, at);

        void HearPing(int owner, int slot, byte kind, Vector3 at) =>
            PingsHeard.Add(owner, slot, (PingKind)kind, at.x, at.y, at.z, Time.time, TeamRules.Current);

        [ServerRpc]
        public void CmdPing(byte kind, Vector3 at) => ServerPing((PingKind)kind, at);
    }
}
