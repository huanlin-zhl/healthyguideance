using HealthyGuidance.Domain.Models;

namespace HealthyGuidance.Application.Interfaces;

public interface IMealRecognizer
{
    string ModelName { get; }

    Task<IReadOnlyList<MealCandidate>> RecognizeAsync(string freeText, DateOnly today, CancellationToken cancellationToken);
}
