namespace FuShi.Infrastructure.Data;

/// <summary>
/// CSV 转义（M5a）：把字段包成合法的 RFC 4180 单元格。
/// 抽成纯函数便于单测——逗号、双引号、换行、中文都要能正确落进单元格。
/// </summary>
public static class Csv
{
    /// <summary>需要整体加引号的字符集合。</summary>
    private static readonly char[] SPECIAL_CHARS = { ',', '"', '\r', '\n' };

    /// <summary>单元格包裹字符。</summary>
    private const char QUOTE = '"';

    /// <summary>单元格内双引号的转义写法（翻倍）。</summary>
    private const string ESCAPED_QUOTE = "\"\"";

    /// <summary>
    /// 转义一个单元格：含逗号 / 双引号 / 换行时整体加双引号，内部双引号翻倍；
    /// 其余原样返回。<c>null</c> 视为空串。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>可安全拼进 CSV 行的文本。</returns>
    public static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (text.IndexOfAny(SPECIAL_CHARS) < 0)
        {
            return text;
        }

        return QUOTE + text.Replace("\"", ESCAPED_QUOTE, StringComparison.Ordinal) + QUOTE;
    }

    /// <summary>把若干单元格拼成一行（以 CRLF 结尾，Excel 兼容）。</summary>
    /// <param name="cells">单元格原始值。</param>
    /// <returns>一整行 CSV 文本。</returns>
    public static string Line(params string?[] cells) =>
        string.Join(",", cells.Select(Escape)) + "\r\n";
}
