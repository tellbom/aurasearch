using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Api.Controllers;

[ApiController]
[Route("api/v1/search-health")]
public sealed class HealthController : ControllerBase
{
    private readonly ISearchReadinessEvaluator _readiness;
    private readonly ISearchModeState _mode;
    private readonly SearchModeOptions _modeOptions;

    public HealthController(
        ISearchReadinessEvaluator readiness,
        ISearchModeState mode,
        IOptions<SearchModeOptions> modeOptions)
    {
        _readiness = readiness;
        _mode = mode;
        _modeOptions = modeOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        SearchReadinessReport report = await _readiness.EvaluateAsync(cancellationToken);
        return Ok(new
        {
            mode = _mode.Current,
            report.ReadyForVespa,
            report.CheckedAt,
            report.Checks,
            report.Indexing,
            report.Engines,
            report.Consistency,
            audit = _mode.Audit.TakeLast(20)
        });
    }

    [HttpPost("mode")]
    public async Task<IActionResult> SetMode(
        [FromBody] ChangeModeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Operator)
            || string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "Operator and reason are required." });
        }

        if (_modeOptions.RequireReadinessForVespa
            && request.Mode is SearchMode.Rrf or SearchMode.VespaOnly)
        {
            SearchReadinessReport report = await _readiness.EvaluateAsync(cancellationToken);
            if (!report.ReadyForVespa)
            {
                return Conflict(new
                {
                    error = "Readiness Gate failed; mode remains unchanged.",
                    failedChecks = report.Checks.Where(x => !x.Passed)
                });
            }
        }

        _mode.Change(request.Mode, request.Operator, request.Reason, automatic: false);
        return Ok(new { mode = _mode.Current });
    }
}

public sealed record ChangeModeRequest(
    SearchMode Mode,
    string Operator,
    string Reason);
