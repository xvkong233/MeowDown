namespace MeowDown.Tests;

public class BinaryLookupTests
{
    [Fact]
    public void ExplicitToolPaths_StillWorkAfterTheWorkingDirectoryChanges()
    {
        using var files = new MediaTestDirectory();
        files.CreateDirectory("tools");
        files.Write("tools/ffmpeg", "ffmpeg");
        files.Write("tools/aria2c", "aria2c");
        var previous = (Environment.CurrentDirectory, MeowDownMuxer.FFMPEG, MeowDownAria2c.ARIA2C);
        try
        {
            Environment.CurrentDirectory = files.Root;
            Program.FindBinaries(new MyOption { FFmpegPath = "tools/ffmpeg", Aria2cPath = "tools/aria2c", SkipMux = true });
            Environment.CurrentDirectory = previous.CurrentDirectory;

            Assert.Equal("ffmpeg", File.ReadAllText(MeowDownMuxer.FFMPEG));
            Assert.Equal("aria2c", File.ReadAllText(MeowDownAria2c.ARIA2C));
        }
        finally
        {
            (Environment.CurrentDirectory, MeowDownMuxer.FFMPEG, MeowDownAria2c.ARIA2C) = previous;
        }
    }

    [Fact]
    public void Mp4box_IsFoundUnderTheNameGpacInstalls()
    {
        using var files = new MediaTestDirectory();
        files.Write("MP4Box" + (OperatingSystem.IsWindows() ? ".exe" : ""), "gpac");
        var previous = (Environment.CurrentDirectory, MeowDownMuxer.MP4BOX);
        try
        {
            Environment.CurrentDirectory = files.Root;
            MeowDownMuxer.MP4BOX = "missing-mp4box";

            var resolved = Program.ResolveMp4box();

            Assert.NotNull(resolved);
            Assert.Equal(resolved, MeowDownMuxer.MP4BOX);
            Assert.Equal("gpac", File.ReadAllText(resolved));
        }
        finally
        {
            (Environment.CurrentDirectory, MeowDownMuxer.MP4BOX) = previous;
        }
    }
}
