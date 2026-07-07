namespace FileTrace.App.Services.Logging;

/// <summary>
/// 空实现：用于测试代码或尚未显式注入 <see cref="IAppLogger"/> 时的默认兜底，
/// 避免到处写 null 检查。
/// </summary>
public sealed class NullAppLogger : IAppLogger
{
    public static readonly NullAppLogger Instance = new();

    private NullAppLogger()
    {
    }

    public void LogInfo(string message)
    {
    }

    public void LogWarning(string message)
    {
    }

    public void LogError(string message, Exception? exception = null)
    {
    }
}
