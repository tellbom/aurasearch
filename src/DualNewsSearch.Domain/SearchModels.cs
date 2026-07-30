namespace DualNewsSearch.Domain;

public sealed record SearchQuery(
    string Query,
    IReadOnlyList<SourceType> SourceTypes,
    DateTimeOffset? PublishTimeFrom,
    DateTimeOffset? PublishTimeTo,
    string? Publisher,
    string? Author,
    int Page,
    int PageSize);

public sealed record SearchCandidate(
    string NewsId,
    string Title,
    string? Highlight,
    string Publisher,
    string Author,
    SourceType SourceType,
    DateTimeOffset PublishTime,
    int Rank,
    double RawScore);

public sealed record EngineSearchResult(
    string Engine,
    IReadOnlyList<SearchCandidate> Candidates,
    long LatencyMs,
    bool TimedOut,
    string? Error,
    string? DiagnosticRequest = null)
{
    public bool Succeeded => Error is null && !TimedOut;
}

public sealed record FusedSearchCandidate(
    string NewsId,
    string Title,
    string? Highlight,
    string Publisher,
    string Author,
    SourceType SourceType,
    DateTimeOffset PublishTime,
    int? EsRank,
    double? EsScore,
    int? VespaRank,
    double? VespaRelevance,
    double RrfScore,
    int RrfRank)
{
    public bool PresentInEs => EsRank.HasValue;
    public bool PresentInVespa => VespaRank.HasValue;
}

public static class ReciprocalRankFusion
{
    public static IReadOnlyList<FusedSearchCandidate> Fuse(
        IEnumerable<SearchCandidate> esCandidates,
        IEnumerable<SearchCandidate> vespaCandidates,
        int rankConstant,
        double esWeight,
        double vespaWeight,
        int maxDepth)
    {
        if (rankConstant <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rankConstant));
        }

        Dictionary<string, SearchCandidate> es = Normalize(esCandidates, maxDepth);
        Dictionary<string, SearchCandidate> vespa = Normalize(vespaCandidates, maxDepth);
        var allIds = new HashSet<string>(es.Keys, StringComparer.Ordinal);
        allIds.UnionWith(vespa.Keys);

        var fused = new List<FusedSearchCandidate>(allIds.Count);
        foreach (string id in allIds)
        {
            es.TryGetValue(id, out SearchCandidate? esHit);
            vespa.TryGetValue(id, out SearchCandidate? vespaHit);
            SearchCandidate display = esHit ?? vespaHit!;
            double score = (esHit is null ? 0 : esWeight / (rankConstant + esHit.Rank))
                + (vespaHit is null ? 0 : vespaWeight / (rankConstant + vespaHit.Rank));
            fused.Add(new FusedSearchCandidate(
                id,
                display.Title,
                esHit?.Highlight ?? vespaHit?.Highlight,
                display.Publisher,
                display.Author,
                display.SourceType,
                display.PublishTime,
                esHit?.Rank,
                esHit?.RawScore,
                vespaHit?.Rank,
                vespaHit?.RawScore,
                score,
                0));
        }

        IOrderedEnumerable<FusedSearchCandidate> ordered = fused
            .OrderByDescending(x => x.RrfScore)
            .ThenByDescending(x => x.PresentInEs && x.PresentInVespa)
            .ThenBy(x => Math.Min(x.EsRank ?? int.MaxValue, x.VespaRank ?? int.MaxValue))
            .ThenByDescending(x => x.PublishTime)
            .ThenBy(x => x.NewsId, StringComparer.Ordinal);

        return ordered
            .Take(maxDepth)
            .Select((x, index) => x with { RrfRank = index + 1 })
            .ToArray();
    }

    private static Dictionary<string, SearchCandidate> Normalize(
        IEnumerable<SearchCandidate> candidates,
        int maxDepth)
    {
        if (maxDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }

        return candidates
            .Where(x => x.Rank > 0 && x.Rank <= maxDepth && !string.IsNullOrWhiteSpace(x.NewsId))
            .GroupBy(x => x.NewsId, StringComparer.Ordinal)
            .Select(x => x.OrderBy(hit => hit.Rank).First())
            .ToDictionary(x => x.NewsId, StringComparer.Ordinal);
    }
}

