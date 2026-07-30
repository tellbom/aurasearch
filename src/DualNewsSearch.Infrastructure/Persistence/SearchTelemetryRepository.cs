using System.Text.Json;
using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Application.Services;
using DualNewsSearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Infrastructure.Persistence;

public sealed class SearchTelemetryRepository : ISearchTelemetryRepository
{
    private readonly IDbContextFactory<SearchDbContext> _dbFactory;
    private readonly TelemetryOptions _options;
    private readonly ElasticsearchOptions _elasticsearch;
    private readonly FusionOptions _fusion;
    private readonly IClock _clock;

    public SearchTelemetryRepository(
        IDbContextFactory<SearchDbContext> dbFactory,
        IOptions<TelemetryOptions> options,
        IOptions<ElasticsearchOptions> elasticsearch,
        IOptions<FusionOptions> fusion,
        IClock clock)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _elasticsearch = elasticsearch.Value;
        _fusion = fusion.Value;
        _clock = clock;
    }

    public async Task SaveSearchAsync(
        SearchTelemetryEnvelope envelope,
        CancellationToken cancellationToken)
    {
        SearchExecution execution = envelope.Execution;
        SearchQuery query = envelope.Query;
        DateTimeOffset now = _clock.UtcNow;
        await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.SearchQueries.Add(new SearchQueryEntity
        {
            SearchTraceId = execution.Response.SearchTraceId,
            QueryText = _options.StoreRawQuery ? query.Query : null,
            NormalizedQuery = query.Query.Trim().ToLowerInvariant(),
            FiltersJson = JsonSerializer.Serialize(new
            {
                query.SourceTypes,
                query.PublishTimeFrom,
                query.PublishTimeTo,
                query.Publisher,
                query.Author
            }),
            SearchTime = now,
            SearchMode = execution.Response.SearchMode.ToString(),
            ResultVersion = BuildResultVersion(),
            EsLatencyMs = execution.Elasticsearch?.LatencyMs ?? 0,
            VespaLatencyMs = execution.Vespa?.LatencyMs ?? 0,
            FusionLatencyMs = execution.FusionLatencyMs,
            TotalLatencyMs = execution.TotalLatencyMs,
            EsHitCount = execution.Elasticsearch?.Candidates.Count ?? 0,
            VespaHitCount = execution.Vespa?.Candidates.Count ?? 0,
            MergedUniqueCount = execution.Ranked.Count,
            EsTimeout = execution.Elasticsearch?.TimedOut ?? false,
            VespaTimeout = execution.Vespa?.TimedOut ?? false,
            DegradationMode = execution.Response.DegradationMode,
            ParametersJson = JsonSerializer.Serialize(_fusion),
            ExpiresAt = now.AddDays(_options.RetentionDays)
        });

        db.SearchResults.AddRange(execution.Ranked.Select(x => new SearchResultEntity
        {
            SearchTraceId = execution.Response.SearchTraceId,
            NewsId = x.NewsId,
            EsRank = x.EsRank,
            EsScore = x.EsScore,
            VespaRank = x.VespaRank,
            VespaRelevance = x.VespaRelevance,
            RrfRank = x.RrfRank,
            RrfScore = x.RrfScore,
            PresentInEs = x.PresentInEs,
            PresentInVespa = x.PresentInVespa
        }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RecordImpressionsAsync(
        Guid searchTraceId,
        IReadOnlyList<string> newsIds,
        CancellationToken cancellationToken)
    {
        if (newsIds.Count == 0 || newsIds.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        string[] distinct = newsIds.Distinct(StringComparer.Ordinal).ToArray();
        SearchResultEntity[] matches = await db.SearchResults
            .Where(x => x.SearchTraceId == searchTraceId && distinct.Contains(x.NewsId))
            .ToArrayAsync(cancellationToken);
        if (matches.Length != distinct.Length)
        {
            return false;
        }

        DateTimeOffset now = _clock.UtcNow;
        foreach (SearchResultEntity result in matches)
        {
            result.Exposed = true;
            result.ExposedAt ??= now;
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RecordClickAsync(
        Guid searchTraceId,
        string newsId,
        int clickPosition,
        long? dwellTimeMs,
        bool allowRepeatedClicks,
        CancellationToken cancellationToken)
    {
        if (clickPosition <= 0 || dwellTimeMs < 0)
        {
            return false;
        }

        await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        SearchResultEntity? result = await db.SearchResults.SingleOrDefaultAsync(
            x => x.SearchTraceId == searchTraceId && x.NewsId == newsId,
            cancellationToken);
        SearchQueryEntity? query = await db.SearchQueries.SingleOrDefaultAsync(
            x => x.SearchTraceId == searchTraceId,
            cancellationToken);
        if (result is null || query is null || query.ExpiresAt <= _clock.UtcNow)
        {
            return false;
        }

        SearchClickEntity? existing = await db.SearchClicks.FirstOrDefaultAsync(
            x => x.SearchTraceId == searchTraceId && x.NewsId == newsId,
            cancellationToken);
        if (existing is not null && !allowRepeatedClicks)
        {
            existing.DwellTimeMs = dwellTimeMs ?? existing.DwellTimeMs;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        db.SearchClicks.Add(new SearchClickEntity
        {
            SearchTraceId = searchTraceId,
            NewsId = newsId,
            ClickPosition = clickPosition,
            DwellTimeMs = dwellTimeMs,
            ClickedAt = _clock.UtcNow,
            ExpiresAt = query.ExpiresAt
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> CleanupExpiredAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        Guid[] traceIds = await db.SearchQueries
            .Where(x => x.ExpiresAt < _clock.UtcNow)
            .OrderBy(x => x.ExpiresAt)
            .Select(x => x.SearchTraceId)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
        if (traceIds.Length == 0)
        {
            return 0;
        }

        db.SearchClicks.RemoveRange(db.SearchClicks.Where(x => traceIds.Contains(x.SearchTraceId)));
        db.SearchResults.RemoveRange(db.SearchResults.Where(x => traceIds.Contains(x.SearchTraceId)));
        db.SearchQueries.RemoveRange(db.SearchQueries.Where(x => traceIds.Contains(x.SearchTraceId)));
        await db.SaveChangesAsync(cancellationToken);
        return traceIds.Length;
    }

    public async Task<TelemetryMetricsReport> GetMetricsAsync(
        string? resultVersion,
        DateTimeOffset from,
        CancellationToken cancellationToken)
    {
        await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<SearchQueryEntity> queryable = db.SearchQueries
            .AsNoTracking()
            .Where(x => x.SearchTime >= from);
        if (!string.IsNullOrWhiteSpace(resultVersion))
        {
            queryable = queryable.Where(x => x.ResultVersion == resultVersion);
        }

        SearchQueryEntity[] queries = await queryable.ToArrayAsync(cancellationToken);
        Guid[] traceIds = queries.Select(x => x.SearchTraceId).ToArray();
        SearchResultEntity[] results = await db.SearchResults
            .AsNoTracking()
            .Where(x => traceIds.Contains(x.SearchTraceId))
            .ToArrayAsync(cancellationToken);
        SearchClickEntity[] clicks = await db.SearchClicks
            .AsNoTracking()
            .Where(x => traceIds.Contains(x.SearchTraceId))
            .ToArrayAsync(cancellationToken);

        HashSet<(Guid TraceId, string NewsId)> clicked = clicks
            .Select(x => (x.SearchTraceId, x.NewsId))
            .ToHashSet();
        SearchResultEntity[] clickedResults = results
            .Where(x => clicked.Contains((x.SearchTraceId, x.NewsId)))
            .ToArray();
        double overlap = queries.Length == 0
            ? 0
            : results.GroupBy(x => x.SearchTraceId)
                .Select(group =>
                {
                    string[] es = group.Where(x => x.EsRank <= 10)
                        .OrderBy(x => x.EsRank)
                        .Select(x => x.NewsId)
                        .ToArray();
                    string[] vespa = group.Where(x => x.VespaRank <= 10)
                        .OrderBy(x => x.VespaRank)
                        .Select(x => x.NewsId)
                        .ToArray();
                    return EvaluationMetrics.OverlapAtK(es, vespa, 10);
                })
                .DefaultIfEmpty(0)
                .Average();
        double[] latencies = queries.Select(x => (double)x.TotalLatencyMs).ToArray();

        return new TelemetryMetricsReport(
            resultVersion,
            queries.LongLength,
            Rate(queries.LongCount(x => x.MergedUniqueCount == 0), queries.LongLength),
            Rate(queries.LongCount(x => x.DegradationMode != null), queries.LongLength),
            EvaluationMetrics.Percentile(latencies, 0.50),
            EvaluationMetrics.Percentile(latencies, 0.95),
            EvaluationMetrics.Percentile(latencies, 0.99),
            overlap,
            ClickMetrics(clickedResults, x => x.EsRank, x => x.PresentInEs,
                x => x.PresentInEs && !x.PresentInVespa),
            ClickMetrics(clickedResults, x => x.VespaRank, x => x.PresentInVespa,
                x => x.PresentInVespa && !x.PresentInEs),
            ClickMetrics(clickedResults, x => x.RrfRank, _ => true, _ => false));
    }

    private string BuildResultVersion()
    {
        return $"{_elasticsearch.ResultVersion};k={_fusion.RankConstant};" +
            $"ew={_fusion.EsWeight:R};vw={_fusion.VespaWeight:R};depth={_fusion.MaxFusionDepth}";
    }

    private static EngineClickMetrics ClickMetrics(
        IReadOnlyCollection<SearchResultEntity> clicked,
        Func<SearchResultEntity, int?> rank,
        Func<SearchResultEntity, bool> present,
        Func<SearchResultEntity, bool> unique)
    {
        if (clicked.Count == 0)
        {
            return new EngineClickMetrics(0, 0, 0);
        }

        return new EngineClickMetrics(
            clicked.Count(present) / (double)clicked.Count,
            clicked.Select(x => rank(x) is int value ? 1d / value : 0).Average(),
            clicked.Count(unique) / (double)clicked.Count);
    }

    private static double Rate(long numerator, long denominator)
    {
        return denominator == 0 ? 0 : numerator / (double)denominator;
    }
}
