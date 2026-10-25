using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static MeowDown.MeowDownLoginUtil;

namespace MeowDown;

[JsonConverter(typeof(JsonStringEnumConverter<WebUserRole>))]
public enum WebUserRole
{
    User,
    Admin,
}

/// <summary>带「最后一个可用管理员」保护的变更结果。</summary>
internal enum WebAdminGuardResult
{
    Ok,
    NotFound,
    LastAdminConflict,
}

/// <summary>
/// Web 用户账号（含口令哈希与角色），持久化到 &lt;web-data-dir&gt;/web-users.json。
/// 文件经 WritePrivateFile 原子写入并收紧权限；用户名唯一按大小写不敏感比较、原样存储。
/// </summary>
internal sealed class WebUser
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public WebUserRole Role { get; set; } = WebUserRole.User;
    public bool Disabled { get; set; }
    public long CreatedAt { get; set; }
    public bool PasswordChangeRequired { get; set; }
}

/// <summary>
/// 用户库：内存持有、整文件原子落盘。实例方法返回的对象为活动引用，
/// 调用方就地修改后须调用 <see cref="Update"/> / <see cref="Persist"/> 持久化。
/// </summary>
internal sealed class WebUserStore
{
    private readonly object stateLock = new();
    private readonly string filePath;
    private readonly Func<long> nowSeconds;
    private readonly Func<string, string> hashPassword;
    private readonly Func<string> passwordGenerator;
    private List<WebUser> users;

    public WebUserStore(string filePath, Func<long>? nowSeconds = null,
        Func<string, string>? hashPassword = null, Func<string>? passwordGenerator = null)
    {
        this.filePath = filePath;
        this.nowSeconds = nowSeconds ?? (() => DateTimeOffset.Now.ToUnixTimeSeconds());
        this.hashPassword = hashPassword ?? (password => WebPasswordHasher.Hash(password));
        this.passwordGenerator = passwordGenerator ?? GeneratePassword;
        users = Load();
    }

    public bool IsEmpty
    {
        get { lock (stateLock) return users.Count == 0; }
    }

    public WebUser? FindById(string id)
    {
        lock (stateLock) return users.FirstOrDefault(user => user.Id == id);
    }

    public WebUser? FindByUsername(string username)
    {
        lock (stateLock) return FindByUsernameLocked(username);
    }

    public List<WebUser> All()
    {
        lock (stateLock) return [.. users];
    }

    /// <summary>创建用户；用户名冲突（大小写不敏感）返回 null。</summary>
    public WebUser? Create(string username, string password, WebUserRole role, bool passwordChangeRequired = false)
    {
        lock (stateLock)
        {
            if (FindByUsernameLocked(username) is not null)
            {
                return null;
            }

            var user = new WebUser
            {
                Id = NewUserId(),
                Username = username,
                PasswordHash = hashPassword(password),
                Role = role,
                CreatedAt = nowSeconds(),
                PasswordChangeRequired = passwordChangeRequired,
            };
            users.Add(user);
            PersistLocked();
            return user;
        }
    }

    /// <summary>就地修改用户字段后调用以持久化整个用户库。</summary>
    public void Update(WebUser user)
    {
        lock (stateLock) PersistLocked();
    }

    /// <summary>
    /// 原子应用角色/停用变更：检查与修改在同一把用户库锁内完成，
    /// 防止「最后一个可用管理员」保护被并发请求绕过（两个管理员互相降级）。
    /// 用户不存在返回 <see cref="WebAdminGuardResult.NotFound"/>（不修改）；
    /// 变更将使可用管理员归零返回 <see cref="WebAdminGuardResult.LastAdminConflict"/>（不修改）。
    /// </summary>
    public WebAdminGuardResult TryUpdateRoleOrDisabled(string userId, WebUserRole? newRole, bool? disabled)
    {
        lock (stateLock)
        {
            var target = FindByIdLocked(userId);
            if (target is null)
            {
                return WebAdminGuardResult.NotFound;
            }

            var removesActiveAdmin = target.Role == WebUserRole.Admin && !target.Disabled
                && ((newRole is { } demotedRole && demotedRole != WebUserRole.Admin) || disabled == true);
            if (removesActiveAdmin && !HasActiveAdminLocked(excludingUserId: target.Id))
            {
                return WebAdminGuardResult.LastAdminConflict;
            }

            if (newRole is { } role) target.Role = role;
            if (disabled is { } value) target.Disabled = value;
            PersistLocked();
            return WebAdminGuardResult.Ok;
        }
    }

