using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Application.Services;
using DualNewsSearch.Domain;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.UnitTests;

public sealed class SearchOrchestratorTests
{
    [Fact]
    public async Task RrfMode_StartsAdaptersConcurrently()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var es = new FakeAdapter("elasticsearch", gate.Task);
        var vespa = new FakeAdapter("vespa", gate.Task);
        SearchOrchestrator orchestrator = Create(SearchMode.Rrf, es, vespa);

        Task<SearchExecution> search = orchestrator.SearchAsync(Query(), default);
        await Task.Delay(50);

        es.Calls.Should().Be(1);
        vespa.Calls.Should().Be(1);
        gate.SetResult(true);
        SearchExecution result = await search;
        result.Response.SearchMode.Should().Be(SearchMode.Rrf);
    }

    [Fact]
    public async Task EsOnly_MakesZeroVespaCalls()
    {
        var es = new FakeAdapter("elasticsearch");
        var vespa = new FakeAdapter("vespa");
        SearchExecution result = await Create(SearchMode.EsOnly, es, vespa)
            .SearchAsync(Query(), default);

        es.Calls.Should().Be(1);
        vespa.Calls.Should().Be(0);
        result.Response.Degraded.Should().BeFalse();
    }

    [Fact]
    public async Task RrfMode_DegradesWhenOneEngineFails()
    {
        var es = new FakeAdapter("elasticsearch");
        var vespa = new FakeAdapter("vespa", error: "down");

        SearchExecution result = await Create(SearchMode.Rrf, es, vespa)
            .SearchAsync(Query(), default);

        result.Response.Degraded.Should().BeTrue();
        result.Response.SearchMode.Should().Be(SearchMode.EsOnly);
        result.Response.DegradationMode.Should().Be("EsOnlyFallback");
    }

    [Fact]
    public async Task BothEnginesFail_ThrowsUnavailable()
    {
        var es = new FakeAdapter("elasticsearch", error: "down");
        var vespa = new FakeAdapter("vespa", error: "down");

        Func<Task> action = async () => await Create(SearchMode.Rrf, es, vespa)
            .SearchAsync(Query(), default);

        await action.Should().ThrowAsync<SearchUnavailableException>();
    }

    [Fact]
    public async Task FixedWindowPaginationDoesNotRepeat()
    {
        SearchCandidate[] hits = Enumerable.Range(1, 50)
            .Select(x => Candidate($"id-{x:00}", x))
            .ToArray();
        var es = new FakeAdapter("elasticsearch", candidates: hits);
        var vespa = new FakeAdapter("vespa", candidates: hits);
        SearchOrchestrator orchestrator = Create(SearchMode.Rrf, es, vespa);

        SearchExecution first = await orchestrator.SearchAsync(Query(page: 1), default);
        SearchExecution second = await orchestrator.SearchAsync(Query(page: 2), default);
        SearchExecution third = await orchestrator.SearchAsync(Query(page: 3), default);

        first.Response.Results.Select(x => x.NewsId)
            .Should().NotIntersectWith(second.Response.Results.Select(x => x.NewsId));
        third.Response.Results.Should().HaveCount(10);
        third.Response.MaxDepthReached.Should().BeTrue();
    }

    private static SearchOrchestrator Create(
        SearchMode mode,
        params ISearchEngineAdapter[] adapters)
    {
        return new SearchOrchestrator(
            adapters,
            Options.Create(new FusionOptions()),
            new SearchModeState(Options.Create(new SearchModeOptions { Default = mode })));
    }

    private static SearchQuery Query(int page = 1)
    {
        return new SearchQuery("测试", Array.Empty<SourceType>(), null, null, null, null, page, 20);
    }

    private static SearchCandidate Candidate(string id, int rank)
    {
        return new SearchCandidate(
            id,
            id,
            null,
            string.Empty,
            string.Empty,
            SourceType.News,
            DateTimeOffset.UnixEpoch,
            rank,
            rank);
    }

    private sealed class FakeAdapter : ISearchEngineAdapter
    {
        private readonly Task? _gate;
        private readonly string? _error;
        private readonly IReadOnlyList<SearchCandidate> _candidates;
        private int _calls;

        public FakeAdapter(
            string name,
            Task? gate = null,
            string? error = null,
            IReadOnlyList<SearchCandidate>? candidates = null)
        {
            Name = name;
            _gate = gate;
            _error = error;
            _candidates = candidates ?? new[] { Candidate($"{name}-1", 1) };
        }

        public string Name { get; }
        public int Calls => _calls;

        public async Task<EngineSearchResult> SearchAsync(
            SearchQuery query,
            int topK,
            Guid searchTraceId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (_gate is not null)
            {
                await _gate.WaitAsync(cancellationToken);
            }

            return new EngineSearchResult(Name, _candidates, 1, false, _error);
        }
    }
}
