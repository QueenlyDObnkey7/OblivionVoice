using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OblivionVoice.Server;

public sealed class VoiceSessionTokenService(VoiceConfigLoader configLoader)
{
    public sealed record Session(string Token, string PlayerId, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    public Session Issue(object playerId)
    {
        CleanupExpired();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var session = new Session(
            token,
            playerId.ToString() ?? "unknown",
            DateTimeOffset.UtcNow.AddSeconds(configLoader.Current.Network.SessionTokenTtlSeconds));
        _sessions[token] = session;
        return session;
    }

    public bool TryConsume(string token, out Session? session)
    {
        CleanupExpired();
        if (_sessions.TryRemove(token, out var found) && found.ExpiresAt > DateTimeOffset.UtcNow)
        {
            session = found;
            return true;
        }
        session = null;
        return false;
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _sessions)
            if (pair.Value.ExpiresAt <= now)
                _sessions.TryRemove(pair.Key, out _);
    }
}
