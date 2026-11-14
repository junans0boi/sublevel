using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Emotes (ADR-131): a short pose everyone nearby sees. The server accepts a request from an able body with empty
    // hands (the owner's client puts the weapon away first), keeps it for the emote's length and ends it early when the
    // body is hurt, knocked down, dies or gets into a vehicle; the owner's client ends it when the player moves or fires.
    // Replicated to everyone like the stance: it is no secret.
    public partial class Combatant
    {
        // The playing emote (an Emote value), 0 none.
        public readonly SyncVar<byte> EmoteId = new SyncVar<byte>();

        const float EmoteMinInterval = 0.4f;
        static readonly EmoteGate EmoteLimit = new EmoteGate();
        float _emoteEnds, _emoteHp;

        public bool Emoting => EmoteId.Value != 0;

        [Server]
        public bool ServerEmote(Emote emote)
        {
            if (emote == Emote.None)
            {
                ServerEndEmote();
                return true;
            }
            if (!Emotes.IsValid((int)emote) || _health == null || _health.IsDead || TeamBlocksAction || VehicleSeat.IsAttached(this) || BeingCarried) return false;
            if (ActiveWeaponId != 0) return false; // holstered counts as empty hands
            if (EmoteId.Value == (byte)emote) return true; // already playing it
            if (!EmoteLimit.TryAccept(ObjectId, Time.time, EmoteMinInterval)) return false;
            _emoteEnds = Time.time + Emotes.Duration(emote);
            _emoteHp = _health.Current;
            EmoteId.Value = (byte)emote;
            return true;
        }

        [Server]
        public void ServerEndEmote()
        {
            if (EmoteId.Value != 0) EmoteId.Value = 0;
        }

        // Server, every frame (from Update).
        void ServerTickEmote()
        {
            if (EmoteId.Value == 0) return;
            if (_health == null || _health.IsDead || TeamBlocksAction || VehicleSeat.IsAttached(this) || Time.time >= _emoteEnds || _health.Current < _emoteHp - 0.01f) ServerEndEmote();
        }

        [ServerRpc]
        public void CmdEmote(byte emote)
        {
            if (NotFlooding(ref _emoteRequests, EmoteMinInterval, "emote")) ServerEmote((Emote)emote);
        }
    }
}
