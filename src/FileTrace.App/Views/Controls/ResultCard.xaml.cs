using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileTrace.App.ViewModels;

namespace FileTrace.App.Views.Controls;

/// <summary>
/// 一条搜索结果卡片。双击/单击卡片时尝试用系统资源管理器定位原文件所在目录
/// （对应设计稿"点击结果 -> 打开所在文件夹"的核心离线定位诉求）。
///
/// Stage2 阶段：这是纯 UI 交互，若源盘不在线（Directory.Exists 为 false），
/// 弹出一个轻量提示而不是抛异常或静默失败——这正是本产品"拔盘后依然可以搜索、
/// 但需要明确告知用户当前无法直接跳转"的关键体验点。
/// </summary>
public partial class ResultCard : UserControl
{
    public ResultCard()
    {
        InitializeComponent();
    }

    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not SearchResultItemViewModel item)
        {
            return;
        }

        if (File.Exists(item.FullPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"")
            {
                UseShellExecute = true,
            });
        }
        else if (Directory.Exists(item.DirectoryPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", item.DirectoryPath)
            {
                UseShellExecute = true,
            });
        }
        else
        {
            MessageBox.Show(
                $"未能在原路径找到该文件：\n{item.FullPath}\n\n源磁盘可能已断开连接。索引记录仍然保留，磁盘重新连接后即可再次定位。",
                "文件当前不可访问",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
