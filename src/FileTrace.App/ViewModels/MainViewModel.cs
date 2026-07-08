using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTrace.App.Services;
using FileTrace.App.Services.Logging;
using FileTrace.Core.Models;
using FileTrace.Core.Scanning;
using FileTrace.Core.Search;

namespace FileTrace.App.ViewModels;

/// <summary>
/// 主窗口视图模型：承载左侧索引抽屉列表、顶部搜索框、类型筛选行、搜索结果列表，
/// 以及"新建索引"对话框的打开/确认流程。
///
/// 依赖通过构造函数注入的 <see cref="IIndexProfileRepository"/> / <see cref="ISearchGateway"/> 抽象，
/// Stage2 由 App.xaml.cs 组装 Mock 实现运行；Stage3 只需要在组合根替换为真实实现，
/// 本类与所有 View 完全不需要改动。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IIndexProfileRepository _profileRepository;
    private readonly ISearchGateway _searchGateway;
    private readonly IIndexingService _indexingService;
    private readonly IAppLogger _logger;
    private readonly string _logsDirectory;
    private CancellationTokenSource? _searchCts;
    private readonly Dictionary<string, CancellationTokenSource> _runningIndexTasks = new();

    public MainViewModel(
        IIndexProfileRepository profileRepository,
        ISearchGateway searchGateway,
        IIndexingService indexingService,
        IAppLogger? logger = null,
        string? logsDirectory = null)
    {
        _profileRepository = profileRepository;
        _searchGateway = searchGateway;
        _indexingService = indexingService;
        _logger = logger ?? NullAppLogger.Instance;
        _logsDirectory = logsDirectory ?? Path.Combine(AppContext.BaseDirectory, "data", "logs");

        IndexProfiles = new ObservableCollection<IndexProfileCardViewModel>();
        SearchResults = new ObservableCollection<SearchResultItemViewModel>();

        FilterChips = new ObservableCollection<FilterChipViewModel>
        {
            new("all", "全部", isSelected: true),
            new("doc", "文档"),
            new("sheet", "表格"),
            new("pdf", "PDF"),
            new("ppt", "演示文稿"),
            new("code", "代码"),
            new("text", "文本"),
        };
        foreach (var chip in FilterChips)
        {
            chip.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FilterChipViewModel.IsSelected) && chip.IsSelected)
                {
                    SelectFilterChip(chip);
                }
            };
        }
    }

    public ObservableCollection<IndexProfileCardViewModel> IndexProfiles { get; }

    public ObservableCollection<SearchResultItemViewModel> SearchResults { get; }

    public ObservableCollection<FilterChipViewModel> FilterChips { get; }

    [ObservableProperty]
    private string queryText = string.Empty;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private bool hasSearched;

    [ObservableProperty]
    private int totalHits;

    [ObservableProperty]
    private string statusMessage = "在上方输入关键词开始搜索，或先在左侧新建一个索引";

    [ObservableProperty]
    private bool isIndexDrawerOpen = true;

    [ObservableProperty]
    private bool isNewIndexDialogOpen;

    [ObservableProperty]
    private NewIndexDialogViewModel? newIndexDialog;

    [ObservableProperty]
    private bool isAboutDialogOpen;

    [ObservableProperty]
    private AboutDialogViewModel? aboutDialog;

    /// <summary>没有任何索引时的空态提示（区别于"搜索无结果"的空态）。</summary>
    public bool HasNoProfiles => IndexProfiles.Count == 0;

    public bool HasNoResults => HasSearched && !IsSearching && SearchResults.Count == 0;

    public async Task InitializeAsync()
    {
        try
        {
            var profiles = await _profileRepository.GetAllAsync();
            IndexProfiles.Clear();
            foreach (var p in profiles)
            {
                AttachCard(new IndexProfileCardViewModel(p));
            }
            OnPropertyChanged(nameof(HasNoProfiles));
            _logger.LogInfo($"启动加载完成，共 {profiles.Count} 个索引配置。");
        }
        catch (Exception ex)
        {
            // 注册表/profile.json 全部读取失败是一个相对极端的场景（例如 data 目录被外部程序
            // 破坏性写坏）；不能让应用直接白屏或崩溃——保留一个空的索引列表让用户至少可以
            // 重新新建索引，同时把详细异常写入日志文件供排查。
            _logger.LogError("启动时加载索引列表失败。", ex);
            StatusMessage = "加载已有索引列表时出现问题，已尝试跳过异常项。详情见 data/logs 日志。";
        }
    }

    private void AttachCard(IndexProfileCardViewModel card)
    {
        card.RemoveRequested += async (_, _) => await RemoveProfileAsync(card);
        card.RebuildRequested += async (_, _) => await RunIndexingAsync(card, rebuildFromScratch: true);
        IndexProfiles.Add(card);
    }

    /// <summary>
    /// 发起一次索引任务（新建首次构建 / 用户点击"重建索引"）。这是本类里唯一直接调用
    /// <see cref="IIndexingService"/> 的地方——持有生命周期较长的 CancellationTokenSource，
    /// 并把扫描进度通过 <see cref="IProgress{ScanProgress}"/> 实时同步回卡片的进度条/文案。
    /// </summary>
    private async Task RunIndexingAsync(IndexProfileCardViewModel card, bool rebuildFromScratch)
    {
        string profileId = card.Profile.Id;
        if (_runningIndexTasks.ContainsKey(profileId))
        {
            return; // 已有一个索引任务在跑，不重复发起
        }

        var cts = new CancellationTokenSource();
        _runningIndexTasks[profileId] = cts;

        // 已知本次构建前的总文件数（例如"重建索引"针对一个之前已经构建过的索引）时，
        // 才有意义展示精确百分比；全新索引首次构建时 card.Profile.FileCount 恒为 0，
        // 这种情况下改用"不确定进度"动画（滚动条），并配合已处理文件数/字节数文案，
        // 避免进度条永远停在 0% 让用户误以为程序卡死。
        long knownTotalFileCount = card.Profile.FileCount;
        bool hasKnownTotal = knownTotalFileCount > 0;

        // 同一张卡片的 PauseController 会跨多次"重建索引"操作被复用，必须先清空上一次
        // 遗留的暂停状态/累计暂停时长，否则这一次的"已用时/预计剩余时间"计算会被污染。
        card.PauseController.Reset();

        // 详情信息框（已用时/预计剩余时间/处理速度）依赖的计时起点：用 Stopwatch 而不是
        // DateTimeOffset.Now 相减，避免系统时钟被用户/NTP 调整时导致耗时计算异常。
        var stopwatch = Stopwatch.StartNew();

        card.Status = IndexStatus.Building;
        card.IsBuilding = true;
        card.IsPaused = false;
        card.IsProgressIndeterminate = !hasKnownTotal;
        card.BuildProgressPercent = 0;
        card.BuildProgressText = "准备扫描…";
        card.ElapsedTimeDisplay = "已用时 0 秒";
        card.EstimatedRemainingDisplay = hasKnownTotal ? "预计剩余 计算中…" : "总量未知，暂无法估算剩余时间";
        card.ProcessingSpeedDisplay = "0 个/秒";
        card.BuildStatsDisplay = "已索引 0 · 跳过 0 · 失败 0 · 删除 0";

        var progress = new Progress<ScanProgress>(p =>
        {
            long processed = p.FilesScanned + p.FilesUnchanged;

            if (hasKnownTotal)
            {
                card.IsProgressIndeterminate = false;
                card.BuildProgressPercent = Math.Min(
                    100.0,
                    100.0 * processed / Math.Max(knownTotalFileCount, processed));
            }
            else
            {
                // 总数未知：保持不确定进度动画，百分比字段不参与展示。
                card.IsProgressIndeterminate = true;
            }

            string sizeText = FormatBytesForProgress(p.BytesProcessed);
            card.BuildProgressText = string.IsNullOrEmpty(p.CurrentPath)
                ? $"已处理 {processed:N0} 个文件 · {sizeText}"
                : $"正在索引: {System.IO.Path.GetFileName(p.CurrentPath)}（已处理 {processed:N0} 个 · {sizeText}）";

            // 有效耗时 = 挂钟耗时 - 累计暂停时长，避免用户长时间暂停期间的"死时间"
            // 拉低算出来的处理速度、抬高预计剩余时间的误差。
            TimeSpan effectiveElapsed = stopwatch.Elapsed - card.PauseController.TotalPausedDuration;
            if (effectiveElapsed < TimeSpan.Zero)
            {
                effectiveElapsed = TimeSpan.Zero;
            }

            card.ElapsedTimeDisplay = $"已用时 {FormatDuration(stopwatch.Elapsed)}";

            double effectiveSeconds = effectiveElapsed.TotalSeconds;
            if (effectiveSeconds >= 1 && processed > 0)
            {
                double filesPerSecond = processed / effectiveSeconds;
                double bytesPerSecond = p.BytesProcessed / effectiveSeconds;
                card.ProcessingSpeedDisplay =
                    $"{filesPerSecond:N1} 个/秒 · {FormatBytesForProgress((long)bytesPerSecond)}/秒";

                if (hasKnownTotal && filesPerSecond > 0)
                {
                    long remaining = Math.Max(0, knownTotalFileCount - processed);
                    var eta = TimeSpan.FromSeconds(remaining / filesPerSecond);
                    card.EstimatedRemainingDisplay = $"预计剩余 {FormatDuration(eta)}";
                }
            }
            else if (!hasKnownTotal)
            {
                card.EstimatedRemainingDisplay = "总量未知，暂无法估算剩余时间";
            }

            card.BuildStatsDisplay =
                $"已索引 {p.FilesIndexed:N0} · 跳过 {p.FilesUnchanged:N0} · 失败 {p.FilesFailed:N0} · 删除 {p.FilesRemoved:N0}";
        });

        try
        {
            var summary = await _indexingService.RunAsync(
                card.Profile, rebuildFromScratch, card.PauseController, progress, cts.Token);

            card.Profile.FileCount = summary.FinalDocumentCount;
            card.Profile.LastUpdatedAt = DateTimeOffset.Now;
            card.Profile.Status = IndexStatus.Ok;
            await _profileRepository.SaveAsync(card.Profile, CancellationToken.None);

            card.FileCount = card.Profile.FileCount;
            card.LastUpdatedAt = card.Profile.LastUpdatedAt;
            card.Status = IndexStatus.Ok;
            _logger.LogInfo(
                $"索引构建完成: {card.Profile.Name} ({card.Profile.RootPath})，共 {summary.FinalDocumentCount:N0} 个文件，" +
                $"新增 {summary.Added}，更新 {summary.Updated}，删除 {summary.Removed}，失败 {summary.Failed}。");
        }
        catch (OperationCanceledException)
        {
            card.Status = IndexStatus.NeedsUpdate;
            _logger.LogInfo($"索引构建被取消: {card.Profile.Name} ({card.Profile.RootPath})。");
        }
        catch (Exception ex)
        {
            card.Status = IndexStatus.Error;
            _logger.LogError($"索引构建失败: {card.Profile.Name} ({card.Profile.RootPath})。", ex);
        }
        finally
        {
            stopwatch.Stop();
            card.IsBuilding = false;
            card.IsPaused = false;
            card.IsProgressIndeterminate = false;
            card.BuildProgressText = null;
            card.ElapsedTimeDisplay = null;
            card.EstimatedRemainingDisplay = null;
            card.ProcessingSpeedDisplay = null;
            card.BuildStatsDisplay = null;
            _runningIndexTasks.Remove(profileId);
            cts.Dispose();
        }
    }

    private static string FormatBytesForProgress(long bytes)
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

    /// <summary>
    /// 把 TimeSpan 格式化成中文友好的"已用时/预计剩余"文案：
    /// 小于1分钟只显示秒；小于1小时显示"X分Y秒"；否则显示"X时Y分"。
    /// </summary>
    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} 时 {duration.Minutes} 分";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes} 分 {duration.Seconds} 秒";
        }

        return $"{Math.Max(1, duration.Seconds)} 秒";
    }

    [RelayCommand]
    private void ToggleIndexDrawer()
    {
        IsIndexDrawerOpen = !IsIndexDrawerOpen;
    }

    [RelayCommand]
    private void OpenNewIndexDialog()
    {
        NewIndexDialog = new NewIndexDialogViewModel();
        IsNewIndexDialogOpen = true;
    }

    [RelayCommand]
    private void CancelNewIndexDialog()
    {
        IsNewIndexDialogOpen = false;
        NewIndexDialog = null;
    }

    /// <summary>
    /// 打开"关于"对话框：展示应用名称/版本号/简介，并提供一键跳转到日志文件夹的入口，
    /// 方便最终用户在遇到问题时能自助定位并提供诊断信息给开发者。
    /// </summary>
    [RelayCommand]
    private void OpenAboutDialog()
    {
        var dialog = new AboutDialogViewModel(_logsDirectory);
        dialog.CloseRequested += (_, _) =>
        {
            IsAboutDialogOpen = false;
            AboutDialog = null;
        };
        AboutDialog = dialog;
        IsAboutDialogOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmNewIndexDialogAsync()
    {
        if (NewIndexDialog is null || !NewIndexDialog.CanConfirm)
        {
            return;
        }

        var profile = NewIndexDialog.BuildProfile();
        var created = await _profileRepository.CreateAsync(profile);

        var card = new IndexProfileCardViewModel(created);
        AttachCard(card);
        OnPropertyChanged(nameof(HasNoProfiles));

        IsNewIndexDialogOpen = false;
        NewIndexDialog = null;

        // 新建索引后立即发起一次首次全量构建，无需用户再手动点"重建索引"。
        _ = RunIndexingAsync(card, rebuildFromScratch: true);
    }

    private async Task RemoveProfileAsync(IndexProfileCardViewModel card)
    {
        try
        {
            await _profileRepository.DeleteAsync(card.Profile.Id);
            IndexProfiles.Remove(card);
            OnPropertyChanged(nameof(HasNoProfiles));
            _logger.LogInfo($"已删除索引: {card.Profile.Name} ({card.Profile.RootPath})。");
        }
        catch (Exception ex)
        {
            _logger.LogError($"删除索引失败: {card.Profile.Name} ({card.Profile.RootPath})。", ex);
            StatusMessage = "删除索引时出现问题，详情见 data/logs 日志。";
        }
    }

    private void SelectFilterChip(FilterChipViewModel selected)
    {
        foreach (var chip in FilterChips)
        {
            if (!ReferenceEquals(chip, selected))
            {
                chip.IsSelected = false;
            }
        }
    }

    partial void OnQueryTextChanged(string value)
    {
        SearchCommand.NotifyCanExecuteChanged();
    }

    private bool CanSearch() => !string.IsNullOrWhiteSpace(QueryText) && !IsSearching;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;

        IsSearching = true;
        HasSearched = true;
        StatusMessage = "搜索中…";

        try
        {
            var selectedCategory = FilterChips.FirstOrDefault(c => c.IsSelected)?.Category ?? "all";
            var profileIds = IndexProfiles.Select(p => p.Profile.Id).ToList();

            var request = new SearchRequest(QueryText.Trim(), SearchFieldScope.FileNameAndContent, profileIds);
            var result = await _searchGateway.SearchAsync(request, cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            var items = selectedCategory == "all"
                ? result.Items
                : result.Items.Where(i => FileTypeCatalog.Find(i.ExtensionNoDot)?.Category == selectedCategory).ToList();

            SearchResults.Clear();
            foreach (var item in items)
            {
                SearchResults.Add(new SearchResultItemViewModel(item));
            }

            TotalHits = result.TotalHits;
            StatusMessage = SearchResults.Count > 0
                ? $"找到 {result.TotalHits} 个结果"
                : "没有找到匹配的文件，换个关键词试试？";
        }
        catch (OperationCanceledException)
        {
            // 新的搜索请求已发出，忽略被取消的旧请求。
        }
        catch (Exception ex)
        {
            _logger.LogError($"搜索失败，关键词: {QueryText}", ex);
            StatusMessage = "搜索时发生错误，请重试。详情见 data/logs 日志。";
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                IsSearching = false;
            }
            OnPropertyChanged(nameof(HasNoResults));
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        QueryText = string.Empty;
        SearchResults.Clear();
        HasSearched = false;
        TotalHits = 0;
        StatusMessage = "在上方输入关键词开始搜索，或先在左侧新建一个索引";
        OnPropertyChanged(nameof(HasNoResults));
    }
}
