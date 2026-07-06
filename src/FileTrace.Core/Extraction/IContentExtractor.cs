namespace FileTrace.Core.Extraction;

/// <summary>
/// 单一文件格式的全文内容提取器。每种格式（docx/xlsx/pdf/txt/代码...）实现一个。
/// 设计原则：任何解析异常（损坏文件、加密文件、格式不兼容）都必须在实现内部捕获并转成
/// <see cref="ExtractResult.Failure"/>，绝不能让异常向上抛出中断整个索引任务。
/// </summary>
public interface IContentExtractor
{
    /// <summary>本提取器能处理的扩展名列表（不含点，小写）。</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>
    /// 从文件中提取全文内容。
    /// </summary>
    /// <param name="filePath">文件完整路径。</param>
    /// <param name="cancellationToken">取消令牌，用于响应索引任务的暂停/取消。</param>
    Task<ExtractResult> ExtractAsync(string filePath, CancellationToken cancellationToken);
}

/// <summary>提取结果。</summary>
public sealed class ExtractResult
{
    public bool Success { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? FailureReason { get; init; }

    public static ExtractResult Ok(string content) => new() { Success = true, Content = content };

    public static ExtractResult Fail(string reason) => new() { Success = false, FailureReason = reason };
}
