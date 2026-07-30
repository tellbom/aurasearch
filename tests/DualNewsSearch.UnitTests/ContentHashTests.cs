using System.Globalization;
using DualNewsSearch.Domain;
using FluentAssertions;

namespace DualNewsSearch.UnitTests;

public sealed class ContentHashTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void Compute_IsStableAcrossCultures(string cultureName)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            string hash = ContentHash.Compute(
                "标题",
                "正文",
                "发布者",
                "作者",
                DateTimeOffset.Parse("2026-07-30T08:00:00+08:00", CultureInfo.InvariantCulture),
                SourceType.News);

            hash.Should().Be("16de045d82607fd087cc6aa26474e7362eb438df2115158ced4af413d6bab30b");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Compute_UsesSeparatorsToAvoidBoundaryCollision()
    {
        string first = ContentHash.Compute("ab", "c", "", "", DateTimeOffset.UnixEpoch, SourceType.News);
        string second = ContentHash.Compute("a", "bc", "", "", DateTimeOffset.UnixEpoch, SourceType.News);

        first.Should().NotBe(second);
    }
}
