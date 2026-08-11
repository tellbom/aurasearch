using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace DualNewsSearch.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class SearchController : ControllerBase
{
    private readonly SearchOrchestrator _orchestrator;
    private readonly ISuggestAdapter _suggest;
    private readonly ISearchTelemetryQueue _telemetry;

    public SearchController(
        SearchOrchestrator orchestrator,
        ISuggestAdapter suggest,
        ISearchTelemetryQueue telemetry)
    {
        _orchestrator = orchestrator;
        _suggest = suggest;
        _telemetry = telemetry;
    }

    [HttpPost("search")]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Search(
        [FromBody] SearchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            SearchExecution execution = await _orchestrator.SearchAsync(
                request.ToDomain(),
                cancellationToken);
            _telemetry.TryEnqueue(new SearchTelemetryEnvelope(request.ToDomain(), execution));
            Response.Headers["X-Search-Trace-ID"] = execution.Response.SearchTraceId.ToString();
            return Ok(execution.Response);
        }
        catch (SearchUnavailableException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Search engines unavailable",
                detail: exception.Message);
        }
    }

    [HttpPost("search/day-groups")]
    [ProducesResponseType(typeof(DayGroupedSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SearchByDay(
        [FromBody] DayGroupedSearchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = request.ToDomain();
            DayGroupedSearchExecution execution = await _orchestrator.SearchByDayAsync(
                query,
                cancellationToken);
            _telemetry.TryEnqueue(new SearchTelemetryEnvelope(query, execution.Search));
            Response.Headers["X-Search-Trace-ID"] = execution.Response.SearchTraceId.ToString();
            return Ok(execution.Response);
        }
        catch (SearchUnavailableException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Search engines unavailable",
                detail: exception.Message);
        }
    }

    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest(
        [FromQuery] string q,
        [FromQuery] int size = 10,
        CancellationToken cancellationToken = default)
    {
        if (size is < 1 or > 50)
        {
            ModelState.AddModelError(nameof(size), "Size must be between 1 and 50.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _suggest.SuggestAsync(q, size, cancellationToken));
    }
}
