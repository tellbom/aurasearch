using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Domain;
using DualNewsSearch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DualNewsSearch.UnitTests;

public sealed class DesiredDocumentStoreTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<SearchDbContext> _factory;

    public DesiredDocumentStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<SearchDbContext>()
            .UseSqlite(_connection)
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

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private static DesiredDocumentWrite Write(long version)
    {
        var document = new NewsSearchDocument(
            "news:1",
            "1",
            SourceType.News,
            "标题",
            "正文",
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
