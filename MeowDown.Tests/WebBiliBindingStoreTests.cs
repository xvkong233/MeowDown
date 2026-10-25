using System;

namespace MeowDown.Tests;

public class WebBiliBindingStoreTests : IDisposable
{
    private readonly WebTestDirectory files = new();

    public void Dispose() => files.Dispose();

    [Fact]
    public void SaveLoadDelete_RoundTripsBindingPerUser()
    {
        var store = CreateStore();

        store.Save("u_abc123", new WebBiliBinding
        {
            Cookie = "SESSDATA=aaa; bili_jct=bbb; DedeUserID=4242; ac_time_value=rt",
            RefreshToken = "rt",
            Mid = "4242",
            Uname = "某用户",
            BoundAt = 1769347000,
        });

        Assert.True(store.Exists("u_abc123"));
        var binding = store.Load("u_abc123");
        Assert.NotNull(binding);
        Assert.Contains("SESSDATA=aaa", binding!.Cookie);
        Assert.Equal("4242", binding.Mid);
        Assert.Equal("某用户", binding.Uname);
        Assert.Equal("rt", binding.RefreshToken);
        Assert.Equal(1769347000, binding.BoundAt);

        // 用户隔离：另一用户不可见
        Assert.Null(store.Load("u_0987"));

        Assert.True(store.Delete("u_abc123"));
        Assert.Null(store.Load("u_abc123"));
        Assert.True(store.Delete("u_abc123")); // 幂等
    }

    [Fact]
    public void Load_MissingOrCorruptFileReturnsNull()
    {
        var store = CreateStore();
        Assert.Null(store.Load("u_abc123"));

        File.WriteAllText(store.PathFor("u_beef"), "{ not json");
        Assert.Null(store.Load("u_beef"));
    }

    [Theory]
    [InlineData("u_abc123", true)]
    [InlineData("u_ABC123", true)]
    [InlineData("u_", false)]
    [InlineData("u_xyz!", false)]
    [InlineData("../etc/passwd", false)]
    [InlineData("u_../../escape", false)]
    public void IsSafeUserId_GuardsPathComposition(string userId, bool safe)
    {
        Assert.Equal(safe, WebBiliBindingStore.IsSafeUserId(userId));
    }

    [Fact]
    public void PathFor_KeepsEveryUserInsideDedicatedDirectory()
    {
        var store = CreateStore();

        var path = store.PathFor("u_deadbeef");

        Assert.Equal(Path.Combine(files.Root, "bili", "u_deadbeef.json"), path);
    }

    private WebBiliBindingStore CreateStore()
        => new(files.PathFor("bili"));
}
