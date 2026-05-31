using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HealthyGuidance.Core.Storage;

public static class RecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static SaveSuccessResult SaveSuccess(
        byte[] imageBytes,
        string imageExtension,
        RecordKind kind,
        JsonObject data,
        ParseMeta parseMeta,
        DateTime? eventTimeFromData,
        DateTime importTime)
    {
        var sha = ComputeSha256(imageBytes);

        var existing = FindRecordBySha(sha);
        if (existing is not null)
            return new SaveSuccessResult { Outcome = SaveOutcome.DuplicateSkipped, Record = existing };

        var failed = FindFailedBySha(sha);
        if (failed is not null)
            Directory.Delete(failed.DirectoryPath, recursive: true);

        var timestamp = eventTimeFromData ?? importTime;
        var id = BuildSuccessId(timestamp, kind, sha);
        var dir = Path.Combine(StorageRoot.RecordsMonthDir(timestamp), id);
        Directory.CreateDirectory(dir);

        var imageFile = $"screenshot{NormalizeExt(imageExtension)}";
        File.WriteAllBytes(Path.Combine(dir, imageFile), imageBytes);

        var record = new SavedRecord
        {
            Id = id,
            Kind = kind,
            SavedAt = importTime,
            ImageFile = imageFile,
            ImageSha256 = sha,
            Parse = parseMeta,
            Data = data,
            DirectoryPath = dir
        };

        WriteParsedJson(dir, record);
        return new SaveSuccessResult { Outcome = SaveOutcome.Created, Record = record };
    }

    public static SaveFailureResult SaveFailure(
        byte[] imageBytes,
        string imageExtension,
        FailureAttempt attempt,
        DateTime importTime)
    {
        var sha = ComputeSha256(imageBytes);

        var alreadySucceeded = FindRecordBySha(sha);
        if (alreadySucceeded is not null)
            throw new InvalidOperationException(
                $"Image already saved as success record {alreadySucceeded.Id}; refusing to log a failure for it.");

        var existing = FindFailedBySha(sha);
        if (existing is not null)
        {
            existing.Attempts.Add(attempt);
            WriteErrorJson(existing.DirectoryPath, existing);
            return new SaveFailureResult { Outcome = SaveOutcome.DuplicateSkipped, Record = existing };
        }

        var id = BuildFailureId(importTime, sha);
        var dir = Path.Combine(StorageRoot.FailedMonthDir(importTime), id);
        Directory.CreateDirectory(dir);

        var imageFile = $"screenshot{NormalizeExt(imageExtension)}";
        File.WriteAllBytes(Path.Combine(dir, imageFile), imageBytes);

        var record = new FailedRecord
        {
            Id = id,
            SavedAt = importTime,
            ImageFile = imageFile,
            ImageSha256 = sha,
            Attempts = new List<FailureAttempt> { attempt },
            DirectoryPath = dir
        };

        WriteErrorJson(dir, record);
        return new SaveFailureResult { Outcome = SaveOutcome.Created, Record = record };
    }

    public static SavedRecord PromoteFailureToSuccess(
        string failureId,
        RecordKind kind,
        JsonObject data,
        ParseMeta parseMeta,
        DateTime? eventTimeFromData)
    {
        var failed = FindFailedById(failureId)
            ?? throw new FileNotFoundException($"Failed record not found: {failureId}");

        var timestamp = eventTimeFromData ?? failed.SavedAt;
        var newId = BuildSuccessId(timestamp, kind, failed.ImageSha256);
        var newDir = Path.Combine(StorageRoot.RecordsMonthDir(timestamp), newId);
        Directory.CreateDirectory(Path.GetDirectoryName(newDir)!);

        Directory.Move(failed.DirectoryPath, newDir);

        var errorJson = Path.Combine(newDir, "error.json");
        if (File.Exists(errorJson)) File.Delete(errorJson);

        var record = new SavedRecord
        {
            Id = newId,
            Kind = kind,
            SavedAt = failed.SavedAt,
            ImageFile = failed.ImageFile,
            ImageSha256 = failed.ImageSha256,
            Parse = parseMeta,
            Data = data,
            DirectoryPath = newDir
        };

        WriteParsedJson(newDir, record);
        return record;
    }

    public static IEnumerable<SavedRecord> ListByMonth(string yearMonth)
    {
        var monthDir = Path.Combine(StorageRoot.RecordsDir, yearMonth);
        if (!Directory.Exists(monthDir)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(monthDir))
        {
            var rec = TryReadParsedJson(dir);
            if (rec is not null) yield return rec;
        }
    }

    public static IEnumerable<SavedRecord> ListInWindow(DateTime start, DateTime end)
    {
        foreach (var month in EnumerateMonths(start, end))
        foreach (var rec in ListByMonth(month))
        {
            var t = ExtractTimestampFromId(rec.Id);
            if (t >= start && t <= end) yield return rec;
        }
    }

    public static IEnumerable<FailedRecord> ListFailedByMonth(string yearMonth)
    {
        var monthDir = Path.Combine(StorageRoot.FailedDir, yearMonth);
        if (!Directory.Exists(monthDir)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(monthDir))
        {
            var rec = TryReadErrorJson(dir);
            if (rec is not null) yield return rec;
        }
    }

    private static SavedRecord? FindRecordBySha(string sha)
    {
        if (!Directory.Exists(StorageRoot.RecordsDir)) return null;
        var suffix = "_" + sha[..8];
        foreach (var monthDir in Directory.EnumerateDirectories(StorageRoot.RecordsDir))
        foreach (var recDir in Directory.EnumerateDirectories(monthDir))
        {
            if (!Path.GetFileName(recDir).EndsWith(suffix, StringComparison.Ordinal)) continue;
            var rec = TryReadParsedJson(recDir);
            if (rec is not null && rec.ImageSha256 == sha) return rec;
        }
        return null;
    }

    private static FailedRecord? FindFailedBySha(string sha)
    {
        if (!Directory.Exists(StorageRoot.FailedDir)) return null;
        var suffix = "_" + sha[..8];
        foreach (var monthDir in Directory.EnumerateDirectories(StorageRoot.FailedDir))
        foreach (var recDir in Directory.EnumerateDirectories(monthDir))
        {
            if (!Path.GetFileName(recDir).EndsWith(suffix, StringComparison.Ordinal)) continue;
            var rec = TryReadErrorJson(recDir);
            if (rec is not null && rec.ImageSha256 == sha) return rec;
        }
        return null;
    }

    private static FailedRecord? FindFailedById(string id)
    {
        if (!Directory.Exists(StorageRoot.FailedDir)) return null;
        foreach (var monthDir in Directory.EnumerateDirectories(StorageRoot.FailedDir))
        {
            var candidate = Path.Combine(monthDir, id);
            if (Directory.Exists(candidate)) return TryReadErrorJson(candidate);
        }
        return null;
    }

    private static string BuildSuccessId(DateTime localTime, RecordKind kind, string sha) =>
        $"{localTime:yyyyMMdd-HHmmss}_{kind.ToSlug()}_{sha[..8]}";

    private static string BuildFailureId(DateTime localTime, string sha) =>
        $"{localTime:yyyyMMdd-HHmmss}_{sha[..8]}";

    private static DateTime ExtractTimestampFromId(string id)
    {
        var stamp = id[..15];
        return DateTime.ParseExact(stamp, "yyyyMMdd-HHmmss", null);
    }

    private static IEnumerable<string> EnumerateMonths(DateTime start, DateTime end)
    {
        var cursor = new DateTime(start.Year, start.Month, 1);
        var stop = new DateTime(end.Year, end.Month, 1);
        while (cursor <= stop)
        {
            yield return cursor.ToString("yyyy-MM");
            cursor = cursor.AddMonths(1);
        }
    }

    private static string NormalizeExt(string ext)
    {
        var lower = ext.ToLowerInvariant();
        if (!lower.StartsWith('.')) lower = "." + lower;
        return lower switch
        {
            ".jpeg" => ".jpg",
            _ => lower
        };
    }

    private static void WriteParsedJson(string dir, SavedRecord r)
    {
        var dto = new
        {
            id = r.Id,
            kind = r.Kind.ToSlug(),
            saved_at = r.SavedAt,
            image_file = r.ImageFile,
            image_sha256 = r.ImageSha256,
            parse = r.Parse,
            data = r.Data
        };
        File.WriteAllText(Path.Combine(dir, "parsed.json"),
            JsonSerializer.Serialize(dto, JsonOptions));
    }

    private static void WriteErrorJson(string dir, FailedRecord r)
    {
        var dto = new
        {
            id = r.Id,
            saved_at = r.SavedAt,
            image_file = r.ImageFile,
            image_sha256 = r.ImageSha256,
            attempts = r.Attempts
        };
        File.WriteAllText(Path.Combine(dir, "error.json"),
            JsonSerializer.Serialize(dto, JsonOptions));
    }

    private static SavedRecord? TryReadParsedJson(string dir)
    {
        var path = Path.Combine(dir, "parsed.json");
        if (!File.Exists(path)) return null;
        var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        if (node is null) return null;

        var parseNode = node["parse"] as JsonObject ?? new JsonObject();
        var missing = parseNode["missing_fields"] as JsonArray ?? new JsonArray();

        return new SavedRecord
        {
            Id = node["id"]!.GetValue<string>(),
            Kind = RecordKindExtensions.FromSlug(node["kind"]!.GetValue<string>()),
            SavedAt = node["saved_at"]!.GetValue<DateTime>(),
            ImageFile = node["image_file"]!.GetValue<string>(),
            ImageSha256 = node["image_sha256"]!.GetValue<string>(),
            Parse = new ParseMeta
            {
                Model = parseNode["model"]!.GetValue<string>(),
                ApiVersion = parseNode["api_version"]!.GetValue<string>(),
                ParsedAt = parseNode["parsed_at"]!.GetValue<DateTime>(),
                TimestampSource = Enum.Parse<TimestampSource>(
                    SnakeToPascal(parseNode["timestamp_source"]!.GetValue<string>())),
                MissingFields = missing.Select(n => n!.GetValue<string>()).ToList(),
                Confidence = Enum.Parse<Confidence>(
                    SnakeToPascal(parseNode["confidence"]!.GetValue<string>()), ignoreCase: true)
            },
            Data = (JsonObject)node["data"]!.DeepClone(),
            DirectoryPath = dir
        };
    }

    private static FailedRecord? TryReadErrorJson(string dir)
    {
        var path = Path.Combine(dir, "error.json");
        if (!File.Exists(path)) return null;
        var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        if (node is null) return null;

        var attempts = (node["attempts"] as JsonArray ?? new JsonArray())
            .Select(a => new FailureAttempt
            {
                AttemptedAt = a!["attempted_at"]!.GetValue<DateTime>(),
                Model = a["model"]!.GetValue<string>(),
                ErrorType = Enum.Parse<ErrorType>(
                    SnakeToPascal(a["error_type"]!.GetValue<string>())),
                ErrorMessage = a["error_message"]!.GetValue<string>()
            })
            .ToList();

        return new FailedRecord
        {
            Id = node["id"]!.GetValue<string>(),
            SavedAt = node["saved_at"]!.GetValue<DateTime>(),
            ImageFile = node["image_file"]!.GetValue<string>(),
            ImageSha256 = node["image_sha256"]!.GetValue<string>(),
            Attempts = attempts,
            DirectoryPath = dir
        };
    }

    private static string SnakeToPascal(string s) =>
        string.Concat(s.Split('_').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
}
