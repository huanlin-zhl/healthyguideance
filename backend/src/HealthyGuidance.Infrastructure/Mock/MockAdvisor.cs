using System.Text.Json;
using HealthyGuidance.Application.Interfaces;
using HealthyGuidance.Contracts.Requests;
using HealthyGuidance.Domain.Models;

namespace HealthyGuidance.Infrastructure.Mock;

public sealed class MockAdvisor : IAdvisor
{
    public string ModelName => "mock-llm-v0";

    public Task<AdviceReport> AnalyzeAsync(
        IReadOnlyList<BodyMetricsSample> bodyMetrics,
        IReadOnlyList<WorkoutSample> workouts,
        IReadOnlyList<MealSample> meals,
        CancellationToken cancellationToken)
    {
        var sorted = bodyMetrics.OrderByDescending(b => b.CapturedAt).ToList();
        var latestWeight = ReadDouble(sorted.FirstOrDefault()?.Structured, "weightKg");
        var earliestWeight = ReadDouble(sorted.LastOrDefault()?.Structured, "weightKg");
        var latestFat = ReadDouble(sorted.FirstOrDefault()?.Structured, "bodyFatPercent");
        var earliestFat = ReadDouble(sorted.LastOrDefault()?.Structured, "bodyFatPercent");

        var weightDelta = latestWeight - earliestWeight;
        var fatDelta = latestFat - earliestFat;

        var verdict = fatDelta switch
        {
            < -0.2 => "improving",
            > 0.2 => "worsening",
            _ => "stable",
        };

        var summary = $"近 {sorted.Count} 次体脂数据：体重变化 {weightDelta:+0.0;-0.0;0.0} kg，体脂率变化 {fatDelta:+0.0;-0.0;0.0}%。趋势 {verdict}。";

        return Task.FromResult(new AdviceReport(
            Summary: summary,
            TrendAnalysis: new TrendAnalysis(weightDelta, fatDelta, verdict),
            Validation: $"基于 {workouts.Count} 条运动 + {meals.Count} 条饮食记录，整体匹配体脂趋势。",
            Suggestions: new[] { "维持当前节奏", "可适当增加蛋白质摄入" },
            Warnings: Array.Empty<string>()));
    }

    private static double ReadDouble(object? structured, string field)
    {
        if (structured is null) return 0;
        var json = JsonSerializer.Serialize(structured);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;
    }
}
