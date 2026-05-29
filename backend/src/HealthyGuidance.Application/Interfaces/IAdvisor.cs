using HealthyGuidance.Contracts.Requests;
using HealthyGuidance.Domain.Models;

namespace HealthyGuidance.Application.Interfaces;

public interface IAdvisor
{
    string ModelName { get; }

    Task<AdviceReport> AnalyzeAsync(
        IReadOnlyList<BodyMetricsSample> bodyMetrics,
        IReadOnlyList<WorkoutSample> workouts,
        IReadOnlyList<MealSample> meals,
        CancellationToken cancellationToken);
}
