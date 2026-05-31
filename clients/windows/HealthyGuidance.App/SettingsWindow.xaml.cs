using HealthyGuidance.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace HealthyGuidance.App;

public sealed partial class SettingsWindow : Window
{
    public event EventHandler? Saved;

    public SettingsWindow()
    {
        InitializeComponent();
        var current = SettingsStore.Load();
        EndpointBox.Text = current.Endpoint;
        DeploymentBox.Text = current.DeploymentName;
        ApiKeyBox.Password = current.ApiKey;
        UpdateMaskedKey(current.ApiKey);
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
            Saved?.Invoke(this, EventArgs.Empty);
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存失败：" + ex.Message;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
