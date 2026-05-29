using HealthyGuidance.Application.Interfaces;
using HealthyGuidance.Contracts.Responses;
using Microsoft.Extensions.Logging;

namespace HealthyGuidance.Application.UseCases;

public sealed class RecognizeMealUseCase(
    IMealRecognizer recognizer,
    ILogger<RecognizeMealUseCase> logger)
{
    public async Task<RecognizeMealResponse> ExecuteAsync(string freeText, DateOnly today, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(freeText))
        {
            throw new ArgumentException("freeText is required", nameof(freeText));
        }

        logger.LogInformation("Recognizing meal text length={Len} today={Today}", freeText.Length, today);

        var candidates = await recognizer.RecognizeAsync(freeText, today, cancellationToken);

        var dtos = candidates.Select(c => new MealCandidateDto(
            c.AppliesToDate,
            c.MealType.ToString(),
            c.Items.Select(i => new MealItemDto(i.Name, i.Quantity, i.EstimatedKcal)).ToList())).ToList();

        return new RecognizeMealResponse(dtos, new LlmModelInfo(recognizer.ModelName));
    }
}
