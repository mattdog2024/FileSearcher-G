using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FileTrace.App.Services;
using FileTrace.App.Services.Logging;
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
/// Stage4 追加：文件日志（<see cref="FileAppLogger"/>）与全局未捕获异常兜底——
/// 便携版发给最终用户时没有附加调试器，任何一个未处理异常都不应该表现为"程序无声消失"，
/// 而应该弹出一个友好提示并在 data/logs 下留一份可发回来的诊断记录。
/// 未来如果引入 Microsoft.Extensions.DependencyInjection 容器，只需要替换本文件里的
/// 组装逻辑，不需要改动任何 View/ViewModel 代码。
/// </summary>
public partial class App : Application
{
    private IAppLogger _logger = NullAppLogger.Instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 注：Stage1 的 GBK 等旧版编码兼容初始化（EncodingBootstrap）通过 [ModuleInitializer]
        // 在 FileTrace.Core 程序集被加载时自动触发一次，本项目引用了 Core 程序集即自动生效，
        // 不需要在这里显式调用。

        // 便携版约定：索引注册表保存在 exe 旁边的 data/registry.json（不依赖用户目录/注册表，
        // 整个 data 文件夹可以随程序一起打包搬迁）。每个索引自身的数据（profile.json/lucene/
        // manifest.db）则保存在用户新建索引时自行选择的 StoragePath，可以是任意盘符。
        // 日志同样落在便携版的 data/logs 目录下，遵循同一个"整个 data 文件夹随程序搬迁"的约定。
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        string registryPath = Path.Combine(dataDirectory, "registry.json");
        string logsDirectory = Path.Combine(dataDirectory, "logs");

        _logger = new FileAppLogger(logsDirectory);
        RegisterGlobalExceptionHandlers();
        _logger.LogInfo("应用启动。");

        IIndexProfileRepository profileRepository = new RealIndexProfileRepository(registryPath, _logger);
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

        var mainViewModel = new MainViewModel(profileRepository, searchGateway, indexingService, _logger, logsDirectory);
        mainViewModelRef = mainViewModel;

        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel,
        };
        MainWindow = mainWindow;
        mainWindow.Show();

        _ = mainViewModel.InitializeAsync();
    }

    /// <summary>
    /// 挂接三类未捕获异常的兜底处理：
    ///   1) DispatcherUnhandledException —— UI 线程上抛出且没被任何地方 catch 的异常
    ///      （最常见的情况，例如某个事件处理器里忘了 try/catch）；
    ///   2) AppDomain.CurrentDomain.UnhandledException —— 非 UI 线程上的致命异常，
    ///      这类异常触发时进程即将终止，只能尽力记录日志，无法阻止退出；
    ///   3) TaskScheduler.UnobservedTaskException —— async void 之外的 Task 在没有被
    ///      await/观察其异常的情况下被终结器回收时触发，本项目里 `_ = SomeAsync();`
    ///      这种"fire-and-forget"写法(例如 RunIndexingAsync 的调用点)如果内部抛出未捕获异常，
    ///      会从这里被捕获而不是静默吞掉。
    /// 三处都遵循同一个原则：记录尽可能详细的日志，UI 线程上尽量弹出提示但不崩溃退出
    /// （e.Handled = true），非 UI 线程异常无法阻止进程终止，但至少留下诊断记录。
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            _logger.LogError("UI 线程未捕获异常。", e.Exception);

            MessageBox.Show(
                $"程序遇到了一个未预期的错误，已尝试恢复但建议保存当前工作并重启程序。\n\n" +
                $"错误详情已记录到 data/logs 目录，如果问题持续出现，请将日志文件提供给开发者。\n\n{e.Exception.Message}",
                "寻迹 FileTrace - 出现错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            _logger.LogError(
                $"非 UI 线程致命异常（IsTerminating={e.IsTerminating}）。",
                e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger.LogError("未观察的 Task 异常（fire-and-forget 任务内部抛出）。", e.Exception);
            e.SetObserved();
        };
    }
}
