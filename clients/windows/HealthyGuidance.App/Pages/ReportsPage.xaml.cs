using HealthyGuidance.Core.AzureOpenAI;
using HealthyGuidance.Core.Reports;
using HealthyGuidance.Core.Schemas;
using HealthyGuidance.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace HealthyGuidance.App.Pages;

public sealed partial class ReportsPage : Page
{
    private List<Report> _all = new();

    public ReportsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ReportsPanel is not null) Reload();
    }

    private void Reload()
    {
        if (ReportsPanel is null) return;
        try
        {
            _all = ReportStore.ListAll()
                .OrderByDescending(r => r.GeneratedAt)
                .ToList();
        }
        catch
        {
            _all = new();
        }

        ReportsPanel.Children.Clear();
        SummaryText.Text = _all.Count == 0 ? "暂无报告" : $"共 {_all.Count} 份";

        if (_all.Count == 0)
        {
            ReportsPanel.Children.Add(new TextBlock
            {
                Text = "还没有报告，点击「+ 生成新报告」生成第一份。",
                Opacity = 0.6
            });
            return;
        }

        foreach (var r in _all)
        {
            try { ReportsPanel.Children.Add(BuildExpander(r)); }
            catch (Exception ex) { ReportsPanel.Children.Add(BuildErrorRow(r.Id, ex)); }
        }
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = SettingsStore.Load();
        if (!settings.IsConfigured)
        {
            await ShowDialogAsync("尚未配置", "请先在 ⚙ 设置 中填入 Azure AI Foundry 配置。");
            return;
        }

        var preset = await PickPresetAsync();
        if (preset is null) return;

        await GenerateAsync(settings, preset);
    }

    private async Task<string?> PickPresetAsync()
    {
        var btn7 = new RadioButton { Content = "过去 7 天", GroupName = "preset", Tag = "7d" };
        var btn30 = new RadioButton { Content = "过去 30 天", GroupName = "preset", Tag = "30d", IsChecked = true };
        var btn90 = new RadioButton { Content = "过去 90 天", GroupName = "preset", Tag = "90d" };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "选择时间窗口：", Opacity = 0.7 });
        panel.Children.Add(btn7);
        panel.Children.Add(btn30);
        panel.Children.Add(btn90);

        var dlg = new ContentDialog
        {
            Title = "生成报告",
            Content = panel,
            PrimaryButtonText = "生成",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return null;
        if (btn7.IsChecked == true) return "7d";
        if (btn90.IsChecked == true) return "90d";
        return "30d";
    }

    private async Task GenerateAsync(AppSettings settings, string preset)
    {
        var days = preset switch { "7d" => 7, "90d" => 90, _ => 30 };
        var now = DateTime.Now;
        var start = new DateTime(now.Year, now.Month, now.Day).AddDays(-days);

        var sharedRoot = Path.Combine(AppContext.BaseDirectory, "shared");
        var ctx = AdviceContextBuilder.Build(sharedRoot, new AdviceContextInputs
        {
            CurrentTime = now,
            WindowStart = start,
            WindowEnd = now,
            GoalWeightKg = settings.GoalWeightKg,
            GoalBodyFatPct = settings.GoalBodyFatPct
        });

        if (ctx.WorkoutCount == 0 && ctx.BodyMetricsCount == 0 && ctx.NoteCount == 0)
        {
            await ShowDialogAsync("无数据",
                "该时间窗口内没有任何数据，请先导入截图或写备注，或换一个更长的窗口。");
            return;
        }

        var progress = new ContentDialog
        {
            Title = "正在生成报告...",
            Content = new ProgressRing { IsActive = true, Width = 48, Height = 48 },
            XamlRoot = XamlRoot
        };
        _ = progress.ShowAsync();

        try
        {
            var schemaJson = SchemaLoader.LoadInlined(sharedRoot, "advice-result.json");
            var client = new AdviceClient(settings.Endpoint, settings.ApiKey, settings.DeploymentName);
            var json = await client.GenerateAsync(ctx.Prompt, schemaJson);

            var root = JsonNode.Parse(json) as JsonObject
                       ?? throw new InvalidOperationException("响应不是 JSON 对象");

            var content = new ReportContent
            {
                Summary = root["summary"]!.GetValue<string>(),
                Trend = root["trend"]!.GetValue<string>(),
                DietAdvice = root["diet_advice"]!.GetValue<string>(),
                WorkoutAdvice = root["workout_advice"]!.GetValue<string>(),
                Warnings = root["warnings"]!.GetValue<string>()
            };

            var generatedAt = DateTime.Now;
            ReportStore.Save(
                generatedAt,
                new ReportWindow { Start = start, End = now, Preset = preset },
                new ReportGoal { WeightKg = settings.GoalWeightKg, BodyFatPct = settings.GoalBodyFatPct },
                new ReportSource
                {
                    Model = settings.DeploymentName,
                    ApiVersion = ReportStore.DefaultApiVersion,
                    WorkoutIds = ctx.WorkoutIds,
                    BodyMetricsIds = ctx.BodyMetricsIds,
                    NotesWindow = ctx.NoteMonths
                },
                content);

            progress.Hide();
            Reload();
        }
        catch (Exception ex)
        {
            progress.Hide();
            await ShowDialogAsync("生成失败", ex.Message);
        }
    }

    private Task ShowDialogAsync(string title, string body)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = body,
            CloseButtonText = "好",
            XamlRoot = XamlRoot
        };
        return dlg.ShowAsync().AsTask();
    }

    private Expander BuildExpander(Report r)
    {
        var header = new TextBlock
        {
            Text = FormatHeader(r),
            TextWrapping = TextWrapping.Wrap
        };

        var body = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };

        var actionBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var regenBtn = new Button { Content = "重新生成" };
        regenBtn.Click += async (_, _) =>
        {
            var settings = SettingsStore.Load();
            if (!settings.IsConfigured)
            {
                await ShowDialogAsync("尚未配置", "请先在 ⚙ 设置 中填入 Azure AI Foundry 配置。");
                return;
            }
            await GenerateAsync(settings, r.Window.Preset);
        };
        var deleteBtn = new Button { Content = "删除" };
        deleteBtn.Click += async (_, _) => await ConfirmAndDeleteAsync(r);
        actionBar.Children.Add(regenBtn);
        actionBar.Children.Add(deleteBtn);
        body.Children.Add(actionBar);

        body.Children.Add(BuildMeta(r));
        body.Children.Add(BuildSection("📊 现状小结", r.Content.Summary));
        body.Children.Add(BuildSection("📈 趋势判断", r.Content.Trend));
        body.Children.Add(BuildSection("🥗 饮食建议", r.Content.DietAdvice));
        body.Children.Add(BuildSection("💪 运动建议", r.Content.WorkoutAdvice));
        if (!string.IsNullOrWhiteSpace(r.Content.Warnings))
            body.Children.Add(BuildSection("⚠ 注意事项", r.Content.Warnings, warning: true));
        body.Children.Add(new TextBlock
        {
            Text = r.Disclaimer,
            Opacity = 0.5,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        return new Expander
        {
            Header = header,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = body
        };
    }

    private async Task ConfirmAndDeleteAsync(Report r)
    {
        var dlg = new ContentDialog
        {
            Title = "删除报告？",
            Content = "此操作不可恢复。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            ReportStore.Delete(r);
            Reload();
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("删除失败", ex.Message);
        }
    }

    private static string FormatHeader(Report r)
    {
        var range = $"{r.Window.Start:yyyy-MM-dd} 至 {r.Window.End:yyyy-MM-dd}";
        var preview = r.Content.Summary;
        if (preview.Length > 60) preview = preview[..60] + "…";
        return $"{range} 分析报告 · {r.GeneratedAt:yyyy-MM-dd HH:mm} · {preview}";
    }

    private static FrameworkElement BuildMeta(Report r)
    {
        var goalText = r.Goal.WeightKg == 0 && r.Goal.BodyFatPct == 0
            ? "未设定目标"
            : $"目标 {r.Goal.WeightKg}kg / {r.Goal.BodyFatPct}%";
        return new TextBlock
        {
            Text = $"窗口：{r.Window.Preset} · {goalText} · 数据：运动 {r.Source.WorkoutIds.Count} · 体成分 {r.Source.BodyMetricsIds.Count} · 模型 {r.Source.Model}",
            Opacity = 0.6,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static Border BuildSection(string title, string content, bool warning = false)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14
        };
        if (warning && Application.Current.Resources["SystemFillColorCriticalBrush"] is Brush b)
            titleBlock.Foreground = b;

        var bodyBlock = new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var stack = new StackPanel();
        stack.Children.Add(titleBlock);
        stack.Children.Add(bodyBlock);

        return new Border
        {
            Padding = new Thickness(12, 10, 12, 12),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = stack
        };
    }

    private static Expander BuildErrorRow(string id, Exception ex) => new()
    {
        Header = new TextBlock { Text = $"{id} · 渲染失败" },
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Content = new TextBox
        {
            Text = ex.ToString(),
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12
        }
    };
}
