using DualNewsSearch.Infrastructure.Persistence;
using FluentAssertions;
using HtmlAgilityPack;

namespace DualNewsSearch.UnitTests;

public sealed class SearchResultContentStoreTests
{
    [Fact]
    public void NewsSummaryIsAlwaysBoundedWithoutSplittingSurrogatePairs()
    {
        string content = new string('中', SearchResultContentStore.NewsSummaryLength - 1) + "😀尾部";

        string summary = SearchResultContentStore.CreateSummary(content);

        summary.Should().EndWith("…");
        summary.Should().NotContain("\ud83d");
        summary.Length.Should().Be(SearchResultContentStore.NewsSummaryLength);
    }

    [Fact]
    public void NewsSummaryIncludesEllipsisWithinConfiguredLimit()
    {
        string summary = SearchResultContentStore.CreateSummary(new string('闻', 300));

        summary.Should().EndWith("…");
        summary.Length.Should().Be(SearchResultContentStore.NewsSummaryLength);
    }

    [Fact]
    public void AnnouncementHighlightPreservesBalancedHtmlAndRemovesExecutableContent()
    {
        const string html = "<div onclick=\"bad()\"><p>系统<strong>维护</strong>通知</p>" +
            "<script>alert(1)</script><a href=\"javascript:bad()\">维护详情</a></div>";

        string formatted = AnnouncementHtmlFormatter.SanitizeAndHighlight(html, "维护");
        var parsed = new HtmlDocument();
        parsed.LoadHtml(formatted);

        formatted.Should().NotContain("script");
        formatted.Should().NotContain("onclick");
        formatted.Should().NotContain("javascript:");
        HtmlNodeCollection marks = parsed.DocumentNode.SelectNodes("//mark");
        marks.Should().HaveCount(2);
        marks.Select(x => HtmlEntity.DeEntitize(x.InnerText)).Should().OnlyContain(x => x == "维护");
        parsed.DocumentNode.SelectSingleNode("//strong").Should().NotBeNull();
    }
}
