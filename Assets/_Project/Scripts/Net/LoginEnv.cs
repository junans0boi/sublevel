using System;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Reads the login the launcher hands over (#389): SUBLEVEL_GAME_TOKEN on a client, SUBLEVEL_TOKEN_SECRET on a dedicated server.
    // Nothing is logged or stored. Without them (a dev run) the old open join stays.
    static class LoginEnv
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Read()
        {
            LobbyNet.ServerTokenSecret = Environment.GetEnvironmentVariable("SUBLEVEL_TOKEN_SECRET");
            var token = Environment.GetEnvironmentVariable("SUBLEVEL_GAME_TOKEN");
            if (!string.IsNullOrEmpty(token) && token.StartsWith("sg_", StringComparison.Ordinal))
            {
                // The launcher's scoped API token: the nickname comes from /api/me, the join token is fetched per join (LobbyNet.Update).
                LobbyNet.ApiToken = token;
                LobbyNet.Backend = HttpBackend.FromEnvironment();
                LobbyNet.Backend.Me().ContinueWith(t => { if (!t.IsFaulted && t.Result.Ok) LobbyNet.AuthNickname = t.Result.Value.nickname; });
            }
            else if (!string.IsNullOrEmpty(token) && GameToken.Peek(token, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out var c))
            {
                LobbyNet.AuthToken = token;
                LobbyNet.AuthNickname = c.Nickname;
            }
            else if (!string.IsNullOrEmpty(token)) Debug.LogWarning("[Login] the launcher's game token is expired or unreadable; run the game from the launcher again");
        }
    }
}
