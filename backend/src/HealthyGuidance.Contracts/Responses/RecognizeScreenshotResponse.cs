namespace HealthyGuidance.Contracts.Responses;

public sealed record RecognizeScreenshotResponse(
    string Kind,
    double Confidence,
    DateTimeOffset? CapturedAt,
    object Structured,
    ModelInfo Model);

public sealed record ModelInfo(string Extractor, string Classifier);
