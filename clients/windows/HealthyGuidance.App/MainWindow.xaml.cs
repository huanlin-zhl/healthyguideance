using HealthyGuidance.App.Pages;
using HealthyGuidance.Core.AzureOpenAI;
using HealthyGuidance.Core.Settings;
using HealthyGuidance.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace HealthyGuidance.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (!SettingsStore.Load().IsConfigured)
        {
            NavView.SelectedItem = NavView.SettingsItem;
            NavigateTo("settings", showUnconfiguredHint: true);
        }
        else
        {
            NavView.SelectedItem = NavView.MenuItems[0];
            NavigateTo("overview");
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateTo("settings");
            return;
        }
        if (args.SelectedItemContainer is NavigationViewItem item && item.Tag is string tag)
            NavigateTo(tag);
    }

    private void NavigateTo(string tag, bool showUnconfiguredHint = false)
    {
        Type? pageType = tag switch
        {
            "overview" => typeof(OverviewPage),
            "records" => typeof(RecordsPage),
            "notes" => typeof(NotesPage),
            "reports" => typeof(ReportsPage),
            "settings" => typeof(SettingsPage),
            _ => null
        };
        if (pageType is null) return;
        var alreadyThere = ContentFrame.CurrentSourcePageType == pageType;
        if (!alreadyThere)
            ContentFrame.Navigate(pageType, showUnconfiguredHint ? "unconfigured" : null,
                new EntranceNavigationTransitionInfo());
        else if (showUnconfiguredHint && ContentFrame.Content is SettingsPage existing)
            existing.ShowUnconfiguredHint();
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = SettingsStore.Load();
        if (!settings.IsConfigured)
        {
            await ShowPlaceholderAsync("尚未配置",
                "请先在 ⚙ 设置 中填入 Azure AI Foundry 的 Endpoint / Deployment / API Key。");
            return;
        }

        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");

        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0) return;

        var items = files.Select(f => new ImportRow(f)).ToList();
        await RunImportDialogAsync(settings, items);
    }

    private async Task RunImportDialogAsync(AppSettings settings, List<ImportRow> items)
    {
        var summary = new TextBlock
        {
            Text = $"共 {items.Count} 张，待处理",
            Opacity = 0.7
        };

        var listPanel = new StackPanel { Spacing = 4 };
        foreach (var item in items)
            listPanel.Children.Add(item.BuildRow());

        var body = new StackPanel
        {
            Spacing = 12,
            MinWidth = 480,
            Children =
            {
                summary,
                new ScrollViewer
                {
                    MaxHeight = 360,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = listPanel
                }
            }
        };

        var dlg = new ContentDialog
        {
            Title = "导入截图",
            Content = body,
            PrimaryButtonText = "开始",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ContentFrame.XamlRoot
        };

        var cts = new CancellationTokenSource();
        var processing = false;
        var finished = false;

        dlg.PrimaryButtonClick += async (s, args) =>
        {
            if (finished)
            {
                // “完成”→ 关闭 + 跳记录页
                return;
            }
            // “开始”
            args.Cancel = true; // 阻止 dialog 关闭
            if (processing) return;
            processing = true;
            dlg.IsPrimaryButtonEnabled = false;
            dlg.CloseButtonText = "停止";

            var sharedRoot = Path.Combine(AppContext.BaseDirectory, "shared");
            var importer = new ImportService(
                settings.Endpoint, settings.ApiKey, settings.DeploymentName, sharedRoot);

            var success = 0;
            var failed = 0;
            var dup = 0;

            for (var i = 0; i < items.Count; i++)
            {
                if (cts.IsCancellationRequested)
                {
                    for (var j = i; j < items.Count; j++)
                        items[j].MarkSkipped();
                    break;
                }

                var item = items[i];
                item.MarkInProgress();
                summary.Text = $"处理中 {i + 1}/{items.Count}";

                try
                {
                    var buffer = await FileIO.ReadBufferAsync(item.File);
                    var bytes = buffer.ToArray();
                    var ext = item.File.FileType.ToLowerInvariant();
                    var mime = ext switch
                    {
                        ".png" => "image/png",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        _ => "image/png"
                    };

                    var result = await importer.ImportAsync(
                        bytes, ext, mime, item.File.Name, DateTime.Now, cts.Token);

                    switch (result.Outcome)
                    {
                        case ImportOutcome.Success:
                            success++;
                            item.MarkSuccess(RecordFormatter.FormatCollapsed(result.Saved!));
                            break;
                        case ImportOutcome.DuplicateSkipped:
                            dup++;
                            item.MarkDuplicate();
                            break;
                        case ImportOutcome.Failed:
                            failed++;
                            item.MarkFailed(result.Message ?? "失败");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    item.MarkFailed(ex.Message);
                }
            }

            summary.Text = $"完成：成功 {success} · 失败 {failed} · 重复 {dup}";
            finished = true;
            dlg.IsPrimaryButtonEnabled = true;
            dlg.PrimaryButtonText = "完成";
            dlg.CloseButtonText = string.Empty;
        };

        dlg.CloseButtonClick += (s, args) =>
        {
            if (processing && !finished)
            {
                cts.Cancel();
                args.Cancel = true; // 不关 dialog，让循环走完到 finished
            }
        };

        var result = await dlg.ShowAsync();

        if (finished)
        {
            // 任一结果都跳记录页并刷新
            NavView.SelectedItem = NavView.MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(i => (i.Tag as string) == "records");
            NavigateTo("records");
            if (ContentFrame.Content is RecordsPage page)
                page.ReloadFromHost();
        }
    }

    private sealed class ImportRow
    {
        public StorageFile File { get; }
        private TextBlock? _status;
        private TextBlock? _detail;

        public ImportRow(StorageFile file) { File = file; }

        public FrameworkElement BuildRow()
        {
            var name = new TextBlock
            {
                Text = File.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _status = new TextBlock { Text = "待处理", Opacity = 0.6, FontSize = 12 };
            _detail = new TextBlock { Opacity = 0.6, FontSize = 12, TextWrapping = TextWrapping.Wrap };

            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(name);
            stack.Children.Add(_status);
            stack.Children.Add(_detail);

            return new Border
            {
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                Child = stack
            };
        }

        public void MarkInProgress()
        {
            if (_status is not null) { _status.Text = "解析中..."; _status.Opacity = 0.9; }
        }

        public void MarkSuccess(string summary)
        {
            if (_status is not null) { _status.Text = "✓ 成功"; _status.Opacity = 1.0; }
            if (_detail is not null) _detail.Text = summary;
        }

        public void MarkDuplicate()
        {
            if (_status is not null) { _status.Text = "↻ 已存在，跳过"; _status.Opacity = 0.7; }
        }

        public void MarkFailed(string message)
        {
            if (_status is not null)
            {
                _status.Text = "✗ 失败";
                if (Application.Current.Resources["SystemFillColorCriticalBrush"] is Brush b)
                    _status.Foreground = b;
            }
            if (_detail is not null) _detail.Text = message;
        }

        public void MarkSkipped()
        {
            if (_status is not null) { _status.Text = "已停止"; _status.Opacity = 0.5; }
        }
    }

    private async void AddNoteButton_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        var datePicker = new CalendarDatePicker { Date = now };
        var timePicker = new TimePicker
        {
            ClockIdentifier = "24HourClock",
            Time = new TimeSpan(now.Hour, now.Minute, 0)
        };
        var timeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        timeRow.Children.Add(new TextBlock { Text = "时间：", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        timeRow.Children.Add(datePicker);
        timeRow.Children.Add(timePicker);

        var input = new TextBox
        {
            PlaceholderText = "今天中午吃了...",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            MinWidth = 420
        };

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(timeRow);
        body.Children.Add(input);

        var dlg = new ContentDialog
        {
            Title = "写备注",
            Content = body,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ContentFrame.XamlRoot
        };

        dlg.PrimaryButtonClick += (s, args) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                args.Cancel = true;
                return;
            }
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text)) return;

        var dateOnly = datePicker.Date?.DateTime.Date ?? DateTime.Today;
        var localTime = dateOnly.Add(timePicker.Time);

        try
        {
            NotesStore.Append(input.Text, localTime);
            if (ContentFrame.Content is NotesPage notesPage)
                notesPage.ReloadFromHost();
        }
        catch (Exception ex)
        {
            await ShowPlaceholderAsync("保存失败", ex.Message);
        }
    }

    private async System.Threading.Tasks.Task ShowPlaceholderAsync(string title, string body)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = body,
            CloseButtonText = "好",
            XamlRoot = ContentFrame.XamlRoot
        };
        await dlg.ShowAsync();
    }
}

