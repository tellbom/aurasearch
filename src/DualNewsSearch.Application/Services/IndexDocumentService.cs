using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Domain;

namespace DualNewsSearch.Application.Services;

public sealed class IndexDocumentService
{
    private readonly IHtmlTextCleaner _cleaner;
    private readonly IDesiredDocumentStore _store;
    private readonly IClock _clock;

    public IndexDocumentService(
        IHtmlTextCleaner cleaner,
        IDesiredDocumentStore store,
        IClock clock)
    {
        _cleaner = cleaner;
        _store = store;
        _clock = clock;
    }

    public async Task<IndexWriteResponse> UpsertAsync(
        string newsId,
        UpsertDocumentRequest request,
        CancellationToken cancellationToken)
    {
        ValidateNewsId(newsId);
        SourceType sourceType = request.SourceType
            ?? throw new ArgumentException("SourceType is required.", nameof(request));
        DateTimeOffset publishTime = request.PublishTime
            ?? throw new ArgumentException("PublishTime is required.", nameof(request));

        HtmlCleanResult clean = _cleaner.Clean(request.ContentHtml);
        DateTimeOffset now = _clock.UtcNow;
        var document = new NewsSearchDocument(
            newsId.Trim(),
            request.SourceId.Trim(),
            sourceType,
            request.Title.Trim(),
            clean.Text,
            request.Publisher.Trim(),
            request.Author.Trim(),
            publishTime.ToUniversalTime(),
            request.IndexVersion,
            ContentHash.Compute(
                request.Title.Trim(),
                clean.Text,
                request.Publisher.Trim(),
                request.Author.Trim(),
                publishTime,
                sourceType),
            now);

        DesiredWriteStatus status = await _store.UpsertAsync(
            new DesiredDocumentWrite(document, request.ContentHtml, DesiredOperation.Upsert),
            cancellationToken);
        return new IndexWriteResponse(document.NewsId, document.IndexVersion, status);
    }

    public async Task<IndexWriteResponse> DeleteAsync(
        string newsId,
        long indexVersion,
        CancellationToken cancellationToken)
    {
        ValidateNewsId(newsId);
        if (indexVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(indexVersion), "IndexVersion must be positive.");
        }

        DesiredWriteStatus status = await _store.DeleteAsync(
            newsId.Trim(),
            indexVersion,
            cancellationToken);
        return new IndexWriteResponse(newsId.Trim(), indexVersion, status);
    }

    private static void ValidateNewsId(string newsId)
    {
        if (string.IsNullOrWhiteSpace(newsId) || newsId.Length > 256)
        {
            throw new ArgumentException("NewsId must contain 1 to 256 characters.", nameof(newsId));
        }
    }
}
