using HealthyGuidance.Application.UseCases;
using HealthyGuidance.Contracts.Requests;
using HealthyGuidance.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace HealthyGuidance.Api.Controllers;

[ApiController]
[Route("api/advice")]
public sealed class AdviceController(AnalyzeAdviceUseCase useCase) : ControllerBase
{
    [HttpPost("analyze")]
    [ProducesResponseType(typeof(AnalyzeAdviceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AnalyzeAdviceResponse>> Analyze(
        [FromBody] AnalyzeAdviceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await useCase.ExecuteAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = ex.Message, Status = 400 });
        }
    }
}
