using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeowDown.Core.Entity;
using MeowDown.Core.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace MeowDown;

/// <summary>Web 服务的内存态组件集合（会话/绑定会话随进程失效）。</summary>
internal sealed class WebServices
{
    public required WebLaunchOptions Launch { get; init; }
    public required string WebDataDir { get; init; }
    public required WebUserStore Users { get; init; }
    public required WebSessionStore Sessions { get; init; }
    public required WebSettingsStore Settings { get; init; }
    public required WebBiliBindingStore Bindings { get; init; }
    public required WebBiliBindSessionManager BindSessions { get; init; }
    public required WebLoginRateLimiter LoginLimiter { get; init; }
    public required WebRegisterRateLimiter RegisterLimiter { get; init; }

    /// <summary>
    /// B站凭据经由 Config.COOKIE 这一全局静态生效（Program.SetUpWork），
    /// 解析与下载共用此闸门串行化，避免并发任务互相覆盖凭据。
    /// </summary>
    public SemaphoreSlim CredentialGate { get; } = new(1, 1);

    /// <summary>每用户在途解析标记（单用户并发 1）。</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, byte> ParseInFlight { get; } = new();

    /// <summary>运行期由服务器写入，仅用于管理员只读配置展示。</summary>
    public string ListenUrl { get; set; } = "";

    /// <summary>首次启动用户库为空时引导 admin 账号，初始密码仅打印一次。</summary>
    public void BootstrapAdminIfNeeded()
    {
        if (!Users.IsEmpty)
        {
            return;
        }

        var (user, password) = Users.BootstrapAdmin(Launch.WebAdminPassword);
        Console.WriteLine($"已创建Web管理员账号: {user.Username} 初始密码: {password} （仅打印一次，请立即登录并修改密码）");
    }
}

/// <summary>/api/* 多用户 Web 服务端点：认证、B站扫码绑定、在线解析、任务、管理员。</summary>
internal static class WebApiEndpoints
{
    internal const string UserItemKey = "MeowDownWebUser";
    internal const string SessionIdItemKey = "MeowDownWebSessionId";
    private static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(30);

    internal const string UnauthenticatedCode = "unauthenticated";

    #region 中间件

    /// <summary>
    /// /api/* 会话中间件：解析 MeowDownWebSession Cookie、吊销停用用户的会话、
    /// 校验管理员角色，并作为 CSRF 防线强制变更类请求携带 application/json。
    /// </summary>
    public static void UseWebAuth(WebApplication app, WebServices web)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            if (IsMutating(context.Request.Method) && !IsApplicationJson(context.Request.ContentType))
            {
                await WriteError(context, StatusCodes.Status415UnsupportedMediaType,
                    "unsupported_media_type", "变更类请求必须携带 Content-Type: application/json");
                return;
            }

            var user = ResolveUser(context, web, out var sessionId);
            var path = context.Request.Path;
            if (user is null && !IsAnonymousPath(path))
            {
                await WriteError(context, StatusCodes.Status401Unauthorized, UnauthenticatedCode, "未登录或会话已过期");
                return;
            }

            if (user is not null && !IsAnonymousPath(path))
            {
                // 滑动续期的浏览器侧半边：认证通过后按当前会话有效期重发 Cookie，
                // 避免活跃用户在登录时固化的 Max-Age 到期后被强制登出（契约：每次认证请求刷新）。
                var lifetimeSeconds = web.Settings.Snapshot().SessionLifetimeHours * 3600L;
                context.Response.Cookies.Append(WebSessionStore.CookieName, sessionId!,
                    SessionCookieOptions(context, web.Launch, lifetimeSeconds));
            }

            if (user is not null && path.StartsWithSegments("/api/admin") && user.Role != WebUserRole.Admin)
            {
                await WriteError(context, StatusCodes.Status403Forbidden, "forbidden", "需要管理员角色");
                return;
            }

