using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FileTrace.App.ViewModels;

/// <summary>
/// "关于"对话框的视图模型。Stage4 打磨的一部分——最终用户遇到问题时，
/// 需要一个显而易见的入口能看到当前版本号，以及一键定位到日志文件夹，
/// 而不需要知道"data/logs"这种内部实现细节该去哪个目录找。
/// </summary>
public sealed partial class AboutDialogViewModel : ObservableObject
{
    public AboutDialogViewModel(string logsDirectory)
    {
        LogsDirectory = logsDirectory;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText = version is null ? "未知版本" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    public string AppName => "寻迹 FileTrace";

    public string VersionText { get; }

    public string Description =>
        "本地文件搜索索引工具：为大容量硬盘建立文件名 + 全文内容索引，硬盘拔出后仍可离线搜索并定位原路径。";

    public string LogsDirectory { get; }

    /// <summary>
    /// 打开日志文件夹。目录可能因为从未记录过任何日志而尚未创建——这里主动创建一次，
    /// 保证按钮点击后始终能看到一个真实存在的文件夹，而不是"目录不存在"的资源管理器错误提示。
    /// </summary>
    [RelayCommand]
    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(LogsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LogsDirectory}\"")
        {
            UseShellExecute = true,
        });
    }

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
