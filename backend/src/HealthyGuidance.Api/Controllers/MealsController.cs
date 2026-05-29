using HealthyGuidance.Application.UseCases;
using HealthyGuidance.Contracts.Requests;
using HealthyGuidance.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace HealthyGuidance.Api.Controllers;

[ApiController]
[Route("api/meals")]
public sealed class MealsController(RecognizeMealUseCase useCase) : ControllerBase
{
    [HttpPost("recognize")]
    [ProducesResponseType(typeof(RecognizeMealResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RecognizeMealResponse>> Recognize(
        [FromBody] RecognizeMealRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await useCase.ExecuteAsync(request.FreeText, request.Today, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = ex.Message, Status = 400 });
        }
    }
}
