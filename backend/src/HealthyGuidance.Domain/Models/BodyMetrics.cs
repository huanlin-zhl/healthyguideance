namespace HealthyGuidance.Domain.Models;

public sealed record BodyMetrics(
    DateTimeOffset MeasuredAt,
    double? WeightKg,
    double? BodyFatPercent,
    double? SkeletalMuscleKg,
    double? VisceralFatLevel,
    double? ProteinPercent,
    double? Bmi,
    IReadOnlyDictionary<string, string> Fields);
