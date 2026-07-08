using System.Text.Json;
using FileTrace.Core.Models;

namespace FileTrace.Core.Persistence;

/// <summary>
/// 索引配置（<see cref="IndexProfile"/>）的磁盘持久化：读写每个索引自己的
/// <c>{StoragePath}/profile.json</c>，以及维护一个"注册表"文件，记录本机所有已知
/// 索引的 StoragePath 列表——这样便携版启动时不需要扫描整个磁盘就能找到所有已建立的索引
/// （索引数据本身可能分散在不同盘符/目录）。
///
/// 注册表文件路径由调用方传入（便携版默认是 exe 旁边的 <c>data/registry.json</c>，
/// 具体由 FileTrace.App 的组合根决定，Core 层不关心"便携版"这个概念，只提供机制）。
/// </summary>
public sealed class IndexProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _registryFilePath;

    public IndexProfileStore(string registryFilePath)
    {
        _registryFilePath = registryFilePath;
    }

    /// <summary>
    /// 加载注册表中记录的全部索引。对于已从注册表中移除（例如目录被手动删除、
    /// profile.json 损坏）的条目，跳过并在结果里通过 <paramref name="skippedPaths"/> 报告，
    /// 而不是让整体加载失败——一个索引损坏不应该导致用户看不到其它正常的索引。
    /// </summary>
    public async Task<IReadOnlyList<IndexProfile>> LoadAllAsync(
        List<string>? skippedPaths = null,
        CancellationToken cancellationToken = default)
    {
        var storagePaths = await LoadRegistryAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<IndexProfile>();

        foreach (var storagePath in storagePaths)
        {
            try
            {
                var profile = await LoadProfileAsync(storagePath, cancellationToken).ConfigureAwait(false);
                if (profile is not null)
                {
                    result.Add(profile);
                }
                else
                {
                    skippedPaths?.Add(storagePath);
                }
            }
            catch (Exception) when (skippedPaths is not null)
            {
                skippedPaths.Add(storagePath);
            }
        }

        return result;
    }

    /// <summary>
    /// 读取单个索引目录下的 profile.json。文件不存在或反序列化失败时返回 null（视为无效索引）。
    /// </summary>
    public async Task<IndexProfile?> LoadProfileAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        string jsonPath = Path.Combine(storagePath, "profile.json");
        if (!File.Exists(jsonPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(jsonPath);
            var profile = await JsonSerializer.DeserializeAsync<IndexProfile>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            // 兜底：StoragePath 理论上应该和 profile.json 所在目录一致，
            // 但防止用户手动移动了整个 data 目录导致 JSON 里记录的旧绝对路径失效，
            // 加载时始终以"实际找到 profile.json 的目录"为准重新赋值。
            if (profile is not null)
            {
                profile.StoragePath = storagePath;
            }

            return profile;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 保存一个索引配置：写入其自身的 profile.json，并确保它的 StoragePath 已登记在注册表中。
    /// </summary>
    public async Task SaveAsync(IndexProfile profile, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(profile.StoragePath);

        await using (var stream = File.Create(profile.ProfileJsonPath))
        {
            await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        await RegisterStoragePathAsync(profile.StoragePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 从注册表移除一个索引（不会删除磁盘上的 profile.json/lucene/manifest.db 数据本身——
    /// 调用方如果需要"彻底删除"，应额外调用 Directory.Delete(storagePath, recursive: true)；
    /// 只解除注册表关联的场景例如"索引所在盘暂时不可用，先从列表隐藏但不销毁数据"）。
    /// </summary>
    public async Task UnregisterAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var paths = await LoadRegistryAsync(cancellationToken).ConfigureAwait(false);
        paths.RemoveAll(p => string.Equals(NormalizePath(p), NormalizePath(storagePath), StringComparison.OrdinalIgnoreCase));
        await SaveRegistryAsync(paths, cancellationToken).ConfigureAwait(false);
    }

    private async Task RegisterStoragePathAsync(string storagePath, CancellationToken cancellationToken)
    {
        var paths = await LoadRegistryAsync(cancellationToken).ConfigureAwait(false);
        if (!paths.Any(p => string.Equals(NormalizePath(p), NormalizePath(storagePath), StringComparison.OrdinalIgnoreCase)))
        {
            paths.Add(storagePath);
            await SaveRegistryAsync(paths, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<string>> LoadRegistryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_registryFilePath))
        {
            return new List<string>();
        }

        try
        {
            await using var stream = File.OpenRead(_registryFilePath);
            var paths = await JsonSerializer.DeserializeAsync<List<string>>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return paths ?? new List<string>();
        }
        catch (JsonException)
        {
            // 注册表文件损坏：不抛异常阻断整个应用启动，视为"没有已知索引"，
            // 用户仍然可以重新新建索引；旧的损坏文件会在下次 SaveRegistryAsync 时被覆盖。
            return new List<string>();
        }
    }

    private async Task SaveRegistryAsync(List<string> paths, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_registryFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_registryFilePath);
        await JsonSerializer.SerializeAsync(stream, paths, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
