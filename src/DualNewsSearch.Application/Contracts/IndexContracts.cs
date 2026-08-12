using System.ComponentModel.DataAnnotations;
using DualNewsSearch.Domain;

namespace DualNewsSearch.Application.Contracts;

public sealed class UpsertDocumentRequest : IValidatableObject
{
    [Required, StringLength(256)]
    public string SourceId { get; init; } = string.Empty;

    [Required]
    public SourceType? SourceType { get; init; }

    [Required, StringLength(1_000)]
    public string Title { get; init; } = string.Empty;

    [Required]
    public string ContentHtml { get; init; } = string.Empty;

    [Url, StringLength(2_048)]
    public string? Cover { get; init; }

    [StringLength(500)]
    public string Publisher { get; init; } = string.Empty;

    [StringLength(500)]
    public string Author { get; init; } = string.Empty;

    public DateTimeOffset? PublishTime { get; init; }

    [Range(1, long.MaxValue)]
    public long IndexVersion { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PublishTime is null)
        {
            yield return new ValidationResult("PublishTime is required.", new[] { nameof(PublishTime) });
        }

        if (PublishTime is { } value && (value.Year < 1900 || value.Year > 2200))
        {
            yield return new ValidationResult(
                "PublishTime must be between years 1900 and 2200.",
                new[] { nameof(PublishTime) });
        }
    }
}

public sealed class BatchDocumentItem : IValidatableObject
{
    [Required, StringLength(256)]
    public string NewsId { get; init; } = string.Empty;

    [Required]
    public UpsertDocumentRequest Document { get; init; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        return Array.Empty<ValidationResult>();
    }
}

public sealed class BatchDocumentRequest
{
    [Required, MinLength(1)]
    public IReadOnlyList<BatchDocumentItem> Documents { get; init; } = Array.Empty<BatchDocumentItem>();
}

public sealed record IndexWriteResponse(
    string NewsId,
    long IndexVersion,
    DesiredWriteStatus Status);

public sealed record BatchIndexItemResponse(
    string NewsId,
    long? IndexVersion,
    string Status,
    IReadOnlyList<string> Errors);

public sealed record HtmlCleanResult(string Text, bool ContentTruncated);

public interface IHtmlTextCleaner
{
    HtmlCleanResult Clean(string? html);
}

public sealed record DesiredDocumentWrite(
    NewsSearchDocument Document,
    string ContentHtml,
    DesiredOperation Operation);

public interface IDesiredDocumentStore
{
    Task<DesiredWriteStatus> UpsertAsync(
        DesiredDocumentWrite write,
        CancellationToken cancellationToken);

    Task<DesiredWriteStatus> DeleteAsync(
        string newsId,
        long indexVersion,
        CancellationToken cancellationToken);
}

public sealed record OutboxWorkItem(
    string ClaimToken,
    DesiredDocumentWrite Write,
    string EsStatus,
    string VespaStatus);

public sealed record EngineApplyCompletion(
    string Engine,
    long IndexVersion,
    IndexApplyResult Result);

public interface IOutboxRepository
{
    Task<OutboxWorkItem?> ClaimNextAsync(
        TimeSpan lease,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        OutboxWorkItem item,
        IReadOnlyList<EngineApplyCompletion> completions,
        bool elasticsearchEnabled,
        bool vespaEnabled,
        int maxRetryCount,
        CancellationToken cancellationToken);

    Task<int> RetryDeadAsync(string? newsId, CancellationToken cancellationToken);

    Task<int> ReindexAsync(
        string? newsId,
        DateTimeOffset? publishTimeFrom,
        DateTimeOffset? publishTimeTo,
        CancellationToken cancellationToken);

    Task<IndexingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DesiredHashSample>> GetHashSamplesAsync(
        int sampleSize,
        CancellationToken cancellationToken);
}

public sealed record DesiredHashSample(
    string NewsId,
    string SourceType,
    string ContentHash,
    long IndexVersion);

public interface IEngineConsistencyProbe
{
    string Name { get; }

    Task<long> CountAsync(string? sourceType, CancellationToken cancellationToken);

    Task<(string ContentHash, long IndexVersion)?> GetVersionHashAsync(
        string newsId,
        CancellationToken cancellationToken);
}

public sealed record EngineConsistencyResult(
    string Engine,
    long Count,
    IReadOnlyDictionary<string, long> CountBySourceType,
    int HashSamplesChecked,
    IReadOnlyList<string> MismatchedNewsIds,
    string? Error);

public sealed record ConsistencyReport(
    DateTimeOffset CheckedAt,
    long DesiredCount,
    IReadOnlyDictionary<string, long> DesiredBySourceType,
    IReadOnlyList<EngineConsistencyResult> Engines)
{
    public bool Passed => Engines.Count > 0
        && Engines.All(x => x.Error is null
            && x.Count == DesiredCount
            && x.MismatchedNewsIds.Count == 0
            && DesiredBySourceType.All(pair =>
                x.CountBySourceType.GetValueOrDefault(pair.Key) == pair.Value));
}

public interface IConsistencyChecker
{
    Task<ConsistencyReport> CheckAsync(int hashSampleSize, CancellationToken cancellationToken);
}

public sealed record IndexingSnapshot(
    long DesiredUpserts,
    long Tombstones,
    long ElasticsearchApplied,
    long VespaApplied,
    long OutboxBacklog,
    DateTimeOffset? OldestOutboxAt,
    long LastHourDesired,
    long LastHourElasticsearchApplied,
    long LastHourVespaApplied,
    long Last24HoursDesired,
    long Last24HoursElasticsearchApplied,
    long Last24HoursVespaApplied,
    IReadOnlyDictionary<string, long> DesiredBySourceType,
    IReadOnlyDictionary<string, long> ElasticsearchBySourceType,
    IReadOnlyDictionary<string, long> VespaBySourceType);
