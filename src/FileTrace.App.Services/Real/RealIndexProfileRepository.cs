using System.IO;
using FileTrace.App.Services.Logging;
using FileTrace.Core.Models;
using FileTrace.Core.Persistence;
using FileTrace.Core.Scanning;
using FileTrace.Core.Search;

namespace FileTrace.App.Services.Real;

/// <summary>
/// <see cref="IIndexProfileRepository"/> 的真实实现：基于 <see cref="IndexProfileStore"/>
/// 读写 profile.json + 注册表文件，并在加载时用 <see cref="IndexStatusEvaluator"/>
/// 刷新每个索引的实时状态（源盘是否在线等），替换 Stage2 的 MockIndexProfileRepository。
/// </summary>
public sealed class RealIndexProfileRepository : IIndexProfileRepository
{
    private readonly IndexProfileStore _store;
    private readonly IAppLogger _logger;

    public RealIndexProfileRepository(string registryFilePath, IAppLogger? logger = null)
    {
        _store = new IndexProfileStore(registryFilePath);
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <summary>
    /// 加载全部索引配置，并顺带做一次"孤儿注册表条目"自愈清理：
    /// 如果注册表里记录的某个 StoragePath 下已经找不到有效的 profile.json
    /// （例如用户手动删除了该目录，或者拷贝/移动 data 目录时漏掉了某个索引），
    /// <see cref="IndexProfileStore.LoadAllAsync"/> 会把它记录进 skippedPaths 而不是让整体加载失败；
    /// 这里进一步把这些孤儿条目从注册表里移除，避免每次启动都重复尝试加载同一个已经不存在的路径，
    /// 注册表文件本身也不会无限增长脏数据。清理动作只解除注册关联，不触碰任何用户源文件。
    /// </summary>
    public async Task<IReadOnlyList<IndexProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var skippedPaths = new List<string>();
        var profiles = await _store.LoadAllAsync(skippedPaths, cancellationToken).ConfigureAwait(false);

        foreach (var profile in profiles)
        {
            RefreshRuntimeStats(profile);
        }

        foreach (var orphanPath in skippedPaths)
        {
            try
            {
                await _store.UnregisterAsync(orphanPath, cancellationToken).ConfigureAwait(false);
                _logger.LogWarning($"启动时清理了一个失效的索引注册条目（目录下未找到有效 profile.json）: {orphanPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"清理失效索引注册条目失败: {orphanPath}", ex);
            }
        }

        return profiles;
    }

    public async Task<IndexProfile> CreateAsync(IndexProfile profile, CancellationToken cancellationToken = default)
    {
        await _store.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public Task SaveAsync(IndexProfile profile, CancellationToken cancellationToken = default) =>
        _store.SaveAsync(profile, cancellationToken);

    public async Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var profiles = await _store.LoadAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var target = profiles.FirstOrDefault(p => p.Id == profileId);
        if (target is null)
        {
            return;
        }

        await _store.UnregisterAsync(target.StoragePath, cancellationToken).ConfigureAwait(false);

        // 彻底删除磁盘上的索引数据（lucene 段文件 + manifest.db + profile.json）。
        // 注意：只删除 StoragePath（索引存储目录），绝不触碰 RootPath（用户的原始文件），
        // 这是本功能"删除索引"与"删除文件"语义的关键边界。
        try
        {
            if (Directory.Exists(target.StoragePath))
            {
                Directory.Delete(target.StoragePath, recursive: true);
            }
        }
        catch (IOException)
        {
            // 磁盘上的文件可能被 Lucene IndexWriter 或搜索用的 IndexReader 占用锁未释放；
            // 已经从注册表移除，用户角度看该索引已经“消失”，物理文件清理失败留待下次
            // 应用启动时的孤儿目录扫描/或用户手动清理，不阻塞当前删除操作的用户体验。
        }
    }

    /// <summary>
    /// 用 IndexStatusEvaluator + ManifestStore 统计信息刷新一个 profile 的运行时状态字段
    /// （Status/FileCount/TotalSizeBytes），这些字段不作为 profile.json 的权威数据来源
    /// （避免每次读取都重写 JSON 文件），而是每次加载时从 manifest.db 实时计算。
    /// </summary>
    private static void RefreshRuntimeStats(IndexProfile profile)
    {
        profile.Status = IndexStatusEvaluator.Evaluate(profile);

        if (!File.Exists(profile.ManifestDbPath))
        {
            profile.FileCount = 0;
            profile.TotalSizeBytes = 0;
            return;
        }

        try
        {
            using var manifest = ManifestStore.Open(profile.ManifestDbPath);
            profile.FileCount = manifest.CountRecords();
            profile.TotalSizeBytes = manifest.SumSizeBytes();
        }
        catch (Exception)
        {
            // manifest.db 损坏或被占用：不阻断整个索引列表加载，保留 profile.json 里
            // 记录的旧统计值（可能略微过期，但比直接崩溃/隐藏这个索引体验更好）。
        }
    }
}
