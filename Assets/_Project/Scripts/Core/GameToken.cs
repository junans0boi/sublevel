using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Game.Core
{
    public enum TokenResult : byte { Ok = 0, Missing = 1, Malformed = 2, BadSignature = 3, Expired = 4 }

    // The short-lived game token the backend signs (POST /api/auth/token, sublevel-backend ADR 0002):
    // base64url(json {sub,nick,iat,exp}) + "." + base64url(HMAC-SHA256(TOKEN_SECRET, body)). The dedicated server checks it offline. Pure.
    public readonly struct GameTokenClaims
    {
        public readonly long AccountId;
        public readonly string Nickname; // "" when the account has none yet
        public readonly long ExpiresAt;  // unix seconds
        public GameTokenClaims(long account, string nick, long exp) { AccountId = account; Nickname = nick ?? ""; ExpiresAt = exp; }
    }

    public static class GameToken
    {
        static readonly Regex Sub = new Regex("\"sub\"\\s*:\\s*(\\d+)");
        static readonly Regex Exp = new Regex("\"exp\"\\s*:\\s*(\\d+)");
        static readonly Regex Nick = new Regex("\"nick\"\\s*:\\s*(null|\"((?:[^\"\\\\]|\\\\.)*)\")");

        public static TokenResult Verify(string secret, string token, long nowUnixSeconds, out GameTokenClaims claims)
        {
            claims = default;
            if (string.IsNullOrEmpty(token)) return TokenResult.Missing;
            var parts = token.Split('.');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0 || string.IsNullOrEmpty(secret)) return TokenResult.Malformed;
            byte[] sig, body;
            try { sig = FromB64Url(parts[1]); body = FromB64Url(parts[0]); } catch (FormatException) { return TokenResult.Malformed; }
            byte[] expected;
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret))) expected = h.ComputeHash(Encoding.ASCII.GetBytes(parts[0]));
            if (!ConstantEquals(sig, expected)) return TokenResult.BadSignature;
            string json = Encoding.UTF8.GetString(body);
            var sub = Sub.Match(json); var exp = Exp.Match(json);
            if (!sub.Success || !exp.Success) return TokenResult.Malformed;
            string nick = "";
            var n = Nick.Match(json);
            if (n.Success && n.Groups[2].Success) nick = Regex.Unescape(n.Groups[2].Value);
            claims = new GameTokenClaims(long.Parse(sub.Groups[1].Value), nick, long.Parse(exp.Groups[1].Value));
            return claims.ExpiresAt > nowUnixSeconds ? TokenResult.Ok : TokenResult.Expired;
        }

        // Client side: reads the claims without the secret (display only, nothing is trusted from this); the server verifies.
        public static bool Peek(string token, long nowUnixSeconds, out GameTokenClaims claims)
        {
            claims = default;
            if (string.IsNullOrEmpty(token)) return false;
            var parts = token.Split('.');
            if (parts.Length != 2) return false;
            try
            {
                string json = Encoding.UTF8.GetString(FromB64Url(parts[0]));
                var sub = Sub.Match(json); var exp = Exp.Match(json); var n = Nick.Match(json);
                if (!sub.Success || !exp.Success) return false;
                claims = new GameTokenClaims(long.Parse(sub.Groups[1].Value), n.Success && n.Groups[2].Success ? Regex.Unescape(n.Groups[2].Value) : "", long.Parse(exp.Groups[1].Value));
                return claims.ExpiresAt > nowUnixSeconds;
            }
            catch (FormatException) { return false; }
        }

        // For tests and local tools only: the same format the backend produces.
        public static string Sign(string secret, long account, string nick, long iat, int ttl = 300)
        {
            string json = "{\"sub\":" + account + ",\"nick\":" + (nick == null ? "null" : "\"" + nick.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"") + ",\"iat\":" + iat + ",\"exp\":" + (iat + ttl) + "}";
            string body = ToB64Url(Encoding.UTF8.GetBytes(json));
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret))) return body + "." + ToB64Url(h.ComputeHash(Encoding.ASCII.GetBytes(body)));
        }

        static string ToB64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        static byte[] FromB64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
        }

        static bool ConstantEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int d = 0;
            for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
            return d == 0;
        }
    }
}
