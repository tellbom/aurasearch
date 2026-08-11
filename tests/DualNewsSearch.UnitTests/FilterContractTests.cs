using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Domain;
using FluentAssertions;

namespace DualNewsSearch.UnitTests;

public sealed class FilterContractTests
{
    [Fact]
    public void SearchRequestAndDomainQueryHaveClosedMatchingFields()
    {
        string[] requestFields = typeof(SearchRequest).GetProperties()
            .Select(x => x.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        string[] domainFields = typeof(SearchQuery).GetProperties()
            .Select(x => x.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        requestFields.Should().Equal(domainFields);
        requestFields.Should().NotContain("Extra");
    }

    [Fact]
    public void DayGroupedSearchDefaultsToFiveDateGroupsPerPage()
    {
        var request = new DayGroupedSearchRequest();

        request.Page.Should().Be(1);
        request.PageSize.Should().Be(5);
        request.Query.Should().BeEmpty();
        request.SourceTypes.Should().Equal(SourceType.News, SourceType.Announcement);
    }
}
