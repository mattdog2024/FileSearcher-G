using System.Text.Json.Serialization;

namespace FileTrace.Core.Models;

/// <summary>
/// 索引的运行状态，对应设计稿 IndexDrawer 卡片上的状态点颜色。
/// </summary>
public enum IndexStatus
{
    /// <summary>索引可正常使用（绿色状态点）。</summary>
    Ok,

    /// <summary>源目录自上次索引后有变化，建议重建/增量更新（橙色状态点）。</summary>
    NeedsUpdate,

    /// <summary>索引正在构建中。</summary>
    Building,

    /// <summary>源目录当前不可访问（例如硬盘未连接），但索引数据仍可离线搜索。</summary>
    SourceUnavailable,

    /// <summary>索引已损坏或无法打开。</summary>
    Error,
}

/// <summary>
/// 一个"索引源"的元数据（对应设计稿 IndexDrawer 里的一张索引卡片）。
/// 每个 IndexProfile 对应磁盘上的一个独立文件夹：
///   {StoragePath}/
///     lucene/           Lucene 索引段文件
///     manifest.db        SQLite 清单库：记录已索引文件的路径+大小+mtime 指纹，用于增量扫描判断
///     profile.json        本文件对应的元数据（名称、根目录、勾选类型等配置）
/// </summary>
public sealed class IndexProfile
{
    /// <summary>索引的唯一标识（GUID），用于内部关联，不展示给用户。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>用户起的索引名称，例如"工作文档""项目代码"。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>被索引的根目录，例如 D:\Documents。</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>索引数据的存储目录（用户指定，可保存到别的盘）。</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>用户勾选的文件扩展名集合（不含点，小写）。</summary>
    public HashSet<string> IncludedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否启用增量索引（只扫描变化的文件）。默认勾选，对应设计稿默认状态。</summary>
    public bool IncrementalEnabled { get; set; } = true;

    /// <summary>
    /// 单文件大小上限（字节）。超过此大小的文件只索引文件名，不提取全文内容，防止个别巨型文件拖慢整体索引速度。
    /// 默认 200MB。
    /// </summary>
    public long MaxFileSizeForContentBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>是否启用 OCR 识别扫描版 PDF/图片（默认关闭，可选功能）。</summary>
    public bool OcrEnabled { get; set; } = false;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUpdatedAt { get; set; }

    public IndexStatus Status { get; set; } = IndexStatus.Building;

    /// <summary>已成功索引的文件总数（供 UI 统计行展示）。</summary>
    public long FileCount { get; set; }

    /// <summary>已索引文件的总大小（字节）。</summary>
    public long TotalSizeBytes { get; set; }

    [JsonIgnore]
    public string LuceneDirectory => Path.Combine(StoragePath, "lucene");

    [JsonIgnore]
    public string ManifestDbPath => Path.Combine(StoragePath, "manifest.db");

    [JsonIgnore]
    public string ProfileJsonPath => Path.Combine(StoragePath, "profile.json");
}
