using DualNewsSearch.Domain;
using FluentAssertions;

namespace DualNewsSearch.UnitTests;

public sealed class EvaluationMetricsTests
{
    [Fact]
    public void NdcgAndMrr_MatchHandCalculatedExample()
    {
        int[] relevance = { 3, 0, 2, 1 };

        EvaluationMetrics.NormalizedDiscountedCumulativeGain(relevance, 4)
            .Should().BeApproximately(0.9508013, 1e-6);
        EvaluationMetrics.MeanReciprocalRank(relevance, 10).Should().Be(1);
    }

    [Fact]
    public void RecallOverlapAndPercentile_UseRawSamples()
    {
        EvaluationMetrics.RecallAtK(
                new[] { "a", "x", "b" },
                new HashSet<string> { "a", "b", "c" },
                2)
            .Should().BeApproximately(1d / 3, 1e-12);
        EvaluationMetrics.OverlapAtK(
                new[] { "a", "b", "c" },
                new[] { "b", "c", "d" },
                3)
            .Should().BeApproximately(2d / 3, 1e-12);
        EvaluationMetrics.Percentile(new double[] { 1, 2, 100, 200 }, 0.95)
            .Should().BeApproximately(185, 1e-12);
    }
}

