namespace FileTrace.App.Tests.TestHelpers;

/// <summary>
/// 测试用的临时目录辅助类：构造时创建一个随机命名的临时目录，Dispose 时递归删除。
/// 与 FileTrace.Core.Tests.TestHelpers.TempDirectory 是同一套设计，因为 App 测试项目
/// 不引用 Core 测试项目（两者是平级的独立测试工程），故在此复制一份保持独立可编译。
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "filetrace-app-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // 测试清理失败不应该导致测试用例本身报错
        }
    }
}
