using System;

namespace MeowDown;

/// <summary>
/// Web 服务的启动期参数（serve 命令传入）。除配置覆盖项外均为只读安全边界，
/// 运行期不可修改；可修改的运行时配置见 <see cref="WebSettingsStore"/>。
/// </summary>
public sealed record WebLaunchOptions
{
    /// <summary>Web 服务数据目录（用户账号、绑定凭据、运行时配置），空表示默认 &lt;程序目录&gt;/web-data。</summary>
    public string? WebDataDir { get; init; }

    /// <summary>引导管理员账号的初始密码；未提供时生成随机密码并打印到日志。</summary>
    public string? WebAdminPassword { get; init; }

    /// <summary>强制会话 Cookie 附加 Secure（部署在 HTTPS 反向代理之后时使用）。</summary>
    public bool WebSecureCookie { get; init; }

    /// <summary>启动时显式开启自助注册；null 表示未在命令行给出（跟随 web-settings.json）。</summary>
    public bool? WebAllowRegistration { get; init; }

    // 显式给出的旧有队列/保留参数优先级高于 web-settings.json（默认值 < 配置文件 < 命令行）。
    public int? MaxQueueLengthOverride { get; init; }
    public int? MaxFinishedTasksOverride { get; init; }
    public long? FinishedTaskRetentionSecondsOverride { get; init; }
}
