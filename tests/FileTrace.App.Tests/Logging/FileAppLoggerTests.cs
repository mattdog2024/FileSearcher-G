using FileTrace.App.Services.Logging;
using FileTrace.App.Tests.TestHelpers;

namespace FileTrace.App.Tests.Logging;

/// <summary>
/// FileAppLogger 的行为验证：确保日志确实落盘、按天分文件命名、
/// 以及最关键的一点——日志写入内部异常不应该向外抛出（否则"记录问题"这件事本身
/// 反而可能造成新的崩溃，违背 Stage4 引入日志的初衷）。
/// </summary>
public class FileAppLoggerTests
{
    [Fact]
    public void LogInfo_WritesLineToTodayLogFile()
    {
        using var dir = new TempDirectory();
        var logger = new FileAppLogger(dir.Path);

        logger.LogInfo("hello world");

        string expectedFile = Path.Combine(dir.Path, $"filetrace-{DateTime.Now:yyyy-MM-dd}.log");
        Assert.True(File.Exists(expectedFile));
        string content = File.ReadAllText(expectedFile);
        Assert.Contains("[INFO]", content);
        Assert.Contains("hello world", content);
    }

    [Fact]
    public void LogError_WithException_IncludesExceptionDetails()
    {
        using var dir = new TempDirectory();
        var logger = new FileAppLogger(dir.Path);

        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            logger.LogError("something failed", ex);
        }

        string expectedFile = Path.Combine(dir.Path, $"filetrace-{DateTime.Now:yyyy-MM-dd}.log");
        string content = File.ReadAllText(expectedFile);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("something failed", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("boom", content);
    }

    [Fact]
    public void LogWarning_MultipleCalls_AppendsRatherThanOverwrites()
    {
        using var dir = new TempDirectory();
        var logger = new FileAppLogger(dir.Path);

        logger.LogWarning("first");
        logger.LogWarning("second");

        string expectedFile = Path.Combine(dir.Path, $"filetrace-{DateTime.Now:yyyy-MM-dd}.log");
        string content = File.ReadAllText(expectedFile);
        Assert.Contains("first", content);
        Assert.Contains("second", content);
    }

    [Fact]
    public void LogInfo_WhenDirectoryUnwritable_DoesNotThrow()
    {
        // 用一个"父目录是一个已存在的普通文件"的路径模拟无法创建日志目录的场景，
        // 验证 FileAppLogger 按设计静默吞掉异常，而不是把日志故障升级成新的崩溃。
        using var dir = new TempDirectory();
        string blockerFile = Path.Combine(dir.Path, "blocker");
        File.WriteAllText(blockerFile, "not a directory");
        string invalidLogDir = Path.Combine(blockerFile, "logs");

        var logger = new FileAppLogger(invalidLogDir);

        var exception = Record.Exception(() => logger.LogError("should not throw", new Exception("inner")));
        Assert.Null(exception);
    }

    [Fact]
    public void CreatesLogDirectory_WhenMissing()
    {
        using var dir = new TempDirectory();
        string nestedLogDir = Path.Combine(dir.Path, "nested", "logs");
        var logger = new FileAppLogger(nestedLogDir);

        logger.LogInfo("first entry");

        Assert.True(Directory.Exists(nestedLogDir));
    }
}
