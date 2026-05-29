using HealthyGuidance.Application.Interfaces;
using HealthyGuidance.Domain.Enums;
using HealthyGuidance.Domain.Models;

namespace HealthyGuidance.Infrastructure.Mock;

public sealed class MockScreenshotRecognizer : IScreenshotRecognizer
{
    public ModelNames Models => new("mock-extractor-v0", "mock-classifier-v0");

    public Task<ScreenshotRecognitionResult> RecognizeAsync(Stream imageStream, CancellationToken cancellationToken)
    {
        var length = imageStream.CanSeek ? imageStream.Length : 0L;
        var kind = (length % 3) switch
        {
            0 => ScreenshotKind.Workout,
            1 => ScreenshotKind.BodyMetrics,
            _ => ScreenshotKind.Menu,
        };

        var now = DateTimeOffset.UtcNow;
        object structured = kind switch
        {
            ScreenshotKind.Workout => new Workout(
                OccurredAt: now,
                Category: "跑步",
                Fields: new Dictionary<string, string>
                {
                    ["距离"]     = "6.1 km",
                    ["时长"]     = "35:12",
                    ["配速"]     = "5'46\"/km",
                    ["卡路里"]   = "410 kcal",
                    ["平均心率"] = "152",
                    ["步频"]     = "178",
                }),

            ScreenshotKind.BodyMetrics => new BodyMetrics(
                MeasuredAt: now,
                WeightKg: 68.4,
                BodyFatPercent: 18.2,
                SkeletalMuscleKg: 30.5,
                VisceralFatLevel: 6,
                ProteinPercent: 16.8,
                Bmi: 22.3,
                Fields: new Dictionary<string, string>
                {
                    ["水分率"]   = "58.5%",
                    ["BMR"]      = "1520 kcal",
                    ["代谢年龄"] = "26",
                }),

            ScreenshotKind.Menu => new Menu(
                RawText: "宫保鸡丁 18\n番茄炒蛋 12\n米饭 2\n紫菜蛋花汤 6",
                Items: new[] { "宫保鸡丁", "番茄炒蛋", "米饭", "紫菜蛋花汤" }),

            _ => throw new ArgumentOutOfRangeException(),
        };

        return Task.FromResult(new ScreenshotRecognitionResult(
            kind, 0.93, now, structured));
    }
}
