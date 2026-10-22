using FluentAssertions;
using FuShi.Infrastructure.Data;

namespace FuShi.Tests;

/// <summary>M5a：CSV 转义纯函数测试（逗号 / 双引号 / 换行 / 中文）。</summary>
public sealed class CsvTests
{
    /// <summary>普通文本原样输出。</summary>
    [Fact]
    public void Escape_PlainText_ReturnsAsIs() => Csv.Escape("数学").Should().Be("数学");

    /// <summary>含逗号的字段整体加引号。</summary>
    [Fact]
    public void Escape_Comma_WrapsInQuotes() => Csv.Escape("数学, 进阶").Should().Be("\"数学, 进阶\"");

    /// <summary>内部双引号翻倍。</summary>
    [Fact]
    public void Escape_Quote_DoublesIt() => Csv.Escape("第 \"1\" 章").Should().Be("\"第 \"\"1\"\" 章\"");

    /// <summary>含换行的字段整体加引号（换行保留）。</summary>
    [Fact]
    public void Escape_Newline_WrapsInQuotes()
    {
        var escaped = Csv.Escape("第一行\n第二行");
        escaped.Should().Be("\"第一行\n第二行\"");
    }

    /// <summary>null 视为空串。</summary>
    [Fact]
    public void Escape_Null_ReturnsEmpty() => Csv.Escape(null).Should().BeEmpty();

    /// <summary>整行拼接使用 CRLF 结尾，且各单元格各自转义。</summary>
    [Fact]
    public void Line_JoinsEscapedCellsWithCrLf() =>
        Csv.Line("a,b", "c").Should().Be("\"a,b\",c\r\n");
}
