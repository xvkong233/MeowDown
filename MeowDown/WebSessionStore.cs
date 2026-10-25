using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace MeowDown;

/// <summary>已验证会话的快照信息。</summary>
internal sealed record WebSessionInfo(string Id, string UserId, long CreatedAt, long AbsoluteExpiresAt, long LastSeenAt);

/// <summary>
/// Web 会话仅存内存（重启即失效，全部用户需重新登录）。
/// 滑动有效期由 WebSettingsStore 的 sessionLifetimeHours 决定（修改对已有会话下次续期时生效），
/// 绝对上限 30 天。会话 ID 为 256 位随机数。
/// </summary>
internal sealed class WebSessionStore
{
    internal const string CookieName = "MeowDownWebSession";
    internal const int DefaultLifetimeHours = 168;
    internal const long AbsoluteLifetimeSeconds = 30L * 24 * 60 * 60;

    private sealed record Entry(string UserId, long CreatedAt, long AbsoluteExpiresAt, long LastSeenAt);

    private readonly ConcurrentDictionary<string, Entry> sessions = new(StringComparer.Ordinal);
    private readonly Func<long> nowSeconds;
    private readonly Func<int> lifetimeHours;

    public WebSessionStore(Func<int>? lifetimeHours = null, Func<long>? nowSeconds = null)
    {
        this.lifetimeHours = lifetimeHours ?? (() => DefaultLifetimeHours);
        this.nowSeconds = nowSeconds ?? (() => DateTimeOffset.Now.ToUnixTimeSeconds());
    }

    public string Create(string userId)
    {
        var now = nowSeconds();
        var id = NewSessionId();
        sessions[id] = new Entry(userId, now, now + AbsoluteLifetimeSeconds, now);
        return id;
    }

    /// <summary>校验会话并滑动续期；不存在或已过期返回 null 并清除。</summary>
    public WebSessionInfo? Validate(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) || !sessions.TryGetValue(sessionId, out var entry))
        {
            return null;
        }

        var now = nowSeconds();
        if (now >= entry.AbsoluteExpiresAt || now - entry.LastSeenAt >= lifetimeHours() * 3600L)
        {
            sessions.TryRemove(sessionId, out _);
            return null;
        }

        sessions[sessionId] = entry with { LastSeenAt = now };
        return new WebSessionInfo(sessionId, entry.UserId, entry.CreatedAt, entry.AbsoluteExpiresAt, now);
    }

    public void Revoke(string sessionId) => sessions.TryRemove(sessionId, out _);

    public void RevokeAllForUser(string userId, string? exceptSessionId = null)
    {
        foreach (var (id, entry) in sessions)
        {
            if (entry.UserId == userId && id != exceptSessionId)
            {
                sessions.TryRemove(id, out _);
            }
        }
    }

    internal static string NewSessionId()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