            context.Items[UserItemKey] = user;
            context.Items[SessionIdItemKey] = sessionId;
            await next();
        });
    }

    private static WebUser? ResolveUser(HttpContext context, WebServices web, out string? sessionId)
    {
        sessionId = null;
        if (!context.Request.Cookies.TryGetValue(WebSessionStore.CookieName, out var value)
            || string.IsNullOrEmpty(value))
        {
            return null;
        }

        var session = web.Sessions.Validate(value);
        if (session is null)
        {
            return null;
        }

        var user = web.Users.FindById(session.UserId);
        if (user is { Disabled: false })
        {
            sessionId = session.Id;
            return user;
        }

        // 停用用户立即失效其会话
        web.Sessions.Revoke(session.Id);
        return null;
    }

    private static bool IsMutating(string method)
        => HttpMethods.IsPost(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static bool IsApplicationJson(string? contentType)
        => !string.IsNullOrEmpty(contentType)
            && contentType.AsSpan().Trim().StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnonymousPath(PathString path)
        => path == "/api/auth/login" || path == "/api/auth/register" || path == "/api/auth/logout";

    #endregion

    #region 路由

    public static void Map(WebApplication app, MeowDownApiServer server, WebServices web)
    {
        var auth = app.MapGroup("/api/auth");
        auth.MapPost("/register", (Delegate)(async (HttpContext context) => await RegisterAsync(context, web)));
        auth.MapPost("/login", (Delegate)(async (HttpContext context) => await LoginAsync(context, web)));
        auth.MapPost("/logout", (Delegate)(async (HttpContext context) => await LogoutAsync(context, web)));
        auth.MapGet("/me", (Delegate)(async (HttpContext context) => await CurrentUserAsync(context, web)));
        auth.MapPost("/password", (Delegate)(async (HttpContext context) => await ChangePasswordAsync(context, web)));

        var bilibili = app.MapGroup("/api/bilibili");
        bilibili.MapPost("/qr/start", (Delegate)(async (HttpContext context) => await StartBindAsync(context, web)));
        bilibili.MapGet("/qr/{bindId}", (HttpContext context, string bindId) => GetBindStatus(context, web, bindId));
        bilibili.MapGet("/qr/{bindId}/image", (HttpContext context, string bindId) => GetBindQrImage(context, web, bindId));
        bilibili.MapGet("/bindings", (Delegate)(async (HttpContext context) => await GetBindingsAsync(context, web)));
        bilibili.MapDelete("/bindings", (Delegate)(async (HttpContext context) => await UnbindAsync(context, web)));

        app.MapPost("/api/parse", (Delegate)(async (HttpContext context) => await ParseAsync(context, server, web)));

        app.MapPost("/api/tasks", (Delegate)(async (HttpContext context) => await SubmitTaskAsync(context, server, web)));
        app.MapGet("/api/tasks", (Delegate)(async (HttpContext context) => await ListTasksAsync(context, server, web)));
        app.MapGet("/api/tasks/{id}", (Delegate)(async (HttpContext context, string id) => await GetTaskAsync(context, server, web, id)));
        app.MapDelete("/api/tasks/finished", (Delegate)(async (HttpContext context) => await RemoveFinishedAsync(context, server, web)));
        app.MapDelete("/api/tasks/finished/{id}", (Delegate)(async (HttpContext context, string id) => await RemoveFinishedByIdAsync(context, server, web, id)));

        var admin = app.MapGroup("/api/admin");
        admin.MapGet("/users", (Delegate)(async (HttpContext context) => await AdminListUsersAsync(context, server, web)));
        admin.MapPost("/users", (Delegate)(async (HttpContext context) => await AdminCreateUserAsync(context, web)));
        admin.MapPatch("/users/{id}", (Delegate)(async (HttpContext context, string id) => await AdminPatchUserAsync(context, web, id)));
        admin.MapDelete("/users/{id}", (Delegate)(async (HttpContext context, string id) => await AdminDeleteUserAsync(context, web, id)));
        admin.MapPost("/users/{id}/password", (Delegate)(async (HttpContext context, string id) => await AdminResetPasswordAsync(context, web, id)));
        admin.MapGet("/config", (Delegate)(async (HttpContext context) => await AdminGetConfigAsync(context, server, web)));
        admin.MapPatch("/config", (Delegate)(async (HttpContext context) => await AdminPatchConfigAsync(context, server, web)));
    }

    #endregion

    #region 任务与管理员查询处理（供路由 lambda await）

    private static Task<IResult> CurrentUserAsync(HttpContext context, WebServices web)
        => Task.FromResult<IResult>(Results.Json(ToAuthResponse(web, CurrentUser(context)!),
            WebJsonContext.Default.WebAuthUserResponse));

    private static Task<IResult> GetBindingsAsync(HttpContext context, WebServices web)
    {
        var binding = web.Bindings.Load(CurrentUser(context)!.Id);
        return Task.FromResult<IResult>(Results.Json(binding is null
            ? new WebBindingResponse(false, null, null, null)
            : new WebBindingResponse(true, binding.Mid, binding.Uname, binding.BoundAt),
            WebJsonContext.Default.WebBindingResponse));
    }

    private static Task<IResult> UnbindAsync(HttpContext context, WebServices web)
    {
        web.Bindings.Delete(CurrentUser(context)!.Id);
        return Task.FromResult<IResult>(Results.NoContent());
    }

    private static Task<IResult> ListTasksAsync(HttpContext context, MeowDownApiServer server, WebServices web)
    {
        var user = CurrentUser(context)!;
        if (!TryResolveScope(context, user, out var error, out var scopeAll))
        {
            return Task.FromResult(error);
        }

        var snapshot = server.TaskStore.GetSnapshot();
        if (!scopeAll)
        {
            snapshot = new DownloadTaskCollection(
                snapshot.Pending.Where(task => task.OwnerId == user.Id).ToList(),
                snapshot.Running.Where(task => task.OwnerId == user.Id).ToList(),
                snapshot.Finished.Where(task => task.OwnerId == user.Id).ToList());
        }
        return Task.FromResult<IResult>(Results.Json(snapshot, WebTaskJson.Options));
    }

    private static Task<IResult> GetTaskAsync(HttpContext context, MeowDownApiServer server, WebServices web, string id)
    {
        var user = CurrentUser(context)!;
        var task = server.TaskStore.FindSnapshot(id);
        if (task is null || (task.OwnerId != user.Id && user.Role != WebUserRole.Admin))
        {
            return Task.FromResult<IResult>(Error(StatusCodes.Status404NotFound, "not_found", "任务不存在"));
        }
        return Task.FromResult<IResult>(Results.Json(task, WebTaskJson.Options));
    }

    private static Task<IResult> RemoveFinishedAsync(HttpContext context, MeowDownApiServer server, WebServices web)
    {
        var user = CurrentUser(context)!;
        if (!TryResolveScope(context, user, out var error, out var scopeAll))
        {
            return Task.FromResult(error);
        }
        server.TaskStore.RemoveFinished(scopeAll ? _ => true : task => task.OwnerId == user.Id);
        return Task.FromResult<IResult>(Results.Ok());
    }

    private static Task<IResult> RemoveFinishedByIdAsync(HttpContext context, MeowDownApiServer server, WebServices web, string id)
    {
        var user = CurrentUser(context)!;
        server.TaskStore.RemoveFinished(task => task.MatchesId(id) && task.OwnerId == user.Id);
        return Task.FromResult<IResult>(Results.Ok());
    }

    private static Task<IResult> AdminListUsersAsync(HttpContext context, MeowDownApiServer server, WebServices web)
        => Task.FromResult<IResult>(Results.Json(
            new WebAdminUserListResponse(BuildAdminUserList(server, web).ToList()),
            WebJsonContext.Default.WebAdminUserListResponse));

    private static Task<IResult> AdminGetConfigAsync(HttpContext context, MeowDownApiServer server, WebServices web)
        => Task.FromResult<IResult>(Results.Json(BuildConfigResponse(server, web), WebJsonContext.Default.WebConfigResponse));

    #endregion

    #region 认证

    private static async Task<IResult> RegisterAsync(HttpContext context, WebServices web)
    {
        if (!web.Settings.Snapshot().AllowRegistration)
        {
            return Error(StatusCodes.Status403Forbidden, "registration_disabled", "自助注册未开放");
        }

        var body = await ReadJsonAsync<WebRegisterRequest>(context);
        if (body is null)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_request", "请求体不是有效的JSON");
        }

        if (!web.RegisterLimiter.TryAcquire(RemoteIp(context)))
        {
            return Error(StatusCodes.Status429TooManyRequests, "rate_limited", "注册过于频繁，请稍后再试");
        }

        if (WebUserStore.ValidateUsername(body.Username) is { } usernameError)
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", usernameError);
        }
        if (WebUserStore.ValidatePassword(body.Password) is { } passwordError)
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", passwordError);
        }
        var user = web.Users.Create(body.Username, body.Password, WebUserRole.User);
        if (user is null)
        {
            return Error(StatusCodes.Status409Conflict, "conflict", "用户名已存在");
        }
        return Results.Json(new WebUserCreatedResponse(user.Id, user.Username, RoleName(user.Role)),
            WebJsonContext.Default.WebUserCreatedResponse, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(HttpContext context, WebServices web)
    {
        var body = await ReadJsonAsync<WebLoginRequest>(context);
        if (body is null)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_request", "请求体不是有效的JSON");
        }
        if (string.IsNullOrEmpty(body.Username) || string.IsNullOrEmpty(body.Password))
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", "用户名和密码不能为空");
        }

        var ip = RemoteIp(context);
        if (web.LoginLimiter.IsLocked(ip, body.Username))
        {
            return Error(StatusCodes.Status429TooManyRequests, "rate_limited", "登录失败次数过多，请15分钟后再试");
        }

        var user = web.Users.FindByUsername(body.Username);
        // 用户不存在时也执行一次等时哈希，避免响应时间泄露用户名是否存在
        var verified = user is not null
            ? WebPasswordHasher.Verify(body.Password, user.PasswordHash)
            : (WebPasswordHasher.Verify(body.Password, WebPasswordHasher.DummyHash), false).Item2;
        if (user is null || user.Disabled || !verified)
        {
            web.LoginLimiter.RecordFailure(ip, body.Username);
            return Error(StatusCodes.Status401Unauthorized, "invalid_credentials", "用户名或密码错误");
        }

        web.LoginLimiter.Reset(ip, body.Username);
        if (WebPasswordHasher.NeedsUpgrade(user.PasswordHash))
        {
            web.Users.UpdatePasswordHash(user, WebPasswordHasher.Hash(body.Password));
        }

        var sessionId = web.Sessions.Create(user.Id);
        var lifetimeSeconds = web.Settings.Snapshot().SessionLifetimeHours * 3600L;
        context.Response.Cookies.Append(WebSessionStore.CookieName, sessionId,
            SessionCookieOptions(context, web.Launch, lifetimeSeconds));
        return Results.Json(ToAuthResponse(web, user), WebJsonContext.Default.WebAuthUserResponse);
    }

    private static Task<IResult> LogoutAsync(HttpContext context, WebServices web)
    {
        if (context.Items[SessionIdItemKey] is string sessionId && sessionId.Length > 0)
        {
            web.Sessions.Revoke(sessionId);
        }
        context.Response.Cookies.Delete(WebSessionStore.CookieName, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = ShouldUseSecureCookie(context, web.Launch),
        });
        return Task.FromResult<IResult>(Results.NoContent());
    }

    private static async Task<IResult> ChangePasswordAsync(HttpContext context, WebServices web)
    {
        var user = CurrentUser(context)!;
        var body = await ReadJsonAsync<WebPasswordChangeRequest>(context);
        if (body is null)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_request", "请求体不是有效的JSON");
        }
        if (WebUserStore.ValidatePassword(body.NewPassword) is { } passwordError)
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", passwordError);
        }
        if (!WebPasswordHasher.Verify(body.CurrentPassword, user.PasswordHash))
        {
            return Error(StatusCodes.Status401Unauthorized, "invalid_credentials", "当前密码错误");
        }

        web.Users.UpdatePasswordHash(user, WebPasswordHasher.Hash(body.NewPassword), clearChangeRequired: true);
        // 吊销除当前会话外的全部会话
        web.Sessions.RevokeAllForUser(user.Id, exceptSessionId: context.Items[SessionIdItemKey] as string);
        return Results.NoContent();
    }

    #endregion

    #region B站绑定

    private static async Task<IResult> StartBindAsync(HttpContext context, WebServices web)
    {
        try
        {
            var session = await web.BindSessions.StartAsync(CurrentUser(context)!.Id);
            return Results.Json(new WebBindStartResponse(session.BindId, session.QrUrl, session.ExpiresAt),
                WebJsonContext.Default.WebBindStartResponse);
        }
        catch (Exception e)
        {
            return Error(StatusCodes.Status502BadGateway, "upstream_error", ErrorText.Describe(e));
        }
    }

    private static IResult GetBindStatus(HttpContext context, WebServices web, string bindId)
    {
        var session = web.BindSessions.Get(bindId, CurrentUser(context)!.Id);
        if (session is null)
        {
            return Error(StatusCodes.Status404NotFound, "not_found", "绑定会话不存在或已结束");
        }
        var (status, mid, uname, message) = session.Snapshot();
        return Results.Json(
            new WebBindStatusResponse(session.BindId, WebBiliBindSessionManager.ToContractStatus(status), mid, uname, message),
            WebJsonContext.Default.WebBindStatusResponse);
    }

    private static IResult GetBindQrImage(HttpContext context, WebServices web, string bindId)
    {
        var session = web.BindSessions.Get(bindId, CurrentUser(context)!.Id);
        var png = session is null ? null : web.BindSessions.RenderQrPng(session);
        if (png is null)
        {
            return Error(StatusCodes.Status404NotFound, "not_found", "绑定会话不存在或已结束");
        }
        context.Response.Headers.CacheControl = "no-store";
        return Results.Bytes(png, "image/png");
    }

    #endregion

    #region 在线解析

    private static async Task<IResult> ParseAsync(HttpContext context, MeowDownApiServer server, WebServices web)
    {
        var user = CurrentUser(context)!;
        var body = await ReadJsonAsync<WebParseRequest>(context);
        if (body is null || string.IsNullOrWhiteSpace(body.Url))
        {
            return Error(StatusCodes.Status400BadRequest, "bad_request", "Url不能为空");
        }
        var url = body.Url.Trim();
        if (!MeowDownUtil.IsSupportedBiliInput(url))
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", "Url只能是B站链接或av/BV/ep/ss等编号");
        }
        // 同一用户同时只允许一个在途解析
        if (!web.ParseInFlight.TryAdd(user.Id, 0))
        {
            return Error(StatusCodes.Status429TooManyRequests, "rate_limited", "同一用户同时只允许一个在途解析");
        }

        try
        {
            var binding = web.Bindings.Load(user.Id);
            var option = new ServeRequestOptions
            {
                Url = url,
                Cookie = binding?.Cookie ?? "",
                SuppressLocalCredentials = true,
                // parse 只取元数据、不产生下载与混流：跳过 muxer 二进制探测，
                // 避免本机缺 ffmpeg 时未触达B站就抛本地环境错误并被误报为 upstream_error。
                SkipMux = true,
            };
            // 排队等待与上游请求共用 30 秒预算并响应断连：下载任务持有凭据闸门期间，解析不再无限挂起
            if (!await web.CredentialGate.WaitAsync(ParseTimeout, context.RequestAborted))
            {
                return Error(StatusCodes.Status504GatewayTimeout, "upstream_timeout", "解析排队超时（30秒），请稍后重试");
            }
            try
            {
                var firstEncoding = Program.SetUpWork(option).firstEncoding;
                var (fetchedAid, vInfo, _) = await Program.GetVideoInfoAsync(option).WaitAsync(ParseTimeout, context.RequestAborted);
                if (vInfo is SpaceVideoInfo)
                {
                    return Error(StatusCodes.Status400BadRequest, "validation_failed",
                        "空间投稿等批量类型不支持在线解析，仅支持提交下载任务");
                }
                var qualities = await ProbeQualitiesAsync(option, vInfo, fetchedAid, firstEncoding);
                return Results.Json(BuildParseResponse(fetchedAid, vInfo, qualities),
                    WebJsonContext.Default.WebParseResponse);
            }
            finally
            {
                web.CredentialGate.Release();
            }
        }
        catch (TimeoutException)
        {
            return Error(StatusCodes.Status504GatewayTimeout, "upstream_timeout", "解析超时（30秒）");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端已断开：交还框架按中止请求处理，不误报为上游错误
            throw;
        }
        catch (Exception e)
        {
            return Error(StatusCodes.Status502BadGateway, "upstream_error", ErrorText.Describe(e));
        }
        finally
        {
            web.ParseInFlight.TryRemove(user.Id, out _);
        }
    }

    private static async Task<System.Collections.Generic.List<WebParseQuality>> ProbeQualitiesAsync(
        ServeRequestOptions option, VInfo vInfo, string aid, string? firstEncoding)
    {
        var firstPage = vInfo.PagesInfo.FirstOrDefault();
        if (firstPage is null)
        {
            return [];
        }
        var tracks = await MeowDown.Core.Parser.ExtractTracksAsync(aid, firstPage.aid, firstPage.cid, firstPage.epid,
            option.UseTvApi, option.UseIntlApi, option.UseAppApi, firstEncoding ?? string.Empty, "0", null,
            vInfo.IntlSeasonId);
        return tracks.VideoTracks
            .Where(track => int.TryParse(track.id, out _))
            .GroupBy(track => track.id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(track => Convert.ToInt32(track.id))
            .Select(track => new WebParseQuality(Convert.ToInt32(track.id), track.dfn))
            .ToList();
    }

    private static WebParseResponse BuildParseResponse(string aid, VInfo vInfo,
        System.Collections.Generic.List<WebParseQuality> qualities)
    {
        var pages = vInfo.PagesInfo;
        var first = pages.FirstOrDefault();
        return new WebParseResponse(
            Aid: first?.aid is { Length: > 0 } pageAid ? pageAid : aid,
            Bvid: first?.bvid ?? "",
            Title: vInfo.Title,
            Desc: vInfo.Desc,
            Pic: vInfo.Pic,
            PubTime: vInfo.PubTime,
            IsBangumi: vInfo.IsBangumi,
            Pages: pages.Select(page => new WebParsePage(page.index, page.cid, page.title, page.dur, page.cover)).ToList(),
            Qualities: qualities);
    }

    #endregion

    #region 任务

    private static async Task<IResult> SubmitTaskAsync(HttpContext context, MeowDownApiServer server, WebServices web)
    {
        var user = CurrentUser(context)!;
        var binding = await MyOptionBindingResult<ServeRequestOptions>.BindAsync(context);
        if (!binding.IsValid)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_request", "请求体不是有效的JSON");
        }
        var request = binding.Result!;
        if (server.ValidateAndNormalizeWebRequest(request, WebUserDownloadDir(server, user.Id)) is { } validationError)
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", validationError);
        }
        var task = new DownloadTask(Guid.NewGuid().ToString("N"), "", request.Url,
            DateTimeOffset.Now.ToUnixTimeSeconds(), user.Id, user.Username);
        if (!server.TryEnqueueWebTask(request, task, user.Id, user.Username))
        {
            return Error(StatusCodes.Status429TooManyRequests, "queue_full", "任务队列已满");
        }
        return Results.Json(new TaskSubmissionResult(task.TaskId), WebJsonContext.Default.TaskSubmissionResult,
            statusCode: StatusCodes.Status202Accepted);
    }

    private static bool TryResolveScope(HttpContext context, WebUser user, out IResult error, out bool scopeAll)
    {
        scopeAll = context.Request.Query["scope"].ToString().Equals("all", StringComparison.OrdinalIgnoreCase);
        if (scopeAll && user.Role != WebUserRole.Admin)
        {
            error = Error(StatusCodes.Status403Forbidden, "forbidden", "scope=all 仅管理员可用");
            return false;
        }
        error = null!;
        return true;
    }

    internal static string WebUserDownloadDir(MeowDownApiServer server, string userId)
        => System.IO.Path.Combine(server.ServerOptions.DownloadRoot, "web-users", userId);

    #endregion

    #region 管理员

    private static System.Collections.Generic.IEnumerable<WebAdminUserResponse> BuildAdminUserList(
        MeowDownApiServer server, WebServices web)
    {
        var snapshot = server.TaskStore.GetSnapshot();
        return web.Users.All().Select(user =>
        {
            var binding = web.Bindings.Load(user.Id);
            return new WebAdminUserResponse(
                user.Id, user.Username, RoleName(user.Role), user.Disabled, user.CreatedAt,
                binding is not null, binding?.Mid is { Length: > 0 } mid ? mid : null,
                binding?.Uname is { Length: > 0 } uname ? uname : null,
                snapshot.Pending.Count(task => task.OwnerId == user.Id),
                snapshot.Running.Count(task => task.OwnerId == user.Id));
        });
    }

    private static async Task<IResult> AdminCreateUserAsync(HttpContext context, WebServices web)
    {
        var body = await ReadJsonAsync<WebAdminCreateUserRequest>(context);
        if (body is null)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_request", "请求体不是有效的JSON");
        }
        if (WebUserStore.ValidateUsername(body.Username) is { } usernameError)
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", usernameError);
        }
        if (WebUserStore.ValidatePassword(body.Password) is { } passwordError)
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", passwordError);
        }
        if (ParseRole(body.Role) is not { } role)
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", "role只能是user或admin");
        }
        var user = web.Users.Create(body.Username, body.Password, role);
        if (user is null)
        {
            return Error(StatusCodes.Status409Conflict, "conflict", "用户名已存在");
        }
        return Results.Json(BuildAdminUserResponse(web, user, 0, 0),
            WebJsonContext.Default.WebAdminUserResponse, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> AdminPatchUserAsync(HttpContext context, WebServices web, string id)
    {
        var target = web.Users.FindById(id);
        if (target is null)
        {
            return Error(StatusCodes.Status404NotFound, "not_found", "用户不存在");
        }
        var body = await ReadJsonAsync<WebAdminPatchUserRequest>(context);
        if (body is null)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_request", "请求体不是有效的JSON");
        }

        WebUserRole? newRole = null;
        if (body.Role is not null)
        {
            newRole = ParseRole(body.Role);
            if (newRole is null)
            {
                return Error(StatusCodes.Status400BadRequest, "validation_failed", "role只能是user或admin");
            }
        }

        // 检查+修改在用户库内部锁内原子执行：两个管理员并发互相降级/停用时，
        // 不会各自把对方视作幸存管理员而双双通过，导致0个可用管理员且无法经API恢复
        var outcome = web.Users.TryUpdateRoleOrDisabled(target.Id, newRole, body.Disabled);
        if (outcome == WebAdminGuardResult.NotFound)
        {
            return Error(StatusCodes.Status404NotFound, "not_found", "用户不存在");
        }
        if (outcome == WebAdminGuardResult.LastAdminConflict)
        {
            return Error(StatusCodes.Status409Conflict, "conflict", "不能停用或降级最后一个管理员");
        }
        if (body.Disabled == true)
        {
            web.Sessions.RevokeAllForUser(target.Id);
        }
        return Results.Json(BuildAdminUserResponse(web, target),
            WebJsonContext.Default.WebAdminUserResponse);
    }

    private static Task<IResult> AdminDeleteUserAsync(HttpContext context, WebServices web, string id)
    {
        var operatorUser = CurrentUser(context)!;
        if (id == operatorUser.Id)
        {
            return Task.FromResult(Error(StatusCodes.Status409Conflict, "conflict", "不能删除自己"));
        }
        var target = web.Users.FindById(id);
        if (target is null)
        {
            return Task.FromResult(Error(StatusCodes.Status404NotFound, "not_found", "用户不存在"));
        }
        var outcome = web.Users.TryRemoveWithAdminGuard(id);
        if (outcome == WebAdminGuardResult.NotFound)
        {
            return Task.FromResult(Error(StatusCodes.Status404NotFound, "not_found", "用户不存在"));
        }
        if (outcome == WebAdminGuardResult.LastAdminConflict)
        {
            return Task.FromResult(Error(StatusCodes.Status409Conflict, "conflict", "不能删除最后一个管理员"));
        }

        // 级联：删除B站凭据文件、吊销全部会话；任务按正常保留策略自然淘汰
        web.Bindings.Delete(target.Id);
        web.Sessions.RevokeAllForUser(target.Id);
        return Task.FromResult<IResult>(Results.NoContent());
    }

    private static async Task<IResult> AdminResetPasswordAsync(HttpContext context, WebServices web, string id)
    {
        var target = web.Users.FindById(id);
        if (target is null)
        {
            return Error(StatusCodes.Status404NotFound, "not_found", "用户不存在");
        }
        var body = await ReadJsonAsync<WebAdminResetPasswordRequest>(context);
        if (body is null)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_request", "请求体不是有效的JSON");
        }
        if (WebUserStore.ValidatePassword(body.NewPassword) is { } passwordError)
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", passwordError);
        }

        web.Users.UpdatePasswordHash(target, WebPasswordHasher.Hash(body.NewPassword), markChangeRequired: true);
        web.Sessions.RevokeAllForUser(target.Id);
        return Results.NoContent();
    }

    private static WebConfigResponse BuildConfigResponse(MeowDownApiServer server, WebServices web)
    {
        var settings = web.Settings.Snapshot();
        var options = server.ServerOptions;
        return new WebConfigResponse(
            new WebConfigWritable(settings.MaxQueueLength, settings.MaxPendingTasksPerUser,
                settings.MaxFinishedTasks, settings.FinishedTaskRetentionSeconds,
                settings.AllowRegistration, settings.SessionLifetimeHours),
            new WebConfigReadOnly(web.ListenUrl, options.DownloadRoot, web.WebDataDir,
                options.AllowAria2cArgs, options.AllowCustomOutput, options.AllowCustomNetworkHosts,
                options.AllowPrivateCallbacks, ServerVersion()));
    }

    private static async Task<IResult> AdminPatchConfigAsync(HttpContext context, MeowDownApiServer server, WebServices web)
    {
        var body = await ReadJsonAsync<WebAdminPatchConfigRequest>(context);
        if (body is null)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_request", "请求体不是有效的JSON");
        }
        if (web.Settings.ValidatePatch(body) is { } validationError)
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", validationError);
        }
        web.Settings.ApplyPatch(body);
        return Results.Json(BuildConfigResponse(server, web), WebJsonContext.Default.WebConfigResponse);
    }

    #endregion

    #region 辅助

    private static WebAuthUserResponse ToAuthResponse(WebServices web, WebUser user)
        => new(user.Id, user.Username, RoleName(user.Role), user.PasswordChangeRequired, web.Bindings.Exists(user.Id));

    internal static WebAdminUserResponse BuildAdminUserResponse(WebServices web, WebUser user,
        int pendingTasks = 0, int runningTasks = 0)
    {
        var binding = web.Bindings.Load(user.Id);
        return new WebAdminUserResponse(
            user.Id, user.Username, RoleName(user.Role), user.Disabled, user.CreatedAt,
            binding is not null, binding?.Mid is { Length: > 0 } mid ? mid : null,
            binding?.Uname is { Length: > 0 } uname ? uname : null,
            pendingTasks, runningTasks);
    }

    internal static IResult Error(int statusCode, string code, string message)
        => Results.Json(new WebErrorResponse(new WebErrorBody(code, message)),
            WebJsonContext.Default.WebErrorResponse, statusCode: statusCode);

    private static async Task WriteError(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new WebErrorResponse(new WebErrorBody(code, message)),
            WebJsonContext.Default.WebErrorResponse);
    }

    private static WebUser? CurrentUser(HttpContext context)
        => context.Items[UserItemKey] as WebUser;

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context) where T : class
    {
        try
        {
            return await context.Request.ReadFromJsonAsync(
                (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)WebJsonContext.Default.GetTypeInfo(typeof(T))!);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string RemoteIp(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    internal static CookieOptions SessionCookieOptions(HttpContext context, WebLaunchOptions launch, long maxAgeSeconds)
        => new()
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
            Secure = ShouldUseSecureCookie(context, launch),
            MaxAge = TimeSpan.FromSeconds(maxAgeSeconds),
        };

    internal static bool ShouldUseSecureCookie(HttpContext context, WebLaunchOptions launch)
        => launch.WebSecureCookie
            || string.Equals(context.Request.Headers["X-Forwarded-Proto"].ToString(), "https", StringComparison.OrdinalIgnoreCase);

    internal static string RoleName(WebUserRole role) => role == WebUserRole.Admin ? "admin" : "user";

    private static WebUserRole? ParseRole(string? role) => role switch
    {
        null or "" => WebUserRole.User,
        "user" => WebUserRole.User,
        "admin" => WebUserRole.Admin,
        _ => null,
    };

    private static string ServerVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!;
        return MeowDownSelfUpdater.CurrentVersion()?.ToString() ?? $"{version.Major}.{version.Minor}.{version.Build}";
    }

    #endregion
}
