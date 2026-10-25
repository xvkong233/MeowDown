using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using static MeowDown.MeowDownLoginUtil;

namespace MeowDown;

/// <summary>单用户的B站绑定凭据，落盘到 &lt;web-data-dir&gt;/bili/&lt;userId&gt;.json。</summary>
internal sealed class WebBiliBinding
{
    public string Cookie { get; set; } = "";
    public string? RefreshToken { get; set; }
    public string Mid { get; set; } = "";
    public string Uname { get; set; } = "";
    public long BoundAt { get; set; }
}

/// <summary>每用户隔离的B站凭据存储；文件经 WritePrivateFile 原子写入并收紧权限。</summary>
internal sealed class WebBiliBindingStore
{
    private readonly string directory;

    public WebBiliBindingStore(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
    }

    /// <summary>凭据不存在或文件损坏时返回 null。</summary>
    public WebBiliBinding? Load(string userId)
    {
        if (!IsSafeUserId(userId) || !File.Exists(PathFor(userId)))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(PathFor(userId)),
                WebStorageJsonContext.Default.WebBiliBinding);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.WriteLine($"用户B站凭据文件读取失败，将视为未绑定。原因：{ex.Message}");
            return null;
        }
    }

    public bool Exists(string userId) => IsSafeUserId(userId) && File.Exists(PathFor(userId));

    public void Save(string userId, WebBiliBinding binding)
    {
        // 写入路径与 Load/Exists/Delete 同等设防：userId 拼进路径前先做防御校验（防御纵深）
        if (!IsSafeUserId(userId))
        {
            throw new ArgumentException("非法的userId，拒绝写入B站凭据文件", nameof(userId));
        }
        WritePrivateFile(PathFor(userId), Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(binding, WebStorageJsonContext.Default.WebBiliBinding)));
    }

    /// <summary>删除凭据文件；文件本就不存在时同样返回 true（幂等）。</summary>
    public bool Delete(string userId)
    {
        if (!IsSafeUserId(userId))
        {
            return false;
        }

        try
        {
            File.Delete(PathFor(userId));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"删除用户B站凭据文件失败: {ex.Message}");
            return false;
        }
    }

    internal string PathFor(string userId) => Path.Combine(directory, $"{userId}.json");

    /// <summary>userId 由服务端生成（u_ + 十六进制），拼进路径前仍做防御校验。</summary>
    internal static bool IsSafeUserId(string userId)
        => userId.Length > 2 && userId.StartsWith("u_", StringComparison.Ordinal)
            && userId[2..].All(char.IsAsciiHexDigit);
}
