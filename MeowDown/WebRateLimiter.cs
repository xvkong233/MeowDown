using System;
using System.Collections.Concurrent;

namespace MeowDown;

/// <summary>
/// 登录暴力破解防护：按「IP+用户名」计数，15 分钟窗口内失败满 5 次锁定该组合 15 分钟。
/// </summary>
internal sealed class WebLoginRateLimiter
{
    internal const int MaxFailures = 5;
    internal const long WindowSeconds = 15 * 60;

    private sealed record State(int Failures, long WindowStart, long LockedUntil);

    private readonly ConcurrentDictionary<(string Ip, string Username), State> states = new();
    private readonly Func<long> nowSeconds;

    public WebLoginRateLimiter(Func<long>? nowSeconds = null)
    {
        this.nowSeconds = nowSeconds ?? (() => DateTimeOffset.Now.ToUnixTimeSeconds());
    }

    private static (string, string) Key(string ip, string username) => (ip, username.ToLowerInvariant());

    public bool IsLocked(string ip, string username)
    {
        var now = nowSeconds();
        if (!states.TryGetValue(Key(ip, username), out var state))
        {
            return false;
        }

        if (state.LockedUntil > now)
        {
            return true;
        }

        if (state.LockedUntil != 0)
        {
            states.TryRemove(Key(ip, username), out _);
        }

        return false;
    }

    public void RecordFailure(string ip, string username)
    {
        var now = nowSeconds();
        states.AddOrUpdate(Key(ip, username),
            _ => new State(1, now, 0),
            (_, state) =>
            {
                var windowStart = now - state.WindowStart <= WindowSeconds ? state.WindowStart : now;
                var failures = windowStart == state.WindowStart ? state.Failures + 1 : 1;
                return failures >= MaxFailures
                    ? new State(failures, windowStart, now + WindowSeconds)
                    : new State(failures, windowStart, 0);
            });
    }

    public void Reset(string ip, string username)
    {
        states.TryRemove(Key(ip, username), out _);
    }
}

/// <summary>注册限流：每 IP 每小时最多 5 次。</summary>
internal sealed class WebRegisterRateLimiter
{
    internal const int MaxPerHour = 5;
    private const long WindowSeconds = 60 * 60;

    private sealed record State(int Count, long WindowStart);

    private readonly ConcurrentDictionary<string, State> states = new(StringComparer.Ordinal);
    private readonly Func<long> nowSeconds;

    public WebRegisterRateLimiter(Func<long>? nowSeconds = null)
    {
        this.nowSeconds = nowSeconds ?? (() => DateTimeOffset.Now.ToUnixTimeSeconds());
    }

    /// <summary>尝试占用一次注册额度；超出限额返回 false。</summary>
    public bool TryAcquire(string ip)
    {
        var now = nowSeconds();
        var state = states.AddOrUpdate(ip,
            _ => new State(1, now),
            (_, existing) => now - existing.WindowStart >= WindowSeconds
                ? new State(1, now)
                : existing with { Count = existing.Count + 1 });
        return state.Count <= MaxPerHour;
    }
}
