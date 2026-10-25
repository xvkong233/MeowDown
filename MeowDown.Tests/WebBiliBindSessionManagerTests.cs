using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MeowDown.Tests;

public class WebBiliBindSessionManagerTests : IDisposable
{
    private readonly WebTestDirectory files = new();

    public void Dispose() => files.Dispose();

    private const string SuccessBody = """
        {"code":0,"data":{"url":"https://passport.bilibili.com/h5-app/passport/login/scan?navhide=1&qrcode_key=abc&gourl=x&remote=1&DedeUserID=4242&SESSDATA=sss&bili_jct=jjj&refresh_token=rtok","refresh_token":"rtok"}}
        """;

    private const string NoCredentialBody = """
        {"code":0,"data":{"url":"https://passport.bilibili.com/h5-app/passport/login/scan?navhide=1&qrcode_key=abc&remote=1"}}
        """;

    [Fact]
    public async Task SuccessFlow_SavesPerUserBindingAndCompletesSession()
    {
        var store = CreateStore();
        var (_, session) = await RunUntilTerminalAsync(store,
            pollBodies: [QrLoginStatus.Waiting, QrLoginStatus.Scanned, QrLoginStatus.Success],
            successBody: SuccessBody,
            account: ("4242", "某用户"),
            terminalStatus: WebBindStatus.Success);

        var (status, mid, uname, message) = session.Snapshot();
        Assert.Equal(WebBindStatus.Success, status);
        Assert.Null(message);
        Assert.Equal("4242", mid);
        Assert.Equal("某用户", uname);

        var binding = store.Load("u_a11ce");
        Assert.NotNull(binding);
        Assert.Contains("SESSDATA=sss", binding!.Cookie);
        Assert.Contains("bili_jct=jjj", binding.Cookie);
        Assert.Contains("DedeUserID=4242", binding.Cookie);
        Assert.Equal("rtok", binding.RefreshToken); // refresh_token 归一为 ac_time_value
        Assert.Contains("ac_time_value=rtok", binding.Cookie);
        Assert.Equal("4242", binding.Mid);
        Assert.Equal("某用户", binding.Uname);
    }

    [Fact]
    public async Task MissingRequiredCookies_ReportsFailedAndSkipsCredentialSave()
    {
        var store = CreateStore();
        var (_, session) = await RunUntilTerminalAsync(store,
            pollBodies: [QrLoginStatus.Success],
            successBody: NoCredentialBody,
            account: ("4242", "某用户"),
            terminalStatus: WebBindStatus.Failed);

        var (status, _, _, message) = session.Snapshot();
        Assert.Equal(WebBindStatus.Failed, status);
        Assert.Contains("SESSDATA", message);
        Assert.Null(store.Load("u_a11ce"));
    }

    [Fact]
    public async Task Restart_InvalidatesPreviousBindSession()
    {
        var store = CreateStore();
        var keys = new Queue<string>(["k1", "k2"]);
        var manager = new WebBiliBindSessionManager(store,
            generateQr: () => Task.FromResult(("https://qr/" + keys.Peek(), keys.Dequeue())),
            poll: key => Task.FromResult(key == "k1"
                ? (QrLoginStatus.Expired, "{}", Array.Empty<string>())
                : (QrLoginStatus.Success, SuccessBody, Array.Empty<string>())),
            delay: InstantDelay,
            fetchAccount: cookie => Task.FromResult(("4242", "某用户")));

        var first = await manager.StartAsync("u_a11ce");
        var second = await manager.StartAsync("u_a11ce");

        Assert.NotEqual(first.BindId, second.BindId);
        Assert.Null(manager.Get(first.BindId, "u_a11ce")); // 旧 bindId 已失效
        Assert.NotNull(manager.Get(second.BindId, "u_a11ce"));
        await WaitAsync(() => second.Status == WebBindStatus.Success);
    }

    [Fact]
    public async Task Get_BindsSessionsToTheirOwner()
    {
        var store = CreateStore();
        var manager = CreateManagedTerminalFactory(store, out var start);
        var session = await start();

        Assert.NotNull(manager.Get(session.BindId, "u_a11ce"));
        Assert.Null(manager.Get(session.BindId, "u_b0b")); // 他人 bindId 不可见
    }

