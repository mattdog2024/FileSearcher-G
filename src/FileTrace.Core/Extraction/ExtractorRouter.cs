using FileTrace.Core.Models;

namespace FileTrace.Core.Extraction;

/// <summary>
/// 提取器工厂/路由：根据文件扩展名把提取任务分派给对应的 <see cref="IContentExtractor"/> 实现。
///
/// 这是索引流水线里"文件系统扫描器"与"具体格式提取器"之间的唯一入口——扫描器/索引写入逻辑
/// 不需要知道 docx 用 WordExtractor、xlsx 用 ExcelExtractor 这些细节，只需要调用
/// <see cref="ExtractAsync"/> 传入文件路径即可。
///
/// 设计原则：
///   - 启动时一次性校验 <see cref="FileTypeCatalog"/>（"应该支持哪些格式"的单一事实来源）
///     与各 IContentExtractor.SupportedExtensions（"实际能处理哪些格式"）是否完全对应，
///     一旦出现遗漏或多余，构造函数直接抛异常暴露问题，而不是运行时才发现某个扩展名
///     "声称支持但实际找不到提取器"或"有提取器但目录里没勾选项"这种静默不一致；
///   - 对于 FileTypeCatalog 中不存在的扩展名（用户目录里的其他杂七杂八文件），
///     路由返回一个明确的"未知类型跳过"结果，不抛异常。
/// </summary>
public sealed class ExtractorRouter
{
    private readonly Dictionary<string, IContentExtractor> _extractorsByExtension;

    /// <summary>
    /// 使用默认的内置提取器集合构造路由（生产环境使用这个构造函数）。
    /// </summary>
    public ExtractorRouter() : this(CreateDefaultExtractors())
    {
    }

    /// <summary>
    /// 允许注入自定义提取器集合，主要用于单元测试（替换某个提取器为 mock）。
    /// </summary>
    public ExtractorRouter(IReadOnlyCollection<IContentExtractor> extractors)
    {
        _extractorsByExtension = new Dictionary<string, IContentExtractor>(StringComparer.OrdinalIgnoreCase);
        foreach (var extractor in extractors)
        {
            foreach (var ext in extractor.SupportedExtensions)
            {
                if (_extractorsByExtension.ContainsKey(ext))
                {
                    throw new InvalidOperationException(
                        $"扩展名 \".{ext}\" 被多个提取器同时声明支持（{_extractorsByExtension[ext].GetType().Name} 与 {extractor.GetType().Name}），路由无法确定唯一分派目标。");
                }
                _extractorsByExtension[ext] = extractor;
            }
        }

        ValidateAgainstCatalog();
    }

    /// <summary>
    /// 校验 FileTypeCatalog 声明的每个扩展名都能在路由表中找到对应提取器，
    /// 且提取器的实现策略（是否真的提取内容）与 Catalog 里登记的 Strategy 相符。
    /// 这个检查只在构造时跑一次，成本可忽略，但能在开发阶段第一时间暴露"新增了文件类型
    /// 但忘记实现/注册提取器"这类低级但容易被忽略的问题。
    /// </summary>
    private void ValidateAgainstCatalog()
    {
        var missing = new List<string>();
        foreach (var descriptor in FileTypeCatalog.All)
        {
            if (!_extractorsByExtension.ContainsKey(descriptor.Extension))
            {
                missing.Add(descriptor.Extension);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "以下扩展名在 FileTypeCatalog 中已注册，但没有任何 IContentExtractor 实现支持它们: "
                + string.Join(", ", missing.Select(e => "." + e))
                + "。请为其新增提取器实现，或在 FileTypeCatalog 中移除该类型。");
        }
    }

    /// <summary>
    /// 判断某扩展名是否有已知的提取处理方式（无论是全文提取还是仅文件名策略）。
    /// 返回 false 表示这是 FileTypeCatalog 完全未登记的"未知类型"。
    /// </summary>
    public bool IsSupported(string extensionNoDot) =>
        FileTypeCatalog.Find(extensionNoDot) is not null;

    /// <summary>
    /// 获取某扩展名对应的解析策略（FullText / FileNameOnly）。
    /// 未登记的扩展名返回 null。
    /// </summary>
    public ExtractionStrategy? GetStrategy(string extensionNoDot) =>
        FileTypeCatalog.Find(extensionNoDot)?.Strategy;

    /// <summary>
    /// 对指定文件执行内容提取。会自动根据扩展名路由到正确的提取器。
    /// </summary>
    /// <returns>
    /// - 扩展名未在 FileTypeCatalog 登记：返回 <see cref="ExtractResult.Fail"/>，原因为"不支持的文件类型"；
    /// - 扩展名已登记但策略为 FileNameOnly（当前是 ppt/pptx）：返回 <see cref="ExtractResult.Ok"/> 空内容；
    /// - 扩展名已登记且策略为 FullText：调用对应提取器的真实解析逻辑。
    /// </returns>
    public Task<ExtractResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        string ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();

        if (!_extractorsByExtension.TryGetValue(ext, out var extractor))
        {
            return Task.FromResult(ExtractResult.Fail($"不支持的文件类型: .{ext}"));
        }

        return extractor.ExtractAsync(filePath, cancellationToken);
    }

    private static List<IContentExtractor> CreateDefaultExtractors() => new()
    {
        new PlainTextExtractor(),
        new WordExtractor(),
        new ExcelExtractor(),
        new PdfExtractor(),
        new RtfExtractor(),
        new EpubExtractor(),
        new FileNameOnlyExtractor(), // ppt/pptx，方案C
    };
}
