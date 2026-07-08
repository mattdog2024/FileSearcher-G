namespace FileTrace.App.Services.Logging;

/// <summary>
/// 应用级简单日志抽象。Stage4 引入的目的很单纯：便携版发给最终用户时不会附带调试器，
/// 一旦出现异常需要有文件留痕，让用户能把 data/logs 目录下的日志发回来定位问题。
///
/// 刻意没有引入 Serilog/NLog/Microsoft.Extensions.Logging 之类的外部依赖——本项目当前
/// 只需要"追加写入按天分割的文本文件"这一个能力，一个几十行的实现足够，减少一个可能出问题的
/// 第三方依赖对便携版体积和兼容性都更有利。
/// </summary>
public interface IAppLogger
{
    void LogInfo(string message);

    void LogWarning(string message);

    void LogError(string message, Exception? exception = null);
}
