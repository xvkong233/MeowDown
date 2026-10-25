using System;
using System.Security.Cryptography;

namespace MeowDown;

/// <summary>
/// 口令哈希：PBKDF2-HMAC-SHA256，16 字节随机盐、32 字节派生密钥，
/// 存储格式 pbkdf2-sha256$&lt;iterations&gt;$&lt;base64(salt)&gt;$&lt;base64(hash)&gt;。
/// 校验按存储值中的迭代数执行并用 FixedTimeEquals 比较；迭代数只增不减，
/// 存储值低于当前默认迭代数时在下次成功认证后重哈希升级。
/// </summary>
internal static class WebPasswordHasher
{
    internal const int DefaultIterations = 600_000;
    internal const int SaltSize = 16;
    internal const int KeySize = 32;
    private const string Prefix = "pbkdf2-sha256";

    // 用户名不存在时的等时哈希目标，避免登录响应时间泄露用户名是否存在。
    internal static readonly string DummyHash = Hash("meowdown-dummy-account");

    internal static string Hash(string password, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    internal static bool Verify(string password, string? stored)
    {
        if (!TryParse(stored, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    internal static bool NeedsUpgrade(string? stored)
        => TryParse(stored, out var iterations, out _, out _) && iterations < DefaultIterations;

    private static bool TryParse(string? stored, out int iterations, out byte[] salt, out byte[] expected)
    {
        iterations = 0;
        salt = expected = [];
        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix
            || !int.TryParse(parts[1], out iterations) || iterations < 1)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && expected.Length > 0;
    }
}
