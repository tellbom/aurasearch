using DualNewsSearch.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace DualNewsSearch.Api.Controllers;

[ApiController]
[Route("api/v1/operations")]
public sealed class OperationsController : ControllerBase
{
    private readonly IOutboxRepository _repository;
    private readonly IConsistencyChecker _consistency;

    public OperationsController(
        IOutboxRepository repository,
        IConsistencyChecker consistency)
    {
        _repository = repository;
        _consistency = consistency;
    }

    [HttpPost("retry-dead")]
    public async Task<IActionResult> RetryDead(
        [FromQuery] string? newsId,
        CancellationToken cancellationToken)
    {
        int count = await _repository.RetryDeadAsync(newsId, cancellationToken);
        return Accepted(new { count });
    }

    [HttpPost("reindex")]
    public async Task<IActionResult> Reindex(
        [FromBody] ReindexRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Full && request.Confirm != "REINDEX_ALL")
        {
            return BadRequest(new { error = "Full reindex requires confirm=REINDEX_ALL." });
        }

        if (!request.Full
            && string.IsNullOrWhiteSpace(request.NewsId)
            && !request.PublishTimeFrom.HasValue
            && !request.PublishTimeTo.HasValue)
        {
            return BadRequest(new { error = "Specify NewsId, a publish-time range, or Full." });
        }

        int count = await _repository.ReindexAsync(
            request.Full ? null : request.NewsId,
            request.Full ? null : request.PublishTimeFrom,
            request.Full ? null : request.PublishTimeTo,
            cancellationToken);
        return Accepted(new { count });
    }

    [HttpGet("indexing-snapshot")]
    public async Task<IActionResult> Snapshot(CancellationToken cancellationToken)
    {
        return Ok(await _repository.GetSnapshotAsync(cancellationToken));
    }

    [HttpGet("consistency")]
    public async Task<IActionResult> Consistency(
        [FromQuery] int hashSampleSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (hashSampleSize is < 1 or > 10_000)
        {
            return BadRequest(new { error = "hashSampleSize must be 1 to 10000." });
        }
        return Ok(await _consistency.CheckAsync(hashSampleSize, cancellationToken));
    }
}


public sealed record ReindexRequest(
    string? NewsId,
    DateTimeOffset? PublishTimeFrom,
    DateTimeOffset? PublishTimeTo,
    bool Full,
    string? Confirm);
