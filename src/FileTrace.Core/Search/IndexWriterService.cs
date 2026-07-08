using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;

namespace FileTrace.Core.Search;

/// <summary>
/// 单个 <see cref="Models.IndexProfile"/> 对应的 Lucene 索引写入端封装。
///
/// 生命周期约定：
/// - 一个 IndexWriterService 实例对应磁盘上一个索引目录（IndexProfile.LuceneDirectory），
///   在一次索引任务（全量或增量扫描）期间保持打开，任务结束后 Dispose 提交并释放文件锁。
/// - Lucene 的 IndexWriter 本身要求同一个目录同一时刻只能有一个 IndexWriter 实例打开
///   （通过 NativeFSLockFactory 文件锁保护），所以这里不做多例复用，调用方需要自己保证
///   同一个索引目录不会被并发打开两次写入器（例如 UI 层要禁止对同一个索引同时发起两个索引任务）。
/// - 使用 <see cref="FileTraceAnalyzer"/> 作为默认分词器，保证索引和 <see cref="SearchQueryBuilder"/>/
///   <see cref="SearchService"/> 查询端使用完全一致的分词逻辑。
/// </summary>
public sealed class IndexWriterService : IDisposable
{
    private readonly FSDirectory _directory;
    private readonly IndexWriter _writer;
    private readonly string _profileId;
    private bool _disposed;

    /// <summary>
    /// 打开（或创建）指定目录下的 Lucene 索引用于写入。
    /// </summary>
    /// <param name="luceneDirectoryPath">
    /// 索引段文件所在目录，通常是 IndexProfile.LuceneDirectory。目录不存在时会自动创建。
    /// </param>
    /// <param name="profileId">
    /// 该索引对应的 IndexProfile.Id，写入每个文档的 <see cref="IndexFieldNames.ProfileId"/> 字段，
    /// 供多索引联合搜索时反查"命中文档来自哪个索引源"。
    /// </param>
    /// <param name="createNew">
    /// true：清空并重新创建索引（用于"全量重建索引"操作）；
    /// false：在已有索引基础上追加/更新（用于增量扫描，目录不存在时等价于创建）。
    /// </param>
    public IndexWriterService(string luceneDirectoryPath, string profileId, bool createNew = false)
    {
        _profileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
        System.IO.Directory.CreateDirectory(luceneDirectoryPath);
        _directory = FSDirectory.Open(luceneDirectoryPath);

        var analyzer = CreateAnalyzer();
        var config = new IndexWriterConfig(FileTraceAnalyzer.MatchVersion, analyzer)
        {
            OpenMode = createNew ? OpenMode.CREATE : OpenMode.CREATE_OR_APPEND,
        };

        _writer = new IndexWriter(_directory, config);
    }

    /// <summary>当前索引中的文档总数（含尚未 Commit 但已 Add/Update 的文档）。</summary>
    public int NumDocs => _writer.NumDocs;

    public static Analyzer CreateAnalyzer() => new FileTraceAnalyzer();

    /// <summary>新增一个文档（调用方需自行保证该路径此前未被索引，否则会产生重复文档）。</summary>
    public void AddDocument(Models.ExtractedDocument extracted)
    {
        ThrowIfDisposed();
        _writer.AddDocument(LuceneDocumentMapper.ToLuceneDocument(extracted, _profileId));
    }

    /// <summary>
    /// 按文件路径更新文档：Lucene 没有真正的"原地更新"，UpdateDocument 内部等价于
    /// "先按 Term 删除旧文档，再插入新文档"。用于处理 FileSystemScanner 报告的 Updated 状态文件，
    /// 以及"文件已存在则覆盖、不存在则新增"的统一入口——因此索引写入协调层可以对 Added/Updated
    /// 两种状态都直接调用本方法，不需要关心区分。
    /// </summary>
    public void UpdateDocument(Models.ExtractedDocument extracted)
    {
        ThrowIfDisposed();
        Term term = LuceneDocumentMapper.BuildPathTerm(extracted.FullPath);
        Document document = LuceneDocumentMapper.ToLuceneDocument(extracted, _profileId);
        _writer.UpdateDocument(term, document);
    }

    /// <summary>按完整路径删除一个文档，用于处理 FileSystemScanner 报告的 Removed 状态文件。</summary>
    public void DeleteDocument(string fullPath)
    {
        ThrowIfDisposed();
        _writer.DeleteDocuments(LuceneDocumentMapper.BuildPathTerm(fullPath));
    }

    /// <summary>清空索引中的全部文档，但保留索引目录结构（用于"清空后重新全量索引"场景）。</summary>
    public void DeleteAll()
    {
        ThrowIfDisposed();
        _writer.DeleteAll();
    }

    /// <summary>
    /// 提交所有挂起的更改到磁盘。索引任务应当在处理完一批文件后定期调用（而不是等到最后才提交一次），
    /// 以便：1) 增量扫描中途若进程异常退出，已提交部分不会丢失；2) 长时间运行的索引任务，
    /// 搜索端可以在扫描过程中就看到部分新结果（DirectoryReader 需要重新 Open 才能感知到 Commit）。
    /// </summary>
    public void Commit()
    {
        ThrowIfDisposed();
        _writer.Commit();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(IndexWriterService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _writer.Commit();
        }
        finally
        {
            _writer.Dispose();
            _directory.Dispose();
            _disposed = true;
        }
    }
}
