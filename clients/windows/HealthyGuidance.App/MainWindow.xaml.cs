using HealthyGuidance.App.Pages;
using HealthyGuidance.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;

namespace HealthyGuidance.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        NavView.SelectedItem = NavView.MenuItems[0];
        NavigateTo("overview");
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

    private void NavigateTo(string tag)
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
        if (pageType is null || ContentFrame.CurrentSourcePageType == pageType) return;
        ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowPlaceholderAsync("导入截图", "导入对话框尚未实现，将在下一批集成。");
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

