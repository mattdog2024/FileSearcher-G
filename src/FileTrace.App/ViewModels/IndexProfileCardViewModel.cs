using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTrace.Core.Models;
using FileTrace.Core.Scanning;

namespace FileTrace.App.ViewModels;

/// <summary>
/// 左侧"索引管理"抽屉里一张索引卡片的视图模型，对应设计稿 IndexDrawer 的卡片项。
///
/// 本类自身不直接调用 IIndexingService——真正发起/取消索引任务的编排逻辑在
/// <see cref="MainViewModel"/>（它持有 IIndexingService 与生命周期更长的取消令牌）。
/// 本类只负责：
///   1) 展示当前状态/进度（由 MainViewModel 在任务运行期间通过 Progress&lt;ScanProgress&gt; 回调更新）；
///   2) 把"重建/暂停/删除"这几个用户操作以事件形式通知 MainViewModel 去执行；
///   3) 持有本次运行关联的 <see cref="ScanPauseController"/>，让"暂停"按钮可以直接调用，
///      不需要每次都经过 MainViewModel 转发（暂停/继续属于纯 UI 交互，不涉及 IO）。
/// </summary>
public sealed partial class IndexProfileCardViewModel : ObservableObject
{
    public IndexProfileCardViewModel(IndexProfile profile)
    {
        Profile = profile;
        name = profile.Name;
        rootPath = profile.RootPath;
        status = profile.Status;
        fileCount = profile.FileCount;
        totalSizeBytes = profile.TotalSizeBytes;
        lastUpdatedAt = profile.LastUpdatedAt;
        isBuilding = profile.Status == IndexStatus.Building;
    }

    /// <summary>底层的领域模型，Stage3 对接真实索引流程时会用到其 Id/StoragePath 等字段。</summary>
    public IndexProfile Profile { get; }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string rootPath;

    [ObservableProperty]
    private IndexStatus status;

    [ObservableProperty]
    private long fileCount;

    [ObservableProperty]
    private long totalSizeBytes;

    [ObservableProperty]
    private DateTimeOffset? lastUpdatedAt;

    [ObservableProperty]
    private bool isBuilding;

    /// <summary>构建中的进度百分比（0-100），仅在 IsBuilding 为 true 时有意义。</summary>
    [ObservableProperty]
    private double buildProgressPercent;

    /// <summary>构建进度文案，例如"正在索引: report.docx (1,204 / 8,530)"。</summary>
    [ObservableProperty]
    private string? buildProgressText;

    /// <summary>当前是否处于"已暂停"状态（仅在 IsBuilding 为 true 时有意义）。</summary>
    [ObservableProperty]
    private bool isPaused;

    /// <summary>
    /// 本次运行关联的暂停控制器：MainViewModel 发起 IIndexingService.RunAsync 时会传入这个实例，
    /// "暂停/继续"按钮可以直接调用它，不需要经过 MainViewModel 转发。
    /// </summary>
    public ScanPauseController PauseController { get; } = new();

    public string StatusText => Status switch
    {
        IndexStatus.Ok => "可用",
        IndexStatus.NeedsUpdate => "待更新",
        IndexStatus.Building => "构建中",
        IndexStatus.SourceUnavailable => "源盘离线（可搜索）",
        IndexStatus.Error => "索引错误",
        _ => "未知",
    };

    public string StatusBrushKey => Status switch
    {
        IndexStatus.Ok => "StatusOkBrush",
        IndexStatus.NeedsUpdate => "StatusNeedsUpdateBrush",
        IndexStatus.Building => "StatusBuildingBrush",
        IndexStatus.SourceUnavailable => "StatusSourceUnavailableBrush",
        IndexStatus.Error => "StatusErrorBrush",
        _ => "TextTertiaryBrush",
    };

    public string SizeDisplay => FormatBytes(TotalSizeBytes);

    public string FileCountDisplay => FileCount.ToString("N0");

    public string LastUpdatedDisplay => LastUpdatedAt is { } t
        ? t.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
        : "尚未构建";

    [RelayCommand]
    private void TogglePause()
    {
        // 纯内存态信号，不涉及 IO：直接操作本卡片持有的 PauseController，
        // 真正阻塞在 WaitIfPaused 上的扫描线程会在下一个文件边界感知到这个状态变化。
        if (!IsBuilding)
        {
            return;
        }

        if (PauseController.IsPaused)
        {
            PauseController.Resume();
            IsPaused = false;
        }
        else
        {
            PauseController.Pause();
            IsPaused = true;
        }
    }

    /// <summary>
    /// "重建索引"按钮：把发起索引任务的实际编排逻辑（打开 ManifestStore、调用 IIndexingService、
    /// 处理取消/异常、任务完成后落盘 profile.json）交给 MainViewModel，本类只负责通知意图。
    /// </summary>
    public event EventHandler? RebuildRequested;

    [RelayCommand]
    private void Rebuild()
    {
        RebuildRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? RemoveRequested;

    [RelayCommand]
    private void Remove()
    {
        RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size:N0} {units[unitIndex]}"
            : $"{size:N1} {units[unitIndex]}";
    }
}
