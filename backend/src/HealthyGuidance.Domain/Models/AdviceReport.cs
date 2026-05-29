namespace HealthyGuidance.Domain.Models;

public sealed record AdviceReport(
    string Summary,
    TrendAnalysis TrendAnalysis,
    string Validation,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<string> Warnings);

public sealed record TrendAnalysis(
    double? WeightChangeKg,
    double? BodyFatChangePercent,
    string Verdict);