    /// <summary>
    /// 原子删除用户，同样带「最后一个可用管理员」保护（检查+删除同锁）。
    /// </summary>
    public WebAdminGuardResult TryRemoveWithAdminGuard(string userId)
    {
        lock (stateLock)
        {
            var target = FindByIdLocked(userId);
            if (target is null)
            {
                return WebAdminGuardResult.NotFound;
            }

            if (target.Role == WebUserRole.Admin && !target.Disabled
                && !HasActiveAdminLocked(excludingUserId: userId))
            {
                return WebAdminGuardResult.LastAdminConflict;
            }

            users.RemoveAll(user => user.Id == userId);
            PersistLocked();
            return WebAdminGuardResult.Ok;
        }
    }

    public void UpdatePasswordHash(WebUser user, string newHash,
        bool markChangeRequired = false, bool clearChangeRequired = false)
    {
        lock (stateLock)
        {
            user.PasswordHash = newHash;
            if (markChangeRequired) user.PasswordChangeRequired = true;
            if (clearChangeRequired) user.PasswordChangeRequired = false;
            PersistLocked();
        }
    }

    public bool Remove(string userId)
    {
        lock (stateLock)
        {
            var removed = users.RemoveAll(user => user.Id == userId);
            if (removed > 0) PersistLocked();
            return removed > 0;
        }
    }

    /// <summary>用户库为空时创建引导管理员；初始密码未修改状态（登录响应携带 passwordChangeRequired）。</summary>
    public (WebUser User, string Password) BootstrapAdmin(string? initialPassword)
    {
        lock (stateLock)
        {
            if (users.Count > 0)
            {
                throw new InvalidOperationException("Web用户库非空，跳过管理员账号引导");
            }

            var password = initialPassword ?? passwordGenerator();
            var user = new WebUser
            {
                Id = NewUserId(),
                Username = "admin",
                PasswordHash = hashPassword(password),
                Role = WebUserRole.Admin,
                CreatedAt = nowSeconds(),
                PasswordChangeRequired = true,
            };
            users.Add(user);
            PersistLocked();
            return (user, password);
        }
    }

    public void Persist()
    {
        lock (stateLock) PersistLocked();
    }

    private WebUser? FindByUsernameLocked(string username)
        => users.FirstOrDefault(user => string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase));

    private WebUser? FindByIdLocked(string id)
        => users.FirstOrDefault(user => user.Id == id);

    private bool HasActiveAdminLocked(string excludingUserId)
        => users.Any(user => user.Role == WebUserRole.Admin && !user.Disabled && user.Id != excludingUserId);

    private void PersistLocked()
        => WritePrivateFile(filePath, Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(users, WebStorageJsonContext.Default.ListWebUser)));

    private List<WebUser> Load()
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(filePath),
                WebStorageJsonContext.Default.ListWebUser) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 用户库损坏时拒绝启动而不是静默重置，避免把既有账号全部抹掉。
            throw new InvalidOperationException($"Web用户库文件损坏，无法启动Web服务: {filePath}", ex);
        }
    }

    internal static string NewUserId()
        => "u_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    /// <summary>16 位随机密码（大小写字母+数字），仅用于管理员账号引导。</summary>
    internal static string GeneratePassword()
        => new(RandomNumberGenerator.GetItems<char>(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 16));

    internal static string? ValidateUsername(string? username)
    {
        if (username is null || username.Length is < 3 or > 32
            || !username.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
        {
            return "用户名须为3-32位字母、数字、下划线或连字符";
        }

        return null;
    }

    internal static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length is < 8 or > 128)
        {
            return "密码长度须为8-128位";
        }

        return null;
    }
}
