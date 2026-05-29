using HealthyGuidance.Domain.Enums;

namespace HealthyGuidance.Application.Interfaces;

public interface IScreenshotRecognizer
{
    ModelNames Models { get; }

    Task<ScreenshotRecognitionResult> RecognizeAsync(Stream imageStream, CancellationToken cancellationToken);
}

public sealed record ScreenshotRecognitionResult(
    ScreenshotKind Kind,
    double Confidence,
    DateTimeOffset? CapturedAt,
    object Structured);

public sealed record ModelNames(string Extractor, string Classifier);
