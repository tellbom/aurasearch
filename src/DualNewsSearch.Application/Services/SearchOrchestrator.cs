using System.Diagnostics;
using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Domain;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Application.Services;

public sealed record SearchExecution(
    SearchResponse Response,
    EngineSearchResult? Elasticsearch,
    EngineSearchResult? Vespa,
    IReadOnlyList<FusedSearchCandidate> Ranked,
    long FusionLatencyMs,
    long TotalLatencyMs);

public sealed class SearchUnavailableException : Exception
{
    public SearchUnavailableException(string message)
        : base(message)
    {
    }
}

public sealed class SearchOrchestrator
{
    private readonly IReadOnlyDictionary<string, ISearchEngineAdapter> _adapters;
    private readonly FusionOptions _fusion;
    private readonly ISearchModeState _modeState;

    public SearchOrchestrator(
        IEnumerable<ISearchEngineAdapter> adapters,
        IOptions<FusionOptions> fusion,
        ISearchModeState modeState)
    {
        _adapters = adapters.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        _fusion = fusion.Value;
        _modeState = modeState;
    }

    public async Task<SearchExecution> SearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken)
    {
        Guid traceId = Guid.NewGuid();
        var totalWatch = Stopwatch.StartNew();
        SearchMode requestedMode = _modeState.Current;
        EngineSearchResult? es = null;
        EngineSearchResult? vespa = null;

        using var global = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        global.CancelAfter(_fusion.GlobalTimeoutMs);

        if (requestedMode == SearchMode.EsOnly)
        {
            es = await ExecuteAsync("elasticsearch", query, _fusion.EsTopK, traceId, global.Token);
        }
        else if (requestedMode == SearchMode.VespaOnly)
        {
            vespa = await ExecuteAsync("vespa", query, _fusion.VespaTopK, traceId, global.Token);
        }
        else
        {
            Task<EngineSearchResult> esTask =
                ExecuteAsync("elasticsearch", query, _fusion.EsTopK, traceId, global.Token);
            Task<EngineSearchResult> vespaTask =
                ExecuteAsync("vespa", query, _fusion.VespaTopK, traceId, global.Token);
            await Task.WhenAll(esTask, vespaTask);
            es = esTask.Result;
            vespa = vespaTask.Result;
        }

        bool esSucceeded = es?.Succeeded == true;
        bool vespaSucceeded = vespa?.Succeeded == true;
        if (!esSucceeded && !vespaSucceeded)
        {
            throw new SearchUnavailableException("All configured search engines failed.");
        }

        var fusionWatch = Stopwatch.StartNew();
        IReadOnlyList<FusedSearchCandidate> ranked;
        bool degraded = false;
        string? degradationMode = null;
        SearchMode responseMode = requestedMode;

        if (requestedMode == SearchMode.Shadow)
        {
            ranked = ToSingleEngine(es!.Candidates, isEs: true);
            if (!vespaSucceeded)
            {
                degraded = true;
                degradationMode = "VespaUnavailable";
            }
        }
        else if (esSucceeded && vespaSucceeded && requestedMode == SearchMode.Rrf)
        {
            ranked = ReciprocalRankFusion.Fuse(
                es!.Candidates,
                vespa!.Candidates,
                _fusion.RankConstant,
                _fusion.EsWeight,
                _fusion.VespaWeight,
                _fusion.MaxFusionDepth);
        }
        else if (esSucceeded)
        {
            ranked = ToSingleEngine(es!.Candidates, isEs: true);
            degraded = requestedMode is SearchMode.Rrf or SearchMode.VespaOnly;
            degradationMode = degraded ? "EsOnlyFallback" : null;
            responseMode = SearchMode.EsOnly;
        }
        else
        {
            ranked = ToSingleEngine(vespa!.Candidates, isEs: false);
            degraded = requestedMode is SearchMode.Rrf or SearchMode.EsOnly;
            degradationMode = degraded ? "VespaOnlyFallback" : null;
            responseMode = SearchMode.VespaOnly;
        }

        fusionWatch.Stop();
        int skip = checked((query.Page - 1) * query.PageSize);
        bool maxDepthReached = skip >= _fusion.MaxFusionDepth
            || skip + query.PageSize > _fusion.MaxFusionDepth;
        IReadOnlyList<FusedSearchCandidate> page = skip >= _fusion.MaxFusionDepth
            ? Array.Empty<FusedSearchCandidate>()
            : ranked.Skip(skip).Take(query.PageSize).ToArray();
        totalWatch.Stop();

        var response = new SearchResponse(
            traceId,
            responseMode,
            degraded,
            degradationMode,
            maxDepthReached,
            query.Page,
            query.PageSize,
            page.Select(x => new SearchResultItem(
                x.NewsId,
                x.Title,
                x.Highlight,
                x.Publisher,
                x.Author,
                x.SourceType,
                x.PublishTime)).ToArray());

        return new SearchExecution(
            response,
            es,
            vespa,
            ranked,
            fusionWatch.ElapsedMilliseconds,
            totalWatch.ElapsedMilliseconds);
    }

    private async Task<EngineSearchResult> ExecuteAsync(
        string name,
        SearchQuery query,
        int topK,
        Guid traceId,
        CancellationToken cancellationToken)
    {
        if (!_adapters.TryGetValue(name, out ISearchEngineAdapter? adapter))
        {
            return new EngineSearchResult(name, Array.Empty<SearchCandidate>(), 0, false, "Adapter not registered.");
        }

        try
        {
            return await adapter.SearchAsync(query, topK, traceId, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new EngineSearchResult(name, Array.Empty<SearchCandidate>(), 0, true, "Timed out.");
        }
        catch (OperationCanceledException)
        {
            return new EngineSearchResult(name, Array.Empty<SearchCandidate>(), 0, true, "Cancelled.");
        }
        catch (Exception exception)
        {
            return new EngineSearchResult(name, Array.Empty<SearchCandidate>(), 0, false, exception.Message);
        }
    }

    private static IReadOnlyList<FusedSearchCandidate> ToSingleEngine(
        IReadOnlyList<SearchCandidate> candidates,
        bool isEs)
    {
        return candidates.Select((x, index) => new FusedSearchCandidate(
            x.NewsId,
            x.Title,
            x.Highlight,
            x.Publisher,
            x.Author,
            x.SourceType,
            x.PublishTime,
            isEs ? x.Rank : null,
            isEs ? x.RawScore : null,
            isEs ? null : x.Rank,
            isEs ? null : x.RawScore,
            1d / x.Rank,
            index + 1)).ToArray();
    }
}
