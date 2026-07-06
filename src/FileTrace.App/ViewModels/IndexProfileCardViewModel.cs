using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTrace.Core.Models;

namespace FileTrace.App.ViewModels;

/// <summary>
/// 左侧"索引管理"抽屉里一张索引卡片的视图模型，对应设计稿 IndexDrawer 的卡片项。
/// 本类只做展示与命令转发，不直接持有 IndexingCoordinator——
/// 具体的"暂停/继续/重建/删除"业务逻辑由 Stage3 接入 MainViewModel 时通过事件/服务调用实现，
/// Stage2 阶段这里的命令先各自置为可执行的占位委托，方便 UI 交互先跑通。
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
        // Stage2 占位：真实暂停/继续逻辑将在接入 IndexingCoordinator + ScanPauseController 时实现。
        IsBuilding = !IsBuilding;
    }

    [RelayCommand]
    private void Rebuild()
    {
        // Stage2 占位：将在 Stage3 触发 IndexingCoordinator.RunAsync(rebuildFromScratch: true)。
        Status = IndexStatus.Building;
        IsBuilding = true;
        BuildProgressPercent = 0;
        BuildProgressText = "准备重建索引…";
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
