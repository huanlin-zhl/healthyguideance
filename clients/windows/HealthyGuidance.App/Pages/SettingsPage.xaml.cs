using HealthyGuidance.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace HealthyGuidance.App.Pages;

public sealed partial class SettingsPage : Page
{
    private DevPanelWindow? _devPanelWindow;

    public SettingsPage()
    {
        InitializeComponent();
        var current = SettingsStore.Load();
        EndpointBox.Text = current.Endpoint;
        DeploymentBox.Text = current.DeploymentName;
        ApiKeyBox.Password = current.ApiKey;
        UpdateMaskedKey(current.ApiKey);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string s && s == "unconfigured")
            ShowUnconfiguredHint();
    }

    public void ShowUnconfiguredHint()
    {
        if (UnconfiguredHint is not null) UnconfiguredHint.IsOpen = true;
    }

    private void UpdateMaskedKey(string apiKey)
    {
        MaskedKeyText.Text = string.IsNullOrEmpty(apiKey)
            ? "（尚未配置）"
            : $"当前：{SettingsStore.MaskApiKey(apiKey)}";
    }

    private void ShowKeyToggle_Checked(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.PasswordRevealMode = PasswordRevealMode.Visible;
        ShowKeyToggle.Content = "隐藏";
    }

    private void ShowKeyToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.PasswordRevealMode = PasswordRevealMode.Hidden;
        ShowKeyToggle.Content = "显示";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            Endpoint = EndpointBox.Text.Trim(),
            DeploymentName = DeploymentBox.Text.Trim(),
            ApiKey = ApiKeyBox.Password.Trim()
        };

        if (!settings.IsConfigured)
        {
            StatusText.Text = "三项都必须填写";
            return;
        }

        try
        {
            SettingsStore.Save(settings);
            UpdateMaskedKey(settings.ApiKey);
            if (UnconfiguredHint is not null) UnconfiguredHint.IsOpen = false;
            StatusText.Text = "已保存";
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存失败：" + ex.Message;
        }
    }

    private void OpenDevPanelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_devPanelWindow != null)
        {
            _devPanelWindow.Activate();
            return;
        }
        _devPanelWindow = new DevPanelWindow();
        _devPanelWindow.Closed += (_, _) => _devPanelWindow = null;
        _devPanelWindow.Activate();
    }
}