    [Fact]
    public async Task Get_EnforcesLocalExpiryDeadline()
    {
        var store = CreateStore();
        var clock = 1000L;
        var stopPolling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new WebBiliBindSessionManager(store,
            nowSeconds: () => clock,
            generateQr: () => Task.FromResult(("https://qr/x", "k")),
            poll: _ => stopPolling.Task.IsCompleted
                ? Task.FromResult((QrLoginStatus.Expired, "{}", Array.Empty<string>()))
                : Task.FromResult((QrLoginStatus.Waiting, "{}", Array.Empty<string>())),
            delay: _ => Task.CompletedTask,
            fetchAccount: null,
            expirySeconds: 10);

        var session = await manager.StartAsync("u_a11ce");
        clock += 11;
        Assert.Equal(WebBindStatus.Expired, manager.Get(session.BindId, "u_a11ce")!.Status);

        stopPolling.SetResult(); // 让后台轮询循环终止
    }

    [Fact]
    public async Task RenderQrPng_ReturnsPngOnlyForLiveSessions()
    {
        var store = CreateStore();
        // 轮询停在 Waiting，保证断言期间会话尚未到达终态
        var stopPolling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new WebBiliBindSessionManager(store,
            generateQr: () => Task.FromResult(("https://qr/x", "k")),
            poll: _ => stopPolling.Task.IsCompleted
                ? Task.FromResult((QrLoginStatus.Expired, "{}", Array.Empty<string>()))
                : Task.FromResult((QrLoginStatus.Waiting, "{}", Array.Empty<string>())),
            delay: InstantDelay);

        var session = await manager.StartAsync("u_a11ce");

        var png = manager.RenderQrPng(session);
        Assert.NotNull(png);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4].ToArray());

        stopPolling.SetResult(); // 结束轮询循环并进入终态
        await WaitAsync(() => session.Status == WebBindStatus.Expired);
        Assert.Null(manager.RenderQrPng(session));
    }

    [Theory]
    [InlineData(WebBindStatus.Waiting, "waiting")]
    [InlineData(WebBindStatus.Scanned, "scanned")]
    [InlineData(WebBindStatus.Success, "success")]
    [InlineData(WebBindStatus.Expired, "expired")]
    [InlineData(WebBindStatus.Failed, "failed")]
    public void ToContractStatus_MapsEveryState(WebBindStatus status, string expected)
    {
        Assert.Equal(expected, WebBiliBindSessionManager.ToContractStatus(status));
    }

    private WebBiliBindSessionManager CreateManagedTerminalFactory(WebBiliBindingStore store, out Func<Task<WebBiliBindSession>> start)
    {
        var manager = new WebBiliBindSessionManager(store,
            generateQr: () => Task.FromResult(("https://qr/x", "k" + Guid.NewGuid().ToString("N"))),
            poll: _ => Task.FromResult((QrLoginStatus.Expired, "{}", Array.Empty<string>())),
            delay: InstantDelay);
        start = () => manager.StartAsync("u_a11ce");
        return manager;
    }

    /// <summary>按注入队列驱动后台轮询循环，直至会话到达期望终态。</summary>
    private async Task<(WebBiliBindSessionManager Manager, WebBiliBindSession Session)> RunUntilTerminalAsync(
        WebBiliBindingStore store, QrLoginStatus[] pollBodies, string successBody,
        (string Mid, string Uname) account, WebBindStatus terminalStatus)
    {
        var queue = new Queue<QrLoginStatus>(pollBodies);
        var manager = new WebBiliBindSessionManager(store,
            generateQr: () => Task.FromResult(("https://qr/x", "k")),
            poll: _ =>
            {
                var status = queue.Count > 0 ? queue.Dequeue() : QrLoginStatus.Expired;
                return Task.FromResult((status, status == QrLoginStatus.Success ? successBody : "{}",
                    Array.Empty<string>()));
            },
            delay: InstantDelay,
            fetchAccount: cookie => Task.FromResult(account));

        var session = await manager.StartAsync("u_a11ce");
        await WaitAsync(() => session.Status == terminalStatus);
        return (manager, session);
    }

    private static Task InstantDelay(TimeSpan _) => Task.CompletedTask;

    private static async Task WaitAsync(Func<bool> condition, int timeoutMs = 10000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
            {
                throw new TimeoutException("绑定会话未在超时内到达期望状态");
            }
            await Task.Delay(10);
        }
    }

    private WebBiliBindingStore CreateStore() => new(files.PathFor("bili"));
}
