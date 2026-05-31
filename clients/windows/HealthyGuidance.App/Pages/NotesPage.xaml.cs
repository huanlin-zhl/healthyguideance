using HealthyGuidance.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;

namespace HealthyGuidance.App.Pages;

public sealed partial class NotesPage : Page
{
    public NotesPage()
    {
        InitializeComponent();
        var now = DateTime.Now;
        NoteDatePicker.Date = now;
        NoteTimePicker.Time = new TimeSpan(now.Hour, now.Minute, 0);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ReloadHistory();
    }

    public void ReloadFromHost() => ReloadHistory();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = NoteInputBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                StatusText.Text = "请输入内容";
                return;
            }

            var dateOnly = NoteDatePicker.Date?.DateTime.Date ?? DateTime.Today;
            var localTime = dateOnly.Add(NoteTimePicker.Time);

            NotesStore.Append(text, localTime);
            NoteInputBox.Text = string.Empty;
            StatusText.Text = $"已保存（{localTime:yyyy-MM-dd HH:mm}）";

            var now = DateTime.Now;
            NoteDatePicker.Date = now;
            NoteTimePicker.Time = new TimeSpan(now.Hour, now.Minute, 0);

            ReloadHistory();
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存失败：" + ex.Message;
        }
    }

    private void ReloadHistory()
    {
        MonthsPanel.Children.Clear();

        var months = NotesStore.EnumerateExistingMonths();
        if (months.Count == 0)
        {
            MonthsPanel.Children.Add(new TextBlock
            {
                Text = "还没有备注。",
                Opacity = 0.6
            });
            return;
        }

        var currentMonth = StorageRoot.MonthKey(DateTime.Now);
        foreach (var month in months)
        {
            var notes = NotesStore.ReadMonth(month);
            if (notes.Count == 0) continue;

            var header = new TextBlock
            {
                Text = $"{month} · {notes.Count} 条",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };

            var body = new StackPanel { Spacing = 8 };
            foreach (var n in notes.OrderByDescending(n => n.Timestamp))
            {
                var entryBorder = new Border
                {
                    Padding = new Thickness(12),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"]
                };
                var entryStack = new StackPanel { Spacing = 4 };
                entryStack.Children.Add(new TextBlock
                {
                    Text = n.Timestamp.ToString("yyyy-MM-dd HH:mm"),
                    Opacity = 0.6,
                    FontSize = 13
                });
                entryStack.Children.Add(new TextBlock
                {
                    Text = n.Text,
                    TextWrapping = TextWrapping.Wrap
                });
                entryBorder.Child = entryStack;
                body.Children.Add(entryBorder);
            }

            var expander = new Expander
            {
                Header = header,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = month == currentMonth,
                Content = body
            };
            MonthsPanel.Children.Add(expander);
        }
    }
}
