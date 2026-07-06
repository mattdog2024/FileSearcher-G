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

    /// <summary>
    /// 持久化一个已存在索引的最新状态（例如一次索引任务完成后更新的 FileCount/TotalSizeBytes/
    /// LastUpdatedAt/Status）。与 <see cref="CreateAsync"/> 的区别只是语义上的"新建 vs 更新"，
    /// Mock 实现里两者行为等价（都只是内存态覆盖），Real 实现里都落到同一个 profile.json 写入逻辑，
    /// 拆成两个方法是为了让调用方代码的意图更清晰。
    /// </summary>
    Task SaveAsync(IndexProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(string profileId, CancellationToken cancellationToken = default);
}
