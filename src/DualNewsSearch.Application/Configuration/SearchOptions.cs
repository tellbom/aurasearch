using System.ComponentModel.DataAnnotations;

namespace DualNewsSearch.Application.Configuration;

public sealed class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    [Required, Url]
    public string Endpoint { get; init; } = string.Empty;

    [Required]
    public string IndexAlias { get; init; } = string.Empty;

    [Required]
    public string IndexName { get; init; } = string.Empty;

    public bool ProvisioningEnabled { get; init; } = true;

    [Range(1, 120_000)]
    public int TimeoutMs { get; init; } = 2_000;

    public string ResultVersion { get; init; } = "es-v1";
}

public sealed class VespaOptions
{
    public const string SectionName = "Vespa";

    [Required, Url]
    public string Endpoint { get; init; } = string.Empty;

    [Required, Url]
    public string ConfigEndpoint { get; init; } = string.Empty;

    public bool ProvisioningEnabled { get; init; } = true;

    [Required]
    public string Namespace { get; init; } = string.Empty;

    [Required]
    public string DocumentType { get; init; } = string.Empty;

    [Required]
    public string RankProfile { get; init; } = string.Empty;

    [Range(1, 120_000)]
    public int TimeoutMs { get; init; } = 2_000;
}

public sealed class FusionOptions : IValidatableObject
{
    public const string SectionName = "Fusion";

    [Range(1, 1_000)]
    public int EsTopK { get; init; } = 50;

    [Range(1, 1_000)]
    public int VespaTopK { get; init; } = 50;

    [Range(1, 1_000)]
    public int FinalTopK { get; init; } = 20;

    [Range(1, 10_000)]
    public int RankConstant { get; init; } = 60;

    [Range(0, 100)]
    public double EsWeight { get; init; } = 1;

    [Range(0, 100)]
    public double VespaWeight { get; init; } = 1;

    [Range(1, 1_000)]
    public int MaxFusionDepth { get; init; } = 50;

    [Range(1, 120_000)]
    public int GlobalTimeoutMs { get; init; } = 3_000;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FinalTopK > MaxFusionDepth)
        {
            yield return new ValidationResult(
                "FinalTopK must not exceed MaxFusionDepth.",
                new[] { nameof(FinalTopK), nameof(MaxFusionDepth) });
        }

        if (MaxFusionDepth > Math.Max(EsTopK, VespaTopK))
        {
            yield return new ValidationResult(
                "MaxFusionDepth must not exceed the largest engine TopK.",
                new[] { nameof(MaxFusionDepth) });
        }

        if (EsWeight == 0 && VespaWeight == 0)
        {
            yield return new ValidationResult(
                "At least one fusion weight must be greater than zero.",
                new[] { nameof(EsWeight), nameof(VespaWeight) });
        }
    }
}

public sealed class IndexingOptions
{
    public const string SectionName = "Indexing";

    [Range(1, 10_000)]
    public int BatchSizeLimit { get; init; } = 200;

    [Range(1, 100)]
    public int MaxRetryCount { get; init; } = 8;

    [Range(10, 3_600_000)]
    public int WorkerPollIntervalMs { get; init; } = 1_000;

    [Range(1, 2_000_000)]
    public int HtmlMaxLength { get; init; } = 200_000;

    public bool ElasticsearchSinkEnabled { get; init; } = true;

    public bool VespaSinkEnabled { get; init; } = true;
}

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    [Range(1, 3_650)]
    public int RetentionDays { get; init; } = 90;

    [Range(1, 100_000)]
    public int CleanupBatchSize { get; init; } = 1_000;

    public bool StoreRawQuery { get; init; }

    public bool AllowRepeatedClicks { get; init; }
}

public enum SearchMode
{
    EsOnly,
    VespaOnly,
    Rrf,
    Shadow
}

public sealed class SearchModeOptions
{
    public const string SectionName = "SearchMode";

    public SearchMode Default { get; init; } = SearchMode.EsOnly;

    public bool RequireReadinessForVespa { get; init; } = true;
}

public sealed class ReadinessOptions
{
    public const string SectionName = "Readiness";

    public bool BackfillComplete { get; init; }

    [Range(0, 1)]
    public double MinimumOneHourSyncRate { get; init; } = 0.99;

    [Range(0, 1)]
    public double Minimum24HourSyncRate { get; init; } = 0.995;

    [Range(0, long.MaxValue)]
    public long MaximumOutboxBacklog { get; init; } = 100;

    [Range(1, 10_080)]
    public int MaximumLagMinutes { get; init; } = 10;

    [Range(5, 3_600)]
    public int CheckIntervalSeconds { get; init; } = 60;

    [Range(1, 10_000)]
    public int HashSampleSize { get; init; } = 100;
}
