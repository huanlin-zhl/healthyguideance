using HealthyGuidance.Application.UseCases;
using HealthyGuidance.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace HealthyGuidance.Api.Controllers;

[ApiController]
[Route("api/screenshots")]
public sealed class ScreenshotsController(RecognizeScreenshotUseCase useCase) : ControllerBase
{
    [HttpPost("recognize")]
    [ProducesResponseType(typeof(RecognizeScreenshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<RecognizeScreenshotResponse>> Recognize(
        [FromForm] IFormFile image,
        CancellationToken cancellationToken)
    {
        if (image is null || image.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "Missing image", Status = 400 });
        }

        var response = await useCase.ExecuteAsync(image, cancellationToken);
        return Ok(response);
    }
}
