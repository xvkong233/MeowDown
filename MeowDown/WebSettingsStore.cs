using System;
using System.IO;
using System.Text;
using System.Text.Json;
using static MeowDown.MeowDownLoginUtil;

namespace MeowDown;

/// <summary>
/// 管理员可改的运行时配置，持久化到 &lt;web-data-dir&gt;/web-settings.json。
/// 启动优先级：默认值 &lt; web-settings.json &lt; 命令行/MeowDown.config 显式覆盖。
/// </summary>
internal sealed record WebSettings
{
    public int MaxQueueLength { get; set; }
    public int MaxPendingTasksPerUser { get; set; }
    public int MaxFinishedTasks { get; set; }
    public long FinishedTaskRetentionSeconds { get; set; }
    public bool AllowRegistration { get; set; }
    public int SessionLifetimeHours { get; set; }
}

internal sealed class WebSettingsStore
{
    internal const int DefaultMaxPendingTasksPerUser = 10;
    internal const int DefaultSessionLifetimeHours = 168;

    internal const int MaxQueueLengthMin = 1, MaxQueueLengthMax = 10000;
    internal const int MaxPendingTasksPerUserMin = 1, MaxPendingTasksPerUserMax = 1000;
    internal const int MaxFinishedTasksMin = 1, MaxFinishedTasksMax = 100000;
    internal const long FinishedTaskRetentionSecondsMin = 3600, FinishedTaskRetentionSecondsMax = 2592000;
    internal const int SessionLifetimeHoursMin = 1, SessionLifetimeHoursMax = 720;

    private readonly object stateLock = new();
    private readonly string filePath;
    private WebSettings current;

    public WebSettingsStore(string filePath, MeowDownServerOptions options, WebLaunchOptions? launch)
    {
        this.filePath = filePath;
        current = new WebSettings
        {
            MaxQueueLength = options.MaxQueueLength,
            MaxPendingTasksPerUser = DefaultMaxPendingTasksPerUser,
            MaxFinishedTasks = options.MaxFinishedTasks,
            FinishedTaskRetentionSeconds = options.FinishedTaskRetentionSeconds,
            AllowRegistration = launch?.WebAllowRegistration ?? false,
            SessionLifetimeHours = DefaultSessionLifetimeHours,
        };
        ApplyStoredSettings(launch);
        current = Validated(current);
    }

    public WebSettings Snapshot()
    {
        lock (stateLock) return current with { };
    }

    /// <summary>应用通过校验的补丁并原子落盘；调用前须先经 <see cref="ValidatePatch"/>。</summary>
    public WebSettings ApplyPatch(WebAdminPatchConfigRequest patch)
    {
        lock (stateLock)
        {
            var next = Validated(current with { });
            if (patch.MaxQueueLength is { } queue) next.MaxQueueLength = queue;
            if (patch.MaxPendingTasksPerUser is { } perUser) next.MaxPendingTasksPerUser = perUser;
            if (patch.MaxFinishedTasks is { } finished) next.MaxFinishedTasks = finished;
            if (patch.FinishedTaskRetentionSeconds is { } retention) next.FinishedTaskRetentionSeconds = retention;
            if (patch.AllowRegistration is { } registration) next.AllowRegistration = registration;
            if (patch.SessionLifetimeHours is { } lifetime) next.SessionLifetimeHours = lifetime;
            current = Validated(next);
            PersistLocked();
            return current with { };
        }
    }

