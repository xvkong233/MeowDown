using System;
using System.Collections.Generic;

namespace MeowDown.Tests;

public class WebSessionStoreTests
{
    [Fact]
    public void CreateAndValidate_RoundTripsUserId()
    {
        var clock = new FakeClock(1000);
        var store = CreateStore(lifetimeHours: 168, clock);

        var sessionId = store.Create("u_alice");
        var session = store.Validate(sessionId);

        Assert.Equal("u_alice", session?.UserId);
        Assert.Equal(sessionId, session?.Id);
    }

    [Fact]
    public void Validate_SlidingExpiryUsesSessionLifetimeHours()
    {
        var clock = new FakeClock(1000);
        var store = CreateStore(lifetimeHours: 1, clock);
        var sessionId = store.Create("u_alice");

        clock.NowSeconds = 1000 + 3599;
        Assert.NotNull(store.Validate(sessionId));

        // 距上次续期超过滑动窗口后失效（28 行那次校验已把窗口刷新到 3599）
        clock.NowSeconds = 1000 + 3599 + 3601;
        Assert.Null(store.Validate(sessionId));
        Assert.Null(store.Validate(sessionId)); // 过期后清除
    }

    [Fact]
    public void Validate_SlidingWindowIsRefreshedByEachValidation()
    {
        var clock = new FakeClock(1000);
        var store = CreateStore(lifetimeHours: 1, clock);
        var sessionId = store.Create("u_alice");

        clock.NowSeconds += 1800;
        Assert.NotNull(store.Validate(sessionId));
        clock.NowSeconds += 1800;
        Assert.NotNull(store.Validate(sessionId)); // 距上次续期恰好未超 1 小时
    }

    [Fact]
    public void Validate_AbsoluteCapOf30DaysExpiresEvenWithContinuousActivity()
    {
        var clock = new FakeClock(0);
        var store = CreateStore(lifetimeHours: 720, clock); // 720h = 30d 滑动 == 绝对上限
        var sessionId = store.Create("u_alice");

        clock.NowSeconds = WebSessionStore.AbsoluteLifetimeSeconds - 60;
        Assert.NotNull(store.Validate(sessionId));

        clock.NowSeconds = WebSessionStore.AbsoluteLifetimeSeconds + 1;
        Assert.Null(store.Validate(sessionId));
    }

    [Fact]
    public void LifetimeChangeAppliesOnNextRenewalOfExistingSessions()
    {
        var clock = new FakeClock(1000);
        var lifetimeHours = 1;
        var store = CreateStore(() => lifetimeHours, clock);
        var sessionId = store.Create("u_alice");

        lifetimeHours = 5;
        clock.NowSeconds = 1000 + 2 * 3600;

        Assert.NotNull(store.Validate(sessionId));
    }

    [Fact]
    public void Revoke_And_RevokeAllForUserWithException()
    {
        var clock = new FakeClock(1000);
        var store = CreateStore(lifetimeHours: 168, clock);
        var first = store.Create("u_alice");
        var second = store.Create("u_alice");
        var other = store.Create("u_bob");

        store.RevokeAllForUser("u_alice", exceptSessionId: first);

        Assert.Null(store.Validate(second));
        Assert.NotNull(store.Validate(first));
        Assert.NotNull(store.Validate(other));

        store.Revoke(first);
        Assert.Null(store.Validate(first));
    }

    [Fact]
    public void Validate_UnknownOrEmptySessionReturnsNull()
    {
        var store = CreateStore(lifetimeHours: 168, new FakeClock(1000));

        Assert.Null(store.Validate(null));
        Assert.Null(store.Validate(""));
        Assert.Null(store.Validate("missing"));
    }

    private static WebSessionStore CreateStore(int lifetimeHours, FakeClock clock)
        => CreateStore(() => lifetimeHours, clock);

    private static WebSessionStore CreateStore(Func<int> lifetimeHours, FakeClock clock)
        => new(lifetimeHours, () => clock.NowSeconds);

    private sealed class FakeClock(long nowSeconds)
    {
        public long NowSeconds { get; set; } = nowSeconds;
    }
}
