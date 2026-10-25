using System;

namespace MeowDown.Tests;

public class WebPasswordHasherTests
{
    [Fact]
    public void Hash_ProducesContractFormatWithConfiguredSizes()
    {
        var stored = WebPasswordHasher.Hash("s3cret-pw", iterations: 1000);

        var parts = stored.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal("1000", parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length); // 16 字节盐
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length); // 32 字节派生密钥
    }

    [Fact]
    public void Hash_UsesDefaultIterationsOf600k()
    {
        var stored = WebPasswordHasher.Hash("s3cret-pw");

        Assert.Contains("$600000$", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HashAndVerify_SupportsDefaultIterationsEndToEnd()
    {
        var stored = await Task.Run(() => WebPasswordHasher.Hash("口令-测试"));

        Assert.True(WebPasswordHasher.Verify("口令-测试", stored));
        Assert.False(WebPasswordHasher.Verify("口令-错误", stored));
    }

    [Fact]
    public void Verify_VerifiesAgainstStoredIterations()
    {
        var stored = WebPasswordHasher.Hash("s3cret-pw", iterations: 1000);

        Assert.True(WebPasswordHasher.Verify("s3cret-pw", stored));
        Assert.False(WebPasswordHasher.Verify("wrong-pw", stored));
        Assert.False(WebPasswordHasher.Verify("s3cret-pw", null));
        Assert.False(WebPasswordHasher.Verify("s3cret-pw", ""));
        Assert.False(WebPasswordHasher.Verify("s3cret-pw", "argon2id$1$aa$bb"));
        Assert.False(WebPasswordHasher.Verify("s3cret-pw", "pbkdf2-sha256$not-a-number$aa$bb"));
    }

    [Fact]
    public void Verify_RejectsTamperedHash()
    {
        var stored = WebPasswordHasher.Hash("s3cret-pw", iterations: 1000);
        var parts = stored.Split('$');
        var tampered = $"{parts[0]}${parts[1]}${parts[2]}${Convert.ToBase64String(new byte[32])}";

        Assert.False(WebPasswordHasher.Verify("s3cret-pw", tampered));
    }

    [Theory]
    [InlineData("pbkdf2-sha256$1000$AAAA$BBBB", true)]
    [InlineData("pbkdf2-sha256$600000$AAAA$BBBB", false)]
    [InlineData("garbage", false)]
    [InlineData(null, false)]
    public void NeedsUpgrade_DetectsOlderIterationCounts(string? stored, bool expected)
    {
        Assert.Equal(expected, WebPasswordHasher.NeedsUpgrade(stored));
    }

    [Fact]
    public void Hash_IsSaltedPerCall()
    {
        Assert.NotEqual(WebPasswordHasher.Hash("same", iterations: 1000),
            WebPasswordHasher.Hash("same", iterations: 1000));
    }
}
