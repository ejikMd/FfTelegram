using System;
using System.Collections.Concurrent;

/// <summary>
/// Simple sliding-window rate limiter keyed by Telegram chat ID.
/// Prevents a single user from exhausting downstream API quotas.
/// </summary>
public sealed class UserRateLimiter
{
    private readonly ConcurrentDictionary<long, DateTime> _lastSeen = new();
    private readonly TimeSpan _cooldown;

    /// <param name="cooldown">Minimum time between allowed requests per user.</param>
    public UserRateLimiter(TimeSpan cooldown)
    {
        _cooldown = cooldown;
    }

    /// <summary>
    /// Returns <c>true</c> if the user is allowed to proceed, <c>false</c> if they should be throttled.
    /// </summary>
    public bool TryConsume(long chatId)
    {
        var now = DateTime.UtcNow;

        // AddOrUpdate is atomic: safe for concurrent access without extra locks.
        var allowed = false;
        _lastSeen.AddOrUpdate(
            chatId,
            // First-time user — always allow.
            _ =>
            {
                allowed = true;
                return now;
            },
            // Returning user — allow only if cooldown has elapsed.
            (_, last) =>
            {
                if (now - last >= _cooldown)
                {
                    allowed = true;
                    return now;
                }
                return last; // Keep the existing timestamp; request is denied.
            });

        return allowed;
    }
}
