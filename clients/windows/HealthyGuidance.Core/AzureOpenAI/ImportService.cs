using HealthyGuidance.Core.Prompts;
using HealthyGuidance.Core.Schemas;
using HealthyGuidance.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HealthyGuidance.Core.AzureOpenAI;

public enum ImportOutcome { Success, DuplicateSkipped, Failed }

public sealed class ImportResult
{
    public required ImportOutcome Outcome { get; init; }
    public required string FileName { get; init; }
    public SavedRecord? Saved { get; init; }
    public FailedRecord? Failed { get; init; }
    public string? Message { get; init; }
}

public sealed class ImportService
{
    private const string ApiVersion = "v1";

    private static readonly string[] WorkoutRequired =
        { "date_time", "sport_type", "duration_text", "calories_text" };
    private static readonly string[] BodyMetricsRequired = { "measured_at", "weight_kg" };

    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _deploymentName;
    private readonly string _sharedRoot;

    private string? _cachedSystemPrompt;
    private string? _cachedSchema;

    public ImportService(string endpoint, string apiKey, string deploymentName, string sharedRoot)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
        _deploymentName = deploymentName;
        _sharedRoot = sharedRoot;
    }

    public async Task<ImportResult> ImportAsync(
        byte[] imageBytes, string extension, string mime, string fileName,
        DateTime importTime, CancellationToken cancellationToken = default)
    {
        // 1) SHA 去重
        var sha = RecordStore.ComputeSha256(imageBytes);
        var existing = RecordStore.TryFindExistingBySha(sha);
        if (existing is not null)
        {
            return new ImportResult
            {
                Outcome = ImportOutcome.DuplicateSkipped,
                FileName = fileName,
                Saved = existing,
                Message = "已存在相同图片"
            };
        }

        // 2) 调模型
        _cachedSystemPrompt ??= PromptLoader.Load(_sharedRoot, "parse.md");
        _cachedSchema ??= SchemaLoader.LoadInlined(_sharedRoot, "parse-result.json");

        var client = new GptVisionClient(_endpoint, _apiKey, _deploymentName);

        string json;
        try
        {
            json = await client.ParseScreenshotAsync(
                imageBytes, mime, _cachedSystemPrompt, _cachedSchema, cancellationToken);
        }
        catch (Exception apiEx)
        {
            var failed = RecordStore.SaveFailure(imageBytes, extension, new FailureAttempt
            {
                AttemptedAt = DateTime.Now,
                Model = _deploymentName,
                ErrorType = ErrorType.ApiError,
                ErrorMessage = apiEx.Message
            }, importTime);
            return new ImportResult
            {
                Outcome = ImportOutcome.Failed,
                FileName = fileName,
                Failed = failed.Record,
                Message = apiEx.Message
            };
        }

        // 3) 解析响应
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidOperationException("响应不是 JSON 对象");
        }
        catch (Exception parseEx)
        {
            var failed = RecordStore.SaveFailure(imageBytes, extension, new FailureAttempt
            {
                AttemptedAt = DateTime.Now,
                Model = _deploymentName,
                ErrorType = ErrorType.SchemaViolation,
                ErrorMessage = parseEx.Message
            }, importTime);
            return new ImportResult
            {
                Outcome = ImportOutcome.Failed,
                FileName = fileName,
                Failed = failed.Record,
                Message = parseEx.Message
            };
        }

        var kindStr = root["kind"]?.GetValue<string>() ?? "unknown";
        if (kindStr == "unknown")
        {
            var reason = root["error_reason"]?.GetValue<string>() ?? "kind=unknown";
            var failed = RecordStore.SaveFailure(imageBytes, extension, new FailureAttempt
            {
                AttemptedAt = DateTime.Now,
                Model = _deploymentName,
                ErrorType = ErrorType.KindUnknown,
                ErrorMessage = reason
            }, importTime);
            return new ImportResult
            {
                Outcome = ImportOutcome.Failed,
                FileName = fileName,
                Failed = failed.Record,
                Message = reason
            };
        }

        var kind = RecordKindExtensions.FromSlug(kindStr);
        var dataKey = kind == RecordKind.Workout ? "workout" : "body_metrics";
        var data = (JsonObject)root[dataKey]!.DeepClone();
        var eventTime = ExtractEventTime(kind, data);
        var (tsSource, missingFields, confidence) = AnalyzeData(kind, data);

        var parseMeta = new ParseMeta
        {
            Model = _deploymentName,
            ApiVersion = ApiVersion,
            ParsedAt = DateTime.Now,
            TimestampSource = eventTime is null ? TimestampSource.Import : TimestampSource.Extracted,
            MissingFields = missingFields,
            Confidence = confidence
        };

        var saved = RecordStore.SaveSuccess(
            imageBytes, extension, kind, data, parseMeta, eventTime, importTime);

        return new ImportResult
        {
            Outcome = saved.Outcome == SaveOutcome.DuplicateSkipped
                ? ImportOutcome.DuplicateSkipped
                : ImportOutcome.Success,
            FileName = fileName,
            Saved = saved.Record
        };
    }

    private static DateTime? ExtractEventTime(RecordKind kind, JsonObject data)
    {
        var key = kind == RecordKind.Workout ? "date_time" : "measured_at";
        var node = data[key];
        if (node is null || node.GetValueKind() == JsonValueKind.Null) return null;
        var s = node.GetValue<string>();
        return DateTime.TryParse(s, out var dt) ? dt : null;
    }

    private static (TimestampSource ts, List<string> missing, Confidence confidence) AnalyzeData(
        RecordKind kind, JsonObject data)
    {
        var required = kind == RecordKind.Workout ? WorkoutRequired : BodyMetricsRequired;
        var missing = required
            .Where(k => data[k] is null || data[k]!.GetValueKind() == JsonValueKind.Null)
            .ToList();
        var confidence = missing.Count switch
        {
            0 => Confidence.High,
            1 => Confidence.Medium,
            _ => Confidence.Low
        };
        return (TimestampSource.Extracted, missing, confidence);
    }
}
