using HealthyGuidance.Domain.Enums;

namespace HealthyGuidance.Domain.Models;

public sealed record MealCandidate(
    DateOnly AppliesToDate,
    MealType MealType,
    IReadOnlyList<MealItem> Items);

public sealed record MealItem(string Name, string? Quantity, int? EstimatedKcal);
