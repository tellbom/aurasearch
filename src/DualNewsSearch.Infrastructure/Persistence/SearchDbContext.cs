using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DualNewsSearch.Infrastructure.Persistence;

public sealed class SearchDbContext : DbContext
{
    public SearchDbContext(DbContextOptions<SearchDbContext> options)
        : base(options)
    {
    }

    public DbSet<DesiredDocumentEntity> DesiredDocuments => Set<DesiredDocumentEntity>();
    public DbSet<IndexOutboxEntity> IndexOutbox => Set<IndexOutboxEntity>();
    public DbSet<SearchQueryEntity> SearchQueries => Set<SearchQueryEntity>();
    public DbSet<SearchResultEntity> SearchResults => Set<SearchResultEntity>();
    public DbSet<SearchClickEntity> SearchClicks => Set<SearchClickEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SearchDbContext).Assembly);
        var converter = new ValueConverter<DateTimeOffset, DateTime>(
            value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        var nullableConverter = new ValueConverter<DateTimeOffset?, DateTime?>(
            value => value.HasValue ? value.Value.UtcDateTime : null,
            value => value.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                : null);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset))
            {
                property.SetValueConverter(converter);
            }
            else if (property.ClrType == typeof(DateTimeOffset?))
            {
                property.SetValueConverter(nullableConverter);
            }
        }
    }
}

public sealed class DesiredDocumentEntity
{
    public string NewsId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public string ContentText { get; set; } = string.Empty;
    public string? Cover { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTimeOffset PublishTime { get; set; }
    public long IndexVersion { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string DesiredOperation { get; set; } = string.Empty;
    public long? EsAppliedVersion { get; set; }
    public long? VespaAppliedVersion { get; set; }
    public string EsStatus { get; set; } = "Pending";
    public string VespaStatus { get; set; } = "Pending";
    public int EsRetryCount { get; set; }
    public int VespaRetryCount { get; set; }
    public DateTimeOffset? EsNextRetryAt { get; set; }
    public DateTimeOffset? VespaNextRetryAt { get; set; }
    public string? EsLastError { get; set; }
    public string? VespaLastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class IndexOutboxEntity
{
    public string NewsId { get; set; } = string.Empty;
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? ClaimedUntil { get; set; }
    public string? ClaimToken { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SearchQueryEntity
{
    public Guid SearchTraceId { get; set; }
    public string? QueryText { get; set; }
    public string NormalizedQuery { get; set; } = string.Empty;
    public string FiltersJson { get; set; } = "{}";
    public DateTimeOffset SearchTime { get; set; }
    public string SearchMode { get; set; } = string.Empty;
    public string ResultVersion { get; set; } = string.Empty;
    public long EsLatencyMs { get; set; }
    public long VespaLatencyMs { get; set; }
    public long FusionLatencyMs { get; set; }
    public long TotalLatencyMs { get; set; }
    public int EsHitCount { get; set; }
    public int VespaHitCount { get; set; }
    public int MergedUniqueCount { get; set; }
    public bool EsTimeout { get; set; }
    public bool VespaTimeout { get; set; }
    public string? DegradationMode { get; set; }
    public string ParametersJson { get; set; } = "{}";
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class SearchResultEntity
{
    public long Id { get; set; }
    public Guid SearchTraceId { get; set; }
    public string NewsId { get; set; } = string.Empty;
    public int? EsRank { get; set; }
    public double? EsScore { get; set; }
    public int? VespaRank { get; set; }
    public double? VespaRelevance { get; set; }
    public int? RrfRank { get; set; }
    public double? RrfScore { get; set; }
    public bool PresentInEs { get; set; }
    public bool PresentInVespa { get; set; }
    public bool Exposed { get; set; }
    public DateTimeOffset? ExposedAt { get; set; }
}

public sealed class SearchClickEntity
{
    public long Id { get; set; }
    public Guid SearchTraceId { get; set; }
    public string NewsId { get; set; } = string.Empty;
    public int ClickPosition { get; set; }
    public long? DwellTimeMs { get; set; }
    public DateTimeOffset ClickedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
