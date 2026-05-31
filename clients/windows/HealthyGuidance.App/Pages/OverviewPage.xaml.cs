using HealthyGuidance.Core.Reports;
using HealthyGuidance.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HealthyGuidance.App.Pages;

public sealed partial class OverviewPage : Page
{
    public OverviewPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (RootPanel is not null) Reload();
    }

    private void Reload()
    {
        if (RootPanel is null) return;
        RenderLatestReport();
        RenderTimeline();
    }

    // ---------- 最近报告卡片 ----------
    private void RenderLatestReport()
    {
        LatestReportSlot.Children.Clear();
        Report? latest = null;
        try
        {
            latest = ReportStore.ListAll()
                .OrderByDescending(r => r.GeneratedAt)
                .FirstOrDefault();
        }
        catch { }

        if (latest is null)
        {
            LatestReportSlot.Children.Add(BuildEmptyCard(
                "还没有分析报告",
                "生成第一份",
                () => GetHost()?.NavigateFromHost("reports")));
            return;
        }

        LatestReportSlot.Children.Add(BuildReportCard(latest));
    }

    private Border BuildReportCard(Report r)
    {
        var range = $"{r.Window.Start:yyyy-MM-dd} 至 {r.Window.End:yyyy-MM-dd}";
        var meta = new TextBlock
        {
            Text = $"{range} · 生成于 {r.GeneratedAt:yyyy-MM-dd HH:mm}",
            Opacity = 0.6,
            FontSize = 12
        };
        var summary = new TextBlock
        {
            Text = r.Content.Summary,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(0, 6, 0, 8)
        };
        var openLink = new HyperlinkButton
        {
            Content = "查看 →",
            Padding = new Thickness(0)
        };
        openLink.Click += (_, _) => GetHost()?.NavigateFromHost("reports");

        var stack = new StackPanel();
        stack.Children.Add(meta);
        stack.Children.Add(summary);
        stack.Children.Add(openLink);

        return WrapCard(stack);
    }

    // ---------- 健康历程 ----------
    private void RenderTimeline()
    {
        TimelineSlot.Children.Clear();

        List<SavedRecord> records;
        try
        {
            records = RecordStore.ListAll().ToList();
        }
        catch
        {
            records = new();
        }

        if (records.Count == 0)
        {
            TimelineSlot.Children.Add(BuildEmptyCard(
                "还没有数据",
                "导入第一张截图",
                () => GetHost()?.TriggerImport()));
            return;
        }

        // 按事件时间分组
        var now = DateTime.Now;
        var todayStart = new DateTime(now.Year, now.Month, now.Day);
        var weekStart = todayStart.AddDays(-((int)todayStart.DayOfWeek + 6) % 7); // 周一
        var monthStart = new DateTime(now.Year, now.Month, 1);

        var groups = new[]
        {
            ("今天", todayStart, DateTime.MaxValue, true),
            ("本周", weekStart, todayStart, false),
            ("本月", monthStart, weekStart, false),
            ("更早", DateTime.MinValue, monthStart, false)
        };

        foreach (var (title, lo, hi, defaultExpanded) in groups)
        {
            var bucket = records
                .Where(r =>
                {
                    var t = EventTime(r);
                    return t >= lo && t < hi;
                })
                .OrderByDescending(EventTime)
                .ToList();

            TimelineSlot.Children.Add(BuildBucketExpander(title, bucket, defaultExpanded));
        }
    }

    private Expander BuildBucketExpander(string title, IReadOnlyList<SavedRecord> bucket, bool defaultExpanded)
    {
        var header = new TextBlock
        {
            Text = $"{title} · {bucket.Count} 条",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };

        UIElement content;
        if (bucket.Count == 0)
        {
            content = new TextBlock { Text = "—", Opacity = 0.5, FontSize = 12 };
        }
        else
        {
            var panel = new StackPanel { Spacing = 4 };
            foreach (var r in bucket)
                panel.Children.Add(BuildTimelineRow(r));
            content = panel;
        }

        return new Expander
        {
            Header = header,
            IsExpanded = defaultExpanded && bucket.Count > 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = content
        };
    }

    private FrameworkElement BuildTimelineRow(SavedRecord r)
    {
        var line = RecordFormatter.FormatCollapsed(r);
        var block = new TextBlock
        {
            Text = "· " + line,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Padding = new Thickness(8, 4, 8, 4)
        };
        var border = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            CornerRadius = new CornerRadius(4),
            Child = block
        };
        border.PointerEntered += (_, _) =>
            border.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        border.PointerExited += (_, _) =>
            border.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        border.Tapped += (_, _) => GetHost()?.NavigateFromHost("records", r.Id);
        return border;
    }

    // ---------- 工具 ----------
    private static DateTime EventTime(SavedRecord r)
    {
        var key = r.Kind == RecordKind.Workout ? "date_time" : "measured_at";
        if (r.Data[key] is JsonValue v
            && v.GetValueKind() == JsonValueKind.String
            && DateTime.TryParse(v.GetValue<string>(), out var dt))
            return dt;
        return r.SavedAt;
    }

    private static Border WrapCard(UIElement child) => new()
    {
        Padding = new Thickness(14, 12, 14, 12),
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1),
        BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = child
    };

    private static Border BuildEmptyCard(string title, string actionText, Action onAction)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Opacity = 0.7,
            FontSize = 14
        });
        var btn = new Button
        {
            Content = actionText,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        btn.Click += (_, _) => onAction();
        stack.Children.Add(btn);
        return WrapCard(stack);
    }

    private static MainWindow? GetHost() => App.MainWindow;
}
