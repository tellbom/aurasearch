namespace DualNewsSearch.Domain;

public static class EvaluationMetrics
{
    public static double DiscountedCumulativeGain(IReadOnlyList<int> relevance, int k)
    {
        return relevance.Take(k)
            .Select((grade, index) => (Math.Pow(2, grade) - 1) / Math.Log2(index + 2))
            .Sum();
    }

    public static double NormalizedDiscountedCumulativeGain(
        IReadOnlyList<int> relevance,
        int k)
    {
        double ideal = DiscountedCumulativeGain(
            relevance.OrderByDescending(x => x).ToArray(),
            k);
        return ideal == 0 ? 0 : DiscountedCumulativeGain(relevance, k) / ideal;
    }

    public static double MeanReciprocalRank(IReadOnlyList<int> relevance, int k)
    {
        int index = relevance.Take(k).ToList().FindIndex(x => x > 0);
        return index < 0 ? 0 : 1d / (index + 1);
    }

    public static double RecallAtK(
        IReadOnlyCollection<string> rankedIds,
        IReadOnlySet<string> relevantIds,
        int k)
    {
        return relevantIds.Count == 0
            ? 0
            : rankedIds.Take(k).Count(relevantIds.Contains) / (double)relevantIds.Count;
    }

    public static double OverlapAtK(
        IReadOnlyCollection<string> first,
        IReadOnlyCollection<string> second,
        int k)
    {
        HashSet<string> left = first.Take(k).ToHashSet(StringComparer.Ordinal);
        HashSet<string> right = second.Take(k).ToHashSet(StringComparer.Ordinal);
        return k == 0 ? 0 : left.Intersect(right).Count() / (double)k;
    }

    public static double Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }
        if (percentile is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        double[] sorted = values.OrderBy(x => x).ToArray();
        double position = percentile * (sorted.Length - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }
}

