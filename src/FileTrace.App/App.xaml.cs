using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using FileTrace.App.Services;
using FileTrace.App.Services.Real;
using FileTrace.App.ViewModels;
using FileTrace.App.Views;
using FileTrace.Core.Models;

namespace FileTrace.App;

/// <summary>
/// 应用程序入口 / 组合根（composition root）。
/// Stage2 阶段手动 new 出 Mock 服务与 MainViewModel 并注入 MainWindow；
/// Stage3 起替换为真实实现：RealIndexProfileRepository（profile.json + 注册表持久化）、
/// RealIndexingService（驱动 IndexingCoordinator 真实扫描+Lucene写入）、
/// RealSearchGateway（基于当前内存中的 IndexProfiles 快照动态定位 Lucene 目录搜索）。
/// 未来如果引入 Microsoft.Extensions.DependencyInjection 容器，只需要替换本文件里的
/// 组装逻辑，不需要改动任何 View/ViewModel 代码。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 注：Stage1 的 GBK 等旧版编码兼容初始化（EncodingBootstrap）通过 [ModuleInitializer]
        // 在 FileTrace.Core 程序集被加载时自动触发一次，本项目引用了 Core 程序集即自动生效，
        // 不需要在这里显式调用。

        // 便携版约定：索引注册表保存在 exe 旁边的 data/registry.json（不依赖用户目录/注册表，
        // 整个 data 文件夹可以随程序一起打包搬迁）。每个索引自身的数据（profile.json/lucene/
        // manifest.db）则保存在用户新建索引时自行选择的 StoragePath，可以是任意盘符。
        string registryPath = Path.Combine(AppContext.BaseDirectory, "data", "registry.json");

        IIndexProfileRepository profileRepository = new RealIndexProfileRepository(registryPath);
        IIndexingService indexingService = new RealIndexingService();

        // ISearchGateway 需要在运行时动态读取 MainViewModel 当前持有的索引列表
        // （新建/删除索引后需要立即反映到可搜索范围），但 MainViewModel 的构造函数又需要
        // 一个 ISearchGateway 实例——用一个可延迟赋值的局部变量打破这个先有鸡还是先有蛋的循环：
        // RealSearchGateway 内部只是持有一个委托，直到真正发起搜索时才会被调用，
        // 那时 mainViewModel 已经完成构造赋值，闭包捕获的局部变量已经不为 null。
        MainViewModel? mainViewModelRef = null;
        ISearchGateway searchGateway = new RealSearchGateway(
            () => mainViewModelRef?.IndexProfiles.Select(c => c.Profile).ToList()
                ?? new List<IndexProfile>());

        var mainViewModel = new MainViewModel(profileRepository, searchGateway, indexingService);
        mainViewModelRef = mainViewModel;

        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel,
        };
        MainWindow = mainWindow;
        mainWindow.Show();

        _ = mainViewModel.InitializeAsync();
    }
}
