namespace FileTrace.Core.Tests.TestHelpers;

/// <summary>
/// 测试用的临时目录辅助类：构造时创建一个随机命名的临时目录，Dispose 时递归删除。
/// 避免各测试用例互相污染，也避免在仓库/工作目录里留下测试垃圾文件。
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "filetrace-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>在临时目录下创建一个文件（自动创建所需的子目录），写入指定内容（UTF-8）。</summary>
    public string CreateTextFile(string relativePath, string content)
    {
        string fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content, System.Text.Encoding.UTF8);
        return fullPath;
    }

    /// <summary>在临时目录下创建一个文件，写入原始字节（用于测试特定编码/二进制内容）。</summary>
    public string CreateBinaryFile(string relativePath, byte[] bytes)
    {
        string fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
        return fullPath;
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
            // 测试清理失败不应该导致测试用例本身报错，操作系统临时目录最终会被清理
        }
    }
}
