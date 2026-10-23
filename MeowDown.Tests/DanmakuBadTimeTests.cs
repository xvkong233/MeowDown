using MeowDown.Core;

namespace MeowDown.Tests;

public class DanmakuBadTimeTests
{
    [Fact]
    public void DanmakuWithIllegalTime_IsSkippedWhileLegalOnesAreKept()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("danmaku.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><i>"
            + "<d p=\"NOT_A_TIME,1,25,16711680,0,0,abc,1\">坏时间弹幕</d>"
            + "<d p=\"2,1,25,16777215,0,0,abc,2\">好弹幕</d></i>");

        var danmakus = DanmakuUtil.ParseXml(path);

        Assert.NotNull(danmakus);
        var danmaku = Assert.Single(danmakus);
        Assert.Equal("好弹幕", danmaku.Content);
    }

    [Fact]
    public void TryParse_ReturnsNullForIllegalTimeAndKeepsLegalTime()
    {
        Assert.Null(DanmakuUtil.DanmakuItem.TryParse("NOT_A_TIME,1,25,0,0,0,abc,1".Split(','), "弹幕"));

        var danmaku = DanmakuUtil.DanmakuItem.TryParse("1.5,1,25,0,0,0,abc,1".Split(','), "弹幕");

        Assert.NotNull(danmaku);
        Assert.Equal("0:00:01.50", danmaku.StartTime);
        Assert.Equal("弹幕", danmaku.Content);
    }

    [Fact]
    public async Task IllegalTimeDanmaku_DoesNotProduceABrokenDialogueLine()
    {
        using var files = new MediaTestDirectory();
        var xmlPath = files.Write("danmaku.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><i>"
            + "<d p=\"NOT_A_TIME,1,25,16711680,0,0,abc,1\">坏时间弹幕</d>"
            + "<d p=\"2,1,25,16777215,0,0,abc,2\">好弹幕</d></i>");
        var outputPath = files.FilePath("danmaku.ass");

        var danmakus = DanmakuUtil.ParseXml(xmlPath);
        Assert.NotNull(danmakus);
        await DanmakuUtil.SaveAsAssAsync(danmakus, outputPath);

        var events = (await File.ReadAllLinesAsync(outputPath)).Where(line => line.StartsWith("Dialogue:")).ToArray();
        var legal = Assert.Single(events);
        Assert.Equal("Dialogue: 2,0:00:02.00,0:00:10.00,BBDOWNT_Style,,0000,0000,0000,,{\\move(1920, 0, -120, 0)}好弹幕", legal);
        Assert.DoesNotContain(events, line => line.Contains(",,,")); // 修复前损坏行形如 "Dialogue: 2,,,"
    }

    [Fact]
    public async Task LegalDanmaku_AssOutputStaysByteIdenticalToBeforeTheFix()
    {
        using var files = new MediaTestDirectory();
        var xmlPath = files.Write("danmaku.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><i>"
            + "<d p=\"2,1,25,16777215,0,0,abc,1\">好弹幕</d></i>");
        var outputPath = files.FilePath("danmaku.ass");

        var danmakus = DanmakuUtil.ParseXml(xmlPath);
        Assert.NotNull(danmakus);
        await DanmakuUtil.SaveAsAssAsync(danmakus, outputPath);

        var expected = string.Join(Environment.NewLine,
        [
            "[Script Info]",
            "Script Updated By: MeowDown(https://github.com/LOVAHE/BBDownT)",
            "ScriptType: v4.00+",
            "PlayResX: 1920",
            "PlayResY: 1080",
            "Aspect Ratio: 1920:1080",
            "Collisions: Normal",
            "WrapStyle: 2",
            "ScaledBorderAndShadow: yes",
            "YCbCr Matrix: TV.601",
            "[V4+ Styles]",
            "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding",
            "Style: BBDOWNT_Style, 黑体, 40, &H00FFFFFF, &H00FFFFFF, &H00000000, &H00000000, 0, 0, 0, 0, 100, 100, 0.00, 0.00, 1, 2, 0, 7, 0, 0, 0, 0",
            "[Events]",
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text",
            "Dialogue: 2,0:00:02.00,0:00:10.00,BBDOWNT_Style,,0000,0000,0000,,{\\move(1920, 0, -120, 0)}好弹幕"
        ]) + Environment.NewLine;
        Assert.Equal(expected, await File.ReadAllTextAsync(outputPath));
    }
}
