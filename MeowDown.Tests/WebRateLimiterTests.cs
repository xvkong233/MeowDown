using System;

namespace MeowDown.Tests;

public class WebRateLimiterTests
{
    [Fact]
    public void LoginLocksAfterFiveFailuresWithinWindow()
    {
        var clock = new FakeClock(1000);
        var limiter = new WebLoginRateLimiter(() => clock.NowSeconds);

        for (var i = 0; i < WebLoginRateLimiter.MaxFailures - 1; i++)
        {
            limiter.RecordFailure("1.2.3.4", "alice");
            Assert.False(limiter.IsLocked("1.2.3.4", "alice"));
        }

        limiter.RecordFailure("1.2.3.4", "alice");
        Assert.True(limiter.IsLocked("1.2.3.4", "alice"));
    }

    [Fact]
    public void LockIsScopedToIpPlusUsername()
    {
        var clock = new FakeClock(1000);
        var limiter = new WebLoginRateLimiter(() => clock.NowSeconds);
        LockAccount(limiter);

        Assert.True(limiter.IsLocked("1.2.3.4", "ALICE")); // 用户名大小写不敏感计入同一组合
        Assert.False(limiter.IsLocked("1.2.3.4", "bob"));
        Assert.False(limiter.IsLocked("5.6.7.8", "alice"));
    }

    [Fact]
    public void LockExpiresAfterFifteenMinutes()
    {
        var clock = new FakeClock(1000);
        var limiter = new WebLoginRateLimiter(() => clock.NowSeconds);
        LockAccount(limiter);
        Assert.True(limiter.IsLocked("1.2.3.4", "alice"));

        clock.NowSeconds += WebLoginRateLimiter.WindowSeconds;
        Assert.False(limiter.IsLocked("1.2.3.4", "alice"));
    }

    [Fact]
    public void ResetOnSuccessfulLoginClearsFailures()
    {
        var clock = new FakeClock(1000);
        var limiter = new WebLoginRateLimiter(() => clock.NowSeconds);
        for (var i = 0; i < WebLoginRateLimiter.MaxFailures - 1; i++)
        {
            limiter.RecordFailure("1.2.3.4", "alice");
        }

        limiter.Reset("1.2.3.4", "alice");
        for (var i = 0; i < WebLoginRateLimiter.MaxFailures - 1; i++)
        {
            limiter.RecordFailure("1.2.3.4", "alice");
        }

        Assert.False(limiter.IsLocked("1.2.3.4", "alice"));
    }

    [Fact]
    public void FailureCounterRestartsWhenWindowElapses()
    {
        var clock = new FakeClock(1000);
        var limiter = new WebLoginRateLimiter(() => clock.NowSeconds);
        for (var i = 0; i < WebLoginRateLimiter.MaxFailures - 1; i++)
        {
            limiter.RecordFailure("1.2.3.4", "alice");
        }

        clock.NowSeconds += WebLoginRateLimiter.WindowSeconds + 1;
        for (var i = 0; i < WebLoginRateLimiter.MaxFailures - 1; i++)
        {
            limiter.RecordFailure("1.2.3.4", "alice");
        }

        Assert.False(limiter.IsLocked("1.2.3.4", "alice"));
    }

    [Fact]
    public void RegisterAllowsFivePerIpPerHour()
    {
        var clock = new FakeClock(1000);
        var limiter = new WebRegisterRateLimiter(() => clock.NowSeconds);

        for (var i = 0; i < WebRegisterRateLimiter.MaxPerHour; i++)
        {
            Assert.True(limiter.TryAcquire("1.2.3.4"));
        }
        Assert.False(limiter.TryAcquire("1.2.3.4"));
        Assert.True(limiter.TryAcquire("5.6.7.8")); // 其他 IP 不受影响
    }

    [Fact]
    public void RegisterWindowRestartsAfterOneHour()
    {
        var clock = new FakeClock(1000);
        var limiter = new WebRegisterRateLimiter(() => clock.NowSeconds);
        for (var i = 0; i < WebRegisterRateLimiter.MaxPerHour; i++)
        {
            limiter.TryAcquire("1.2.3.4");
        }
        Assert.False(limiter.TryAcquire("1.2.3.4"));

        clock.NowSeconds += 3601;
        Assert.True(limiter.TryAcquire("1.2.3.4"));
    }

    private static void LockAccount(WebLoginRateLimiter limiter)
    {
        for (var i = 0; i < WebLoginRateLimiter.MaxFailures; i++)
        {
            limiter.RecordFailure("1.2.3.4", "alice");
        }
    }

    private sealed class FakeClock(long nowSeconds)
    {
        public long NowSeconds { get; set; } = nowSeconds;
    }
}
