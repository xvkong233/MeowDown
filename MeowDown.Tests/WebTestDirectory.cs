using System;

namespace MeowDown.Tests;

/// <summary>Web 存储类测试用的临时目录（递归清理，不校验未注册产物）。</summary>
internal sealed class WebTestDirectory : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "bbdownt-web-" + Guid.NewGuid().ToString("N"));

    public WebTestDirectory()
    {
        Directory.CreateDirectory(Root);
    }

    internal string PathFor(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
