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

    public MeowDownApiServer(MeowDownServerOptions? options = null)
    {
        var configured = options ?? new MeowDownServerOptions();
        serverOptions = configured with
        {
            DownloadRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.DownloadRoot))
        };
        taskStore = new DownloadTaskStore(options: serverOptions);
        requestValidator = new ServerRequestValidator(serverOptions);
        serverAuth = new ServerAuth();
        callbackSender = new TaskCallbackSender(serverOptions);
    }

    public void SetUpServer()
    {
        if (app is not null) return;
        var builder = WebApplication.CreateSlimBuilder();
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
        app.UseCors("BrowserClients");
        app.Use(async (context, next) =>
        {
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
        EnsureQueueWorkerStarted();
        app.Run(url);
    }

    private bool TryEnqueueDownloadTask(ServeRequestOptions request, DownloadTask task)
    {
        if (!taskStore.TryAddPending(task, serverOptions.MaxQueueLength))
        {
            return false;
        }

        pendingTasks.Enqueue(new QueuedDownloadTask(request, task));
        pendingTaskSignal.Release();
        return true;
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
            await Program.ExecuteWorkAsync(queuedTask.Request, task);
            succeeded = true;
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

internal sealed record QueuedDownloadTask(ServeRequestOptions Request, DownloadTask Task);
