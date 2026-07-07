using System.Collections.ObjectModel;
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
    private CancellationTokenSource? _searchCts;
    private readonly Dictionary<string, CancellationTokenSource> _runningIndexTasks = new();

    public MainViewModel(
        IIndexProfileRepository profileRepository,
        ISearchGateway searchGateway,
        IIndexingService indexingService,
        IAppLogger? logger = null)
    {
        _profileRepository = profileRepository;
        _searchGateway = searchGateway;
        _indexingService = indexingService;
        _logger = logger ?? NullAppLogger.Instance;

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

        card.Status = IndexStatus.Building;
        card.IsBuilding = true;
        card.IsPaused = false;
        card.BuildProgressPercent = 0;
        card.BuildProgressText = "准备扫描…";

        var progress = new Progress<ScanProgress>(p =>
        {
            card.BuildProgressPercent = p.FilesScanned + p.FilesUnchanged > 0 && card.Profile.FileCount > 0
                ? Math.Min(100.0, 100.0 * (p.FilesScanned + p.FilesUnchanged) / Math.Max(card.Profile.FileCount, p.FilesScanned + p.FilesUnchanged))
                : 0;
            card.BuildProgressText = string.IsNullOrEmpty(p.CurrentPath)
                ? $"已处理 {p.FilesScanned:N0} 个文件…"
                : $"正在索引: {System.IO.Path.GetFileName(p.CurrentPath)} ({p.FilesScanned:N0} 已处理)";
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
            card.IsBuilding = false;
            card.IsPaused = false;
            card.BuildProgressText = null;
            _runningIndexTasks.Remove(profileId);
            cts.Dispose();
        }
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
