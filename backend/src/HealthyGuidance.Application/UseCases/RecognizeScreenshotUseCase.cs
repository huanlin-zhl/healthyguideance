using HealthyGuidance.Application.Interfaces;
using HealthyGuidance.Contracts.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HealthyGuidance.Application.UseCases;

public sealed class RecognizeScreenshotUseCase(
    IScreenshotRecognizer recognizer,
    ILogger<RecognizeScreenshotUseCase> logger)
{
    public async Task<RecognizeScreenshotResponse> ExecuteAsync(IFormFile image, CancellationToken cancellationToken)
    {
        logger.LogInformation("Recognizing screenshot size={Size}", image.Length);

        await using var stream = image.OpenReadStream();
        var result = await recognizer.RecognizeAsync(stream, cancellationToken);

        return new RecognizeScreenshotResponse(
            result.Kind.ToString(),
            result.Confidence,
            result.CapturedAt,
            result.Structured,
            new ModelInfo(recognizer.Models.Extractor, recognizer.Models.Classifier));
    }
}
