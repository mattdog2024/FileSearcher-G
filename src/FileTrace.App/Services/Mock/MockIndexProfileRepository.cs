using FileTrace.Core.Models;

namespace FileTrace.App.Services.Mock;

/// <summary>
/// Stage2 UI 骨架阶段使用的内存态假数据仓库，让 IndexDrawer 有真实感的卡片可以演示
/// 4 种状态（可用/待更新/构建中/源盘离线）而不必真的挂载磁盘、真的跑 Lucene。
/// Stage3 会替换为读写 %storage%/profile.json 清单文件 + 调用 IndexingCoordinator 的实现。
/// </summary>
public sealed class MockIndexProfileRepository : IIndexProfileRepository
{
    private readonly List<IndexProfile> _profiles;

    public MockIndexProfileRepository()
    {
        _profiles = new List<IndexProfile>
        {
            new()
            {
                Name = "工作文档",
                RootPath = @"D:\Documents\Work",
                StoragePath = @"D:\FileTraceIndex\work",
                IncludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "doc", "docx", "xls", "xlsx", "pdf", "ppt", "pptx",
                },
                Status = IndexStatus.Ok,
                FileCount = 8_530,
                TotalSizeBytes = 42L * 1024 * 1024 * 1024,
                LastUpdatedAt = DateTimeOffset.Now.AddHours(-3),
            },
            new()
            {
                Name = "项目代码",
                RootPath = @"E:\Projects",
                StoragePath = @"D:\FileTraceIndex\projects",
                IncludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "py", "java", "c", "cpp", "js", "ts", "css", "json", "xml", "md",
                },
                Status = IndexStatus.NeedsUpdate,
                FileCount = 21_940,
                TotalSizeBytes = 6L * 1024 * 1024 * 1024,
                LastUpdatedAt = DateTimeOffset.Now.AddDays(-2),
            },
            new()
            {
                Name = "移动硬盘备份（旧照片资料）",
                RootPath = @"F:\Backup2023",
                StoragePath = @"D:\FileTraceIndex\backup2023",
                IncludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "doc", "docx", "pdf", "txt",
                },
                Status = IndexStatus.SourceUnavailable,
                FileCount = 3_207,
                TotalSizeBytes = 980L * 1024 * 1024,
                LastUpdatedAt = DateTimeOffset.Now.AddMonths(-4),
            },
        };
    }

    public Task<IReadOnlyList<IndexProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IndexProfile> snapshot = _profiles.ToList();
        return Task.FromResult(snapshot);
    }

    public Task<IndexProfile> CreateAsync(IndexProfile profile, CancellationToken cancellationToken = default)
    {
        _profiles.Add(profile);
        return Task.FromResult(profile);
    }

    public Task SaveAsync(IndexProfile profile, CancellationToken cancellationToken = default)
    {
        // 内存态 Mock：profile 是引用类型，调用方对同一实例的字段修改已经"生效"，
        // 这里无需任何额外操作，仅为满足接口契约。
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        _profiles.RemoveAll(p => p.Id == profileId);
        return Task.CompletedTask;
    }
}
