namespace HealthyGuidance.Contracts.Responses;

public sealed record AnalyzeAdviceResponse(
    string Summary,
    TrendAnalysisDto TrendAnalysis,
    string Validation,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<string> Warnings,
    LlmModelInfo Model);

public sealed record TrendAnalysisDto(
    double? WeightChangeKg,
    double? BodyFatChangePercent,
    string Verdict);
