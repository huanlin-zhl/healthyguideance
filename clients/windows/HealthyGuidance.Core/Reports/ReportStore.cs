using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using HealthyGuidance.Core.Storage;

namespace HealthyGuidance.Core.Reports;

public sealed class ReportWindow
{
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }
    public required string Preset { get; init; } // "7d" / "30d" / "90d" / "custom"
}

public sealed class ReportGoal
{
    public required double WeightKg { get; init; }
    public required double BodyFatPct { get; init; }
}

public sealed class ReportSource
{
    public required string Model { get; init; }
    public required string ApiVersion { get; init; }
    public required IReadOnlyList<string> WorkoutIds { get; init; }
    public required IReadOnlyList<string> BodyMetricsIds { get; init; }
    public required IReadOnlyList<string> NotesWindow { get; init; }
}

public sealed class ReportContent
{
    public required string Summary { get; init; }
    public required string Trend { get; init; }
    public required string DietAdvice { get; init; }
    public required string WorkoutAdvice { get; init; }
    public required string Warnings { get; init; }
}

public sealed class Report
{
    public required string Id { get; init; }
    public required DateTime GeneratedAt { get; init; }
    public required ReportWindow Window { get; init; }
    public required ReportGoal Goal { get; init; }
    public required ReportSource Source { get; init; }
    public required ReportContent Content { get; init; }
    public required string Disclaimer { get; init; }
    public required string FilePath { get; init; }
}

public static class ReportStore
{
    public const string DefaultDisclaimer = "本建议仅供参考，不构成医疗或营养专业意见。";
    public const string DefaultApiVersion = "v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static Report Save(
        DateTime generatedAt,
        ReportWindow window,
        ReportGoal goal,
        ReportSource source,
        ReportContent content)
    {
        var id = generatedAt.ToString("yyyyMMdd-HHmmss");
        var dir = Path.Combine(StorageRoot.ReportsDir, StorageRoot.MonthKey(generatedAt));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id + ".json");

        var report = new Report
        {
            Id = id,
            GeneratedAt = generatedAt,
            Window = window,
            Goal = goal,
            Source = source,
            Content = content,
            Disclaimer = DefaultDisclaimer,
            FilePath = path
        };

        WriteJson(path, report);
        return report;
    }

    public static IEnumerable<Report> ListAll()
    {
        if (!Directory.Exists(StorageRoot.ReportsDir)) yield break;
        foreach (var monthDir in Directory.EnumerateDirectories(StorageRoot.ReportsDir))
        foreach (var file in Directory.EnumerateFiles(monthDir, "*.json"))
        {
            var r = TryRead(file);
            if (r is not null) yield return r;
        }
    }

    public static void Delete(Report report)
    {
        if (File.Exists(report.FilePath)) File.Delete(report.FilePath);
    }

    private static void WriteJson(string path, Report r)
    {
        var dto = new JsonObject
        {
            ["id"] = r.Id,
            ["generated_at"] = r.GeneratedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
            ["window"] = new JsonObject
            {
                ["start"] = r.Window.Start.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["end"] = r.Window.End.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["preset"] = r.Window.Preset
            },
            ["goal"] = new JsonObject
            {
                ["weight_kg"] = r.Goal.WeightKg,
                ["body_fat_pct"] = r.Goal.BodyFatPct
            },
            ["source"] = new JsonObject
            {
                ["model"] = r.Source.Model,
                ["api_version"] = r.Source.ApiVersion,
                ["workout_ids"] = ToJsonArray(r.Source.WorkoutIds),
                ["body_metrics_ids"] = ToJsonArray(r.Source.BodyMetricsIds),
                ["notes_window"] = ToJsonArray(r.Source.NotesWindow)
            },
            ["content"] = new JsonObject
            {
                ["summary"] = r.Content.Summary,
                ["trend"] = r.Content.Trend,
                ["diet_advice"] = r.Content.DietAdvice,
                ["workout_advice"] = r.Content.WorkoutAdvice,
                ["warnings"] = r.Content.Warnings
            },
            ["disclaimer"] = r.Disclaimer
        };
        File.WriteAllText(path, dto.ToJsonString(JsonOptions));
    }

    private static JsonArray ToJsonArray(IReadOnlyList<string> items)
    {
        var arr = new JsonArray();
        foreach (var s in items) arr.Add(s);
        return arr;
    }

    private static Report? TryRead(string path)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (node is null) return null;

            var window = (JsonObject)node["window"]!;
            var goal = (JsonObject)node["goal"]!;
            var source = (JsonObject)node["source"]!;
            var content = (JsonObject)node["content"]!;

            return new Report
            {
                Id = node["id"]!.GetValue<string>(),
                GeneratedAt = node["generated_at"]!.GetValue<DateTime>(),
                Window = new ReportWindow
                {
                    Start = window["start"]!.GetValue<DateTime>(),
                    End = window["end"]!.GetValue<DateTime>(),
                    Preset = window["preset"]!.GetValue<string>()
                },
                Goal = new ReportGoal
                {
                    WeightKg = goal["weight_kg"]!.GetValue<double>(),
                    BodyFatPct = goal["body_fat_pct"]!.GetValue<double>()
                },
                Source = new ReportSource
                {
                    Model = source["model"]!.GetValue<string>(),
                    ApiVersion = source["api_version"]!.GetValue<string>(),
                    WorkoutIds = ReadStringArray(source["workout_ids"]),
                    BodyMetricsIds = ReadStringArray(source["body_metrics_ids"]),
                    NotesWindow = ReadStringArray(source["notes_window"])
                },
                Content = new ReportContent
                {
                    Summary = content["summary"]!.GetValue<string>(),
                    Trend = content["trend"]!.GetValue<string>(),
                    DietAdvice = content["diet_advice"]!.GetValue<string>(),
                    WorkoutAdvice = content["workout_advice"]!.GetValue<string>(),
                    Warnings = content["warnings"]!.GetValue<string>()
                },
                Disclaimer = node["disclaimer"]?.GetValue<string>() ?? DefaultDisclaimer,
                FilePath = path
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ReadStringArray(JsonNode? node)
    {
        if (node is not JsonArray arr) return new();
        return arr.Select(n => n!.GetValue<string>()).ToList();
    }
}
