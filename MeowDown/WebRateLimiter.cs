using System;
using System.Collections.Concurrent;
using System.Threading;

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
    private long nextSweepAt;

    public WebLoginRateLimiter(Func<long>? nowSeconds = null)
    {
        this.nowSeconds = nowSeconds ?? (() => DateTimeOffset.Now.ToUnixTimeSeconds());
    }

    private static (string, string) Key(string ip, string username) => (ip, username.ToLowerInvariant());

    public bool IsLocked(string ip, string username)
    {
        var now = nowSeconds();
        SweepIfDue(now);
        if (!states.TryGetValue(Key(ip, username), out var state))
        {
            return false;
        }

        if (state.LockedUntil > now)
        {
            return true;
        }

        // 过期的锁定与滑出统计窗口的失败计数条目直接移除，避免匿名请求造成字典无界增长
        if (state.LockedUntil != 0 || now - state.WindowStart > WindowSeconds)
        {
            states.TryRemove(Key(ip, username), out _);
        }

        return false;
    }

    public void RecordFailure(string ip, string username)
    {
        var now = nowSeconds();
        SweepIfDue(now);
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

    /// <summary>
    /// 每过一个统计窗口做一次全量清扫，清掉已过期或滑出窗口的条目——
    /// 否则未达锁定阈值的随机「IP+用户名」条目永不淘汰，可被匿名请求撑到无界。
    /// </summary>
    private void SweepIfDue(long now)
    {
        var due = Interlocked.Read(ref nextSweepAt);
        if (now < due)
        {
            return;
        }
        if (Interlocked.CompareExchange(ref nextSweepAt, now + WindowSeconds, due) != due)
        {
            return;
        }
        foreach (var (key, state) in states)
        {
            if (state.LockedUntil != 0 ? state.LockedUntil <= now : now - state.WindowStart > WindowSeconds)
            {
                states.TryRemove(key, out _);
            }
        }
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
    private long nextSweepAt;

    public WebRegisterRateLimiter(Func<long>? nowSeconds = null)
    {
        this.nowSeconds = nowSeconds ?? (() => DateTimeOffset.Now.ToUnixTimeSeconds());
    }

    /// <summary>尝试占用一次注册额度；超出限额返回 false。</summary>
    public bool TryAcquire(string ip)
    {
        var now = nowSeconds();
        SweepIfDue(now);
        var state = states.AddOrUpdate(ip,
            _ => new State(1, now),
            (_, existing) => now - existing.WindowStart >= WindowSeconds
                ? new State(1, now)
                : existing with { Count = existing.Count + 1 });
        return state.Count <= MaxPerHour;
    }

    /// <summary>每过一个窗口做一次全量清扫，清掉已滑出窗口的条目，避免匿名请求把字典撑到无界。</summary>
    private void SweepIfDue(long now)
    {
        var due = Interlocked.Read(ref nextSweepAt);
        if (now < due)
        {
            return;
        }
        if (Interlocked.CompareExchange(ref nextSweepAt, now + WindowSeconds, due) != due)
        {
            return;
        }
        foreach (var (ip, state) in states)
        {
            if (now - state.WindowStart >= WindowSeconds)
            {
                states.TryRemove(ip, out _);
            }
        }
    }
}
