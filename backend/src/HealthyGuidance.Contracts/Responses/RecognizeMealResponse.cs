namespace HealthyGuidance.Contracts.Responses;

public sealed record RecognizeMealResponse(
    IReadOnlyList<MealCandidateDto> Candidates,
    LlmModelInfo Model);

public sealed record MealCandidateDto(
    DateOnly AppliesToDate,
    string MealType,
    IReadOnlyList<MealItemDto> Items);

public sealed record MealItemDto(string Name, string? Quantity, int? EstimatedKcal);

public sealed record LlmModelInfo(string Llm);
