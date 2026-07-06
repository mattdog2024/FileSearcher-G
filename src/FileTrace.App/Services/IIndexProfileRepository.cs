using FileTrace.Core.Models;

namespace FileTrace.App.Services;

/// <summary>
/// 索引配置的读写抽象。Stage2 由 <see cref="Mock.MockIndexProfileRepository"/> 提供内存态假数据，
/// Stage3 将替换为读写 profile.json + 驱动 IndexingCoordinator 的真实实现，
/// MainViewModel 只依赖本接口，替换实现不需要改动任何 UI/ViewModel 代码。
/// </summary>
public interface IIndexProfileRepository
{
    Task<IReadOnlyList<IndexProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IndexProfile> CreateAsync(IndexProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(string profileId, CancellationToken cancellationToken = default);
}
