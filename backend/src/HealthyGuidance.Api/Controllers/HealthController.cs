using Microsoft.AspNetCore.Mvc;

namespace HealthyGuidance.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        version = typeof(HealthController).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        utcNow = DateTimeOffset.UtcNow,
    });
}
