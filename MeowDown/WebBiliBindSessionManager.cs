using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MeowDown.Core.Util;
using QRCoder;
using static MeowDown.MeowDownLoginUtil;

namespace MeowDown;

public enum WebBindStatus { Waiting, Scanned, Success, Expired, Failed }

/// <summary>一次扫码绑定会话（仅存内存，随进程失效；同一用户再次 start 后旧会话被移除）。</summary>
internal sealed class WebBiliBindSession
{
    private readonly object stateLock = new();
    private WebBindStatus status = WebBindStatus.Waiting;
    private string? mid;
    private string? uname;
    private string? message;

    public required string BindId { get; init; }
    public required string UserId { get; init; }
    public required string QrUrl { get; init; }
    public required string QrcodeKey { get; init; }
    public required long ExpiresAt { get; init; }

    /// <summary>最近一次上游 poll 的原始响应，成功终态时用于提取凭据。</summary>
    public string? LastResponseBody;
    public string[]? LastSetCookies;

    public WebBindStatus Status { get { lock (stateLock) return status; } }

    public void SetScanned()
    {
        lock (stateLock)
        {
            if (status == WebBindStatus.Waiting) status = WebBindStatus.Scanned;
        }
    }

    public void Complete(string accountMid, string accountUname)
    {
        lock (stateLock)
        {
            status = WebBindStatus.Success;
            mid = accountMid;
            uname = accountUname;
        }
    }

    public void End(WebBindStatus finalStatus, string? reason = null)
    {
        lock (stateLock)
        {
            if (status is WebBindStatus.Success or WebBindStatus.Expired or WebBindStatus.Failed) return;
            status = finalStatus;
            message = reason;
        }
    }

    public (WebBindStatus Status, string? Mid, string? Uname, string? Message) Snapshot()
    {
        lock (stateLock) return (status, mid, uname, message);
    }
}

/// <summary>
/// B站扫码绑定会话管理：每个绑定会话由独立后台循环按 1 秒节流轮询上游
/// （复用 WaitForQrLoginAsync 的节奏），多个前端并发查询共享同一次上游结果。
/// 绑定成功后凭据经 NormalizeLoginCookie 归一并按用户落盘（缺少 SESSDATA/bili_jct 则记 failed）。
/// </summary>
internal sealed class WebBiliBindSessionManager
{
    private const int DefaultExpirySeconds = 180;

    private readonly ConcurrentDictionary<string, WebBiliBindSession> sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> activeBindByUser = new(StringComparer.Ordinal);
    private readonly WebBiliBindingStore bindingStore;
    private readonly Func<long> nowSeconds;
    private readonly Func<string> newBindId;
    private readonly Func<Task<(string Url, string QrcodeKey)>> generateQr;
    private readonly Func<string, Task<(QrLoginStatus Status, string Body, string[] SetCookies)>> poll;
    private readonly Func<TimeSpan, Task> delay;
    private readonly Func<string, Task<(string Mid, string Uname)>>? fetchAccount;
    private readonly int expirySeconds;

    public WebBiliBindSessionManager(WebBiliBindingStore bindingStore,
        Func<long>? nowSeconds = null,
        Func<string>? newBindId = null,
        Func<Task<(string Url, string QrcodeKey)>>? generateQr = null,
        Func<string, Task<(QrLoginStatus Status, string Body, string[] SetCookies)>>? poll = null,
        Func<TimeSpan, Task>? delay = null,
        Func<string, Task<(string Mid, string Uname)>>? fetchAccount = null,
        int expirySeconds = DefaultExpirySeconds)
    {
        this.bindingStore = bindingStore;
        this.nowSeconds = nowSeconds ?? (() => DateTimeOffset.Now.ToUnixTimeSeconds());
        this.newBindId = newBindId ?? NewBindId;
        this.generateQr = generateQr ?? MeowDownLoginUtil.GenerateWebQrLoginAsync;
        this.poll = poll ?? DefaultPollAsync;
        this.delay = delay ?? Task.Delay;
        this.fetchAccount = fetchAccount ?? FetchAccountInfoAsync;
        this.expirySeconds = expirySeconds;
    }

    /// <summary>发起扫码绑定；同一用户的上一个绑定会话立即失效（移除，其后查询按 404 处理）。</summary>
    public async Task<WebBiliBindSession> StartAsync(string userId)
    {
        if (activeBindByUser.TryRemove(userId, out var previousId))
        {
            sessions.TryRemove(previousId, out _);
        }

        var (url, qrcodeKey) = await generateQr();
        var session = new WebBiliBindSession
        {
            BindId = newBindId(),
            UserId = userId,
            QrUrl = url,
            QrcodeKey = qrcodeKey,
            ExpiresAt = nowSeconds() + expirySeconds,
        };
        sessions[session.BindId] = session;
        activeBindByUser[userId] = session.BindId;
        _ = Task.Run(() => RunPollLoopAsync(session));
        return session;
    }

