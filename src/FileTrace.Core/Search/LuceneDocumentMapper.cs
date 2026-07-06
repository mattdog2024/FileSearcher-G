using FileTrace.Core.Models;
using Lucene.Net.Documents;
using Lucene.Net.Index;

namespace FileTrace.Core.Search;

/// <summary>
/// 负责把扫描/提取阶段产出的 <see cref="ExtractedDocument"/> 转换成 Lucene 的
/// <see cref="Document"/>，字段命名统一引用 <see cref="IndexFieldNames"/>。
///
/// 字段选型说明：
/// - <see cref="IndexFieldNames.Path"/>：StringField（不分词），Store.YES —— 既是唯一键
///   （用于 UpdateDocument/DeleteDocuments 定位），也要存储原文用于结果展示"定位到原路径"。
/// - <see cref="IndexFieldNames.FileName"/>：TextField（分词，走 FileTraceAnalyzer 的中文分词），
///   Store.YES —— 支持"文件名关键词搜索"与高亮展示。
/// - <see cref="IndexFieldNames.FileNameKeyword"/>：StringField（不分词，小写化），Store.NO ——
///   专门给通配符查询（*.docx、报告2024*）与精确匹配使用，分词字段无法支持前缀/中缀通配符语义。
/// - <see cref="IndexFieldNames.Content"/>：TextField（分词），Store.YES —— 这里刻意选择存储
///   原文而非只索引：寻迹的核心卖点是"硬盘拔出后仍可离线搜索并高亮定位"，如果内容不存储在索引里，
///   搜索结果高亮（<see cref="SearchService"/> 用 Lucene Highlighter 生成摘要片段）在源盘离线时
///   将无法读取原文而彻底失效。存储原文会增大索引体积，这是为离线可用性做出的刻意取舍
///   （Lucene 的 stored fields 默认按 codec 压缩，实际膨胀幅度小于"体积等于原文大小"的朴素估计）。
/// - <see cref="IndexFieldNames.DirectoryPath"/>/<see cref="IndexFieldNames.Extension"/>：
///   StringField，Store.YES，用于结果展示与后续可能的目录/类型过滤。
/// - 数值字段用 Int64Field，NumericType 支持范围查询与排序。
/// </summary>
public static class LuceneDocumentMapper
{
    /// <summary>
    /// 用文件的完整路径构造 Lucene 的 Term，作为该文档在索引中的唯一标识。
    /// UpdateDocument/DeleteDocuments 均按这个 Term 定位。
    /// </summary>
    public static Term BuildPathTerm(string fullPath) => new(IndexFieldNames.Path, NormalizePath(fullPath));

    public static Document ToLuceneDocument(ExtractedDocument extracted, string profileId)
    {
        ArgumentNullException.ThrowIfNull(extracted);

        string normalizedPath = NormalizePath(extracted.FullPath);
        string? directoryPath = Path.GetDirectoryName(normalizedPath);

        var document = new Document
        {
            new StringField(IndexFieldNames.Path, normalizedPath, Field.Store.YES),
            new StringField(IndexFieldNames.ProfileId, profileId, Field.Store.YES),
            new TextField(IndexFieldNames.FileName, extracted.FileName, Field.Store.YES),
            new StringField(
                IndexFieldNames.FileNameKeyword,
                extracted.FileName.ToLowerInvariant(),
                Field.Store.NO),
            new TextField(IndexFieldNames.Content, extracted.Content ?? string.Empty, Field.Store.YES),
            new StringField(
                IndexFieldNames.DirectoryPath,
                directoryPath ?? string.Empty,
                Field.Store.YES),
            new StringField(
                IndexFieldNames.Extension,
                extracted.ExtensionNoDot.ToLowerInvariant(),
                Field.Store.YES),
            new Int64Field(IndexFieldNames.SizeBytes, extracted.SizeBytes, Field.Store.YES),
            new Int64Field(
                IndexFieldNames.LastWriteTimeUtcTicks,
                extracted.LastWriteTimeUtc.UtcTicks,
                Field.Store.YES),
            new StringField(
                IndexFieldNames.ContentIndexed,
                extracted.ContentExtracted ? "1" : "0",
                Field.Store.YES),
        };

        return document;
    }

    /// <summary>
    /// 统一路径分隔符，确保同一个物理文件无论以什么形式传入（Windows 反斜杠 \ 或正斜杠 /）
    /// 都能生成一致的 Term，避免因路径字符串表示差异导致增量更新/删除匹配不到已有文档。
    /// 不改变大小写：展示给用户的原始路径大小写按文件系统实际返回值原样保留。
    /// </summary>
    private static string NormalizePath(string fullPath) =>
        fullPath.Replace('\\', '/');
}
