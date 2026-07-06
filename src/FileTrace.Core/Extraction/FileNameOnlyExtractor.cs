namespace FileTrace.Core.Extraction;

/// <summary>
/// "仅索引文件名"策略的占位提取器——不做任何全文内容提取，直接返回空内容。
///
/// 当前唯一使用场景：ppt/pptx（方案C决策：演示文稿格式暂不做全文内容提取，
/// 只索引文件名/路径/大小/修改时间等基本信息，后续版本可再加强为真正的全文提取）。
///
/// 之所以仍然实现一个"提取器"而不是在路由层直接跳过，是为了让上层的索引流程
/// （ExtractorRouter → 文件扫描器 → 索引写入）保持统一的处理路径：无论哪种策略，
/// 都调用 ExtractAsync 拿到一个 ExtractResult，由调用方根据
/// <see cref="Models.FileTypeDescriptor.Strategy"/> 决定是否需要真正提取内容，
/// 而不是在多处代码里散落 if/else 特殊分支。
/// </summary>
public sealed class FileNameOnlyExtractor : IContentExtractor
{
    /// <summary>
    /// 当前受本提取器管辖、按方案C仅索引文件名的扩展名列表。
    /// 与 <see cref="Models.FileTypeCatalog"/> 中标记为 FileNameOnly 的条目保持一致，
    /// 由 <see cref="ExtractorRouter"/> 在构造时做一致性校验，避免两处配置漂移。
    /// </summary>
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { "ppt", "pptx" };

    public Task<ExtractResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        // 直接返回"成功但内容为空"——不是失败，只是策略上不提取正文，
        // 调用方（索引写入阶段）应当仍然把文件名/路径/大小/时间写入索引，只是内容字段留空。
        return Task.FromResult(ExtractResult.Ok(string.Empty));
    }
}
