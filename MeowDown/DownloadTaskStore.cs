using System;
using System.Collections.Generic;
using System.Linq;

namespace MeowDown;

internal sealed class DownloadTaskStore
{
    private readonly List<DownloadTask> pendingTasks = [];
    private readonly List<DownloadTask> runningTasks = [];
    private readonly List<DownloadTask> finishedTasks = [];
    private readonly object stateLock = new();
    private readonly Func<long> getCurrentTimeSeconds;
    private readonly Func<int> maxFinishedTasksProvider;
    private readonly Func<long> finishedRetentionSecondsProvider;

    internal DownloadTaskStore(Func<long>? getCurrentTimeSeconds = null, MeowDownServerOptions? options = null,
        Func<int>? maxFinishedTasks = null, Func<long>? finishedRetentionSeconds = null)
    {
        this.getCurrentTimeSeconds = getCurrentTimeSeconds
            ?? (() => DateTimeOffset.Now.ToUnixTimeSeconds());
        var resolvedOptions = options ?? new MeowDownServerOptions();
        // 提供者允许管理员在运行时修改保留配置（web-settings.json），默认回退到启动值
        maxFinishedTasksProvider = maxFinishedTasks ?? (() => resolvedOptions.MaxFinishedTasks);
        finishedRetentionSecondsProvider = finishedRetentionSeconds ?? (() => resolvedOptions.FinishedTaskRetentionSeconds);
    }

    public bool TryAddPending(DownloadTask task, int maxPendingTasks)
    {
        lock (stateLock)
        {
            if (pendingTasks.Count >= maxPendingTasks)
            {
                return false;
            }

            pendingTasks.Add(task);
            return true;
        }
    }

    public void Start(DownloadTask task)
    {
        lock (stateLock)
        {
            if (pendingTasks.Remove(task))
            {
                task.MarkStarted(getCurrentTimeSeconds());
                runningTasks.Add(task);
            }
        }
    }

    public void Complete(DownloadTask task, long finishedAt, bool succeeded)
    {
        lock (stateLock)
        {
            task.Finish(finishedAt, succeeded);
            if (runningTasks.Remove(task))
            {
                finishedTasks.Add(task);
            }

            PruneFinishedLocked(finishedAt);
            if (finishedTasks.Count > maxFinishedTasksProvider())
            {
                finishedTasks.RemoveRange(0, finishedTasks.Count - maxFinishedTasksProvider());
            }
        }
    }

    public DownloadTaskCollection GetSnapshot()
    {
        lock (stateLock)
        {
            PruneFinishedLocked(getCurrentTimeSeconds());
            return new(
                pendingTasks.Select(task => task.CreateSnapshot()).ToList(),
                runningTasks.Select(task => task.CreateSnapshot()).ToList(),
                finishedTasks.Select(task => task.CreateSnapshot()).ToList());
        }
    }

    public List<DownloadTask> GetPendingSnapshot()
    {
        lock (stateLock)
        {
            return pendingTasks.Select(task => task.CreateSnapshot()).ToList();
        }
    }

    public List<DownloadTask> GetRunningSnapshot()
    {
        lock (stateLock)
        {
            return runningTasks.Select(task => task.CreateSnapshot()).ToList();
        }
    }

    public List<DownloadTask> GetFinishedSnapshot()
    {
        lock (stateLock)
        {
            PruneFinishedLocked(getCurrentTimeSeconds());
            return finishedTasks.Select(task => task.CreateSnapshot()).ToList();
        }
    }

    public DownloadTask? FindSnapshot(string id)
    {
        lock (stateLock)
        {
            PruneFinishedLocked(getCurrentTimeSeconds());
            return (pendingTasks.FirstOrDefault(task => task.MatchesId(id))
                ?? runningTasks.FirstOrDefault(task => task.MatchesId(id))
                ?? finishedTasks.FirstOrDefault(task => task.MatchesId(id)))?.CreateSnapshot();
        }
    }

    public void RemoveFinished(Predicate<DownloadTask> predicate)
    {
        lock (stateLock)
        {
            finishedTasks.RemoveAll(predicate);
        }
    }

    /// <summary>某用户当前等待中+执行中的任务数（Web 模式单用户任务上限依据）。</summary>
    public int CountActiveForUser(string ownerId)
    {
        lock (stateLock)
        {
            return pendingTasks.Count(task => task.OwnerId == ownerId)
                + runningTasks.Count(task => task.OwnerId == ownerId);
        }
    }

    private void PruneFinishedLocked(long nowSeconds)
    {
        var oldestAllowed = nowSeconds - finishedRetentionSecondsProvider();
        finishedTasks.RemoveAll(finishedTask => finishedTask.TaskFinishTime < oldestAllowed);
    }
}
