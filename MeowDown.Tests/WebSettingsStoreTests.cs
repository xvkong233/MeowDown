using System;
using System.IO;
using System.Text;

namespace MeowDown.Tests;

public class WebSettingsStoreTests : IDisposable
{
    private readonly WebTestDirectory files = new();
    private readonly MeowDownServerOptions serverOptions = new()
    {
        MaxQueueLength = 100,
        MaxFinishedTasks = 1000,
        FinishedTaskRetentionSeconds = 86400,
    };
    private string SettingsPath => files.PathFor("web-settings.json");

    public void Dispose() => files.Dispose();

    [Fact]
    public void Defaults_ComeFromServerOptionsAndContractConstants()
    {
        var store = CreateStore();

        var settings = store.Snapshot();

        Assert.Equal(100, settings.MaxQueueLength);
        Assert.Equal(WebSettingsStore.DefaultMaxPendingTasksPerUser, settings.MaxPendingTasksPerUser);
        Assert.Equal(1000, settings.MaxFinishedTasks);
        Assert.Equal(86400, settings.FinishedTaskRetentionSeconds);
        Assert.False(settings.AllowRegistration);
        Assert.Equal(WebSettingsStore.DefaultSessionLifetimeHours, settings.SessionLifetimeHours);
    }

    [Fact]
    public void SettingsFileOverridesDefaults()
    {
        File.WriteAllText(SettingsPath, """
            { "maxQueueLength": 50, "allowRegistration": true, "sessionLifetimeHours": 24 }
            """);

        var store = CreateStore();

        var settings = store.Snapshot();
        Assert.Equal(50, settings.MaxQueueLength);
        Assert.True(settings.AllowRegistration);
        Assert.Equal(24, settings.SessionLifetimeHours);
        Assert.Equal(1000, settings.MaxFinishedTasks); // 未给出的键保持默认
    }

    [Fact]
    public void InvalidValuesInSettingsFileAreIgnoredInFavorOfDefaults()
    {
        File.WriteAllText(SettingsPath, """
            { "maxQueueLength": 99999, "sessionLifetimeHours": 0 }
            """);

        var store = CreateStore();

        Assert.Equal(100, store.Snapshot().MaxQueueLength);
        Assert.Equal(WebSettingsStore.DefaultSessionLifetimeHours, store.Snapshot().SessionLifetimeHours);
    }

    [Fact]
    public void CommandLineOverridesBeatSettingsFile()
    {
        File.WriteAllText(SettingsPath, """{ "maxQueueLength": 50, "maxFinishedTasks": 42 }""");
        var launch = new WebLaunchOptions
        {
            MaxQueueLengthOverride = 77,
            MaxFinishedTasksOverride = 43,
        };

        var store = new WebSettingsStore(SettingsPath, serverOptions, launch);

        Assert.Equal(77, store.Snapshot().MaxQueueLength);
        Assert.Equal(43, store.Snapshot().MaxFinishedTasks);
    }

    [Fact]
    public void ExplicitRegistrationFlagBeatsSettingsFile()
    {
        File.WriteAllText(SettingsPath, """{ "allowRegistration": true }""");

        var withFlag = new WebSettingsStore(SettingsPath, serverOptions,
            new WebLaunchOptions { WebAllowRegistration = true });
        var withoutFlag = CreateStore();

        Assert.True(withFlag.Snapshot().AllowRegistration);
        Assert.True(withoutFlag.Snapshot().AllowRegistration); // 未给 flag 时跟随配置文件
    }

    [Fact]
    public void ValidatePatch_ReportsRangeViolationsPerField()
    {
        var store = CreateStore();

        Assert.Null(store.ValidatePatch(new WebAdminPatchConfigRequest
        {
            MaxQueueLength = 10000,
            MaxPendingTasksPerUser = 1000,
            MaxFinishedTasks = 100000,
            FinishedTaskRetentionSeconds = 2592000,
            SessionLifetimeHours = 720,
            AllowRegistration = true,
        }));

        Assert.Contains("maxQueueLength", store.ValidatePatch(new WebAdminPatchConfigRequest { MaxQueueLength = 0 }));
        Assert.Contains("maxPendingTasksPerUser", store.ValidatePatch(new WebAdminPatchConfigRequest { MaxPendingTasksPerUser = 1001 }));
        Assert.Contains("maxFinishedTasks", store.ValidatePatch(new WebAdminPatchConfigRequest { MaxFinishedTasks = 100001 }));
        Assert.Contains("finishedTaskRetentionSeconds", store.ValidatePatch(new WebAdminPatchConfigRequest { FinishedTaskRetentionSeconds = 3599 }));
        Assert.Contains("sessionLifetimeHours", store.ValidatePatch(new WebAdminPatchConfigRequest { SessionLifetimeHours = 721 }));
    }

    [Fact]
    public void ApplyPatch_UpdatesSnapshotAndPersistsToDisk()
    {
        var store = CreateStore();
        store.ApplyPatch(new WebAdminPatchConfigRequest
        {
            MaxQueueLength = 200,
            AllowRegistration = true,
            FinishedTaskRetentionSeconds = 7200,
        });

        var reloaded = CreateStore();
        var settings = reloaded.Snapshot();
        Assert.Equal(200, settings.MaxQueueLength);
        Assert.True(settings.AllowRegistration);
        Assert.Equal(7200, settings.FinishedTaskRetentionSeconds);
    }

    [Fact]
    public void Snapshot_ReturnsIndependentCopy()
    {
        var store = CreateStore();
        var snapshot = store.Snapshot();
        snapshot.MaxQueueLength = 1;

        Assert.NotEqual(1, store.Snapshot().MaxQueueLength);
    }

    private WebSettingsStore CreateStore()
        => new(SettingsPath, serverOptions, null);
}
