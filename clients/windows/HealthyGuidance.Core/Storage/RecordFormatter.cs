using System.Text.Json.Nodes;

namespace HealthyGuidance.Core.Storage;

public sealed record DisplayField(string Label, string Value, bool IsMissing, IReadOnlyList<DisplayField>? Children = null);

public enum FieldGroupKind
{
    /// <summary>简单字段方阵：自适应多列，每个字段一个小卡片。</summary>
    Cards,
    /// <summary>嵌套子项方阵：所有子项作为同一组横向卡片，组上方有标题。</summary>
    SubCards,
    /// <summary>长文本：占满一整行的卡片。</summary>
    LongText
}

public sealed record FieldGroup(FieldGroupKind Kind, string? Title, IReadOnlyList<DisplayField> Fields);

public static class RecordFormatter
{
    public static string FormatCollapsed(SavedRecord record)
    {
        var time = ExtractDisplayTime(record).ToString("yyyy-MM-dd HH:mm");
        return record.Kind switch
        {
            RecordKind.Workout => FormatWorkoutCollapsed(time, record.Data),
            RecordKind.BodyMetrics => FormatBodyMetricsCollapsed(time, record.Data),
            _ => time
        };
    }

    public static string FormatCollapsed(FailedRecord record)
    {
        var last = record.Attempts.LastOrDefault();
        var summary = last?.ErrorMessage ?? last?.ErrorType.ToString() ?? "未知错误";
        if (summary.Length > 60) summary = summary[..60] + "...";
        return $"{record.SavedAt:yyyy-MM-dd HH:mm} · 解析失败：{summary}";
    }

    public static IReadOnlyList<DisplayField> FormatExpanded(SavedRecord record) =>
        BuildFromSchema(RecordSchema.ForKind(record.Kind), record.Data);

    public static IReadOnlyList<FieldGroup> FormatGroups(SavedRecord record) =>
        record.Kind switch
        {
            RecordKind.Workout => BuildGroups(FormatExpanded(record)),
            RecordKind.BodyMetrics => BuildGroups(FormatExpanded(record), longTextKeys: new[] { "体脂秤小结" }),
            _ => Array.Empty<FieldGroup>()
        };

    public static IReadOnlyList<FieldGroup> FormatGroups(FailedRecord record) =>
        new List<FieldGroup> { new(FieldGroupKind.Cards, null, FormatExpanded(record)) };

    private static IReadOnlyList<FieldGroup> BuildGroups(
        IReadOnlyList<DisplayField> flat,
        IReadOnlyList<string>? longTextKeys = null)
    {
        var groups = new List<FieldGroup>();
        var currentCards = new List<DisplayField>();

        void FlushCards()
        {
            if (currentCards.Count == 0) return;
            groups.Add(new FieldGroup(FieldGroupKind.Cards, null, currentCards.ToList()));
            currentCards.Clear();
        }

        foreach (var f in flat)
        {
            // 嵌套对象 -> 独立 SubCards 段
            if (f.Children is { Count: > 0 })
            {
                FlushCards();
                groups.Add(new FieldGroup(FieldGroupKind.SubCards, f.Label, f.Children));
                continue;
            }

            // 长文本 -> 独立 LongText 段
            if (longTextKeys is not null && longTextKeys.Contains(f.Label))
            {
                FlushCards();
                groups.Add(new FieldGroup(FieldGroupKind.LongText, null, new List<DisplayField> { f }));
                continue;
            }

            currentCards.Add(f);
        }
        FlushCards();
        return groups;
    }

    public static IReadOnlyList<DisplayField> FormatExpanded(FailedRecord record)
    {
        var list = new List<DisplayField>
        {
            new("文件 hash", record.ImageSha256, false),
            new("导入时间", record.SavedAt.ToString("yyyy-MM-dd HH:mm:ss"), false),
            new("尝试次数", record.Attempts.Count.ToString(), false)
        };

        for (var i = 0; i < record.Attempts.Count; i++)
        {
            var a = record.Attempts[i];
            list.Add(new DisplayField(
                $"尝试 #{i + 1}",
                $"{a.AttemptedAt:yyyy-MM-dd HH:mm:ss} · {a.ErrorType} · {a.ErrorMessage}",
                false));
        }
        return list;
    }

    private static DateTime ExtractDisplayTime(SavedRecord record)
    {
        var key = record.Kind == RecordKind.Workout ? "date_time" : "measured_at";
        if (record.Data[key] is JsonValue v
            && v.GetValueKind() == System.Text.Json.JsonValueKind.String
            && DateTime.TryParse(v.GetValue<string>(), out var dt))
            return dt;
        return record.SavedAt;
    }

    private static string FormatWorkoutCollapsed(string time, JsonObject data)
    {
        var sport = GetString(data, "sport_type") ?? "—";
        var duration = GetString(data, "duration_text") ?? "—";
        var calories = GetString(data, "calories_text") ?? "—";
        return $"{time} · {sport} · {duration} · {calories}";
    }

    private static string FormatBodyMetricsCollapsed(string time, JsonObject data)
    {
        var weight = GetNumber(data, "weight_kg") is double w ? $"{w} kg" : "—";
        var bmi = GetNumber(data, "bmi") is double b ? $"BMI {b}" : "BMI —";
        return $"{time} · 体成分 · {weight} · {bmi}";
    }

    private static IReadOnlyList<DisplayField> BuildFromSchema(IReadOnlyList<FieldDef> defs, JsonObject data)
    {
        var list = new List<DisplayField>(defs.Count);
        foreach (var def in defs)
        {
            if (def.ValueType == FieldValueType.NestedObject)
            {
                var nested = data[def.Key] as JsonObject;
                if (nested is null)
                {
                    list.Add(new DisplayField(def.Label, "—", true));
                    continue;
                }
                var children = new List<DisplayField>(def.Children!.Count);
                foreach (var c in def.Children!)
                    children.Add(LeafField(c, nested));
                list.Add(new DisplayField(def.Label, string.Empty, false, children));
            }
            else
            {
                list.Add(LeafField(def, data));
            }
        }
        return list;
    }

    private static DisplayField LeafField(FieldDef def, JsonObject parent)
    {
        var node = parent[def.Key];
        if (node is null || node.GetValueKind() == System.Text.Json.JsonValueKind.Null)
            return new DisplayField(def.Label, "—", true);

        string text = def.ValueType switch
        {
            FieldValueType.String when node.GetValueKind() == System.Text.Json.JsonValueKind.String
                => node.GetValue<string>(),
            FieldValueType.Number => node.ToJsonString() + def.Suffix,
            _ => node.ToJsonString() + def.Suffix
        };
        return new DisplayField(def.Label, text, false);
    }

    private static string? GetString(JsonObject data, string key)
    {
        var node = data[key];
        if (node is null || node.GetValueKind() != System.Text.Json.JsonValueKind.String) return null;
        return node.GetValue<string>();
    }

    private static double? GetNumber(JsonObject data, string key)
    {
        var node = data[key];
        if (node is null || node.GetValueKind() != System.Text.Json.JsonValueKind.Number) return null;
        return node.GetValue<double>();
    }
}
