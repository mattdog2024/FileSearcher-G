using Microsoft.Data.Sqlite;
using FileTrace.Core.Models;

namespace FileTrace.Core.Scanning;

/// <summary>
/// 单个索引的"文件指纹清单库"，用 SQLite 持久化在 <see cref="IndexProfile.ManifestDbPath"/>。
///
/// 职责：记录每个已索引文件的路径 + 大小 + 修改时间指纹，供增量扫描时快速判断
/// "这个文件自上次索引后是否变化，需不需要重新解析内容"，避免每次都要重新读取、
/// 解析全部文件——这对"持续追加索引其他硬盘/电脑文档"的使用场景（索引会越滚越大）
/// 是必要的性能保障。
///
/// 选择 SQLite 而不是 Lucene 本身存指纹的原因：Lucene 索引段是仅追加/合并式的
/// 倒排索引结构，不适合做"按主键快速点查 + 频繁更新单条记录"这种事务型操作；
/// SQLite 做这类操作有原生索引和事务支持，职责分离更清晰，也方便以后单独重建/
/// 清空 manifest 而不影响 Lucene 全文索引数据。
/// </summary>
public sealed class ManifestStore : IDisposable
{
    private readonly SqliteConnection _connection;

    private ManifestStore(SqliteConnection connection)
    {
        _connection = connection;
    }

    /// <summary>
    /// 打开（或创建）指定路径的清单库，并确保表结构存在。
    /// </summary>
    public static ManifestStore Open(string manifestDbPath)
    {
        string? directory = Path.GetDirectoryName(manifestDbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection($"Data Source={manifestDbPath}");
        connection.Open();

        using (var pragmaCmd = connection.CreateCommand())
        {
            // WAL 模式：扫描器持续写入的同时，UI 线程可以并发只读查询索引统计信息，不会互相阻塞。
            pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragmaCmd.ExecuteNonQuery();
        }

        using (var createCmd = connection.CreateCommand())
        {
            createCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS file_fingerprint (
                    full_path TEXT PRIMARY KEY,
                    size_bytes INTEGER NOT NULL,
                    last_write_time_utc_ticks INTEGER NOT NULL,
                    indexed_at_utc_ticks INTEGER NOT NULL,
                    content_indexed INTEGER NOT NULL
                );
                """;
            createCmd.ExecuteNonQuery();
        }

        return new ManifestStore(connection);
    }

    /// <summary>
    /// 根据完整路径查询已记录的指纹。找不到返回 null（表示这是一个从未索引过的新文件）。
    /// </summary>
    public FileFingerprint? Find(string fullPath)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT full_path, size_bytes, last_write_time_utc_ticks, indexed_at_utc_ticks, content_indexed
            FROM file_fingerprint WHERE full_path = $path;
            """;
        cmd.Parameters.AddWithValue("$path", fullPath);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new FileFingerprint
        {
            FullPath = reader.GetString(0),
            SizeBytes = reader.GetInt64(1),
            LastWriteTimeUtcTicks = reader.GetInt64(2),
            IndexedAt = new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero),
            ContentIndexed = reader.GetInt64(4) != 0,
        };
    }

    /// <summary>
    /// 判断某个文件相对已记录的指纹是否"未发生变化"（大小和修改时间都一致）。
    /// 用于增量扫描：未变化的文件可以跳过重新解析内容这一步。
    /// </summary>
    public bool IsUnchanged(string fullPath, long currentSizeBytes, long currentLastWriteTimeUtcTicks)
    {
        var existing = Find(fullPath);
        return existing is not null
            && existing.SizeBytes == currentSizeBytes
            && existing.LastWriteTimeUtcTicks == currentLastWriteTimeUtcTicks;
    }

    /// <summary>
    /// 写入或更新一条指纹记录（INSERT OR REPLACE 语义）。
    /// </summary>
    public void Upsert(FileFingerprint fingerprint)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO file_fingerprint
                (full_path, size_bytes, last_write_time_utc_ticks, indexed_at_utc_ticks, content_indexed)
            VALUES
                ($path, $size, $mtime, $indexedAt, $contentIndexed)
            ON CONFLICT(full_path) DO UPDATE SET
                size_bytes = excluded.size_bytes,
                last_write_time_utc_ticks = excluded.last_write_time_utc_ticks,
                indexed_at_utc_ticks = excluded.indexed_at_utc_ticks,
                content_indexed = excluded.content_indexed;
            """;
        cmd.Parameters.AddWithValue("$path", fingerprint.FullPath);
        cmd.Parameters.AddWithValue("$size", fingerprint.SizeBytes);
        cmd.Parameters.AddWithValue("$mtime", fingerprint.LastWriteTimeUtcTicks);
        cmd.Parameters.AddWithValue("$indexedAt", fingerprint.IndexedAt.UtcTicks);
        cmd.Parameters.AddWithValue("$contentIndexed", fingerprint.ContentIndexed ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>批量写入，包裹在一个事务里，避免逐条 commit 拖慢大规模扫描的写入速度。</summary>
    public void UpsertBatch(IEnumerable<FileFingerprint> fingerprints)
    {
        using var transaction = _connection.BeginTransaction();
        foreach (var fp in fingerprints)
        {
            Upsert(fp);
        }
        transaction.Commit();
    }

    /// <summary>删除一条指纹记录（源文件已被物理删除时，用于清理 manifest 和触发 Lucene 侧删除对应文档）。</summary>
    public void Remove(string fullPath)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM file_fingerprint WHERE full_path = $path;";
        cmd.Parameters.AddWithValue("$path", fullPath);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 返回清单库中记录的全部文件路径集合。用于扫描完成后对比"清单里有但这次扫描没发现"的路径，
    /// 从而识别出已被删除/移动的文件，需要从 manifest 和 Lucene 索引中一并清除。
    /// </summary>
    public HashSet<string> GetAllPaths()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT full_path FROM file_fingerprint;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    public long CountRecords()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM file_fingerprint;";
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
