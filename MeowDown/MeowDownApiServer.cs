using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using MeowDown.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
namespace MeowDown;

public class MeowDownApiServer
{
    private WebApplication? app;
    private readonly DownloadTaskStore taskStore;
    private readonly ConcurrentQueue<QueuedDownloadTask> pendingTasks = new();
    private readonly SemaphoreSlim pendingTaskSignal = new(0);
    private int workerStarted;
    private readonly MeowDownServerOptions serverOptions;
    private readonly ServerRequestValidator requestValidator;
    private readonly ServerAuth serverAuth;
    private readonly TaskCallbackSender callbackSender;
    private readonly WebLaunchOptions webLaunch;
    private WebServices? webServices;
    private WebSettingsStore? settingsStore;

    public MeowDownApiServer(MeowDownServerOptions? options = null, WebLaunchOptions? webLaunch = null)
    {
        var configured = options ?? new MeowDownServerOptions();
        serverOptions = configured with
        {
            DownloadRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.DownloadRoot))
        };
        this.webLaunch = webLaunch ?? new WebLaunchOptions();
        // 已完成任务保留数/时长经设置提供者读取，管理员可在运行时通过 /api/admin/config 调整
        taskStore = new DownloadTaskStore(options: serverOptions,
            maxFinishedTasks: () => SettingsStore.Snapshot().MaxFinishedTasks,
            finishedRetentionSeconds: () => SettingsStore.Snapshot().FinishedTaskRetentionSeconds);
        requestValidator = new ServerRequestValidator(serverOptions);
        serverAuth = new ServerAuth();
        callbackSender = new TaskCallbackSender(serverOptions);
    }

    /// <summary>Web 服务的内存态组件（懒构建：旧单元测试仅用到校验逻辑时不落任何文件）。</summary>
    internal WebServices Web => webServices ??= BuildWebServices();

    internal MeowDownServerOptions ServerOptions => serverOptions;

    internal DownloadTaskStore TaskStore => taskStore;

    internal WebSettingsStore SettingsStore => settingsStore ??= new WebSettingsStore(
        Path.Combine(ResolveWebDataDir(), "web-settings.json"), serverOptions, webLaunch);

    private string ResolveWebDataDir()
    {
        var configured = webLaunch.WebDataDir;
        var dataDir = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Program.APP_DIR, "web-data")
            : Environment.ExpandEnvironmentVariables(configured);
        return Path.GetFullPath(dataDir);
    }

    private WebServices BuildWebServices()
    {
        var dataDir = ResolveWebDataDir();
        Directory.CreateDirectory(dataDir);
        var settings = SettingsStore;
        var users = new WebUserStore(Path.Combine(dataDir, "web-users.json"));
        var bindings = new WebBiliBindingStore(Path.Combine(dataDir, "bili"));
        return new WebServices
        {
            Launch = webLaunch,
            WebDataDir = dataDir,
            Users = users,
            Sessions = new WebSessionStore(() => settings.Snapshot().SessionLifetimeHours),
            Settings = settings,
            Bindings = bindings,
            BindSessions = new WebBiliBindSessionManager(bindings),
            LoginLimiter = new WebLoginRateLimiter(),
            RegisterLimiter = new WebRegisterRateLimiter(),
        };
    }

    private static bool IsLegacyApiPath(PathString path)
        => path.StartsWithSegments("/get-tasks") || path.StartsWithSegments("/add-task")
            || path.StartsWithSegments("/remove-finished");

    public void SetUpServer()
    {
        if (app is not null) return;
        var web = Web;
        // Web 前端静态根固定为程序目录下的 wwwroot，不随任务切换的工作目录漂移
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            WebRootPath = Path.Combine(Program.APP_DIR, "wwwroot"),
        });
        builder.Services.ConfigureHttpJsonOptions((options) =>
        {
            options.SerializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(options.SerializerOptions.TypeInfoResolver, AppJsonSerializerContext.Default);
        });
        builder.Services.AddCors((options) =>
        {
            options.AddPolicy("BrowserClients",
                policy =>
                {
                    policy.SetIsOriginAllowed(origin => serverAuth.requireApiToken || ServerAuth.IsLoopbackOrigin(origin))
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
        });
        app = builder.Build();
        // /api/* 走 Web 会话且不参与 CORS 放行（Web 前端必须同源部署）；旧 API 与静态文件维持原行为
        app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"),
            branch => branch.UseCors("BrowserClients"));
        app.Use(async (context, next) =>
        {
            if (!IsLegacyApiPath(context.Request.Path))
            {
                await next();
                return;
            }
            if (!serverAuth.HasValidApiToken(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }
            if (!serverAuth.requireApiToken && !ServerAuth.IsDirectLocalRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(ServerAuth.TokenlessAccessMessage);
                return;
            }
            await next();
        });
        WebApiEndpoints.UseWebAuth(app, web);
        app.UseDefaultFiles();
        app.UseStaticFiles();
        var taskStatusApi = app.MapGroup("/get-tasks");
        taskStatusApi.MapGet("/", handler: () => Results.Json(taskStore.GetSnapshot(), AppJsonSerializerContext.Default.DownloadTaskCollection));
        taskStatusApi.MapGet("/running", handler: () => Results.Json(taskStore.GetRunningSnapshot(), AppJsonSerializerContext.Default.ListDownloadTask));
        taskStatusApi.MapGet("/finished", handler: () => Results.Json(taskStore.GetFinishedSnapshot(), AppJsonSerializerContext.Default.ListDownloadTask));
        taskStatusApi.MapGet("/pending", handler: () => Results.Json(taskStore.GetPendingSnapshot(), AppJsonSerializerContext.Default.ListDownloadTask));
        taskStatusApi.MapGet("/{id}", (string id) =>
        {
            var task = taskStore.FindSnapshot(id);
            if (task is null)
            {
                return Results.NotFound();
            }
            return Results.Json(task, AppJsonSerializerContext.Default.DownloadTask);
        });
        app.MapPost("/add-task", (MyOptionBindingResult<ServeRequestOptions> bindingResult) =>
        {
            if (!bindingResult.IsValid)
            {
                return Results.BadRequest("输入有误");
            }
            var req = bindingResult.Result!;
            var validationMessage = ValidateAndNormalizeServerRequest(req);
            if (validationMessage is not null)
            {
                return Results.BadRequest(validationMessage);
            }
            var task = new DownloadTask("", req.Url, DateTimeOffset.Now.ToUnixTimeSeconds());
            if (!TryEnqueueDownloadTask(req, task))
            {
                return Results.Text("任务队列已满", statusCode: StatusCodes.Status429TooManyRequests);
            }
            return SubmissionAccepted(task.TaskId);
        });
        var finishedRemovalApi = app.MapGroup("remove-finished");
        finishedRemovalApi.MapDelete("/", () => { taskStore.RemoveFinished(_ => true); return Results.Ok(); });
        finishedRemovalApi.MapDelete("/failed", () => { taskStore.RemoveFinished(t => !t.IsSuccessful); return Results.Ok(); });
        finishedRemovalApi.MapDelete("/{id}", (string id) => { taskStore.RemoveFinished(t => t.MatchesId(id)); return Results.Ok(); });
        WebApiEndpoints.Map(app, this, web);
        // 未命中路径：/api 返回 404 JSON；其余 GET 回退到前端单页入口 index.html
        app.MapFallback((HttpContext context) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                return WebApiEndpoints.Error(StatusCodes.Status404NotFound, "not_found", "接口不存在");
            }
            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            {
                var indexPath = Path.Combine(Program.APP_DIR, "wwwroot", "index.html");
                if (File.Exists(indexPath))
                {
                    return Results.File(indexPath, "text/html");
                }
            }
            return Results.NotFound();
        });
    }

    public void Run(string url, string? configuredApiToken)
    {
        if (app is null) return;
        bool result = Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult)
            && uriResult.Scheme == Uri.UriSchemeHttp;
        if (!result)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"{url}不是合法的http URL，url示例：http://0.0.0.0:5000");
            Console.WriteLine("如果您需要https，请额外配置反向代理");
            Console.ResetColor();
            Console.WriteLine();
            Thread.Sleep(1);
            Environment.Exit(1);
        }
        serverAuth.ConfigureApiToken(uriResult!, configuredApiToken);
        Web.ListenUrl = url;
        Web.BootstrapAdminIfNeeded();
        EnsureQueueWorkerStarted();
        app.Run(url);
    }

    private bool TryEnqueueDownloadTask(ServeRequestOptions request, DownloadTask task,
        string? ownerId = null, string? ownerName = null)
    {
        if (!taskStore.TryAddPending(task, SettingsStore.Snapshot().MaxQueueLength))
        {
            return false;
        }

        // ownerId/ownerName 必须随任务入队：出队执行时据此注入该用户绑定的B站凭据，
        // 丢失会导致 Web 任务恒以无凭据（未登录）状态运行
        pendingTasks.Enqueue(new QueuedDownloadTask(request, task, ownerId, ownerName));
        pendingTaskSignal.Release();
        return true;
    }

    /// <summary>Web 任务入队：先校验单用户等待+执行上限，再校验全局队列长度（均返回 queue_full）。</summary>
    internal bool TryEnqueueWebTask(ServeRequestOptions request, DownloadTask task, string ownerId, string ownerName)
    {
        var settings = SettingsStore.Snapshot();
        if (taskStore.CountActiveForUser(ownerId) >= settings.MaxPendingTasksPerUser)
        {
            return false;
        }
        return TryEnqueueDownloadTask(request, task, ownerId, ownerName);
    }

    /// <summary>
    /// Web 任务校验：禁止自带 Cookie/AccessToken（凭据由服务端按用户注入），
    /// 无论是否开启 --server-allow-custom-output 一律拒绝自定义 WorkDir，
    /// 其余规则沿用服务器校验，最终把工作目录与受限输出根固定为用户专属目录。
    /// </summary>
    internal string? ValidateAndNormalizeWebRequest(ServeRequestOptions req, string userWorkDir)
    {
        if (!string.IsNullOrWhiteSpace(req.Cookie) || !string.IsNullOrWhiteSpace(req.AccessToken))
        {
            return "Web任务禁止自带Cookie或AccessToken，凭据由服务端按用户绑定注入";
        }
        if (!string.IsNullOrWhiteSpace(req.WorkDir))
        {
            return "Web任务不允许自定义WorkDir，输出目录由服务端分配";
        }
        var validator = new ServerRequestValidator(serverOptions with { AllowCustomOutput = false });
        var error = validator.ValidateAndNormalizeServerRequest(req);
        if (error is not null)
        {
            return error;
        }
        req.WorkDir = userWorkDir;
        req.RestrictedOutputRoot = userWorkDir;
        req.SuppressLocalCredentials = true;
        return null;
    }

    private void EnsureQueueWorkerStarted()
    {
        if (Interlocked.Exchange(ref workerStarted, 1) == 1)
        {
            return;
        }

        _ = Task.Run(ProcessPendingTasksAsync);
    }

    private async Task ProcessPendingTasksAsync()
    {
        while (true)
        {
            await pendingTaskSignal.WaitAsync();
            if (!pendingTasks.TryDequeue(out var queuedTask))
            {
                continue;
            }

            taskStore.Start(queuedTask.Task);
            try
            {
                var downloadTask = await AddDownloadTaskAsync(queuedTask);
                _ = callbackSender.SendCallbackAsync(queuedTask.Request, downloadTask);
            }
            catch (Exception e)
            {
                Logger.LogDebug("服务器任务处理异常: {0}", e);
            }
        }
    }

    internal string? ValidateAndNormalizeServerRequest(ServeRequestOptions req)
        => requestValidator.ValidateAndNormalizeServerRequest(req);

    internal static IResult SubmissionAccepted(string taskId)
        => Results.Json(new TaskSubmissionResult(taskId), AppJsonSerializerContext.Default.TaskSubmissionResult,
            statusCode: StatusCodes.Status202Accepted);

    internal static bool IsPrivateOrReservedAddress(IPAddress address)
        => TaskCallbackSender.IsPrivateOrReservedAddress(address);

    internal static SocketsHttpHandler CreatePinnedCallbackHandler(IPAddress address)
        => TaskCallbackSender.CreatePinnedCallbackHandler(address);

    internal static bool IsDirectLocalRequest(HttpRequest request)
        => ServerAuth.IsDirectLocalRequest(request);

    private async Task<DownloadTask> AddDownloadTaskAsync(QueuedDownloadTask queuedTask)
    {
        var task = queuedTask.Task;
        var succeeded = false;
        try
        {
            if (queuedTask.OwnerId is not null)
            {
                // Web 任务：凭据在出队执行时按提交用户注入（解绑后自动恢复无 Cookie），
                // 并禁止回退到服务器全局登录文件
                queuedTask.Request.SuppressLocalCredentials = true;
                queuedTask.Request.Cookie = Web.Bindings.Load(queuedTask.OwnerId)?.Cookie ?? "";
            }
            // SetUpWork 经 Config.COOKIE 这一全局静态传递凭据，闸门保证解析与下载互不覆盖
            await Web.CredentialGate.WaitAsync();
            try
            {
                await Program.ExecuteWorkAsync(queuedTask.Request, task);
                succeeded = true;
            }
            finally
            {
                Web.CredentialGate.Release();
            }
        }
        catch (Exception e)
        {
            task.SetError(Program.PrintFailure(e, $"{(string.IsNullOrEmpty(task.Aid) ? task.TaskId : task.Aid)}下载失败"));
        }
        taskStore.Complete(task, DateTimeOffset.Now.ToUnixTimeSeconds(), succeeded);
        return task;
    }
}

public sealed record TaskSubmissionResult(string TaskId);

internal sealed record QueuedDownloadTask(ServeRequestOptions Request, DownloadTask Task,
    string? OwnerId = null, string? OwnerName = null);
