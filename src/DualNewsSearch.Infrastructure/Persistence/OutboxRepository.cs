using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Domain;
using Microsoft.EntityFrameworkCore;

namespace DualNewsSearch.Infrastructure.Persistence;

public sealed class OutboxRepository : IOutboxRepository
{
    private static readonly SemaphoreSlim ClaimLock = new(1, 1);
    private readonly IDbContextFactory<SearchDbContext> _dbFactory;
    private readonly IClock _clock;

    public OutboxRepository(IDbContextFactory<SearchDbContext> dbFactory, IClock clock)
    {
        _dbFactory = dbFactory;
        _clock = clock;
    }

    public async Task<OutboxWorkItem?> ClaimNextAsync(
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        await ClaimLock.WaitAsync(cancellationToken);
        try
        {
            await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            DateTimeOffset now = _clock.UtcNow;
            IndexOutboxEntity? trigger = await db.IndexOutbox
                .Where(x => x.AvailableAt <= now
                    && (x.ClaimedUntil == null || x.ClaimedUntil < now))
                .OrderBy(x => x.AvailableAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (trigger is null)
            {
                return null;
            }

            string claimToken = Guid.NewGuid().ToString("N");
            trigger.ClaimToken = claimToken;
            trigger.ClaimedUntil = now.Add(lease);
            trigger.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);

            DesiredDocumentEntity desired = await db.DesiredDocuments
                .AsNoTracking()
                .SingleAsync(x => x.NewsId == trigger.NewsId, cancellationToken);
            return new OutboxWorkItem(
                claimToken,
                MapWrite(desired),
                desired.EsStatus,
                desired.VespaStatus);
        }
        finally
        {
            ClaimLock.Release();
        }
    }

