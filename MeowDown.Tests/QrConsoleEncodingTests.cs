using System.Text;
using QRCoder;

namespace MeowDown.Tests;

public class QrConsoleEncodingTests
{
    static QrConsoleEncodingTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static readonly MeowDownLoginUtil.QrGlyphs HalfBlocks = new('█', '▀', '▄', ' ', DoubleRows: false);

    [Fact]
    public void Utf8_RepresentAllHalfBlockGlyphs()
    {
        Assert.True(MeowDownLoginUtil.EncodingSupports(Encoding.UTF8, HalfBlocks));
    }

    [Fact]
    public void Gbk_MissingUpperHalfBlock_ButRepresentFallbackGlyphs()
    {
        var gbk = Encoding.GetEncoding(936);
        Assert.False(MeowDownLoginUtil.EncodingSupports(gbk, HalfBlocks));
        Assert.True(MeowDownLoginUtil.EncodingSupports(gbk,
            new MeowDownLoginUtil.QrGlyphs('█', '█', '█', ' ', DoubleRows: true)));
        Assert.True(MeowDownLoginUtil.EncodingSupports(gbk,
            new MeowDownLoginUtil.QrGlyphs('#', '#', '#', ' ', DoubleRows: true)));
    }

    [Fact]
    public void CreateQrRows_HalfBlockPacksTwoModuleRowsPerLine()
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode("https://example.com/meowdown", QRCodeGenerator.ECCLevel.Q);
        var rows = MeowDownLoginUtil.CreateQrRows(data);
        Assert.Equal((data.ModuleMatrix.Count + 1) / 2, rows.Length);
        Assert.All(rows, row => Assert.Equal(data.ModuleMatrix.Count, row.Length));
    }

    [Fact]
    public void CreateQrRows_FallbackDoublesRowsAndUsesOnlyRepresentableChars()
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode("https://example.com/meowdown", QRCodeGenerator.ECCLevel.Q);
        var rows = MeowDownLoginUtil.CreateQrRows(data,
            new MeowDownLoginUtil.QrGlyphs('#', '#', '#', ' ', DoubleRows: true));
        Assert.Equal(data.ModuleMatrix.Count * 2, rows.Length);
        Assert.All(rows, row => Assert.Equal(data.ModuleMatrix.Count, row.Length));
        Assert.All(rows, row => Assert.All(row, c => Assert.True(c is '#' or ' ')));
    }
}
