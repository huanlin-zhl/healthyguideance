using System.Text.Json.Nodes;

namespace HealthyGuidance.Core.Storage;

public enum RecordKind { Workout, BodyMetrics }

public static class RecordKindExtensions
{
    public static string ToSlug(this RecordKind kind) => kind switch
    {
        RecordKind.Workout => "workout",
        RecordKind.BodyMetrics => "body-metrics",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static RecordKind FromSlug(string slug) => slug switch
    {
        "workout" => RecordKind.Workout,
        "body-metrics" => RecordKind.BodyMetrics,
        _ => throw new ArgumentException($"Unknown kind slug: {slug}")
    };
}

public enum TimestampSource { Extracted, Import }

public enum Confidence { High, Medium, Low }

public sealed class ParseMeta
{
    public required string Model { get; init; }
    public required string ApiVersion { get; init; }
    public required DateTime ParsedAt { get; init; }
    public required TimestampSource TimestampSource { get; init; }
    public required List<string> MissingFields { get; init; }
    public required Confidence Confidence { get; init; }
}

public sealed class SavedRecord
{
    public required string Id { get; init; }
    public required RecordKind Kind { get; init; }
    public required DateTime SavedAt { get; init; }
    public required string ImageFile { get; init; }
    public required string ImageSha256 { get; init; }
    public required ParseMeta Parse { get; init; }
    public required JsonObject Data { get; init; }
    public required string DirectoryPath { get; init; }
}

public enum ErrorType
{
    SchemaViolation,
    KindUnknown,
    ApiError,
    NetworkError,
    Timeout
}

public sealed class FailureAttempt
{
    public required DateTime AttemptedAt { get; init; }
    public required string Model { get; init; }
    public required ErrorType ErrorType { get; init; }
    public required string ErrorMessage { get; init; }
}

public sealed class FailedRecord
{
    public required string Id { get; init; }
    public required DateTime SavedAt { get; init; }
    public required string ImageFile { get; init; }
    public required string ImageSha256 { get; init; }
    public required List<FailureAttempt> Attempts { get; init; }
    public required string DirectoryPath { get; init; }
}

public enum SaveOutcome { Created, DuplicateSkipped }

public sealed class SaveSuccessResult
{
    public required SaveOutcome Outcome { get; init; }
    public required SavedRecord Record { get; init; }
}

public sealed class SaveFailureResult
{
    public required SaveOutcome Outcome { get; init; }
    public required FailedRecord Record { get; init; }
}
