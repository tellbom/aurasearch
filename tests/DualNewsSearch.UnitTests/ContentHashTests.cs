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
                null,
                "发布者",
                "作者",
                DateTimeOffset.Parse("2026-07-30T08:00:00+08:00", CultureInfo.InvariantCulture),
                SourceType.News);

            hash.Should().Be("f532140402f6de05cbd72f5cfb59fb5073946f0db1b5bc3c65886b87afb9a30d");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Compute_UsesSeparatorsToAvoidBoundaryCollision()
    {
        string first = ContentHash.Compute("ab", "c", null, "", "", DateTimeOffset.UnixEpoch, SourceType.News);
        string second = ContentHash.Compute("a", "bc", null, "", "", DateTimeOffset.UnixEpoch, SourceType.News);

        first.Should().NotBe(second);
    }

    [Fact]
    public void Compute_ChangesWhenCoverChanges()
    {
        string first = ContentHash.Compute("title", "content", "https://img/1.jpg", "", "", DateTimeOffset.UnixEpoch, SourceType.News);
        string second = ContentHash.Compute("title", "content", "https://img/2.jpg", "", "", DateTimeOffset.UnixEpoch, SourceType.News);

        first.Should().NotBe(second);
    }
}
