namespace HealthyGuidance.Domain.Models;

public sealed record Workout(
    DateTimeOffset OccurredAt,
    string? Category,
    IReadOnlyDictionary<string, string> Fields);
