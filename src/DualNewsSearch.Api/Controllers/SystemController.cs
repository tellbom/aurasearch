using Microsoft.AspNetCore.Mvc;

namespace DualNewsSearch.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    [HttpGet("version")]
    public IActionResult VersionInfo()
    {
        return Ok(new
        {
            service = "DualNewsSearch",
            framework = "net6.0",
            runtime = Environment.Version.ToString()
        });
    }
}