    public async Task CompleteAsync(
        OutboxWorkItem item,
        IReadOnlyList<EngineApplyCompletion> completions,
        bool elasticsearchEnabled,
        bool vespaEnabled,
        int maxRetryCount,
        CancellationToken cancellationToken)
    {
        await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        DesiredDocumentEntity? desired = await db.DesiredDocuments
            .SingleOrDefaultAsync(x => x.NewsId == item.Write.Document.NewsId, cancellationToken);
        IndexOutboxEntity? trigger = await db.IndexOutbox
            .SingleOrDefaultAsync(x => x.NewsId == item.Write.Document.NewsId, cancellationToken);
        if (desired is null || trigger is null || trigger.ClaimToken != item.ClaimToken)
        {
            return;
        }

        if (desired.IndexVersion != item.Write.Document.IndexVersion)
        {
            ReleaseTrigger(trigger, _clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        foreach (EngineApplyCompletion completion in completions)
        {
            ApplyCompletion(desired, completion, maxRetryCount, _clock.UtcNow);
        }

        bool esTerminal = !elasticsearchEnabled || IsTerminal(desired.EsStatus);
        bool vespaTerminal = !vespaEnabled || IsTerminal(desired.VespaStatus);
        if (esTerminal && vespaTerminal)
        {
            db.IndexOutbox.Remove(trigger);
        }
        else
        {
            DateTimeOffset next = new[]
                {
                    elasticsearchEnabled ? desired.EsNextRetryAt : null,
                    vespaEnabled ? desired.VespaNextRetryAt : null
                }
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .DefaultIfEmpty(_clock.UtcNow.AddSeconds(1))
                .Min();
            ReleaseTrigger(trigger, next);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RetryDeadAsync(string? newsId, CancellationToken cancellationToken)
    {
        await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<DesiredDocumentEntity> query = db.DesiredDocuments
            .Where(x => x.EsStatus == "Dead" || x.VespaStatus == "Dead");
        if (!string.IsNullOrWhiteSpace(newsId))
        {
            query = query.Where(x => x.NewsId == newsId);
        }

        DesiredDocumentEntity[] documents = await query.ToArrayAsync(cancellationToken);
        DateTimeOffset now = _clock.UtcNow;
        foreach (DesiredDocumentEntity document in documents)
        {
            if (document.EsStatus == "Dead")
            {
                document.EsStatus = "Pending";
                document.EsRetryCount = 0;
                document.EsNextRetryAt = now;
            }
            if (document.VespaStatus == "Dead")
            {
                document.VespaStatus = "Pending";
                document.VespaRetryCount = 0;
                document.VespaNextRetryAt = now;
            }
            await EnsureTriggerAsync(db, document.NewsId, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return documents.Length;
    }

    public async Task<int> ReindexAsync(
        string? newsId,
        DateTimeOffset? publishTimeFrom,
        DateTimeOffset? publishTimeTo,
        CancellationToken cancellationToken)
    {
        await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<DesiredDocumentEntity> query = db.DesiredDocuments;
        if (!string.IsNullOrWhiteSpace(newsId))
        {
            query = query.Where(x => x.NewsId == newsId);
        }
        if (publishTimeFrom.HasValue)
        {
            query = query.Where(x => x.PublishTime >= publishTimeFrom.Value);
        }
        if (publishTimeTo.HasValue)
        {
            query = query.Where(x => x.PublishTime <= publishTimeTo.Value);
        }

        DesiredDocumentEntity[] documents = await query.ToArrayAsync(cancellationToken);
        DateTimeOffset now = _clock.UtcNow;
        foreach (DesiredDocumentEntity document in documents)
        {
            document.EsStatus = "Pending";
            document.VespaStatus = "Pending";
            document.EsAppliedVersion = null;
            document.VespaAppliedVersion = null;
            document.EsRetryCount = 0;
            document.VespaRetryCount = 0;
            document.EsNextRetryAt = now;
            document.VespaNextRetryAt = now;
            await EnsureTriggerAsync(db, document.NewsId, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return documents.Length;
    }

    public async Task<IndexingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = _clock.UtcNow;
        DateTimeOffset hour = now.AddHours(-1);
        DateTimeOffset day = now.AddHours(-24);
        IQueryable<DesiredDocumentEntity> upserts =
            db.DesiredDocuments.AsNoTracking().Where(x => x.DesiredOperation == "Upsert");
        Dictionary<string, long> desiredByType = await CountByType(upserts, cancellationToken);
        Dictionary<string, long> esByType = await CountByType(
            upserts.Where(x => x.EsAppliedVersion == x.IndexVersion),
            cancellationToken);
        Dictionary<string, long> vespaByType = await CountByType(
            upserts.Where(x => x.VespaAppliedVersion == x.IndexVersion),
            cancellationToken);
        DateTimeOffset? oldest = await db.IndexOutbox
            .OrderBy(x => x.CreatedAt)
            .Select(x => (DateTimeOffset?)x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new IndexingSnapshot(
            await upserts.LongCountAsync(cancellationToken),
            await db.DesiredDocuments.LongCountAsync(x => x.DesiredOperation == "Delete", cancellationToken),
            await upserts.LongCountAsync(x => x.EsAppliedVersion == x.IndexVersion, cancellationToken),
            await upserts.LongCountAsync(x => x.VespaAppliedVersion == x.IndexVersion, cancellationToken),
            await db.IndexOutbox.LongCountAsync(cancellationToken),
            oldest,
            await upserts.LongCountAsync(x => x.UpdatedAt >= hour, cancellationToken),
            await upserts.LongCountAsync(x => x.UpdatedAt >= hour && x.EsAppliedVersion == x.IndexVersion, cancellationToken),
            await upserts.LongCountAsync(x => x.UpdatedAt >= hour && x.VespaAppliedVersion == x.IndexVersion, cancellationToken),
            await upserts.LongCountAsync(x => x.UpdatedAt >= day, cancellationToken),
            await upserts.LongCountAsync(x => x.UpdatedAt >= day && x.EsAppliedVersion == x.IndexVersion, cancellationToken),
            await upserts.LongCountAsync(x => x.UpdatedAt >= day && x.VespaAppliedVersion == x.IndexVersion, cancellationToken),
            desiredByType,
            esByType,
            vespaByType);
    }

    public async Task<IReadOnlyList<DesiredHashSample>> GetHashSamplesAsync(
        int sampleSize,
        CancellationToken cancellationToken)
    {
        await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.DesiredDocuments
            .AsNoTracking()
            .Where(x => x.DesiredOperation == "Upsert")
            .OrderBy(x => x.NewsId)
            .Take(sampleSize)
            .Select(x => new DesiredHashSample(
                x.NewsId,
                x.SourceType,
                x.ContentHash,
                x.IndexVersion))
            .ToArrayAsync(cancellationToken);
    }

    private static DesiredDocumentWrite MapWrite(DesiredDocumentEntity entity)
    {
        _ = Enum.TryParse(entity.SourceType, true, out SourceType sourceType);
        var document = new NewsSearchDocument(
            entity.NewsId,
            entity.SourceId,
            sourceType,
            entity.Title,
            entity.ContentText,
            entity.Publisher,
            entity.Author,
            entity.PublishTime,
            entity.IndexVersion,
            entity.ContentHash,
            entity.UpdatedAt);
        _ = Enum.TryParse(entity.DesiredOperation, true, out DesiredOperation operation);
        return new DesiredDocumentWrite(document, entity.ContentHtml, operation);
    }

    private static void ApplyCompletion(
        DesiredDocumentEntity desired,
        EngineApplyCompletion completion,
        int maxRetryCount,
        DateTimeOffset now)
    {
        bool es = completion.Engine.Equals("elasticsearch", StringComparison.OrdinalIgnoreCase);
        string status;
        long? appliedVersion = null;
        int retries = es ? desired.EsRetryCount : desired.VespaRetryCount;
        DateTimeOffset? nextRetry = null;
        string? error = completion.Result.Error;
        switch (completion.Result.Status)
        {
            case IndexApplyStatus.Applied:
            case IndexApplyStatus.NoOp:
            case IndexApplyStatus.Stale:
                status = "Applied";
                appliedVersion = completion.IndexVersion;
                error = null;
                break;
            case IndexApplyStatus.PermanentFailure:
                status = "Dead";
                break;
            default:
                retries++;
                status = retries >= maxRetryCount ? "Dead" : "Pending";
                if (status == "Pending")
                {
                    double seconds = Math.Min(300, Math.Pow(2, retries));
                    nextRetry = now.AddSeconds(seconds);
                }
                break;
        }

        if (es)
        {
            desired.EsStatus = status;
            desired.EsAppliedVersion = appliedVersion ?? desired.EsAppliedVersion;
            desired.EsRetryCount = retries;
            desired.EsNextRetryAt = nextRetry;
            desired.EsLastError = Sanitize(error);
        }
        else
        {
            desired.VespaStatus = status;
            desired.VespaAppliedVersion = appliedVersion ?? desired.VespaAppliedVersion;
            desired.VespaRetryCount = retries;
            desired.VespaNextRetryAt = nextRetry;
            desired.VespaLastError = Sanitize(error);
        }
        desired.UpdatedAt = now;
    }

    private static bool IsTerminal(string status) => status is "Applied" or "Dead";

    private static string? Sanitize(string? error)
    {
        return string.IsNullOrWhiteSpace(error)
            ? null
            : error.Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(error.Length, 1_000)];
    }

    private static void ReleaseTrigger(IndexOutboxEntity trigger, DateTimeOffset availableAt)
    {
        trigger.AvailableAt = availableAt;
        trigger.ClaimToken = null;
        trigger.ClaimedUntil = null;
        trigger.UpdatedAt = availableAt;
    }

    private static async Task EnsureTriggerAsync(
        SearchDbContext db,
        string newsId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IndexOutboxEntity? trigger = await db.IndexOutbox
            .SingleOrDefaultAsync(x => x.NewsId == newsId, cancellationToken);
        if (trigger is null)
        {
            db.IndexOutbox.Add(new IndexOutboxEntity
            {
                NewsId = newsId,
                AvailableAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            ReleaseTrigger(trigger, now);
        }
    }

    private static async Task<Dictionary<string, long>> CountByType(
        IQueryable<DesiredDocumentEntity> query,
        CancellationToken cancellationToken)
    {
        return await query
            .GroupBy(x => x.SourceType)
            .Select(x => new { SourceType = x.Key, Count = x.LongCount() })
            .ToDictionaryAsync(x => x.SourceType, x => x.Count, cancellationToken);
    }
}
