using System.Text.Json.Nodes;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Domain;
using DualNewsSearch.Infrastructure.Search;
using FluentAssertions;

namespace DualNewsSearch.UnitTests;

public sealed class AdapterRequestConstructionTests
{
    [Fact]
    public void ElasticsearchQueryIncludesEveryClosedFilter()
    {
        SearchQuery query = FullQuery("关键字\"}恶意");

        JsonObject body = ElasticsearchAdapter.BuildSearchBody(query, 50);
        string json = body.ToJsonString();

        json.Should().Contain("source_type");
        json.Should().Contain("publish_time");
        json.Should().Contain("\"publisher\"");
        json.Should().Contain("\"author\"");
        body["query"]!["bool"]!["must"]![0]!["multi_match"]!["query"]!
            .GetValue<string>()
            .Should().Be("关键字\"}恶意");
    }

    [Fact]
    public void VespaUserInputNeverChangesYqlStructure()
    {
        const string hostile = "x') or true or ('";
        IReadOnlyDictionary<string, string> parameters = VespaAdapter.BuildQueryParameters(
            FullQuery(hostile),
            50,
            new VespaOptions
            {
                Endpoint = "http://vespa:8080",
                Namespace = "news",
                DocumentType = "news",
                RankProfile = "cjk_bm25_all",
                TimeoutMs = 2000
            });

        parameters["query"].Should().Be(hostile);
        parameters["yql"].Should().NotContain(hostile);
        parameters["yql"].Should().Contain("userQuery()");
        parameters["yql"].Should().Contain("@publisher");
        parameters["yql"].Should().Contain("@author");
    }

    private static SearchQuery FullQuery(string text)
    {
        return new SearchQuery(
            text,
            new[] { SourceType.News, SourceType.Announcement },
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-12-31T23:59:59Z"),
            "发布者",
            "作者",
            1,
            20);
    }
}