    /// <summary>返回第一个越界字段的错误描述；全部合法返回 null。</summary>
    public string? ValidatePatch(WebAdminPatchConfigRequest patch)
    {
        if (patch.MaxQueueLength is { } queue && queue is < MaxQueueLengthMin or > MaxQueueLengthMax)
            return $"maxQueueLength 允许范围 {MaxQueueLengthMin}-{MaxQueueLengthMax}";
        if (patch.MaxPendingTasksPerUser is { } perUser && perUser is < MaxPendingTasksPerUserMin or > MaxPendingTasksPerUserMax)
            return $"maxPendingTasksPerUser 允许范围 {MaxPendingTasksPerUserMin}-{MaxPendingTasksPerUserMax}";
        if (patch.MaxFinishedTasks is { } finished && finished is < MaxFinishedTasksMin or > MaxFinishedTasksMax)
            return $"maxFinishedTasks 允许范围 {MaxFinishedTasksMin}-{MaxFinishedTasksMax}";
        if (patch.FinishedTaskRetentionSeconds is { } retention
            && retention is < FinishedTaskRetentionSecondsMin or > FinishedTaskRetentionSecondsMax)
            return $"finishedTaskRetentionSeconds 允许范围 {FinishedTaskRetentionSecondsMin}-{FinishedTaskRetentionSecondsMax}";
        if (patch.SessionLifetimeHours is { } lifetime && lifetime is < SessionLifetimeHoursMin or > SessionLifetimeHoursMax)
            return $"sessionLifetimeHours 允许范围 {SessionLifetimeHoursMin}-{SessionLifetimeHoursMax}";
        return null;
    }

    private void ApplyStoredSettings(WebLaunchOptions? launch)
    {
        var stored = TryReadFile();
        if (stored is not null)
        {
            if (stored.MaxQueueLength is >= MaxQueueLengthMin and <= MaxQueueLengthMax)
                current.MaxQueueLength = stored.MaxQueueLength;
            if (stored.MaxPendingTasksPerUser is >= MaxPendingTasksPerUserMin and <= MaxPendingTasksPerUserMax)
                current.MaxPendingTasksPerUser = stored.MaxPendingTasksPerUser;
            if (stored.MaxFinishedTasks is >= MaxFinishedTasksMin and <= MaxFinishedTasksMax)
                current.MaxFinishedTasks = stored.MaxFinishedTasks;
            if (stored.FinishedTaskRetentionSeconds is >= FinishedTaskRetentionSecondsMin and <= FinishedTaskRetentionSecondsMax)
                current.FinishedTaskRetentionSeconds = stored.FinishedTaskRetentionSeconds;
            if (stored.SessionLifetimeHours is >= SessionLifetimeHoursMin and <= SessionLifetimeHoursMax)
                current.SessionLifetimeHours = stored.SessionLifetimeHours;
            // 命令行显式给出的开关优先于配置文件
            if (launch?.WebAllowRegistration is null)
            {
                current.AllowRegistration = stored.AllowRegistration;
            }
        }

        if (launch?.MaxQueueLengthOverride is { } queue) current.MaxQueueLength = queue;
        if (launch?.MaxFinishedTasksOverride is { } finished) current.MaxFinishedTasks = finished;
        if (launch?.FinishedTaskRetentionSecondsOverride is { } retention) current.FinishedTaskRetentionSeconds = retention;
    }

    private WebSettings? TryReadFile()
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(filePath),
                WebStorageJsonContext.Default.WebSettings);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.WriteLine($"web-settings.json 读取失败，将使用默认配置。原因：{ex.Message}");
            return null;
        }
    }

    private WebSettings Validated(WebSettings settings)
    {
        settings.MaxQueueLength = Math.Clamp(settings.MaxQueueLength, MaxQueueLengthMin, MaxQueueLengthMax);
        settings.MaxPendingTasksPerUser = Math.Clamp(settings.MaxPendingTasksPerUser, MaxPendingTasksPerUserMin, MaxPendingTasksPerUserMax);
        settings.MaxFinishedTasks = Math.Clamp(settings.MaxFinishedTasks, MaxFinishedTasksMin, MaxFinishedTasksMax);
        settings.FinishedTaskRetentionSeconds = Math.Clamp(settings.FinishedTaskRetentionSeconds,
            FinishedTaskRetentionSecondsMin, FinishedTaskRetentionSecondsMax);
        settings.SessionLifetimeHours = Math.Clamp(settings.SessionLifetimeHours, SessionLifetimeHoursMin, SessionLifetimeHoursMax);
        return settings;
    }

    private void PersistLocked()
        => WritePrivateFile(filePath, Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(current, WebStorageJsonContext.Default.WebSettings)));
}
