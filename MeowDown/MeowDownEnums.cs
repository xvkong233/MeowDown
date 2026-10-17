using System;
using System.Linq;

namespace MeowDown;

public enum MeowDownDanmakuFormat
{
    Xml,
    Ass,
}

public static class MeowDownDanmakuFormatInfo
{
    // 默认
    public static readonly MeowDownDanmakuFormat[] DefaultFormats = [MeowDownDanmakuFormat.Xml, MeowDownDanmakuFormat.Ass];
    public static readonly string[] DefaultFormatsNames = DefaultFormats.Select(f => f.ToString().ToLower()).ToArray();
    // 可选项
    public static readonly string[] AllFormatNames = Enum.GetNames(typeof(MeowDownDanmakuFormat)).Select(f => f.ToLower()).ToArray();

    public static MeowDownDanmakuFormat FromFormatName(string formatName)
    {
        return formatName switch
        {
            "xml" => MeowDownDanmakuFormat.Xml,
            "ass" => MeowDownDanmakuFormat.Ass,
            _ => MeowDownDanmakuFormat.Xml,
        };
    }
}