    /// <summary>按绑定会话归属查询；不存在、已结束或属于其他用户返回 null。</summary>
    public WebBiliBindSession? Get(string bindId, string userId)
    {
        if (!sessions.TryGetValue(bindId, out var session)
            || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            return null;
        }

        if (nowSeconds() >= session.ExpiresAt)
        {
            session.End(WebBindStatus.Expired);
        }

        return session;
    }

    /// <summary>二维码 PNG（白底黑码，8px/模块）；仅未结束的会话可出图。</summary>
    public byte[]? RenderQrPng(WebBiliBindSession session)
    {
        if (session.Status is not (WebBindStatus.Waiting or WebBindStatus.Scanned))
        {
            return null;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(session.QrUrl, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(8);
    }

    internal static string ToContractStatus(WebBindStatus status) => status switch
    {
        WebBindStatus.Waiting => "waiting",
        WebBindStatus.Scanned => "scanned",
        WebBindStatus.Success => "success",
        WebBindStatus.Expired => "expired",
        _ => "failed",
    };

    internal static string NewBindId()
        => "b_" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..16].ToLowerInvariant();

    private async Task RunPollLoopAsync(WebBiliBindSession session)
    {
        try
        {
            var status = await WaitForQrLoginAsync(
                async () =>
                {
                    if (!sessions.ContainsKey(session.BindId)) return QrLoginStatus.Expired;
                    var (pollStatus, body, setCookies) = await poll(session.QrcodeKey);
                    lock (session)
                    {
                        session.LastResponseBody = body;
                        session.LastSetCookies = setCookies;
                    }
                    return pollStatus;
                },
                delay, TimeSpan.FromSeconds(1), session.SetScanned);
            if (!sessions.ContainsKey(session.BindId)) return;
            if (status == QrLoginStatus.Success)
            {
                await CompleteAsync(session);
            }
            else
            {
                session.End(WebBindStatus.Expired);
            }
        }
        catch (Exception e)
        {
            session.End(WebBindStatus.Failed, ErrorText.Describe(e));
        }
    }

    private async Task CompleteAsync(WebBiliBindSession session)
    {
        string body;
        string[] setCookies;
        lock (session)
        {
            body = session.LastResponseBody ?? "";
            setCookies = session.LastSetCookies ?? [];
        }

        using var loginDoc = JsonDocument.Parse(body);
        var loginData = BilibiliApi.ReadPayload(loginDoc.RootElement, "完成登录");
        string cc = loginData.GetProperty("url").ToString();
        string? refreshToken = loginData.TryGetProperty("refresh_token", out var refreshTokenElement)
            ? refreshTokenElement.GetString() : null;
        var cookie = MeowDownCookieRefreshUtil.NormalizeLoginCookie(cc, refreshToken, setCookies);
        if (!MeowDownCookieRefreshUtil.HasRequiredLoginCookies(cookie))
        {
            session.End(WebBindStatus.Failed, "登录响应缺少SESSDATA或bili_jct，未保存绑定");
            return;
        }

        var mid = ExtractCookieValue(cookie, "DedeUserID") ?? "";
        var uname = "";
        if (fetchAccount is not null)
        {
            try
            {
                var (accountMid, accountUname) = await fetchAccount(cookie);
                if (!string.IsNullOrEmpty(accountMid)) mid = accountMid;
                uname = accountUname;
            }
            catch { /* 账号摘要获取失败不影响绑定本身 */ }
        }

        bindingStore.Save(session.UserId, new WebBiliBinding
        {
            Cookie = cookie,
            RefreshToken = refreshToken,
            Mid = mid,
            Uname = uname,
            BoundAt = nowSeconds(),
        });
        session.Complete(mid, uname);
    }

    private static async Task<(QrLoginStatus Status, string Body, string[] SetCookies)> DefaultPollAsync(string qrcodeKey)
    {
        var result = await MeowDownLoginUtil.GetLoginStatusAsync(qrcodeKey);
        return (MeowDownLoginUtil.ReadWebQrStatus(result.ResponseBody), result.ResponseBody, result.SetCookieHeaders);
    }

    private static async Task<(string Mid, string Uname)> FetchAccountInfoAsync(string cookie)
    {
        const string url = "https://api.bilibili.com/x/web-interface/nav";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        HTTPUtil.ApplyWebRequestHeaders(request, url, sendCookie: false, forceAuthenticatedProfile: true);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        using var response = await HTTPUtil.AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = BilibiliApi.ReadPayload(document.RootElement, "获取B站账号信息");
        return (data.GetProperty("mid").ToString(), data.GetProperty("uname").ToString());
    }

    private static string? ExtractCookieValue(string cookieHeader, string name)
        => cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim())
            .FirstOrDefault();
}
