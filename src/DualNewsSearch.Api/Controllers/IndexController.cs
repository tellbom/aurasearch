using System.ComponentModel.DataAnnotations;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Api.Controllers;

[ApiController]
[Route("api/v1/index/documents")]
public sealed class IndexController : ControllerBase
{
    private readonly IndexDocumentService _service;
    private readonly IndexingOptions _options;

    public IndexController(IndexDocumentService service, IOptions<IndexingOptions> options)
    {
        _service = service;
        _options = options.Value;
    }

    [HttpPut("{newsId}")]
    [ProducesResponseType(typeof(IndexWriteResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Upsert(
        string newsId,
        [FromBody] UpsertDocumentRequest request,
        CancellationToken cancellationToken)
    {
        IndexWriteResponse response = await _service.UpsertAsync(newsId, request, cancellationToken);
        return Accepted(response);
    }

    [HttpDelete("{newsId}")]
    [ProducesResponseType(typeof(IndexWriteResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Delete(
        string newsId,
        [FromQuery, Range(1, long.MaxValue)] long indexVersion,
        CancellationToken cancellationToken)
    {
        IndexWriteResponse response = await _service.DeleteAsync(newsId, indexVersion, cancellationToken);
        return Accepted(response);
    }

    [HttpPost("batch")]
    public async Task<IActionResult> Batch(
        [FromBody] BatchDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Documents.Count > _options.BatchSizeLimit)
        {
            ModelState.AddModelError(
                nameof(request.Documents),
                $"Batch size cannot exceed {_options.BatchSizeLimit}.");
            return ValidationProblem(ModelState);
        }

        var responses = new List<BatchIndexItemResponse>(request.Documents.Count);
        foreach (BatchDocumentItem item in request.Documents)
        {
            var validationResults = new List<ValidationResult>();
            bool valid = Validator.TryValidateObject(
                item.Document,
                new ValidationContext(item.Document),
                validationResults,
                validateAllProperties: true);
            if (!valid || string.IsNullOrWhiteSpace(item.NewsId))
            {
                responses.Add(new BatchIndexItemResponse(
                    item.NewsId,
                    item.Document.IndexVersion,
                    "Invalid",
                    validationResults.Select(x => x.ErrorMessage ?? "Invalid request").ToArray()));
                continue;
            }

            try
            {
                IndexWriteResponse response = await _service.UpsertAsync(
                    item.NewsId,
                    item.Document,
                    cancellationToken);
                responses.Add(new BatchIndexItemResponse(
                    response.NewsId,
                    response.IndexVersion,
                    response.Status.ToString(),
                    Array.Empty<string>()));
            }
            catch (ArgumentException exception)
            {
                responses.Add(new BatchIndexItemResponse(
                    item.NewsId,
                    item.Document.IndexVersion,
                    "Invalid",
                    new[] { exception.Message }));
            }
        }

        return Accepted(responses);
    }
}

