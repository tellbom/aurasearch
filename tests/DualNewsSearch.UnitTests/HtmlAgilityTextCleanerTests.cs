using System.Globalization;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Infrastructure.Content;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.UnitTests;

public sealed class HtmlAgilityTextCleanerTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        yield return new object[] { "<p>第一段</p><p>第二段</p>", "第一段\n第二段" };
        yield return new object[] { "<div><strong>嵌套</strong>标签</div>", "嵌套标签" };
        yield return new object[] { "<table><tr><th>名称</th><th>值</th></tr><tr><td>A</td><td>1</td></tr></table>", "名称\t值\nA\t1" };
        yield return new object[] { "&lt;公告&gt;&amp;通知", "<公告>&通知" };
        yield return new object[] { "&amp;lt;公告&amp;gt;", "<公告>" };
        yield return new object[] { "<style>x{}</style><script>alert(1)</script><p>正文</p>", "正文" };
        yield return new object[] { "", "" };
        yield return new object[] { "<p>损坏<strong>HTML", "损坏HTML" };
        yield return new object[] { "<p>中文，标点！保留。</p>", "中文，标点！保留。" };
        yield return new object[] { "<p>A</p>\r\n\r\n<div>B</div><br><br>C", "A\nB\n\nC" };
        yield return new object[] { "<div style=\"display:none\">秘密</div><p>可见</p>", "可见" };
        yield return new object[] { "<!--comment--><p>内容</p>", "内容" };
        yield return new object[] { "<p>中文<strong>连续</strong>字符</p>", "中文连续字符" };
        yield return new object[] { "<ul><li>一</li><li>二</li></ul>", "一\n二" };
        yield return new object[] { "<h1>标题</h1><div>正文<br>换行</div>", "标题\n正文\n换行" };
        yield return new object[] { "<p>&nbsp; A \t B &nbsp;</p>", "A B" };
        yield return new object[] { "<noscript>隐藏</noscript><p>显示</p>", "显示" };
        yield return new object[] { "<iframe>隐藏</iframe><p>显示</p>", "显示" };
        yield return new object[] { "<p aria-hidden=\"true\">隐藏</p><p>显示</p>", "显示" };
        yield return new object[] { "纯文本", "纯文本" };
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Clean_CoversFixtureMatrix(string html, string expected)
    {
        CreateCleaner().Clean(html).Text.Should().Be(expected);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void Clean_IsCultureIndependentAndIdempotent(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            HtmlAgilityTextCleaner cleaner = CreateCleaner();
            string once = cleaner.Clean("<p>中文<strong>连续</strong>字符&amp;amp;</p>").Text;
            string twice = cleaner.Clean(once).Text;
            twice.Should().Be(once);
            once.Should().Be("中文连续字符&");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Clean_DoesNotSplitSurrogatePairWhenTruncating()
    {
        HtmlAgilityTextCleaner cleaner = CreateCleaner(maxLength: 2);
        var result = cleaner.Clean("A😀B");

        result.ContentTruncated.Should().BeTrue();
        result.Text.Should().Be("A");
    }

    private static HtmlAgilityTextCleaner CreateCleaner(int maxLength = 200_000)
    {
        return new HtmlAgilityTextCleaner(
            Options.Create(new IndexingOptions
            {
                SqlitePath = "unused.db",
                HtmlMaxLength = maxLength
            }));
    }
}

