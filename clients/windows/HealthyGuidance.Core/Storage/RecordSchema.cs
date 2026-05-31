namespace HealthyGuidance.Core.Storage;

public enum FieldValueType { String, Number, NestedObject }

public sealed record FieldDef(
    string Label,
    string Key,
    FieldValueType ValueType,
    string Suffix = "",
    IReadOnlyList<FieldDef>? Children = null);

public static class RecordSchema
{
    public static IReadOnlyList<FieldDef> ForKind(RecordKind kind) => kind switch
    {
        RecordKind.Workout => WorkoutFields,
        RecordKind.BodyMetrics => BodyMetricsFields,
        _ => Array.Empty<FieldDef>()
    };

    public static readonly IReadOnlyList<FieldDef> WorkoutFields = new FieldDef[]
    {
        new("时间", "date_time", FieldValueType.String),
        new("运动类型", "sport_type", FieldValueType.String),
        new("时长", "duration_text", FieldValueType.String),
        new("消耗", "calories_text", FieldValueType.String),
        new("平均心率", "avg_heart_rate", FieldValueType.Number, " bpm"),
        new("最大心率", "max_heart_rate", FieldValueType.Number, " bpm"),
        new("心率区间", "hr_zones", FieldValueType.NestedObject, Children: new FieldDef[]
        {
            new("极限", "extreme", FieldValueType.String),
            new("无氧耐力", "anaerobic", FieldValueType.String),
            new("有氧耐力", "aerobic", FieldValueType.String),
            new("燃脂", "fat_burn", FieldValueType.String),
            new("热身", "warmup", FieldValueType.String)
        }),
        new("距离", "distance_text", FieldValueType.String),
        new("配速/速度", "avg_pace_or_speed", FieldValueType.String)
    };

    public static readonly IReadOnlyList<FieldDef> BodyMetricsFields = new FieldDef[]
    {
        new("时间", "measured_at", FieldValueType.String),
        new("体重", "weight_kg", FieldValueType.Number, " kg"),
        new("BMI", "bmi", FieldValueType.Number),
        new("体脂率", "body_fat_pct", FieldValueType.Number, " %"),
        new("内脏脂肪等级", "visceral_fat_level", FieldValueType.Number),
        new("骨骼肌量", "skeletal_muscle_kg", FieldValueType.Number, " kg"),
        new("静息心率", "heart_rate_bpm", FieldValueType.Number, " bpm")
    };
}
