using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Domain;
using Microsoft.EntityFrameworkCore;

namespace DualNewsSearch.Infrastructure.Persistence;

public sealed class DesiredDocumentStore : IDesiredDocumentStore
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);
    private readonly IDbContextFactory<SearchDbContext> _dbFactory;
    private readonly IClock _clock;

    public DesiredDocumentStore(
        IDbContextFactory<SearchDbContext> dbFactory,
        IClock clock)
    {
        _dbFactory = dbFactory;
        _clock = clock;
    }

    public async Task<DesiredWriteStatus> UpsertAsync(
        DesiredDocumentWrite write,
        CancellationToken cancellationToken)
    {
        return await WriteLockAndExecuteAsync(
            write.Document.NewsId,
            write.Document.IndexVersion,
            async (db, existing, now) =>
            {
                if (existing is null)
                {
                    existing = new DesiredDocumentEntity
                    {
                        NewsId = write.Document.NewsId,
                        CreatedAt = now
                    };
                    db.DesiredDocuments.Add(existing);
                }

                MapUpsert(existing, write, now);
                await UpsertTriggerAsync(db, write.Document.NewsId, now, cancellationToken);
            },
            cancellationToken);
    }

    public async Task<DesiredWriteStatus> DeleteAsync(
        string newsId,
        long indexVersion,
        CancellationToken cancellationToken)
    {
        return await WriteLockAndExecuteAsync(
            newsId,
            indexVersion,
            async (db, existing, now) =>
            {
                if (existing is null)
                {
                    existing = new DesiredDocumentEntity
                    {
                        NewsId = newsId,
                        SourceId = string.Empty,
                        SourceType = SourceType.News.ToString(),
                        Title = string.Empty,
                        ContentHtml = string.Empty,
                        ContentText = string.Empty,
                        Publisher = string.Empty,
                        Author = string.Empty,
                        PublishTime = DateTimeOffset.UnixEpoch,
                        ContentHash = string.Empty,
                        CreatedAt = now
                    };
                    db.DesiredDocuments.Add(existing);
                }

                existing.IndexVersion = indexVersion;
                existing.DesiredOperation = DesiredOperation.Delete.ToString();
                existing.EsStatus = "Pending";
                existing.VespaStatus = "Pending";
                existing.EsRetryCount = 0;
                existing.VespaRetryCount = 0;
                existing.EsNextRetryAt = now;
                existing.VespaNextRetryAt = now;
                existing.UpdatedAt = now;
                await UpsertTriggerAsync(db, newsId, now, cancellationToken);
            },
            cancellationToken);
    }

    private async Task<DesiredWriteStatus> WriteLockAndExecuteAsync(
        string newsId,
        long indexVersion,
        Func<SearchDbContext, DesiredDocumentEntity?, DateTimeOffset, Task> update,
        CancellationToken cancellationToken)
    {
        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            DesiredDocumentEntity? existing = await db.DesiredDocuments
                .SingleOrDefaultAsync(x => x.NewsId == newsId, cancellationToken);
            if (existing is not null)
            {
                if (indexVersion < existing.IndexVersion)
                {
                    return DesiredWriteStatus.Stale;
                }

                if (indexVersion == existing.IndexVersion)
                {
                    return DesiredWriteStatus.NoOp;
                }
            }

            await update(db, existing, _clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return DesiredWriteStatus.Accepted;
        }
        finally
        {
            WriteLock.Release();
        }
    }

    private static void MapUpsert(
        DesiredDocumentEntity entity,
        DesiredDocumentWrite write,
        DateTimeOffset now)
    {
        NewsSearchDocument document = write.Document;
        entity.SourceId = document.SourceId;
        entity.SourceType = document.SourceType.ToString();
        entity.Title = document.Title;
        entity.ContentHtml = write.ContentHtml;
        entity.ContentText = document.ContentText;
        entity.Publisher = document.Publisher;
        entity.Author = document.Author;
        entity.PublishTime = document.PublishTime;
        entity.IndexVersion = document.IndexVersion;
        entity.ContentHash = document.ContentHash;
        entity.DesiredOperation = DesiredOperation.Upsert.ToString();
        entity.EsStatus = "Pending";
        entity.VespaStatus = "Pending";
        entity.EsRetryCount = 0;
        entity.VespaRetryCount = 0;
        entity.EsNextRetryAt = now;
        entity.VespaNextRetryAt = now;
        entity.UpdatedAt = now;
    }

    private static async Task UpsertTriggerAsync(
        SearchDbContext db,
        string newsId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IndexOutboxEntity? trigger = await db.IndexOutbox
            .SingleOrDefaultAsync(x => x.NewsId == newsId, cancellationToken);
        if (trigger is null)
        {
            trigger = new IndexOutboxEntity
            {
                NewsId = newsId,
                AvailableAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.IndexOutbox.Add(trigger);
        }
        else
        {
            trigger.AvailableAt = now;
            trigger.ClaimToken = null;
            trigger.ClaimedUntil = null;
            trigger.UpdatedAt = now;
        }
    }
}
