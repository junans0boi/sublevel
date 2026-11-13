using UnityEngine;

namespace Game.Net
{
    // How this process takes part in the match the next scene starts (#160): alone (the default, as before), as the
    // host of a lobby's players, or as one of them. The lobby sets it, MatchDirector reads it, the main menu resets it.
    public static class NetSession
    {
        public enum RoleKind { Single, Host, Client }

        public static RoleKind Role = RoleKind.Single;
        // Client: where the host's match listens. Host: the port it listens on.
        public static string Address = "127.0.0.1";
        public static ushort Port;
        // Host: the people the lobby counted (placed first in the elevators) and the bots that fill the match; -1 = the rules' own.
        public static int Players = 1;
        public static int Bots = -1;

        // Names this player across reconnects to a match (#166): the server hands the same combatant back to the same token.
        public static readonly string Token = System.Guid.NewGuid().ToString("N");

        // Host role without a local player: a dedicated server (#168).
        public static bool Dedicated;

        public static bool IsMultiplayer => Role != RoleKind.Single;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Role = RoleKind.Single;
            Address = "127.0.0.1";
            Port = 0;
            Players = 1;
            Bots = -1;
            Dedicated = false;
        }

        // Called with what the lobby decided, right before the match scene loads.
        public static void Begin(LobbyNet lobby)
        {
            var start = lobby.StartInfo;
            Role = lobby.IsHost ? RoleKind.Host : RoleKind.Client;
            Address = lobby.IsHost ? "127.0.0.1" : lobby.Address;
            Port = start.MatchPort;
            Players = start.Players;
            Bots = start.Bots;
        }
    }
}
