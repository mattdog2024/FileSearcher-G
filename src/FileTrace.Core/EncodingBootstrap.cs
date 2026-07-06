using System.Runtime.CompilerServices;
using System.Text;

namespace FileTrace.Core;

/// <summary>
/// .NET (Core/5+) 默认只内置 Unicode 系列编码（UTF-8/16/32）和 ASCII，
/// 不再自带 GBK/GB18030/GB2312/Big5 等旧版代码页编码——直接调用
/// <c>Encoding.GetEncoding("GBK")</c> 会抛 <see cref="NotSupportedException"/>，
/// 而且 UTF.Unknown 的编码探测结果对象在拿不到对应 Encoding 实现时会退化，
/// 导致中文老系统产出的 GBK/GB2312 文本文件被完全误判（例如误判成 iso-8859 系列）
/// 从而读出乱码——这对本产品是致命问题，因为中文系统里老旧 txt/csv/代码文件
/// 用 GBK/ANSI 编码的比例非常高。
///
/// 这里用 <see cref="ModuleInitializerAttribute"/> 在程序集被加载、
/// 任何提取器代码运行之前，自动注册 <c>System.Text.Encoding.CodePages</c>
/// 提供的扩展代码页支持，确保 GBK/GB18030/Big5 等编码在整个进程生命周期内可用。
/// 只要引用了 FileTrace.Core 程序集（无论是 WPF 主程序、单元测试还是探测工具），
/// 这个初始化都会自动生效，调用方不需要显式做任何操作。
/// </summary>
internal static class EncodingBootstrap
{
    // CA2255: 分析器默认不建议库代码使用 ModuleInitializer（通常建议由最终应用自己决定初始化时机）。
    // 这里刻意采用它：Core 是被 WPF 主程序、单元测试、探测工具等多个不同宿主引用的类库，
    // 无法保证每个宿主都会记得在 Main 入口手动调用编码注册，而一旦漏掉就会在中文老文件场景
    // 静默产生乱码（而不是直接报错），非常隐蔽。用 ModuleInitializer 保证"只要加载了这个程序集，
    // 编码支持就一定就绪"，属于刻意的防御性设计，而非误用。
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
#pragma warning restore CA2255
}
