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

public sealed record DayGroupedSearchExecution(
    DayGroupedSearchResponse Response,
    SearchExecution Search);

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
    private readonly ISearchResultContentStore _contentStore;

    public SearchOrchestrator(
        IEnumerable<ISearchEngineAdapter> adapters,
        IOptions<FusionOptions> fusion,
        ISearchModeState modeState,
        ISearchResultContentStore contentStore)
    {
        _adapters = adapters.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        _fusion = fusion.Value;
        _modeState = modeState;
        _contentStore = contentStore;
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
        IReadOnlyDictionary<string, SearchResultContent> content = await _contentStore.GetAsync(
            page.Select(x => x.NewsId).ToArray(),
            query.Query,
            cancellationToken);
        totalWatch.Stop();

        var response = new SearchResponse(
            traceId,
            responseMode,
            degraded,
            degradationMode,
            maxDepthReached,
            query.Page,
            query.PageSize,
            page.Select(x => ToResponseItem(x, content.GetValueOrDefault(x.NewsId))).ToArray());

        return new SearchExecution(
            response,
            es,
            vespa,
            ranked,
            fusionWatch.ElapsedMilliseconds,
            totalWatch.ElapsedMilliseconds);
    }

    public async Task<DayGroupedSearchExecution> SearchByDayAsync(
        SearchQuery query,
        CancellationToken cancellationToken)
    {
        SearchExecution search = await SearchAsync(
            query with { Page = 1, PageSize = _fusion.MaxFusionDepth },
            cancellationToken);

        IReadOnlyDictionary<string, SearchResultItem> responseItems = search.Response.Results
            .ToDictionary(x => x.NewsId, StringComparer.Ordinal);
        SearchDayGroup[] allDays = search.Ranked
            .GroupBy(x => x.PublishTime.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd"))
            .OrderByDescending(x => x.Key, StringComparer.Ordinal)
            .Select(group => new SearchDayGroup(
                group.Key,
                group.Select(x => responseItems[x.NewsId]).ToArray()))
            .ToArray();
        int totalPages = allDays.Length == 0
            ? 0
            : (int)Math.Ceiling(allDays.Length / (double)query.PageSize);
        int skip = checked((query.Page - 1) * query.PageSize);
        SearchDayGroup[] page = allDays.Skip(skip).Take(query.PageSize).ToArray();

        var response = new DayGroupedSearchResponse(
            search.Response.SearchTraceId,
            search.Response.SearchMode,
            search.Response.Degraded,
            search.Response.DegradationMode,
            search.Ranked.Count >= _fusion.MaxFusionDepth,
            query.Page,
            query.PageSize,
            allDays.Length,
            totalPages,
            search.Ranked.Count,
            search.Ranked.Count(x => x.SourceType == SourceType.News),
            search.Ranked.Count(x => x.SourceType == SourceType.Announcement),
            page);
        return new DayGroupedSearchExecution(response, search);
    }

    private static SearchResultItem ToResponseItem(
        FusedSearchCandidate candidate,
        SearchResultContent? content)
    {
        return new SearchResultItem(
            candidate.NewsId,
            candidate.Title,
            candidate.Highlight,
            candidate.Publisher,
            candidate.Author,
            candidate.SourceType,
            candidate.PublishTime,
            content?.Summary ?? (candidate.SourceType == SourceType.News ? candidate.Highlight : null),
            content?.ContentHtml,
            content?.Cover);
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
