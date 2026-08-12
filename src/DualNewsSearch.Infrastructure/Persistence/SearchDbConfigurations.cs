using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DualNewsSearch.Infrastructure.Persistence;

public sealed class DesiredDocumentConfiguration : IEntityTypeConfiguration<DesiredDocumentEntity>
{
    public void Configure(EntityTypeBuilder<DesiredDocumentEntity> builder)
    {
        builder.ToTable("aurasearch_desired_documents");
        builder.HasKey(x => x.NewsId);
        builder.Property(x => x.NewsId).HasColumnName("news_id").HasMaxLength(256);
        builder.Property(x => x.SourceId).HasMaxLength(256);
        builder.Property(x => x.SourceType).HasMaxLength(32);
        builder.Property(x => x.Title).HasMaxLength(1000);
        builder.Property(x => x.ContentHtml).HasColumnName("content_html").HasColumnType("CLOB");
        builder.Property(x => x.ContentText).HasColumnName("content_text").HasColumnType("CLOB");
        builder.Property(x => x.Cover).HasMaxLength(2048);
        builder.Property(x => x.Publisher).HasMaxLength(500);
        builder.Property(x => x.Author).HasMaxLength(500);
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
        builder.Property(x => x.IndexVersion).HasColumnName("index_version");
        builder.Property(x => x.DesiredOperation).HasMaxLength(16);
        builder.Property(x => x.EsStatus).HasMaxLength(16);
        builder.Property(x => x.VespaStatus).HasMaxLength(16);
        builder.Property(x => x.EsLastError).HasMaxLength(1000);
        builder.Property(x => x.VespaLastError).HasMaxLength(1000);
        builder.HasIndex(x => new { x.SourceType, x.PublishTime }).HasDatabaseName("ix_aura_doc_type_time");
        builder.HasIndex(x => x.UpdatedAt).HasDatabaseName("ix_aura_doc_updated");
    }
}

public sealed class IndexOutboxConfiguration : IEntityTypeConfiguration<IndexOutboxEntity>
{
    public void Configure(EntityTypeBuilder<IndexOutboxEntity> builder)
    {
        builder.ToTable("aurasearch_index_outbox");
        builder.HasKey(x => x.NewsId);
        builder.Property(x => x.NewsId).HasColumnName("news_id").HasMaxLength(256);
        builder.Property(x => x.ClaimToken).HasMaxLength(64);
        builder.HasIndex(x => new { x.AvailableAt, x.ClaimedUntil }).HasDatabaseName("ix_aura_outbox_claim");
    }
}

public sealed class SearchQueryConfiguration : IEntityTypeConfiguration<SearchQueryEntity>
{
    public void Configure(EntityTypeBuilder<SearchQueryEntity> builder)
    {
        builder.ToTable("aurasearch_search_queries");
        builder.HasKey(x => x.SearchTraceId);
        builder.Property(x => x.SearchTraceId).HasColumnName("search_trace_id");
        builder.Property(x => x.QueryText).HasMaxLength(2000);
        builder.Property(x => x.NormalizedQuery).HasMaxLength(2000);
        builder.Property(x => x.FiltersJson).HasColumnType("CLOB");
        builder.Property(x => x.SearchMode).HasMaxLength(32);
        builder.Property(x => x.ResultVersion).HasMaxLength(256);
        builder.Property(x => x.DegradationMode).HasMaxLength(64);
        builder.Property(x => x.ParametersJson).HasColumnType("CLOB");
        builder.HasIndex(x => new { x.ResultVersion, x.SearchTime }).HasDatabaseName("ix_aura_query_version_time");
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_aura_query_expiry");
    }
}

public sealed class SearchResultConfiguration : IEntityTypeConfiguration<SearchResultEntity>
{
    public void Configure(EntityTypeBuilder<SearchResultEntity> builder)
    {
        builder.ToTable("aurasearch_search_results");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NewsId).HasMaxLength(256);
        builder.HasIndex(x => new { x.SearchTraceId, x.NewsId })
            .HasDatabaseName("ux_aura_result_trace_news").IsUnique();
        builder.HasIndex(x => new { x.SearchTraceId, x.RrfRank }).HasDatabaseName("ix_aura_result_trace_rank");
    }
}

public sealed class SearchClickConfiguration : IEntityTypeConfiguration<SearchClickEntity>
{
    public void Configure(EntityTypeBuilder<SearchClickEntity> builder)
    {
        builder.ToTable("aurasearch_search_clicks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NewsId).HasMaxLength(256);
        builder.HasIndex(x => new { x.SearchTraceId, x.NewsId }).HasDatabaseName("ix_aura_click_trace_news");
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_aura_click_expiry");
    }
}
