using System;
using System.IO;

namespace MeowDown.Tests;

/// <summary>Web 模式任务校验：凭据/输出边界由服务端强制。</summary>
public class WebTaskValidationTests
{
    [Fact]
    public void RejectsSelfProvidedCookieEvenWhenServerWouldAllowIt()
    {
        var server = new MeowDownApiServer(new MeowDownServerOptions { AllowCustomOutput = true });

        var error = server.ValidateAndNormalizeWebRequest(
            new ServeRequestOptions { Url = "BV1xx411c7mD", Cookie = "SESSDATA=leak" },
            UserDir());

        Assert.Contains("禁止自带Cookie", error);
    }

    [Fact]
    public void RejectsSelfProvidedAccessToken()
    {
        var server = new MeowDownApiServer();

        var error = server.ValidateAndNormalizeWebRequest(
            new ServeRequestOptions { Url = "BV1xx411c7mD", AccessToken = "access_token=leak" },
            UserDir());

        Assert.Contains("AccessToken", error);
    }

    [Fact]
    public void RejectsCustomWorkDirEvenWhenServerAllowsCustomOutput()
    {
        var server = new MeowDownApiServer(new MeowDownServerOptions { AllowCustomOutput = true });

        var error = server.ValidateAndNormalizeWebRequest(
            new ServeRequestOptions { Url = "BV1xx411c7mD", WorkDir = "custom" },
            UserDir());

        Assert.Contains("WorkDir", error);
    }

    [Fact]
    public void ValidRequest_IsPinnedToUserWorkDirWithRestrictedRoot()
    {
        var server = new MeowDownApiServer();
        var userDir = UserDir();
        var request = new ServeRequestOptions { Url = "BV1xx411c7mD" };

        var error = server.ValidateAndNormalizeWebRequest(request, userDir);

        Assert.Null(error);
        Assert.Equal(userDir, request.WorkDir);
        Assert.Equal(userDir, request.RestrictedOutputRoot);
        Assert.True(request.SuppressLocalCredentials);
        Assert.Equal("", request.Cookie);
    }

    [Fact]
    public void ValidRequest_StillRejectsNonBilibiliUrlAndUnsafePatterns()
    {
        var server = new MeowDownApiServer();
        var userDir = UserDir();

        Assert.Contains("Url只能是B站链接", server.ValidateAndNormalizeWebRequest(
            new ServeRequestOptions { Url = "http://example.com/video" }, userDir));
        Assert.Contains("只允许相对输出路径", server.ValidateAndNormalizeWebRequest(
            new ServeRequestOptions { Url = "BV1xx411c7mD", FilePattern = "../escape" }, userDir));
    }

    [Fact]
    public void PerUserTaskLimitCountsWithPendingAndRunningTasks()
    {
        var store = new DownloadTaskStore(maxFinishedTasks: () => 10, finishedRetentionSeconds: () => 3600);
        var first = NewOwnedTask("u_alice");
        var second = NewOwnedTask("u_alice");
        var otherUserTask = NewOwnedTask("u_bob");

        Assert.True(store.TryAddPending(first, 100));
        Assert.True(store.TryAddPending(second, 100));
        store.Start(first); // 移入执行
        Assert.True(store.TryAddPending(otherUserTask, 100));

        Assert.Equal(2, store.CountActiveForUser("u_alice"));
        Assert.Equal(1, store.CountActiveForUser("u_bob"));
        Assert.Equal(0, store.CountActiveForUser("u_carol"));
    }

    private static DownloadTask NewOwnedTask(string ownerId)
        => new(Guid.NewGuid().ToString("N"), "", "BV1xx411c7mD", 1000, ownerId, ownerId[2..]);

    private static string UserDir()
        => Path.Combine(Path.GetTempPath(), "meowdown-web-test", "u_alice");
}
