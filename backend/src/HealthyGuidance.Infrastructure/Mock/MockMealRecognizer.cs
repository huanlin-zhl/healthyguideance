using HealthyGuidance.Application.Interfaces;
using HealthyGuidance.Domain.Enums;
using HealthyGuidance.Domain.Models;

namespace HealthyGuidance.Infrastructure.Mock;

public sealed class MockMealRecognizer : IMealRecognizer
{
    public string ModelName => "mock-llm-v0";

    public Task<IReadOnlyList<MealCandidate>> RecognizeAsync(string freeText, DateOnly today, CancellationToken cancellationToken)
    {
        var weekStart = today.AddDays(-(int)today.DayOfWeek + (today.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));

        var items = new List<MealItem> { new("水煮蛋", "2 个", 156) };
        var candidates = new List<MealCandidate>();
        for (var i = 0; i < 5; i++)
        {
            candidates.Add(new MealCandidate(weekStart.AddDays(i), MealType.Breakfast, items));
        }

        return Task.FromResult<IReadOnlyList<MealCandidate>>(candidates);
    }
}
