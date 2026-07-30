using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DualNewsSearch.Infrastructure.Persistence;

public sealed class DesiredDocumentConfiguration : IEntityTypeConfiguration<DesiredDocumentEntity>
{
    public void Configure(EntityTypeBuilder<DesiredDocumentEntity> builder)
    {
        builder.ToTable("desired_documents");
        builder.HasKey(x => x.NewsId);
        builder.Property(x => x.NewsId).HasColumnName("news_id").HasMaxLength(256);
        builder.Property(x => x.ContentHtml).HasColumnName("content_html");
        builder.Property(x => x.ContentText).HasColumnName("content_text");
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
        builder.Property(x => x.IndexVersion).HasColumnName("index_version");
        builder.HasIndex(x => new { x.SourceType, x.PublishTime });
        builder.HasIndex(x => x.UpdatedAt);
    }
}

public sealed class IndexOutboxConfiguration : IEntityTypeConfiguration<IndexOutboxEntity>
{
    public void Configure(EntityTypeBuilder<IndexOutboxEntity> builder)
    {
        builder.ToTable("index_outbox");
        builder.HasKey(x => x.NewsId);
        builder.Property(x => x.NewsId).HasColumnName("news_id").HasMaxLength(256);
        builder.HasIndex(x => new { x.AvailableAt, x.ClaimedUntil });
    }
}

public sealed class SearchQueryConfiguration : IEntityTypeConfiguration<SearchQueryEntity>
{
    public void Configure(EntityTypeBuilder<SearchQueryEntity> builder)
    {
        builder.ToTable("search_queries");
        builder.HasKey(x => x.SearchTraceId);
        builder.Property(x => x.SearchTraceId).HasColumnName("search_trace_id");
        builder.HasIndex(x => new { x.ResultVersion, x.SearchTime });
        builder.HasIndex(x => x.ExpiresAt);
    }
}

public sealed class SearchResultConfiguration : IEntityTypeConfiguration<SearchResultEntity>
{
    public void Configure(EntityTypeBuilder<SearchResultEntity> builder)
    {
        builder.ToTable("search_results");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.SearchTraceId, x.NewsId }).IsUnique();
        builder.HasIndex(x => new { x.SearchTraceId, x.RrfRank });
    }
}

public sealed class SearchClickConfiguration : IEntityTypeConfiguration<SearchClickEntity>
{
    public void Configure(EntityTypeBuilder<SearchClickEntity> builder)
    {
        builder.ToTable("search_clicks");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.SearchTraceId, x.NewsId });
        builder.HasIndex(x => x.ExpiresAt);
    }
}

