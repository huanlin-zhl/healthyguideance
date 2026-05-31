using HealthyGuidance.Core.Prompts;
using HealthyGuidance.Core.Storage;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HealthyGuidance.Core.Reports;

public sealed class AdviceContextInputs
{
    public required DateTime CurrentTime { get; init; }
    public required DateTime WindowStart { get; init; }
    public required DateTime WindowEnd { get; init; }
    public required double GoalWeightKg { get; init; }
    public required double GoalBodyFatPct { get; init; }
}

public sealed class AdviceContext
{
    public required string Prompt { get; init; }
    public required IReadOnlyList<string> WorkoutIds { get; init; }
    public required IReadOnlyList<string> BodyMetricsIds { get; init; }
    public required IReadOnlyList<string> NoteMonths { get; init; }
    public required int WorkoutCount { get; init; }
    public required int BodyMetricsCount { get; init; }
    public required int NoteCount { get; init; }
}

public static class AdviceContextBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AdviceContext Build(string sharedRoot, AdviceContextInputs inputs)
    {
        var template = PromptLoader.Load(sharedRoot, "advice.md");

        var saved = RecordStore.ListInWindow(inputs.WindowStart, inputs.WindowEnd)
            .OrderBy(r => RecordTimestamp(r))
            .ToList();

        var workouts = saved.Where(r => r.Kind == RecordKind.Workout).ToList();
        var bodyMetrics = saved.Where(r => r.Kind == RecordKind.BodyMetrics).ToList();
        var notes = NotesStore.ReadWindow(inputs.WindowStart, inputs.WindowEnd);

        var workoutsJson = BuildRecordsJson(workouts);
        var bodyMetricsJson = BuildRecordsJson(bodyMetrics);
        var notesText = BuildNotesText(notes);

        var noteMonths = EnumerateMonths(inputs.WindowStart, inputs.WindowEnd)
            .Select(m => m + ".txt")
            .ToList();

        var prompt = template
            .Replace("{current_time}", inputs.CurrentTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Replace("{window_start}", inputs.WindowStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Replace("{window_end}", inputs.WindowEnd.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Replace("{goal_weight_kg}", inputs.GoalWeightKg.ToString(CultureInfo.InvariantCulture))
            .Replace("{goal_body_fat_pct}", inputs.GoalBodyFatPct.ToString(CultureInfo.InvariantCulture))
            .Replace("{body_metrics_json}", bodyMetricsJson)
            .Replace("{workouts_json}", workoutsJson)
            .Replace("{notes_text}", notesText);

        return new AdviceContext
        {
            Prompt = prompt,
            WorkoutIds = workouts.Select(r => r.Id).ToList(),
            BodyMetricsIds = bodyMetrics.Select(r => r.Id).ToList(),
            NoteMonths = noteMonths,
            WorkoutCount = workouts.Count,
            BodyMetricsCount = bodyMetrics.Count,
            NoteCount = notes.Count
        };
    }

    private static string BuildRecordsJson(IReadOnlyList<SavedRecord> records)
    {
        if (records.Count == 0) return "[]";
        var arr = new JsonArray();
        foreach (var r in records)
        {
            var item = new JsonObject
            {
                ["id"] = r.Id,
                ["saved_at"] = r.SavedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                ["data"] = r.Data.DeepClone()
            };
            arr.Add(item);
        }
        return arr.ToJsonString(JsonOptions);
    }

    private static string BuildNotesText(IReadOnlyList<DietNote> notes)
    {
        if (notes.Count == 0) return "（无）";
        var sb = new StringBuilder();
        foreach (var n in notes)
        {
            sb.Append(n.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            sb.Append('\n');
            sb.Append(n.Text);
            sb.Append("\n\n");
        }
        return sb.ToString().TrimEnd();
    }

    private static DateTime RecordTimestamp(SavedRecord r)
    {
        var key = r.Kind == RecordKind.Workout ? "date_time" : "measured_at";
        if (r.Data[key] is JsonValue v
            && v.GetValueKind() == JsonValueKind.String
            && DateTime.TryParse(v.GetValue<string>(), out var dt))
            return dt;
        return r.SavedAt;
    }

    private static IEnumerable<string> EnumerateMonths(DateTime start, DateTime end)
    {
        var cursor = new DateTime(start.Year, start.Month, 1);
        var stop = new DateTime(end.Year, end.Month, 1);
        while (cursor <= stop)
        {
            yield return StorageRoot.MonthKey(cursor);
            cursor = cursor.AddMonths(1);
        }
    }
}
