using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Fabulis.Server.Data;

/// <summary>
/// Holds short-lived, one-shot tokens that authorize a single GET to
/// /narration/play/{token}. Used because AVPlayer on iOS can only fetch
/// audio via HTTP GET (no POST, no custom request bodies), so we hand
/// off the validated synthesis params from a session-authed POST to a
/// token-authed GET that the player can consume.
/// </summary>
public sealed class NarrationTokenStore
{
    public sealed record Params(string Text, string Voice, double Speed);

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    private sealed record Entry(Params Params, DateTimeOffset ExpiresAt);

    /// <summary>Creates a one-shot token good for ~5 minutes.</summary>
    public string Create(Params parameters)
    {
        SweepExpired();
        var token = GenerateToken();
        _entries[token] = new Entry(parameters, DateTimeOffset.UtcNow + Ttl);
        return token;
    }

    /// <summary>
    /// Returns the params for a token without removing it. Returns null if
    /// the token doesn't exist or has expired. Tokens are reusable within
    /// the TTL window — AVPlayer often sends a HEAD probe before the GET,
    /// or replays a GET after a brief pause, and one-shot semantics would
    /// 404 on the second request.
    /// </summary>
    public Params? Lookup(string token)
    {
        if (!_entries.TryGetValue(token, out var entry)) return null;
        if (DateTimeOffset.UtcNow >= entry.ExpiresAt)
        {
            _entries.TryRemove(token, out _);
            return null;
        }
        return entry.Params;
    }

    private void SweepExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _entries)
        {
            if (now >= kvp.Value.ExpiresAt)
                _entries.TryRemove(kvp.Key, out _);
        }
    }

    private static string GenerateToken()
    {
        // 22-char URL-safe base64 of 128 random bits.
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
