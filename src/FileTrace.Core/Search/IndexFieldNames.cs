namespace FileTrace.Core.Search;

/// <summary>
/// Lucene 索引文档的字段名常量集中定义，避免在写入端（IndexWriterService/LuceneDocumentMapper）
/// 与查询端（SearchQueryBuilder/SearchService）之间因手写字符串拼写不一致导致的隐蔽 bug。
/// </summary>
public static class IndexFieldNames
{
    /// <summary>
    /// 文件的完整路径，作为 Lucene 文档的唯一标识（不分词，Term 精确匹配），
    /// 用于增量更新时的 UpdateDocument(Term, ...) 定位与删除时的 DeleteDocuments(Term)。
    /// </summary>
    public const string Path = "path";

    /// <summary>
    /// 文件名（含扩展名，不含目录），分词字段，用于"文件名搜索"以及高亮展示。
    /// </summary>
    public const string FileName = "fileName";

    /// <summary>
    /// 文件名的不分词版本（小写化），用于通配符匹配（*.docx 之类）以及精确文件名比较。
    /// </summary>
    public const string FileNameKeyword = "fileNameKeyword";

    /// <summary>
    /// 文件全文内容，分词字段，用于内容关键词搜索与高亮片段生成。
    /// FileNameOnly 策略（如 ppt/pptx）或提取失败的文件此字段为空字符串。
    /// </summary>
    public const string Content = "content";

    /// <summary>文件所在目录（不含文件名），不分词，用于按目录范围过滤/展示。</summary>
    public const string DirectoryPath = "directoryPath";

    /// <summary>文件扩展名（不含点，小写），不分词，用于按类型过滤。</summary>
    public const string Extension = "extension";

    /// <summary>文件大小（字节），数值字段，用于排序/范围过滤。</summary>
    public const string SizeBytes = "sizeBytes";

    /// <summary>文件最后修改时间的 UTC Ticks，数值字段，用于排序/范围过滤。</summary>
    public const string LastWriteTimeUtcTicks = "lastWriteTimeUtcTicks";

    /// <summary>
    /// 是否成功提取了全文内容（"1"/"0"，StringField）。
    /// 供 UI 展示"该文件仅索引了文件名"之类的提示，也可用于过滤查询。
    /// </summary>
    public const string ContentIndexed = "contentIndexed";

    /// <summary>
    /// 该文档所属的 <see cref="Models.IndexProfile"/>.Id（不分词，StringField）。
    /// 多索引联合搜索（<see cref="SearchService"/> 用 MultiReader 合并多个索引目录）时，
    /// Lucene 的 MultiReader 本身不会记录"这个命中文档来自哪个子索引"，
    /// 所以必须在写入阶段把所属索引的标识存进文档字段里，搜索结果才能反查出"这个文件属于
    /// 哪个索引源（对应哪块硬盘）"，从而在源盘当前不可用时依然能展示"来自：XX索引（离线）"。
    /// </summary>
    public const string ProfileId = "profileId";
}
