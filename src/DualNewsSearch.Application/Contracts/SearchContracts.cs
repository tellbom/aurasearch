using System.ComponentModel.DataAnnotations;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Services;
using DualNewsSearch.Domain;

namespace DualNewsSearch.Application.Contracts;

public sealed class SearchRequest : IValidatableObject
{
    [Required, StringLength(1_000, MinimumLength = 1)]
    public string Query { get; init; } = string.Empty;

    public IReadOnlyList<SourceType> SourceTypes { get; init; } = Array.Empty<SourceType>();

    public DateTimeOffset? PublishTimeFrom { get; init; }

    public DateTimeOffset? PublishTimeTo { get; init; }

    [StringLength(500)]
    public string? Publisher { get; init; }

    [StringLength(500)]
    public string? Author { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PublishTimeFrom > PublishTimeTo)
        {
            yield return new ValidationResult(
                "PublishTimeFrom must not be later than PublishTimeTo.",
                new[] { nameof(PublishTimeFrom), nameof(PublishTimeTo) });
        }
    }

    public SearchQuery ToDomain()
    {
        return new SearchQuery(
            Query.Trim(),
            SourceTypes,
            PublishTimeFrom?.ToUniversalTime(),
            PublishTimeTo?.ToUniversalTime(),
            string.IsNullOrWhiteSpace(Publisher) ? null : Publisher.Trim(),
            string.IsNullOrWhiteSpace(Author) ? null : Author.Trim(),
            Page,
            PageSize);
    }
}

public sealed record SearchResultItem(
    string NewsId,
    string Title,
    string? Highlight,
    string Publisher,
    string Author,
    SourceType SourceType,
    DateTimeOffset PublishTime);

public sealed record SearchResponse(
    Guid SearchTraceId,
    SearchMode SearchMode,
    bool Degraded,
    string? DegradationMode,
    bool MaxDepthReached,
    int Page,
    int PageSize,
    IReadOnlyList<SearchResultItem> Results);

public sealed class DayGroupedSearchRequest : IValidatableObject
{
    [StringLength(1_000)]
    public string Query { get; init; } = string.Empty;

    public IReadOnlyList<SourceType> SourceTypes { get; init; } =
        new[] { SourceType.News, SourceType.Announcement };

    public DateTimeOffset? PublishTimeFrom { get; init; }

    public DateTimeOffset? PublishTimeTo { get; init; }

    [StringLength(500)]
    public string? Publisher { get; init; }

    [StringLength(500)]
    public string? Author { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 30)]
    public int PageSize { get; init; } = 5;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PublishTimeFrom > PublishTimeTo)
        {
            yield return new ValidationResult(
                "PublishTimeFrom must not be later than PublishTimeTo.",
                new[] { nameof(PublishTimeFrom), nameof(PublishTimeTo) });
        }
    }

    public SearchQuery ToDomain()
    {
        return new SearchQuery(
            Query.Trim(),
            SourceTypes,
            PublishTimeFrom?.ToUniversalTime(),
            PublishTimeTo?.ToUniversalTime(),
            string.IsNullOrWhiteSpace(Publisher) ? null : Publisher.Trim(),
            string.IsNullOrWhiteSpace(Author) ? null : Author.Trim(),
            Page,
            PageSize);
    }
}

public sealed record SearchDayGroup(
    string Date,
    IReadOnlyList<SearchResultItem> Items);

public sealed record DayGroupedSearchResponse(
    Guid SearchTraceId,
    SearchMode SearchMode,
    bool Degraded,
    string? DegradationMode,
    bool MaxDepthReached,
    int Page,
    int PageSize,
    int TotalDays,
    int TotalPages,
    int TotalItems,
    int NewsItems,
    int AnnouncementItems,
    IReadOnlyList<SearchDayGroup> Days);

public interface ISearchEngineAdapter
{
    string Name { get; }

    Task<EngineSearchResult> SearchAsync(
        SearchQuery query,
        int topK,
        Guid searchTraceId,
        CancellationToken cancellationToken);
}

public interface ISuggestAdapter
{
    Task<IReadOnlyList<string>> SuggestAsync(
        string query,
        int size,
        CancellationToken cancellationToken);
}

public interface IQueryDiagnosticsRenderer
{
    string Name { get; }

    string RenderQuery(SearchQuery query, int topK);
}

public enum IndexApplyStatus
{
    Applied,
    NoOp,
    Stale,
    TransientFailure,
    PermanentFailure
}

public sealed record IndexApplyResult(IndexApplyStatus Status, string? Error = null)
{
    public bool IsSuccess => Status is IndexApplyStatus.Applied
        or IndexApplyStatus.NoOp
        or IndexApplyStatus.Stale;
}

public interface IIndexSink
{
    string Name { get; }

    Task<IndexApplyResult> ApplyAsync(
        DesiredDocumentWrite write,
        CancellationToken cancellationToken);
}

public interface IEngineDiagnostics
{
    string Name { get; }

    Task<EngineHealth> CheckAsync(CancellationToken cancellationToken);
}

public sealed record EngineHealth(
    string Name,
    bool Reachable,
    string? Version,
    string? Error,
    DateTimeOffset CheckedAt);

public sealed record SearchTelemetryEnvelope(
    SearchQuery Query,
    SearchExecution Execution);

public interface ISearchTelemetryQueue
{
    bool TryEnqueue(SearchTelemetryEnvelope envelope);

    ValueTask<SearchTelemetryEnvelope> DequeueAsync(CancellationToken cancellationToken);
}

public interface ISearchTelemetryRepository
{
    Task SaveSearchAsync(SearchTelemetryEnvelope envelope, CancellationToken cancellationToken);

    Task<bool> RecordImpressionsAsync(
        Guid searchTraceId,
        IReadOnlyList<string> newsIds,
        CancellationToken cancellationToken);

    Task<bool> RecordClickAsync(
        Guid searchTraceId,
        string newsId,
        int clickPosition,
        long? dwellTimeMs,
        bool allowRepeatedClicks,
        CancellationToken cancellationToken);

    Task<int> CleanupExpiredAsync(int batchSize, CancellationToken cancellationToken);

    Task<TelemetryMetricsReport> GetMetricsAsync(
        string? resultVersion,
        DateTimeOffset from,
        CancellationToken cancellationToken);
}

public sealed record ImpressionRequest(Guid SearchTraceId, IReadOnlyList<string> NewsIds);

public sealed record ClickRequest(
    Guid SearchTraceId,
    string NewsId,
    int ClickPosition,
    long? DwellTimeMs);

public sealed record EngineClickMetrics(
    double ClickedRecall,
    double ClickMrr,
    double UniqueClickRate);

public sealed record TelemetryMetricsReport(
    string? ResultVersion,
    long QueryCount,
    double ZeroResultRate,
    double DegradationRate,
    double LatencyP50Ms,
    double LatencyP95Ms,
    double LatencyP99Ms,
    double OverlapAt10,
    EngineClickMetrics Elasticsearch,
    EngineClickMetrics Vespa,
    EngineClickMetrics Rrf);
