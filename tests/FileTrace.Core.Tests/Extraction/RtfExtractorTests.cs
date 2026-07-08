using System.Text;
using FileTrace.Core.Extraction;
using FileTrace.Core.Tests.TestHelpers;

namespace FileTrace.Core.Tests.Extraction;

public class RtfExtractorTests
{
    [Fact]
    public async Task ExtractAsync_AsciiRtf_StripsControlWordsAndKeepsText()
    {
        using var dir = new TempDirectory();
        string rtf = "{\\rtf1\\ansi\\deff0\n" +
                     "{\\fonttbl{\\f0 Times New Roman;}}\n" +
                     "\\f0\\fs24 Hello World! This is a \\b bold\\b0  test.\\par\n" +
                     "Second line with \\i italic\\i0  text.\\par\n}";
        string path = dir.CreateBinaryFile("test.rtf", Encoding.Latin1.GetBytes(rtf));

        var extractor = new RtfExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Hello World! This is a", result.Content);
        Assert.Contains("bold", result.Content);
        Assert.Contains("Second line with", result.Content);
        Assert.Contains("italic", result.Content);
        // 控制字本身不应该出现在结果里
        Assert.DoesNotContain("\\b0", result.Content);
        Assert.DoesNotContain("fonttbl", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_GbkHexEscapedChinese_DecodesCorrectlyViaAnsiCpg()
    {
        // 回归测试：验证 \ansicpg936 声明能正确驱动 \'hh 十六进制转义的 GBK 双字节解码，
        // 这是此前发现并修复过的一个真实bug（最初实现里全文启发式探测会被ASCII控制字
        // 主导，误判成ASCII/latin，导致中文变成一堆问号）。
        using var dir = new TempDirectory();
        byte[] gbkBytes = Encoding.GetEncoding("GBK").GetBytes("中文测试");
        string hexEscapes = string.Concat(gbkBytes.Select(b => $"\\'{b:x2}"));
        string rtf = "{\\rtf1\\ansi\\ansicpg936\\deff0\n{\\fonttbl{\\f0 SimSun;}}\n\\f0\\fs24 " + hexEscapes + " Hello\\par\n}";
        string path = dir.CreateBinaryFile("gbk.rtf", Encoding.Latin1.GetBytes(rtf));

        var extractor = new RtfExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("中文测试", result.Content);
        Assert.Contains("Hello", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_UnicodeEscape_DecodesCorrectly()
    {
        using var dir = new TempDirectory();
        // \u20013 是 "中" 的十进制码点转义（RTF Unicode 转义格式），后面紧跟一个 ANSI 后备字符 '?'
        string rtf = "{\\rtf1\\ansi\\deff0\\uc1 \\u20013?\\u25991?\\par}";
        string path = dir.CreateBinaryFile("unicode.rtf", Encoding.Latin1.GetBytes(rtf));

        var extractor = new RtfExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("中文", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_SkipsFontTableAndColorTableGroups()
    {
        using var dir = new TempDirectory();
        string rtf = "{\\rtf1\\ansi{\\fonttbl{\\f0 Arial;}}{\\colortbl;\\red255\\green0\\blue0;}\\f0 Visible text\\par}";
        string path = dir.CreateBinaryFile("skipgroups.rtf", Encoding.Latin1.GetBytes(rtf));

        var extractor = new RtfExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Visible text", result.Content);
        Assert.DoesNotContain("Arial", result.Content);
        Assert.DoesNotContain("red255", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_EmptyFile_ReturnsSuccessWithEmptyContent()
    {
        using var dir = new TempDirectory();
        string path = dir.CreateBinaryFile("empty.rtf", Array.Empty<byte>());

        var extractor = new RtfExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Content);
    }
}
