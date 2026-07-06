namespace FileTrace.Core.Models;

/// <summary>
/// 一个文件类型的解析策略。
/// </summary>
public enum ExtractionStrategy
{
    /// <summary>可以提取全文内容并建立内容索引（docx/xlsx/pdf/txt/代码等）。</summary>
    FullText,

    /// <summary>暂不提取全文内容，仅索引文件名/路径/大小/时间（当前版本的 ppt/pptx 属于此类）。</summary>
    FileNameOnly,
}

/// <summary>
/// 一种受支持文件类型的静态描述信息：扩展名、分类、图标配色、解析策略。
/// 对应设计稿里 IndexDrawer 的"包含的文件类型"勾选列表，以及 ResultCard 左侧色块。
/// </summary>
public sealed record FileTypeDescriptor(
    string Extension,        // 不含点，小写，例如 "docx"
    string Category,         // 用于 FilterRow 的类型筛选分组：doc/sheet/pdf/code/text/other
    string IconLabel,        // 卡片左侧色块文字，例如 "DOC" "XLS" "PPT"
    string IconColorHex,     // 色块背景色
    ExtractionStrategy Strategy
);

/// <summary>
/// 全部受支持文件类型的注册表。来源：designer handoff README 中列出的完整类型列表
/// txt, doc, docx, pdf, xls, xlsx, ppt, pptx, csv, md, html, py, java, c, cpp, js, ts, css,
/// json, xml, yaml, ini, cfg, log, sql, bat, sh, ps1, rtf, epub
/// </summary>
public static class FileTypeCatalog
{
    private const string ColorPdf = "#DC2626";
    private const string ColorDoc = "#2563EB";
    private const string ColorXls = "#059669";
    private const string ColorPpt = "#EA580C";
    private const string ColorMd = "#0891B2";
    private const string ColorTxt = "#64748B";
    private const string ColorCode = "#7C3AED";
    private const string ColorHtml = "#DB2777";
    private const string ColorJson = "#B45309";

    public static readonly IReadOnlyList<FileTypeDescriptor> All = new List<FileTypeDescriptor>
    {
        // ---- 文档类：可全文提取 ----
        new("doc",  "doc",  "DOC", ColorDoc, ExtractionStrategy.FullText),
        new("docx", "doc",  "DOC", ColorDoc, ExtractionStrategy.FullText),
        new("rtf",  "doc",  "RTF", ColorDoc, ExtractionStrategy.FullText),

        // ---- 表格类：可全文提取 ----
        new("xls",  "sheet", "XLS", ColorXls, ExtractionStrategy.FullText),
        new("xlsx", "sheet", "XLS", ColorXls, ExtractionStrategy.FullText),
        new("csv",  "sheet", "CSV", ColorXls, ExtractionStrategy.FullText),

        // ---- 演示文稿：当前版本仅索引文件名（见方案C决策）----
        new("ppt",  "ppt", "PPT", ColorPpt, ExtractionStrategy.FileNameOnly),
        new("pptx", "ppt", "PPT", ColorPpt, ExtractionStrategy.FileNameOnly),

        // ---- PDF：可全文提取 ----
        new("pdf",  "pdf", "PDF", ColorPdf, ExtractionStrategy.FullText),

        // ---- 纯文本 / 标记类：可全文提取 ----
        new("txt",  "text", "TXT", ColorTxt, ExtractionStrategy.FullText),
        new("md",   "text", "MD",  ColorMd,  ExtractionStrategy.FullText),
        new("log",  "text", "LOG", ColorTxt, ExtractionStrategy.FullText),
        new("html", "code", "HTM", ColorHtml, ExtractionStrategy.FullText),
        new("json", "code", "JSON", ColorJson, ExtractionStrategy.FullText),
        new("xml",  "code", "XML", ColorJson, ExtractionStrategy.FullText),
        new("yaml", "code", "YML", ColorJson, ExtractionStrategy.FullText),
        new("epub", "text", "EPUB", ColorMd, ExtractionStrategy.FullText),

        // ---- 代码/配置类：可全文提取 ----
        new("py",   "code", "PY",  ColorCode, ExtractionStrategy.FullText),
        new("java", "code", "JAVA", ColorCode, ExtractionStrategy.FullText),
        new("c",    "code", "C",   ColorCode, ExtractionStrategy.FullText),
        new("cpp",  "code", "CPP", ColorCode, ExtractionStrategy.FullText),
        new("js",   "code", "JS",  ColorCode, ExtractionStrategy.FullText),
        new("ts",   "code", "TS",  ColorCode, ExtractionStrategy.FullText),
        new("css",  "code", "CSS", ColorCode, ExtractionStrategy.FullText),
        new("ini",  "code", "INI", ColorCode, ExtractionStrategy.FullText),
        new("cfg",  "code", "CFG", ColorCode, ExtractionStrategy.FullText),
        new("sql",  "code", "SQL", ColorCode, ExtractionStrategy.FullText),
        new("bat",  "code", "BAT", ColorCode, ExtractionStrategy.FullText),
        new("sh",   "code", "SH",  ColorCode, ExtractionStrategy.FullText),
        new("ps1",  "code", "PS1", ColorCode, ExtractionStrategy.FullText),
    };

    private static readonly Dictionary<string, FileTypeDescriptor> ByExtension =
        All.ToDictionary(t => t.Extension, StringComparer.OrdinalIgnoreCase);

    public static FileTypeDescriptor? Find(string extensionNoDot) =>
        ByExtension.TryGetValue(extensionNoDot.TrimStart('.').ToLowerInvariant(), out var d) ? d : null;

    /// <summary>
    /// 判断某扩展名是否在用户勾选的类型集合中（大小写不敏感，自动去掉前导点）。
    /// </summary>
    public static bool IsSelected(string extensionNoDot, IReadOnlySet<string> selectedExtensions) =>
        selectedExtensions.Contains(extensionNoDot.TrimStart('.').ToLowerInvariant());
}
