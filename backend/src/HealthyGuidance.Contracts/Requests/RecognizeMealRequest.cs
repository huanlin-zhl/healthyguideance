namespace HealthyGuidance.Contracts.Requests;

public sealed class RecognizeMealRequest
{
    public required string FreeText { get; init; }

    public required DateOnly Today { get; init; }
}
