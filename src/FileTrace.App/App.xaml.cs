using System.Windows;
using FileTrace.App.Services;
using FileTrace.App.Services.Mock;
using FileTrace.App.ViewModels;
using FileTrace.App.Views;

namespace FileTrace.App;

/// <summary>
/// 应用程序入口 / 组合根（composition root）。
/// Stage2 阶段手动 new 出 Mock 服务与 MainViewModel 并注入 MainWindow；
/// Stage3 如果引入 Microsoft.Extensions.DependencyInjection 容器，只需要替换本文件里的
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

        IIndexProfileRepository profileRepository = new MockIndexProfileRepository();
        ISearchGateway searchGateway = new MockSearchGateway();

        var mainViewModel = new MainViewModel(profileRepository, searchGateway);

        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel,
        };
        MainWindow = mainWindow;
        mainWindow.Show();

        _ = mainViewModel.InitializeAsync();
    }
}
