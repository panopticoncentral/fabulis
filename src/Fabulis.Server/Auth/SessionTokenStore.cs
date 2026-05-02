using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Fabulis.Server.Auth;

public sealed record TokenInfo(string Token, DateTime IssuedAt);

public sealed class SessionTokenStore
{
    private readonly ConcurrentDictionary<string, TokenInfo> _tokens = new(StringComparer.Ordinal);

    public TokenInfo Issue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var info = new TokenInfo(token, DateTime.UtcNow);
        _tokens[token] = info;
        return info;
    }

    public bool IsValid(string? token)
    {
        return token is not null && _tokens.ContainsKey(token);
    }

    public void Revoke(string token) => _tokens.TryRemove(token, out _);

    public void RevokeAll() => _tokens.Clear();
}
