using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Domain;
using DualNewsSearch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DualNewsSearch.UnitTests;

public sealed class DesiredDocumentStoreTests
{
    private readonly IDbContextFactory<SearchDbContext> _factory;

    public DesiredDocumentStoreTests()
    {
        var options = new DbContextOptionsBuilder<SearchDbContext>()
            .UseInMemoryDatabase($"desired-store-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _factory = new TestDbContextFactory(options);
        using SearchDbContext db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task StateMachine_PreservesHighestVersionAndTombstone()
    {
        var store = new DesiredDocumentStore(_factory, new SystemClock());

        (await store.UpsertAsync(Write(10), default)).Should().Be(DesiredWriteStatus.Accepted);
        (await store.UpsertAsync(Write(12), default)).Should().Be(DesiredWriteStatus.Accepted);
        (await store.UpsertAsync(Write(11), default)).Should().Be(DesiredWriteStatus.Stale);
        (await store.UpsertAsync(Write(12), default)).Should().Be(DesiredWriteStatus.NoOp);
        (await store.DeleteAsync("news:1", 20, default)).Should().Be(DesiredWriteStatus.Accepted);
        (await store.UpsertAsync(Write(19), default)).Should().Be(DesiredWriteStatus.Stale);

        await using SearchDbContext db = await _factory.CreateDbContextAsync();
        DesiredDocumentEntity state = await db.DesiredDocuments.SingleAsync();
        state.IndexVersion.Should().Be(20);
        state.DesiredOperation.Should().Be("Delete");
        (await db.IndexOutbox.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentWrites_EndAtMaximumVersion()
    {
        var store = new DesiredDocumentStore(_factory, new SystemClock());
        long[] versions = Enumerable.Range(1, 25).Select(x => (long)x).OrderBy(_ => Guid.NewGuid()).ToArray();

        await Task.WhenAll(versions.Select(x => store.UpsertAsync(Write(x), default)));

        await using SearchDbContext db = await _factory.CreateDbContextAsync();
        (await db.DesiredDocuments.SingleAsync()).IndexVersion.Should().Be(25);
    }

    private static DesiredDocumentWrite Write(long version)
    {
        var document = new NewsSearchDocument(
            "news:1",
            "1",
            SourceType.News,
            "标题",
            "正文",
            "https://example.com/cover.jpg",
            "发布者",
            "作者",
            DateTimeOffset.UnixEpoch,
            version,
            version.ToString(),
            DateTimeOffset.UtcNow);
        return new DesiredDocumentWrite(document, "<p>正文</p>", DesiredOperation.Upsert);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<SearchDbContext>
    {
        private readonly DbContextOptions<SearchDbContext> _options;

        public TestDbContextFactory(DbContextOptions<SearchDbContext> options)
        {
            _options = options;
        }

        public SearchDbContext CreateDbContext() => new(_options);

        public Task<SearchDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}
