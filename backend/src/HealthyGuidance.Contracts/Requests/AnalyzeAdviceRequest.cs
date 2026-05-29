namespace HealthyGuidance.Contracts.Requests;

public sealed class AnalyzeAdviceRequest
{
    public required IReadOnlyList<BodyMetricsSample> BodyMetrics { get; init; }

    public IReadOnlyList<WorkoutSample> Workouts { get; init; } = Array.Empty<WorkoutSample>();

    public IReadOnlyList<MealSample> Meals { get; init; } = Array.Empty<MealSample>();
}

public sealed record BodyMetricsSample(DateTimeOffset CapturedAt, object Structured);

public sealed record WorkoutSample(DateTimeOffset CapturedAt, object Structured);

public sealed record MealSample(DateOnly AppliesToDate, string MealType, IReadOnlyList<MealSampleItem> Items);

public sealed record MealSampleItem(string Name, string? Quantity, int? EstimatedKcal);
