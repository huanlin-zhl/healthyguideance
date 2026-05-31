using HealthyGuidance.Core.AzureOpenAI;
using HealthyGuidance.Core.Prompts;
using HealthyGuidance.Core.Schemas;
using HealthyGuidance.Core.Settings;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace HealthyGuidance.App;

public sealed partial class MainWindow : Window
{
    private string _cachedSystemPrompt = string.Empty;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        LoadPromptIntoUi();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Saved += (_, _) => StatusText.Text = "设置已保存";
        _settingsWindow.Activate();
    }

    private void ReloadPromptButton_Click(object sender, RoutedEventArgs e)
    {
        LoadPromptIntoUi();
        StatusText.Text = "Prompt 已重新加载";
    }

    private void LoadPromptIntoUi()
    {
        try
        {
            var sharedRoot = Path.Combine(AppContext.BaseDirectory, "shared");
            _cachedSystemPrompt = PromptLoader.Load(sharedRoot, "parse.md");
            PromptBox.Text = _cachedSystemPrompt;
            PromptHeader.Text = $"System Prompt（{_cachedSystemPrompt.Length} 字符，点击展开）";
        }
        catch (Exception ex)
        {
            PromptBox.Text = "加载 prompt 失败：" + ex.Message;
            PromptHeader.Text = "System Prompt（加载失败）";
        }
    }

    private async void PickAndParseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PickAndParseButton.IsEnabled = false;

            var settings = SettingsStore.Load();
            if (!settings.IsConfigured)
            {
                StatusText.Text = "请先在设置中配置 Endpoint / Deployment / API Key";
                ResultBox.Text = "尚未配置 Azure AI Foundry 连接信息。点击右上角 ⚙ 设置 填写。";
                return;
            }

            StatusText.Text = "选择文件...";

            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");

            var file = await picker.PickSingleFileAsync();
            if (file == null)
            {
                StatusText.Text = "已取消";
                return;
            }

            StatusText.Text = $"读取 {file.Name}...";
            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
            var imageBytes = buffer.ToArray();
            var mime = file.FileType.ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => "image/png"
            };

            var sharedRoot = Path.Combine(AppContext.BaseDirectory, "shared");
            StatusText.Text = "加载 prompt 与 schema...";
            _cachedSystemPrompt = PromptLoader.Load(sharedRoot, "parse.md");
            PromptBox.Text = _cachedSystemPrompt;
            PromptHeader.Text = $"System Prompt（{_cachedSystemPrompt.Length} 字符，点击展开）";
            var schema = SchemaLoader.LoadInlined(sharedRoot, "parse-result.json");

            StatusText.Text = $"调用 {settings.DeploymentName} 解析中...";
            var client = new GptVisionClient(settings.Endpoint, settings.ApiKey, settings.DeploymentName);
            var json = await client.ParseScreenshotAsync(imageBytes, mime, _cachedSystemPrompt, schema);

            ResultBox.Text = json;
            StatusText.Text = "完成";
        }
        catch (Exception ex)
        {
            ResultBox.Text = ex.ToString();
            StatusText.Text = "出错：" + ex.Message;
        }
        finally
        {
            PickAndParseButton.IsEnabled = true;
        }
    }
}
