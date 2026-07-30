using DualNewsSearch.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace DualNewsSearch.Api.Controllers;

[ApiController]
[Route("api/v1/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IReadOnlyDictionary<string, IQueryDiagnosticsRenderer> _renderers;

    public DiagnosticsController(IEnumerable<IQueryDiagnosticsRenderer> renderers)
    {
        _renderers = renderers.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    [HttpPost("{engine}/query")]
    public IActionResult Render(
        string engine,
        [FromBody] SearchRequest request,
        [FromQuery] int topK = 50)
    {
        if (topK is < 1 or > 1_000)
        {
            return BadRequest(new { error = "topK must be 1 to 1000." });
        }
        if (!_renderers.TryGetValue(engine, out IQueryDiagnosticsRenderer? renderer))
        {
            return NotFound(new { error = "Unknown engine." });
        }
        return Ok(new
        {
            engine = renderer.Name,
            request = renderer.RenderQuery(request.ToDomain(), topK)
        });
    }
}

