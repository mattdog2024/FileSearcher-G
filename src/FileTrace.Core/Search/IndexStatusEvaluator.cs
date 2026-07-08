using FileTrace.Core.Models;
using FileTrace.Core.Scanning;

namespace FileTrace.Core.Search;

/// <summary>
/// 根据 <see cref="IndexProfile"/> 的磁盘状态（源目录是否可访问、Lucene 索引是否存在）
/// 与 manifest 统计数据，计算出该索引当前应该展示的 <see cref="IndexStatus"/>。
///
/// 之所以独立成一个无状态的静态工具类（而不是把判断逻辑塞进 IndexProfile 本身），
/// 是因为"源盘是否可访问"这类判断依赖运行时的文件系统 IO，属于服务层职责，
/// 不适合放进纯数据模型；同时保持无状态也方便脱离真实文件系统单元测试
/// （通过 <see cref="Evaluate(bool, bool, long)"/> 重载直接传入布尔值）。
/// </summary>
public static class IndexStatusEvaluator
{
    /// <summary>
    /// 基于真实文件系统评估某个索引的当前状态。
    /// </summary>
    public static IndexStatus Evaluate(IndexProfile profile)
    {
        bool rootExists = Directory.Exists(profile.RootPath);
        bool luceneExists = Directory.Exists(profile.LuceneDirectory)
            && Directory.EnumerateFileSystemEntries(profile.LuceneDirectory).Any();

        return Evaluate(rootExists, luceneExists, profile.FileCount);
    }

    /// <summary>
    /// 纯函数版本，方便单元测试：不接触真实文件系统，直接根据布尔状态推导。
    /// </summary>
    public static IndexStatus Evaluate(bool rootPathExists, bool luceneIndexExists, long fileCount)
    {
        if (!luceneIndexExists || fileCount == 0)
        {
            // 索引目录为空或从未成功写入过任何文档：要么是刚创建还没跑过第一次扫描，
            // 要么是索引数据丢失/损坏，统一归类为"待更新"，引导用户触发一次构建/重建。
            return rootPathExists ? IndexStatus.NeedsUpdate : IndexStatus.SourceUnavailable;
        }

        return rootPathExists ? IndexStatus.Ok : IndexStatus.SourceUnavailable;
    }
}
