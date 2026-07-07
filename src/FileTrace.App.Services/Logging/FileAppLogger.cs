using System.Text;

namespace FileTrace.App.Services.Logging;

/// <summary>
/// 把日志追加写入 <c>{logDirectory}/filetrace-yyyy-MM-dd.log</c> 的简单实现。
///
/// 设计要点：
/// - 按天分文件，避免单个日志文件无限增长；旧日志文件的清理由用户/未来功能自行处理，
///   本类不做自动删除（删错用户想保留的日志比日志占一点磁盘空间的代价更大）。
/// - 所有写入都包裹在 try/catch 里静默失败：日志功能本身绝不能成为新的崩溃源
///   （例如日志目录所在盘意外只读/已被拔出），这是"打磨"阶段最容易忽略但最重要的细节。
/// - 用一个简单的 lock 保证多线程/多任务并发写入时同一行不会互相交错。
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    private readonly string _logDirectory;
    private readonly object _writeLock = new();

    public FileAppLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
    }

    public void LogInfo(string message) => Write("INFO", message);

    public void LogWarning(string message) => Write("WARN", message);

    public void LogError(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_writeLock)
            {
                Directory.CreateDirectory(_logDirectory);
                string filePath = Path.Combine(_logDirectory, $"filetrace-{DateTime.Now:yyyy-MM-dd}.log");
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(filePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志写入失败不应该向上抛出——那样反而可能把一次原本可以被优雅处理的错误
            // 升级成一次真正的崩溃。这里选择彻底静默，是"日志系统"这类辅助设施的合理取舍。
        }
    }
}
