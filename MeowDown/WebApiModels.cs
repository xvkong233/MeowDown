using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MeowDown;

#region 请求体

internal sealed class WebRegisterRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

internal sealed class WebLoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

internal sealed class WebPasswordChangeRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

internal sealed class WebParseRequest
{
    public string Url { get; set; } = "";
}

internal sealed class WebAdminCreateUserRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Role { get; set; }
}

internal sealed class WebAdminPatchUserRequest
{
    public string? Role { get; set; }
    public bool? Disabled { get; set; }
}

internal sealed class WebAdminResetPasswordRequest
{
    public string NewPassword { get; set; } = "";
}

internal sealed class WebAdminPatchConfigRequest
{
    public int? MaxQueueLength { get; set; }
    public int? MaxPendingTasksPerUser { get; set; }
    public int? MaxFinishedTasks { get; set; }
    public long? FinishedTaskRetentionSeconds { get; set; }
    public bool? AllowRegistration { get; set; }
    public int? SessionLifetimeHours { get; set; }
}

#endregion

#region 响应体

internal sealed record WebErrorBody(string Code, string Message);

internal sealed record WebErrorResponse(WebErrorBody Error);

internal sealed record WebAuthUserResponse(
    string Id, string Username, string Role, bool PasswordChangeRequired, bool BilibiliBound);

internal sealed record WebUserCreatedResponse(string Id, string Username, string Role);

internal sealed record WebBindStartResponse(string BindId, string QrUrl, long ExpiresAt);

internal sealed record WebBindStatusResponse(string BindId, string Status, string? Mid, string? Uname, string? Message);

internal sealed record WebBindingResponse(bool Bound, string? Mid, string? Uname, long? BoundAt);

internal sealed record WebParsePage(int Index, string Cid, string Title, int DurationSeconds, string? Cover);

internal sealed record WebParseQuality(int Id, string Name);

internal sealed record WebParseResponse(
    string Aid, string Bvid, string Title, string Desc, string Pic, long PubTime, bool IsBangumi,
    List<WebParsePage> Pages, List<WebParseQuality> Qualities);

internal sealed record WebAdminUserResponse(
    string Id, string Username, string Role, bool Disabled, long CreatedAt,
    bool BilibiliBound, string? BilibiliMid, string? BilibiliUname,
    int PendingTasks, int RunningTasks);

internal sealed record WebAdminUserListResponse(List<WebAdminUserResponse> Users);

internal sealed record WebConfigWritable(
    int MaxQueueLength, int MaxPendingTasksPerUser, int MaxFinishedTasks,
    long FinishedTaskRetentionSeconds, bool AllowRegistration, int SessionLifetimeHours);

internal sealed record WebConfigReadOnly(
    string ListenUrl, string DownloadRoot, string WebDataDir,
    bool AllowAria2cArgs, bool AllowCustomOutput, bool AllowCustomNetworkHosts, bool AllowPrivateCallbacks,
    string Version);

internal sealed record WebConfigResponse(WebConfigWritable Writable, WebConfigReadOnly ReadOnly);

#endregion

/// <summary>
/// Web API 的 JSON 契约（camelCase 字段名，请求读取大小写不敏感）。
/// 旧 Token API 继续使用 AppJsonSerializerContext（PascalCase），互不影响。
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WebErrorResponse))]
[JsonSerializable(typeof(WebAuthUserResponse))]
[JsonSerializable(typeof(WebUserCreatedResponse))]
[JsonSerializable(typeof(WebBindStartResponse))]
[JsonSerializable(typeof(WebBindStatusResponse))]
[JsonSerializable(typeof(WebBindingResponse))]
[JsonSerializable(typeof(WebParseResponse))]
[JsonSerializable(typeof(WebRegisterRequest))]
[JsonSerializable(typeof(WebLoginRequest))]
[JsonSerializable(typeof(WebPasswordChangeRequest))]
[JsonSerializable(typeof(WebParseRequest))]
[JsonSerializable(typeof(WebAdminCreateUserRequest))]
[JsonSerializable(typeof(WebAdminPatchUserRequest))]
[JsonSerializable(typeof(WebAdminResetPasswordRequest))]
[JsonSerializable(typeof(WebAdminPatchConfigRequest))]
[JsonSerializable(typeof(DownloadTask))]
[JsonSerializable(typeof(DownloadTaskCollection))]
[JsonSerializable(typeof(TaskSubmissionResult))]
[JsonSerializable(typeof(WebAdminUserListResponse))]
[JsonSerializable(typeof(WebAdminUserResponse))]
[JsonSerializable(typeof(WebConfigResponse))]
internal partial class WebJsonContext : JsonSerializerContext
{
}

/// <summary>本地存储文件（web-users.json / web-settings.json / bili/*.json）的序列化契约（camelCase）。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<WebUser>))]
[JsonSerializable(typeof(WebSettings))]
[JsonSerializable(typeof(WebBiliBinding))]
internal partial class WebStorageJsonContext : JsonSerializerContext
{
}
