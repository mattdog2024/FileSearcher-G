using System.Text;
using System.Text.RegularExpressions;
using UtfUnknown;

namespace FileTrace.Core.Extraction;

/// <summary>
/// RTF 文件内容提取器。
///
/// RTF 是纯文本格式的控制字/控制符标记语言（不是二进制 OLE 容器），因此不需要 NPOI/第三方库，
/// 用一个轻量级的状态机剥离控制字、控制符、分组花括号和内嵌的二进制/图片数据块，
/// 只保留可读文本——这对"搜索定位文件"这个场景已经足够，不追求 100% 精确还原排版。
///
/// 关键容错点：
///   - 转义字符 \{ \} \\ 需要还原成普通字符；
///   - \uNNNN 是 Unicode 转义（后面通常紧跟一个后备 ANSI 字符需要跳过）；
///   - 图片/对象数据（\pict 等）体积可能很大且是十六进制编码的二进制垃圾，必须整段跳过，
///     否则会把大量无意义的十六进制字符串索引进去。
/// </summary>
public sealed class RtfExtractor : IContentExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { "rtf" };

    private static readonly Regex ControlWordRegex = new(@"\\([a-zA-Z]+)(-?\d+)?[ ]?", RegexOptions.Compiled);
    private static readonly Regex UnicodeEscapeRegex = new(@"\\u(-?\d+)\??", RegexOptions.Compiled);
    private static readonly Regex HexEscapeRegex = new(@"\\'([0-9a-fA-F]{2})", RegexOptions.Compiled);
    private static readonly Regex AnsiCpgRegex = new(@"\\ansicpg(\d+)", RegexOptions.Compiled);

    // 遇到这些控制字所在的分组，整段跳过（图片、对象、字体表、样式表、颜色表等非正文内容）
    private static readonly HashSet<string> SkipGroupKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "pict", "object", "fonttbl", "colortbl", "stylesheet", "info", "generator",
        "themedata", "colorschememapping", "latentstyles", "listtable", "listoverridetable",
        "rsidtbl", "datastore", "xmlnstbl",
    };

    public Task<ExtractResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        return Task.Run(() => Extract(filePath), cancellationToken);
    }

    private static ExtractResult Extract(string filePath)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == 0)
            {
                return ExtractResult.Ok(string.Empty);
            }

            // RTF 规范要求头部必须是纯 ASCII 的控制结构（"{\rtf1\ansi\ansicpg936..."），
            // 真正决定 \'hh 十六进制转义里 8-bit 字节含义的，是文件头部显式声明的 \ansicpgNNNN
            // Windows 代码页编号（例如中文简体常见 936=GBK，繁体常见 950=Big5），而不是整份文件
            // 的"通用编码探测"——RTF 正文绝大部分字节都是 ASCII 控制字，把 UTF.Unknown
            // 直接丢给整个文件字节流会被 ASCII 控制字主导，误判成 ascii/latin，
            // 导致真正的中文内容被当成 ASCII 解码成一堆 '?'。因此这里改为优先解析 \ansicpg 声明，
            // 找不到该声明（极少数非标准 RTF）时才退化为对全文做启发式编码探测。
            Encoding encoding = ResolveEncodingFromAnsiCpg(bytes) ?? DetectEncodingHeuristically(bytes);

            // 注意：RTF 控制结构本身永远是 ASCII，非 ASCII 字符要么用 \uNNNN Unicode 转义，
            // 要么用 \'hh 十六进制字节转义（按 \ansicpg 声明的代码页，例如中文常见 GBK 是双字节，
            // 需要连续两个 \'hh\'hh 才能拼出一个汉字）。因此这里先把整个文件当 ASCII/Latin1 读取
            // 保留原始字节的一一对应关系，交给 StripRtf 内部按 \'hh 序列自行用目标编码解码。
            string raw = Encoding.Latin1.GetString(bytes);
            string text = StripRtf(raw, encoding);
            return ExtractResult.Ok(text);
        }
        catch (Exception ex)
        {
            return ExtractResult.Fail("rtf 解析失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 从文件头部（RTF 规范要求 \ansicpg 必须出现在文档开头附近的控制字序列里）解析
    /// Windows 代码页编号并映射为 .NET Encoding。找不到声明或代码页不受支持时返回 null。
    /// </summary>
    private static Encoding? ResolveEncodingFromAnsiCpg(byte[] bytes)
    {
        // \ansicpg 一定出现在文件最前面的控制字区域，只需要看开头一小段字节，
        // 用 Latin1 解码不会破坏字节与字符的一一对应关系（不影响后续正则匹配数字）。
        int headerLength = Math.Min(bytes.Length, 512);
        string header = Encoding.Latin1.GetString(bytes, 0, headerLength);
        var match = AnsiCpgRegex.Match(header);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int codePage))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 找不到 \ansicpg 声明时的兜底方案：对全文做一次启发式编码探测。
    /// 由于正文夹杂大量 ASCII 控制字，探测准确率有限，最终还有一层 GB18030 -> Latin1 的硬兜底，
    /// 保证无论如何都能返回一个可用的 Encoding，不会抛异常中断整个提取流程。
    /// </summary>
    private static Encoding DetectEncodingHeuristically(byte[] bytes)
    {
        try
        {
            var detected = CharsetDetector.DetectFromBytes(bytes)?.Detected;
            if (detected is not null)
            {
                return detected.Encoding;
            }
        }
        catch
        {
            // 忽略探测异常，走下面的硬兜底
        }

        try
        {
            return Encoding.GetEncoding("GB18030");
        }
        catch
        {
            return Encoding.Latin1;
        }
    }

    private static string StripRtf(string input, Encoding byteEncoding)
    {
        var output = new StringBuilder(input.Length / 2);
        int i = 0;
        int depth = 0;
        var skipDepthStack = new Stack<int>(); // 记录需要跳过的分组深度，遇到对应 '}' 才恢复
        var pendingHexBytes = new List<byte>(); // 连续的 \'hh 转义先攒成字节序列，遇到非十六进制转义时统一按目标编码解码（处理GBK双字节汉字）

        void FlushPendingHexBytes(bool skip)
        {
            if (pendingHexBytes.Count == 0)
            {
                return;
            }
            if (!skip)
            {
                try
                {
                    output.Append(byteEncoding.GetString(pendingHexBytes.ToArray()));
                }
                catch
                {
                    // 解码失败（字节序列不完整/非法）时静默丢弃这一小段，不影响其余内容
                }
            }
            pendingHexBytes.Clear();
        }

        while (i < input.Length)
        {
            char c = input[i];
            bool inSkippedGroup = skipDepthStack.Count > 0;

            if (c == '{')
            {
                FlushPendingHexBytes(inSkippedGroup);
                depth++;
                i++;
                continue;
            }

            if (c == '}')
            {
                FlushPendingHexBytes(inSkippedGroup);
                if (skipDepthStack.Count > 0 && skipDepthStack.Peek() == depth)
                {
                    skipDepthStack.Pop();
                }
                depth--;
                i++;
                continue;
            }

            if (c == '\\')
            {
                // 十六进制字节转义：\'hh —— 中文 RTF 里 GBK 双字节字符会连续出现两个 \'hh\'hh，
                // 必须先攒够字节再一次性用目标代码页解码，逐字节单独解码会产生乱码。
                var hexMatch = HexEscapeRegex.Match(input, i);
                if (hexMatch.Success && hexMatch.Index == i)
                {
                    if (byte.TryParse(hexMatch.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    {
                        pendingHexBytes.Add(b);
                    }
                    i += hexMatch.Length;
                    continue;
                }

                FlushPendingHexBytes(inSkippedGroup);

                // Unicode 转义：\uNNNN，后面紧跟一个替代字符（按 \ucN 声明的个数，默认1个）需要跳过
                var uMatch = UnicodeEscapeRegex.Match(input, i);
                if (uMatch.Success && uMatch.Index == i)
                {
                    if (!inSkippedGroup && int.TryParse(uMatch.Groups[1].Value, out int codePoint))
                    {
                        if (codePoint < 0) codePoint += 65536;
                        try { output.Append(char.ConvertFromUtf32(codePoint)); } catch { /* 忽略非法码点 */ }
                    }
                    i += uMatch.Length;
                    // 跳过紧随其后的一个后备字符（RTF 默认 \uc1）
                    if (i < input.Length && input[i] != '\\' && input[i] != '{' && input[i] != '}')
                    {
                        i++;
                    }
                    continue;
                }

                // 转义字符：\{ \} \\
                if (i + 1 < input.Length && (input[i + 1] == '{' || input[i + 1] == '}' || input[i + 1] == '\\'))
                {
                    if (!inSkippedGroup) output.Append(input[i + 1]);
                    i += 2;
                    continue;
                }

                // \par \line 等换行控制字
                var wordMatch = ControlWordRegex.Match(input, i);
                if (wordMatch.Success && wordMatch.Index == i)
                {
                    string keyword = wordMatch.Groups[1].Value;
                    if (SkipGroupKeywords.Contains(keyword))
                    {
                        skipDepthStack.Push(depth);
                    }
                    else if (!inSkippedGroup && (keyword is "par" or "line" or "tab"))
                    {
                        output.Append(keyword == "tab" ? '\t' : '\n');
                    }
                    i += wordMatch.Length;
                    continue;
                }

                // 未识别的控制符，保守跳过一个字符（反斜杠本身）
                i++;
                continue;
            }

            FlushPendingHexBytes(inSkippedGroup);
            if (!inSkippedGroup)
            {
                output.Append(c);
            }
            i++;
        }

        FlushPendingHexBytes(skipDepthStack.Count > 0);
        return output.ToString();
    }
}
