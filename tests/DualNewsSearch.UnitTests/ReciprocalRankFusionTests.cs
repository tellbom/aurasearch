using DualNewsSearch.Domain;
using FluentAssertions;

namespace DualNewsSearch.UnitTests;

public sealed class ReciprocalRankFusionTests
{
    [Fact]
    public void Fuse_UsesRanksAndPreservesDiagnostics()
    {
        SearchCandidate[] es =
        {
            Candidate("a", 1, 100),
            Candidate("b", 2, 5)
        };
        SearchCandidate[] vespa =
        {
            Candidate("b", 1, 0.99),
            Candidate("c", 2, 0.98)
        };

        IReadOnlyList<FusedSearchCandidate> actual =
            ReciprocalRankFusion.Fuse(es, vespa, 60, 1, 1, 50);

        actual.Select(x => x.NewsId).Should().Equal("b", "a", "c");
        actual[0].EsRank.Should().Be(2);
        actual[0].VespaRank.Should().Be(1);
        actual[0].RrfScore.Should().BeApproximately((1d / 62) + (1d / 61), 1e-12);
        actual[0].EsScore.Should().Be(5);
        actual[0].VespaRelevance.Should().Be(0.99);
    }

    [Fact]
    public void Fuse_DoesNotAddRawScores()
    {
        SearchCandidate highRawLowRank = Candidate("a", 2, 1_000_000);
        SearchCandidate lowRawHighRank = Candidate("b", 1, 0.00001);

        IReadOnlyList<FusedSearchCandidate> actual = ReciprocalRankFusion.Fuse(
            new[] { highRawLowRank, lowRawHighRank },
            Array.Empty<SearchCandidate>(),
            60,
            1,
            1,
            50);

        actual.Select(x => x.NewsId).Should().Equal("b", "a");
    }

    [Fact]
    public void Fuse_TieBreakIsDeterministicAcrossInputOrder()
    {
        SearchCandidate[] source =
        {
            Candidate("z", 1, 1, DateTimeOffset.UnixEpoch),
            Candidate("a", 1, 1, DateTimeOffset.UnixEpoch)
        };

        for (int i = 0; i < 100; i++)
        {
            SearchCandidate[] shuffled = source.OrderBy(_ => Guid.NewGuid()).ToArray();
            IReadOnlyList<FusedSearchCandidate> actual = ReciprocalRankFusion.Fuse(
                shuffled,
                Array.Empty<SearchCandidate>(),
                60,
                1,
                0,
                50);
            actual.Select(x => x.NewsId).Should().Equal("a", "z");
        }
    }

    [Fact]
    public void Fuse_HandlesDuplicateAndMissingCandidates()
    {
        IReadOnlyList<FusedSearchCandidate> actual = ReciprocalRankFusion.Fuse(
            new[] { Candidate("a", 2, 2), Candidate("a", 1, 1) },
            new[] { Candidate("b", 1, 3) },
            10,
            0,
            2,
            50);

        actual.Should().HaveCount(2);
        actual.Single(x => x.NewsId == "a").EsRank.Should().Be(1);
        actual.Single(x => x.NewsId == "a").RrfScore.Should().Be(0);
        actual.Single(x => x.NewsId == "b").RrfScore.Should().BeApproximately(2d / 11, 1e-12);
    }

    private static SearchCandidate Candidate(
        string id,
        int rank,
        double rawScore,
        DateTimeOffset? publishTime = null)
    {
        return new SearchCandidate(
            id,
            id,
            null,
            string.Empty,
            string.Empty,
            SourceType.News,
            publishTime ?? DateTimeOffset.UnixEpoch,
            rank,
            rawScore);
    }
}

