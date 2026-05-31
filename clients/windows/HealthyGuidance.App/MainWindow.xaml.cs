using HealthyGuidance.App.Pages;
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
        await ShowPlaceholderAsync("写备注", "写备注对话框尚未实现，将在下一批集成。");
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
