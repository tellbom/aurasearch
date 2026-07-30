using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Api.Controllers;

[ApiController]
[Route("api/v1/telemetry")]
public sealed class TelemetryController : ControllerBase
{
    private readonly ISearchTelemetryRepository _repository;
    private readonly TelemetryOptions _options;

    public TelemetryController(
        ISearchTelemetryRepository repository,
        IOptions<TelemetryOptions> options)
    {
        _repository = repository;
        _options = options.Value;
    }

    [HttpPost("impressions")]
    public async Task<IActionResult> Impressions(
        [FromBody] ImpressionRequest request,
        CancellationToken cancellationToken)
    {
        bool accepted = await _repository.RecordImpressionsAsync(
            request.SearchTraceId,
            request.NewsIds,
            cancellationToken);
        return accepted
            ? Accepted()
            : BadRequest(new { error = "Trace/result pair is invalid." });
    }

    [HttpPost("clicks")]
    public async Task<IActionResult> Clicks(
        [FromBody] ClickRequest request,
        CancellationToken cancellationToken)
    {
        bool accepted = await _repository.RecordClickAsync(
            request.SearchTraceId,
            request.NewsId,
            request.ClickPosition,
            request.DwellTimeMs,
            _options.AllowRepeatedClicks,
            cancellationToken);
        return accepted
            ? Accepted()
            : BadRequest(new { error = "Trace/result pair is invalid or expired." });
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics(
        [FromQuery] string? resultVersion,
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        if (days is < 1 or > 365)
        {
            return BadRequest(new { error = "days must be 1 to 365." });
        }
        return Ok(await _repository.GetMetricsAsync(
            resultVersion,
            DateTimeOffset.UtcNow.AddDays(-days),
            cancellationToken));
    }
}
