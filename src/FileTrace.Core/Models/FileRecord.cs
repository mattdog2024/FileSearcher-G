namespace FileTrace.Core.Models;

/// <summary>
/// 一条"文件指纹"记录，存放在每个索引的 manifest 清单库里，
/// 用于增量扫描时快速判断"这个文件自上次索引后是否发生变化"，
/// 不需要重新读取/解析文件内容即可判断是否跳过。
/// </summary>
public sealed class FileFingerprint
{
    /// <summary>文件的完整路径（作为清单库主键）。</summary>
    public string FullPath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>文件最后修改时间的 UTC Ticks，用于指纹比较（避免时区问题）。</summary>
    public long LastWriteTimeUtcTicks { get; set; }

    /// <summary>上次成功索引的时间。</summary>
    public DateTimeOffset IndexedAt { get; set; }

    /// <summary>
    /// 上次索引时该文件是否提取了全文内容（false 表示仅索引了文件名，
    /// 例如 ppt/pptx，或超过大小上限的文件，或解析失败/加密文件）。
    /// </summary>
    public bool ContentIndexed { get; set; }
}

/// <summary>
/// 一次文件内容解析的结果，供索引写入阶段使用。
/// </summary>
public sealed class ExtractedDocument
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public required string ExtensionNoDot { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTimeOffset LastWriteTimeUtc { get; init; }

    /// <summary>提取到的全文内容；FileNameOnly 策略或解析失败时为空字符串。</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>是否成功提取了内容（区分于"策略上就不提取"与"提取失败"，都会是 false，但可通过 FailureReason 区分）。</summary>
    public bool ContentExtracted { get; init; }

    /// <summary>解析失败或跳过的原因（用于写入索引任务日志，帮助用户排查哪些文件没建上内容索引）。</summary>
    public string? SkipOrFailureReason { get; init; }
}
